cmake_minimum_required(VERSION 3.16)
project(curl-shim VERSION 1.0.0 LANGUAGES C)

# Build shared library
add_library(curl-shim SHARED shim.c)

# Set C standard
set_target_properties(curl-shim PROPERTIES
    C_STANDARD 11
    C_STANDARD_REQUIRED ON
)

# Vendor paths
set(VENDOR_DIR ${CMAKE_CURRENT_SOURCE_DIR}/vendor)
set(VENDOR_INCLUDE ${VENDOR_DIR}/include)
set(VENDOR_LIB ${VENDOR_DIR}/lib)

# Include directories
target_include_directories(curl-shim PRIVATE ${VENDOR_INCLUDE})

# Platform-specific configuration
if(WIN32)
    # Windows configuration
    target_compile_definitions(curl-shim PRIVATE
        _CRT_SECURE_NO_WARNINGS
        CURL_STATICLIB  # If linking against static curl
    )

    # Find libcurl in vendor directory
    find_library(CURL_LIBRARY
        NAMES libcurl curl libcurl_imp
        PATHS ${VENDOR_LIB}
        NO_DEFAULT_PATH
    )

    if(CURL_LIBRARY)
        target_link_libraries(curl-shim PRIVATE ${CURL_LIBRARY})
    else()
        # Fallback: link directly to DLL import lib
        target_link_libraries(curl-shim PRIVATE ${VENDOR_LIB}/libcurl.lib)
    endif()

    # Windows system libraries for curl
    target_link_libraries(curl-shim PRIVATE ws2_32 crypt32 advapi32)

    # Output naming
    set_target_properties(curl-shim PROPERTIES
        OUTPUT_NAME "curl-shim"
        PREFIX ""
        SUFFIX ".dll"
    )

elseif(APPLE)
    # macOS configuration
    target_compile_options(curl-shim PRIVATE -fvisibility=hidden)

    # Find libcurl-impersonate
    find_library(CURL_LIBRARY
        NAMES curl-impersonate curl-impersonate-chrome curl
        PATHS ${VENDOR_LIB}
        NO_DEFAULT_PATH
    )

    if(CURL_LIBRARY)
        target_link_libraries(curl-shim PRIVATE ${CURL_LIBRARY})
    else()
        target_link_libraries(curl-shim PRIVATE ${VENDOR_LIB}/libcurl-impersonate.dylib)
    endif()

    # RPATH for finding libraries relative to the shim
    set_target_properties(curl-shim PROPERTIES
        INSTALL_RPATH "@loader_path"
        BUILD_WITH_INSTALL_RPATH TRUE
        OUTPUT_NAME "curl-shim"
        PREFIX "lib"
        SUFFIX ".dylib"
    )

    # Post-build: Copy vendor libraries and fix install names for proper runtime linking
    add_custom_command(TARGET curl-shim POST_BUILD
        # Copy libcurl-impersonate libraries to build directory
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            ${VENDOR_LIB}/libcurl-impersonate.dylib
            $<TARGET_FILE_DIR:curl-shim>/libcurl-impersonate.dylib
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            ${VENDOR_LIB}/libcurl-impersonate.4.dylib
            $<TARGET_FILE_DIR:curl-shim>/libcurl-impersonate.4.dylib

        # Fix the install name of libcurl-impersonate to use @loader_path
        COMMAND install_name_tool -id @loader_path/libcurl-impersonate.dylib
            $<TARGET_FILE_DIR:curl-shim>/libcurl-impersonate.dylib
        COMMAND install_name_tool -id @loader_path/libcurl-impersonate.4.dylib
            $<TARGET_FILE_DIR:curl-shim>/libcurl-impersonate.4.dylib

        COMMENT "Copying and fixing library install names"
    )

    # Separate command to fix the shim's dependency on libcurl-impersonate
    # This runs after the library is built and uses a helper script
    add_custom_command(TARGET curl-shim POST_BUILD
        COMMAND ${CMAKE_CURRENT_SOURCE_DIR}/scripts/fix_macos_rpath.sh $<TARGET_FILE:curl-shim>
        COMMENT "Fixing curl-shim library dependencies"
    )

else()
    # Linux configuration
    target_compile_options(curl-shim PRIVATE -fvisibility=hidden)

    # Find libcurl-impersonate
    find_library(CURL_LIBRARY
        NAMES curl-impersonate curl-impersonate-chrome curl
        PATHS ${VENDOR_LIB}
        NO_DEFAULT_PATH
    )

    if(CURL_LIBRARY)
        target_link_libraries(curl-shim PRIVATE ${CURL_LIBRARY})
    else()
        target_link_libraries(curl-shim PRIVATE ${VENDOR_LIB}/libcurl-impersonate.so)
    endif()

    # RPATH for finding libraries relative to the shim ($ORIGIN)
    set_target_properties(curl-shim PROPERTIES
        INSTALL_RPATH "$ORIGIN"
        BUILD_WITH_INSTALL_RPATH TRUE
        OUTPUT_NAME "curl-shim"
        PREFIX "lib"
        SUFFIX ".so"
    )
endif()

# Install targets
install(TARGETS curl-shim
    LIBRARY DESTINATION lib
    RUNTIME DESTINATION bin
    ARCHIVE DESTINATION lib
)

# Copy vendor libraries alongside the shim for distribution
if(WIN32)
    install(FILES ${VENDOR_LIB}/libcurl.dll DESTINATION bin OPTIONAL)
elseif(APPLE)
    install(DIRECTORY ${VENDOR_LIB}/ DESTINATION lib
        FILES_MATCHING PATTERN "*.dylib")
