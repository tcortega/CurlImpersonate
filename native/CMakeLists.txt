cmake_minimum_required(VERSION 3.24)
project(curl_shim C)

# Vendor directory with pre-built static archive and headers
set(VENDOR_DIR "${CMAKE_CURRENT_SOURCE_DIR}/vendor")

add_library(curl_shim SHARED shim.c)
target_include_directories(curl_shim PRIVATE ${VENDOR_DIR}/include)
target_compile_definitions(curl_shim PRIVATE CURL_STATICLIB)

set_target_properties(curl_shim PROPERTIES
    POSITION_INDEPENDENT_CODE ON
    PREFIX "lib"
)

if(WIN32)
    # Windows: dynamic linking against vendor import libs
    target_link_directories(curl_shim PRIVATE ${VENDOR_DIR}/lib)
    target_link_libraries(curl_shim PRIVATE
        libcurl ws2_32 crypt32 iphlpapi wldap32 advapi32 normaliz)
else()
    if(APPLE)
        # macOS: use globalized archive (N_PEXT cleared by build_shim.py)
        set(CURL_ARCHIVE "${VENDOR_DIR}/lib/libcurl-impersonate-global.a")
    else()
        set(CURL_ARCHIVE "${VENDOR_DIR}/lib/libcurl-impersonate.a")
    endif()

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
        # Linux
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
