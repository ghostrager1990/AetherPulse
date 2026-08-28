cmake_minimum_required(VERSION 3.20)
project(AetherPulseCore LANGUAGES C CXX)

# Enforce 64-bit build
if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "AetherPulseCore requires 64-bit target (x64).")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# MinHook sources
set(MINHOOK_SOURCES
    Vendor/MinHook/buffer.c
    Vendor/MinHook/hde64.c
    Vendor/MinHook/hook.c
    Vendor/MinHook/trampoline.c
)

# Core sources
set(CORE_SOURCES
    dllmain.cpp
    Shared/Config.cpp
    DXGI/FramePacer.cpp
    DXGI/AntiLag2Bridge.cpp
    FidelityFX/DenoiserBridge.cpp
    FidelityFX/RCASBridge.cpp
    FidelityFX/FSRBridge.cpp
    Hooks/RenderHook.cpp
    Logging/CrashHandler.cpp
)

add_library(AetherPulseCore SHARED
    ${CORE_SOURCES}
    ${MINHOOK_SOURCES}
)

set_target_properties(AetherPulseCore PROPERTIES
    OUTPUT_NAME "AetherPulseCore"
    PREFIX ""
    SUFFIX ".dll"
)

target_include_directories(AetherPulseCore PRIVATE
    .
    Include
    Shared
    DXGI
    Hooks
    Logging
    FidelityFX
    Vendor/MinHook
)

target_link_libraries(AetherPulseCore PRIVATE
    d3d12
    dxgi
    d3dcompiler
    dxguid
    user32
    gdi32
    advapi32
)

target_compile_definitions(AetherPulseCore PRIVATE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    _CRT_SECURE_NO_WARNINGS
    UNICODE
    _UNICODE
)