else()
    install(DIRECTORY ${VENDOR_LIB}/ DESTINATION lib
        FILES_MATCHING PATTERN "*.so*")
endif()

# =============================================================================
# Testing
# =============================================================================
enable_testing()

# Chrome fingerprint test
add_executable(test_chrome_fingerprint tests/test_chrome_fingerprint.c)
target_include_directories(test_chrome_fingerprint PRIVATE ${VENDOR_INCLUDE})
target_link_libraries(test_chrome_fingerprint PRIVATE curl-shim)

# Set RPATH for test executable to find libraries in build directory
if(APPLE)
    set_target_properties(test_chrome_fingerprint PROPERTIES
        BUILD_RPATH "${CMAKE_BINARY_DIR}"
        INSTALL_RPATH "@loader_path"
    )
elseif(UNIX)
    set_target_properties(test_chrome_fingerprint PROPERTIES
        BUILD_RPATH "${CMAKE_BINARY_DIR}"
        INSTALL_RPATH "$ORIGIN"
    )
endif()

# Register test with CTest
add_test(NAME chrome_fingerprint_test COMMAND test_chrome_fingerprint)

# Set environment for test to find shared libraries
if(APPLE)
    set_tests_properties(chrome_fingerprint_test PROPERTIES
        ENVIRONMENT "DYLD_LIBRARY_PATH=${CMAKE_BINARY_DIR}"
    )
elseif(UNIX)
    set_tests_properties(chrome_fingerprint_test PROPERTIES
        ENVIRONMENT "LD_LIBRARY_PATH=${CMAKE_BINARY_DIR}"
    )
endif()

# =============================================================================
# Python Test Suite (minicurl + pytest)
# =============================================================================

# Build minicurl test application
add_executable(minicurl tests/minicurl.c)
target_include_directories(minicurl PRIVATE ${VENDOR_INCLUDE})
target_link_libraries(minicurl PRIVATE curl-shim)

# Set RPATH for minicurl to find libraries in build directory
if(APPLE)
    set_target_properties(minicurl PROPERTIES
        BUILD_RPATH "${CMAKE_BINARY_DIR}"
        INSTALL_RPATH "@loader_path"
    )
elseif(UNIX)
    set_target_properties(minicurl PROPERTIES
        BUILD_RPATH "${CMAKE_BINARY_DIR}"
        INSTALL_RPATH "$ORIGIN"
    )
endif()

# Copy minicurl to tests directory after build so pytest can find it
add_custom_command(TARGET minicurl POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy $<TARGET_FILE:minicurl> ${CMAKE_BINARY_DIR}/tests/minicurl
    COMMENT "Copying minicurl to tests directory"
)

# Create tests directory in build folder
file(MAKE_DIRECTORY ${CMAKE_BINARY_DIR}/tests)

# Copy Python test files to build directory
configure_file(tests/pytest.ini ${CMAKE_BINARY_DIR}/tests/pytest.ini COPYONLY)
configure_file(tests/conftest.py ${CMAKE_BINARY_DIR}/tests/conftest.py COPYONLY)
configure_file(tests/test_impersonate.py ${CMAKE_BINARY_DIR}/tests/test_impersonate.py COPYONLY)
configure_file(tests/targets.yaml ${CMAKE_BINARY_DIR}/tests/targets.yaml COPYONLY)
configure_file(tests/requirements.txt ${CMAKE_BINARY_DIR}/tests/requirements.txt COPYONLY)

# Copy SSL certificates to build directory
file(COPY tests/ssl DESTINATION ${CMAKE_BINARY_DIR}/tests/)

# Create symlink to signatures directory in build folder
if(NOT EXISTS ${CMAKE_BINARY_DIR}/tests/signatures)
    execute_process(COMMAND ${CMAKE_COMMAND} -E create_symlink
        ${CMAKE_CURRENT_SOURCE_DIR}/tests/signatures
        ${CMAKE_BINARY_DIR}/tests/signatures
    )
endif()

# Determine capture interface based on platform
# TLS tests hit external URLs, so we need the actual network interface
if(APPLE)
    set(CAPTURE_INTERFACE "en0")  # WiFi interface for external traffic
else()
    set(CAPTURE_INTERFACE "eth0")  # Linux ethernet interface
endif()

# Register Python TLS tests with CTest (requires sudo for tcpdump)
# Note: --install-dir is "." since minicurl is copied to tests directory
add_test(NAME python_tls_tests
    COMMAND python3 -m pytest test_impersonate.py::test_tls_client_hello
        --log-cli-level DEBUG
        --install-dir .
        --capture-interface ${CAPTURE_INTERFACE}
        -v
    WORKING_DIRECTORY ${CMAKE_BINARY_DIR}/tests
)

# Register Python HTTP/2 tests with CTest (requires nghttpd)
add_test(NAME python_http2_tests
    COMMAND python3 -m pytest test_impersonate.py::test_http2_headers
        --log-cli-level DEBUG
        --install-dir .
        --capture-interface ${CAPTURE_INTERFACE}
        -v
    WORKING_DIRECTORY ${CMAKE_BINARY_DIR}/tests
)

# Set environment for Python tests
if(APPLE)
    set_tests_properties(python_tls_tests python_http2_tests PROPERTIES
        ENVIRONMENT "DYLD_LIBRARY_PATH=${CMAKE_BINARY_DIR}"
    )
elseif(UNIX)
    set_tests_properties(python_tls_tests python_http2_tests PROPERTIES
        ENVIRONMENT "LD_LIBRARY_PATH=${CMAKE_BINARY_DIR}"
    )
endif()
