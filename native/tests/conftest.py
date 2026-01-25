import sys
import os
import pytest


def pytest_addoption(parser):
    # Where to find curl-impersonate's binaries
    parser.addoption("--install-dir", action="store", default=".")
    # Default capture interface: en0 for macOS (WiFi), eth0 for Linux
    default_interface = "en0" if sys.platform == "darwin" else "eth0"
    parser.addoption("--capture-interface", action="store", default=default_interface)


@pytest.fixture
def worker_port_range():
    """
    Assign unique port range per pytest-xdist worker.

    Each worker gets a 200-port range to avoid packet capture conflicts
    when running tests in parallel with pytest-xdist (-n option).

    Worker gw0: 50000-50199
    Worker gw1: 50200-50399
    Worker gw2: 50400-50599
    etc.
    """
    worker_id = os.environ.get("PYTEST_XDIST_WORKER", "gw0")
    if worker_id == "master" or not worker_id.startswith("gw"):
        worker_num = 0
    else:
        worker_num = int(worker_id.replace("gw", ""))

    base_port = 50000 + (worker_num * 200)
    return (base_port, base_port + 100)


@pytest.fixture
def worker_nghttpd_port():
    """
    Assign unique nghttpd port per pytest-xdist worker.

    Each worker gets a unique port for its nghttpd instance to avoid
    conflicts when running HTTP/2 tests in parallel.

    Worker gw0: 8443
    Worker gw1: 8444
    Worker gw2: 8445
    etc.
    """
    worker_id = os.environ.get("PYTEST_XDIST_WORKER", "gw0")
    if worker_id == "master" or not worker_id.startswith("gw"):
        worker_num = 0
    else:
        worker_num = int(worker_id.replace("gw", ""))

    return 8443 + worker_num
