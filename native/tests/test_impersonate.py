"""
Test suite for curl-impersonate shim library.

Validates that browser impersonation produces correct TLS and HTTP/2 fingerprints
by comparing captured network traffic against known browser signatures.

Based on curl-impersonate/tests/test_impersonate.py but adapted for shim usage.
"""
import os
import sys
import random
import logging
import pathlib
import subprocess
import asyncio

import yaml
import pytest
from th1.tls.parser import parse_pcap
from th1.tls.signature import TLSClientHelloSignature
from th1.http2.parser import parse_nghttpd_log
from th1.http2.signature import HTTP2Signature
import dpkt


@pytest.fixture
def browser_signatures():
    """Load all browser signatures from YAML files."""
    docs = {}
    for path in pathlib.Path("signatures").glob("**/*.yaml"):
        with open(path, "r") as f:
            docs.update(
                {
                    f'{doc["browser"]["name"]}_{doc["browser"]["version"]}_{doc["browser"]["os"]}': doc
                    for doc in yaml.safe_load_all(f.read())
                    if doc
                }
            )
    return docs


# Default port range (used when not running with pytest-xdist)
# When running in parallel, each worker gets its own port range via worker_port_range fixture
DEFAULT_LOCAL_PORTS = (50000, 50100)

# Test URLs for TLS fingerprint validation
TEST_URLS = [
    "https://www.wikimedia.org",
    "https://www.wikipedia.org",
    "https://www.mozilla.org/en-US/",
    "https://www.apache.org",
    "https://git-scm.com",
]

# Load test targets from YAML
CURL_BINARIES_AND_SIGNATURES = yaml.safe_load(open("./targets.yaml"))


@pytest.fixture
def test_urls():
    """Shuffle TEST_URLS randomly for each test."""
    return random.sample(TEST_URLS, k=len(TEST_URLS))


@pytest.fixture
def tcpdump(pytestconfig, worker_port_range):
    """Initialize a sniffer to capture curl's traffic.

    Uses worker_port_range fixture to get unique port range per pytest-xdist worker,
    enabling parallel test execution without packet capture conflicts.
    """
    interface = pytestconfig.getoption("capture_interface")
    local_ports = worker_port_range

    logging.debug(f"Running tcpdump on interface {interface}, ports {local_ports[0]}-{local_ports[1]}")

    p = subprocess.Popen(
        [
            "tcpdump",
            "-n",
            "-i",
            interface,
            "-s",
            "0",
            "-w",
            "-",
            "-U",  # Important, makes tcpdump unbuffered
            (
                f"(tcp src portrange {local_ports[0]}-{local_ports[1]}"
                f" and tcp dst port 443) or"
                f"(tcp dst portrange {local_ports[0]}-{local_ports[1]}"
                f" and tcp src port 443)"
            ),
        ],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )

    yield p

    p.terminate()
    p.wait(timeout=10)


async def _read_proc_output(proc, timeout: int = 5):
    """Read an async process' output until timeout is reached."""
    data = bytes()
    loop = asyncio.get_running_loop()
    start_time = loop.time()
    passed = loop.time() - start_time
    while passed < timeout:
        try:
            data += await asyncio.wait_for(
                proc.stdout.readline(), timeout=timeout - passed
            )
        except asyncio.TimeoutError:
            pass
        passed = loop.time() - start_time
    return data


async def _wait_nghttpd(proc):
    """Wait for nghttpd to start listening on its designated port."""
    data = bytes()
    while data is not None:
        data = await proc.stdout.readline()
        if not data:
            return False

        line = data.decode("utf-8").rstrip()
        if "listen 0.0.0.0:8443" in line:
            return True

    return False


@pytest.fixture
async def nghttpd():
    """Initialize an HTTP/2 server for testing."""
    logging.debug("Running nghttpd on :8443")

    proc = await asyncio.create_subprocess_exec(
        "nghttpd",
        "-v",
        "8443",
        "ssl/server.key",
        "ssl/server.crt",
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )

    try:
        started = await asyncio.wait_for(_wait_nghttpd(proc), timeout=3)
        if not started:
            raise Exception("nghttpd failed to start")
    except asyncio.TimeoutError:
        raise Exception("nghttpd failed to start on time")

    yield proc

    proc.terminate()
    await proc.wait()


