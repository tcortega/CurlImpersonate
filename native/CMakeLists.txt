cmake_minimum_required(VERSION 3.16)
project(curl_shim C)

add_library(curl_shim SHARED shim.c)
target_include_directories(curl_shim PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/vendor/include)
target_link_directories(curl_shim PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/vendor/lib)
target_link_libraries(curl_shim curl-impersonate)

# macOS: use @loader_path so dylib finds libcurl-impersonate in same directory
if(APPLE)
    set_target_properties(curl_shim PROPERTIES
        BUILD_RPATH "@loader_path"
        INSTALL_RPATH "@loader_path"
    )
endif()
