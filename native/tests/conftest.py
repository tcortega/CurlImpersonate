import sys

def pytest_addoption(parser):
    # Where to find curl-impersonate's binaries
    parser.addoption("--install-dir", action="store", default=".")
    # Default capture interface: en0 for macOS (WiFi), eth0 for Linux
    default_interface = "en0" if sys.platform == "darwin" else "eth0"
    parser.addoption("--capture-interface", action="store", default=default_interface)