def _run_curl(pytestconfig, curl_binary, env_vars, extra_args, urls, local_ports=None, output="/dev/null"):
    """Run minicurl with the given environment and arguments.

    Args:
        pytestconfig: pytest config object
        curl_binary: name of the curl binary to run
        env_vars: environment variables to set
        extra_args: additional command line arguments
        urls: URLs to request
        local_ports: tuple of (start, end) port range for --local-port
        output: output file path
    """
    env = os.environ.copy()
    if env_vars:
        env.update(env_vars)

    # Binary is in the install directory
    curl_binary = os.path.join(
        pytestconfig.getoption("install_dir"), curl_binary
    )

    # Use provided port range or default
    if local_ports is None:
        local_ports = DEFAULT_LOCAL_PORTS

    logging.debug(f"Launching '{curl_binary}' to {urls}")
    if env_vars:
        logging.debug(
            "Environment variables: {}".format(
                " ".join([f"{k}={v}" for k, v in env_vars.items()])
            )
        )

    args = [
        curl_binary,
        "-o",
        output,
        "-o",
        output,
        "--local-port",
        f"{local_ports[0]}-{local_ports[1]}",
    ]
    if extra_args:
        args += extra_args
    args.extend(urls)
    logging.debug("Running curl with: %s", " ".join(args))

    curl = subprocess.Popen(args, env=env)
    return curl.wait(timeout=60)


