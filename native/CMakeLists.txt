cmake_minimum_required(VERSION 3.24)
project(curl_shim C)

# Vendor directory with pre-built static archive and headers
set(VENDOR_DIR "${CMAKE_CURRENT_SOURCE_DIR}/vendor")

add_library(curl_shim SHARED shim.c)
target_include_directories(curl_shim PRIVATE ${VENDOR_DIR}/include)
target_compile_definitions(curl_shim PRIVATE CURL_STATICLIB)

set_target_properties(curl_shim PROPERTIES
    POSITION_INDEPENDENT_CODE ON
)

# libcurl_shim.{so,dylib} on Unix, curl_shim.dll on Windows
if(NOT WIN32)
    set_target_properties(curl_shim PROPERTIES PREFIX "lib")
endif()

if(WIN32)
    # Windows: dynamic linking against vendor import libs
    target_link_directories(curl_shim PRIVATE ${VENDOR_DIR}/lib)
    target_link_libraries(curl_shim PRIVATE
        libcurl-impersonate_imp ws2_32 crypt32 iphlpapi wldap32 advapi32 normaliz)
else()
    # macOS/Linux: use the globalized archive (hidden-visibility symbols
    # promoted by build_shim.py so they can be re-exported)
    set(CURL_ARCHIVE "${VENDOR_DIR}/lib/libcurl-impersonate-global.a")

    # macOS/Linux: static link the mega archive
    target_link_libraries(curl_shim PRIVATE
        "$<LINK_LIBRARY:WHOLE_ARCHIVE,${CURL_ARCHIVE}>")

    if(APPLE)
        # Only export shim_* and curl_* symbols we P/Invoke
        target_link_options(curl_shim PRIVATE
            "LINKER:-exported_symbols_list,${CMAKE_CURRENT_SOURCE_DIR}/shim.exp")
        target_link_libraries(curl_shim PRIVATE
            "-framework CoreFoundation"
            "-framework SystemConfiguration"
            "-framework Security"
            "-lc++" pthread iconv icucore)
        set_target_properties(curl_shim PROPERTIES
            MACOSX_RPATH ON
            BUILD_WITH_INSTALL_RPATH ON
            INSTALL_RPATH "@loader_path"
            INSTALL_NAME_DIR "@rpath")
        # Ad-hoc sign (required for Apple Silicon)
        add_custom_command(TARGET curl_shim POST_BUILD
            COMMAND codesign --force -s - "$<TARGET_FILE:curl_shim>")
    else()
        # Linux: link with the C++ driver so the BoringSSL C++ objects in the
        # mega archive get a C++ runtime, statically (-static-libstdc++ is a
        # no-op under the C driver and the package must not depend on
        # libstdc++/libgcc_s at runtime)
        enable_language(CXX)
        set_target_properties(curl_shim PROPERTIES LINKER_LANGUAGE CXX)
        target_link_options(curl_shim PRIVATE
            -static-libgcc -static-libstdc++
            "LINKER:--gc-sections"
            "LINKER:--version-script=${CMAKE_CURRENT_SOURCE_DIR}/shim.map")
        target_link_libraries(curl_shim PRIVATE pthread dl m rt)
        set_target_properties(curl_shim PROPERTIES
            BUILD_WITH_INSTALL_RPATH ON
            INSTALL_RPATH "$ORIGIN")
    endif()
endif()
