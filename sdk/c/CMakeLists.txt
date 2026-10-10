cmake_minimum_required(VERSION 3.16)
project(symbolon_c_sdk VERSION 2.4.0 LANGUAGES C)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)

include_directories(include)

add_library(symbolon STATIC
    src/symbolon.c
)

add_library(flexnet_shim STATIC
    src/flexnet_shim.c
)
target_link_libraries(flexnet_shim PRIVATE symbolon)

if(WIN32)
    target_link_libraries(symbolon PRIVATE ws2_32)
endif()

# Shared libraries for dynamic linking
add_library(symbolon_shared SHARED src/symbolon.c)
set_target_properties(symbolon_shared PROPERTIES OUTPUT_NAME "symbolon")
if(WIN32)
    target_link_libraries(symbolon_shared PRIVATE ws2_32)
endif()

add_library(flexnet_shim_shared SHARED src/flexnet_shim.c)
set_target_properties(flexnet_shim_shared PROPERTIES OUTPUT_NAME "flexnet_shim")
target_link_libraries(flexnet_shim_shared PRIVATE symbolon_shared)
