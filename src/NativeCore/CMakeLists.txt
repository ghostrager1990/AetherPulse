cmake_minimum_required(VERSION 3.20)
project(AetherPulseNative LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_compile_definitions(
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    _CRT_SECURE_NO_WARNINGS
    AETHERPULSE_EXPORTS
)

include_directories(
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/Shared
    ${CMAKE_CURRENT_SOURCE_DIR}/Telemetry
    ${CMAKE_CURRENT_SOURCE_DIR}/DXGI
    ${CMAKE_CURRENT_SOURCE_DIR}/Utils
    ${CMAKE_CURRENT_SOURCE_DIR}/Logging
    ${CMAKE_CURRENT_SOURCE_DIR}/Vendor/MinHook
)

set(SOURCES
    # Core Proxy & Present Hooks (already contains Hooked_Present and exports)
    DXGI/dxgi_main.cpp
    
    # Cadence and Pacing
    DXGI/CadenceEngine.cpp
    FramePacer.cpp
    
    # Telemetry and Shared Memory IPC
    Telemetry/TelemetryCore.cpp
    Shared/Config.cpp
    Logging/CrashHandler.cpp
    
    # MinHook Core
    Vendor/MinHook/buffer.c
    Vendor/MinHook/hook.c
    Vendor/MinHook/trampoline.c
    Vendor/MinHook/hde64.c
)

add_library(dxgi SHARED ${SOURCES})

target_link_libraries(dxgi PRIVATE
    dxgi.lib
    d3d11.lib
    d3d12.lib
    winmm.lib
    version.lib
)

set_target_properties(dxgi PROPERTIES
    OUTPUT_NAME "dxgi"
    PREFIX ""
    SUFFIX ".dll"
    LINK_FLAGS "/DEF:\"${CMAKE_CURRENT_SOURCE_DIR}/DXGI/dxgi.def\""
)

