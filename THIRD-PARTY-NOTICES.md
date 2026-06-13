# Third-Party Notices

CurlImpersonate packages native assets built from `lexiforest/curl-impersonate`
`v1.5.6`. The pinned release assets and SHA-256 hashes are recorded in
`native/native-assets.json`; packaged native file hashes are recorded in
`runtimes/native-files.json` during the release build.

The native assets include curl-impersonate/libcurl functionality and native TLS,
compression, DNS, HTTP/2, and HTTP/3 dependencies from the upstream release
asset. Those components remain under their upstream licenses.

| Component | License |
| --- | --- |
| `lexiforest/curl-impersonate` | MIT |
| `curl` / `libcurl` | curl license |
| BoringSSL / OpenSSL-compatible TLS components | ISC-style / OpenSSL-derived notices |
| `nghttp2` | MIT |
| `nghttp3` | MIT |
| `ngtcp2` | MIT |
| Brotli | MIT |
| Zstandard | BSD-3-Clause |
| c-ares | MIT |
| zlib or compatible compression libraries | zlib license |

Upstream project and license locations:

- `lexiforest/curl-impersonate`: https://github.com/lexiforest/curl-impersonate
- `curl`: https://curl.se/docs/copyright.html
- BoringSSL: https://boringssl.googlesource.com/boringssl/+/HEAD/LICENSE
- `nghttp2`: https://github.com/nghttp2/nghttp2
- `nghttp3`: https://github.com/ngtcp2/nghttp3
- `ngtcp2`: https://github.com/ngtcp2/ngtcp2
- Brotli: https://github.com/google/brotli
- Zstandard: https://github.com/facebook/zstd
- c-ares: https://github.com/c-ares/c-ares
- zlib: https://zlib.net/zlib_license.html