@pytest.mark.parametrize(
    "curl_binary, env_vars, ld_preload, expected_signature",
    CURL_BINARIES_AND_SIGNATURES,
)
def test_tls_client_hello(
    pytestconfig,
    tcpdump,
    curl_binary,
    env_vars,
    ld_preload,
    browser_signatures,
    expected_signature,
    test_urls,
    worker_port_range,
):
    """
    Check that curl's TLS signature is identical to that of a real browser.

    Launches curl while sniffing its TLS traffic with tcpdump. Then
    extracts the Client Hello packet from the capture and compares its
    signature with the expected one defined in the YAML database.
    """
    # ld_preload is not used for shim-based tests (we link directly)
    if ld_preload:
        pytest.skip("LD_PRELOAD not needed for shim-based tests")

    test_urls = test_urls[0:2]
    ret = _run_curl(pytestconfig, curl_binary, env_vars=env_vars, extra_args=None, urls=test_urls, local_ports=worker_port_range)
    assert ret == 0

    try:
        pcap, stderr = tcpdump.communicate(timeout=5)
        assert tcpdump.returncode == 0, (
            f"tcpdump failed with error code {tcpdump.returncode}, stderr: {stderr}"
        )
    except subprocess.TimeoutExpired:
        tcpdump.kill()
        pcap, stderr = tcpdump.communicate(timeout=3)

    assert len(pcap) > 0
    logging.debug(f"Captured pcap of length {len(pcap)} bytes")

    try:
        client_hellos = parse_pcap(pcap)
    except dpkt.NeedData:
        logging.error("DPKT does not support parsing this TLS version yet.")
        return

    # At least one client hello message for each URL
    # (may capture more due to HTTP redirects or TLS retries)
    assert len(client_hellos) >= len(test_urls), (
        f"Expected at least {len(test_urls)} Client Hello packets, got {len(client_hellos)}"
    )

    logging.debug(
        f"Found {len(client_hellos)} Client Hello messages, "
        f"comparing to signature '{expected_signature}'"
    )

    for client_hello in client_hellos:
        sig = client_hello["signature"]
        expected_sig = TLSClientHelloSignature.from_dict(
            browser_signatures[expected_signature]["signature"]["tls_client_hello"]
        )

        allow_tls_permutation = (
            browser_signatures[expected_signature]["signature"]
            .get("options", {})
            .get("tls_permute_extensions", False)
        )

        equals, reason = expected_sig.equals(sig, allow_tls_permutation)
        assert equals, reason


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "curl_binary, env_vars, ld_preload, expected_signature",
    CURL_BINARIES_AND_SIGNATURES,
)
async def test_http2_headers(
    pytestconfig,
    nghttpd,
    curl_binary,
    env_vars,
    ld_preload,
    browser_signatures,
    expected_signature,
):
    """
    Check that curl's HTTP/2 signature is identical to that of a real browser.
    """
    # ld_preload is not used for shim-based tests
    if ld_preload:
        pytest.skip("LD_PRELOAD not needed for shim-based tests")

    ret = _run_curl(
        pytestconfig,
        curl_binary,
        env_vars=env_vars,
        extra_args=["-k"],
        urls=["https://localhost:8443"],
    )
    assert ret == 0

    output = await _read_proc_output(nghttpd, timeout=2)

    assert len(output) > 0
    sig = parse_nghttpd_log(output)

    logging.debug(f"Received {len(sig.frames)} HTTP/2 frames")

    expected_sig = HTTP2Signature.from_dict(
        browser_signatures[expected_signature]["signature"]["http2"]
    )

    equals, msg = sig.equals(expected_sig)
    assert equals, msg


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "curl_binary, env_vars",
    [
        (
            "minicurl",
            {"CURL_IMPERSONATE": "chrome101", "CURL_IMPERSONATE_HEADERS": "no"},
        ),
    ],
)
async def test_no_builtin_headers(
    pytestconfig, nghttpd, curl_binary, env_vars
):
    """
    Ensure the built-in headers are not added when
    CURL_IMPERSONATE_HEADERS is set to "no".
    """
    import itertools

    # Use some custom headers with a specific order.
    headers = [
        "X-Hello: World",
        "Accept: application/json",
        "X-Goodbye: World",
        "Accept-Encoding: deflate, gzip, br",
        "X-Foo: Bar",
        "User-Agent: curl-impersonate",
    ]
    header_args = list(itertools.chain(*[["-H", header] for header in headers]))

    ret = _run_curl(
        pytestconfig,
        curl_binary,
        env_vars=env_vars,
        extra_args=["-k"] + header_args,
        urls=["https://localhost:8443"],
    )
    assert ret == 0

    output = await _read_proc_output(nghttpd, timeout=5)

    assert len(output) > 0
    sig = parse_nghttpd_log(output)
    for frame in sig.frames:
        if frame.frame_type == "HEADERS":
            headers_frame = frame
    for i, header in enumerate(headers_frame.headers):
        assert header.lower() == headers[i].lower()


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "curl_binary, env_vars",
    [
        (
            "minicurl",
            {"CURL_IMPERSONATE": "chrome101"},
        ),
        (
            "minicurl",
            {"CURL_IMPERSONATE": "chrome101", "CURL_IMPERSONATE_HEADERS": "no"},
        ),
    ],
)
async def test_user_agent(pytestconfig, nghttpd, curl_binary, env_vars):
    """
    Ensure that any user-agent set with CURLOPT_HTTPHEADER will override
    the one set by libcurl-impersonate.
    """
    user_agent = "My-User-Agent"

    ret = _run_curl(
        pytestconfig,
        curl_binary,
        env_vars=env_vars,
        extra_args=["-k", "-H", f"User-Agent: {user_agent}"],
        urls=["https://localhost:8443"],
    )
    assert ret == 0

    output = await _read_proc_output(nghttpd, timeout=5)

    assert len(output) > 0

    sig = parse_nghttpd_log(output)
    for frame in sig.frames:
        if frame.frame_type == "HEADERS":
            headers_frame = frame
    assert any(
        [header.lower().startswith("user-agent:") for header in headers_frame.headers]
    )

    for header in headers_frame.headers:
        if header.lower().startswith("user-agent:"):
            assert header[len("user-agent:") :].strip() == user_agent


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "curl_binary, env_vars",
    [
        (
            "minicurl",
            {"CURL_IMPERSONATE": "chrome101"},
        ),
        (
            "minicurl",
            {"CURL_IMPERSONATE": "chrome101", "CURL_IMPERSONATE_HEADERS": "no"},
        ),
    ],
)
async def test_user_agent_curlopt_useragent(
    pytestconfig, nghttpd, curl_binary, env_vars
):
    """
    Ensure that any user-agent set with CURLOPT_USERAGENT will override
    the one set by libcurl-impersonate.
    """
    user_agent = "My-User-Agent"

    ret = _run_curl(
        pytestconfig,
        curl_binary,
        env_vars=env_vars,
        extra_args=["-k", "-A", user_agent],
        urls=["https://localhost:8443"],
    )
    assert ret == 0

    output = await _read_proc_output(nghttpd, timeout=5)

    assert len(output) > 0

    sig = parse_nghttpd_log(output)
    for frame in sig.frames:
        if frame.frame_type == "HEADERS":
            headers_frame = frame
    headers = headers_frame.headers
    assert any([header.lower().startswith("user-agent:") for header in headers])

    for header in headers:
        if header.lower().startswith("user-agent:"):
            assert header[len("user-agent:") :].strip() == user_agent
