cmake_minimum_required(VERSION 3.20)
project(AetherPulseCore LANGUAGES C CXX)

# Enforce 64-bit build
if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "AetherPulseCore requires 64-bit target (x64).")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Output directory configuration
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")

# Source files
set(MINHOOK_SOURCES
    Vendor/MinHook/buffer.c
    Vendor/MinHook/hde64.c
    Vendor/MinHook/hook.c
    Vendor/MinHook/trampoline.c
)

set(MINHOOK_HEADERS
    Vendor/MinHook/buffer.h
    Vendor/MinHook/hde64.h
    Vendor/MinHook/MinHook.h
    Vendor/MinHook/pstdint.h
    Vendor/MinHook/table64.h
    Vendor/MinHook/trampoline.h
)

set(CORE_SOURCES
    dllmain.cpp
    Shared/Config.cpp
    DXGI/DXGIProxy.cpp
    DXGI/FramePacer.cpp
    Streamline/StreamlineProxy.cpp
    Streamline/NVNGXProxy.cpp
    FidelityFX/DenoiserBridge.cpp
    FidelityFX/RCASBridge.cpp
    FidelityFX/FSRBridge.cpp
)

set(CORE_HEADERS
    Shared/AetherTelemetry.h
    Shared/Config.h
    DXGI/IFSRFramePacer.h
    DXGI/DXGIProxy.h
    DXGI/FramePacer.h
    Streamline/StreamlineTags.h
    Streamline/StreamlineProxy.h
    Streamline/NVNGXProxy.h
    FidelityFX/IFidelityFXDenoiser.h
    FidelityFX/DenoiserBridge.h
    FidelityFX/RCASBridge.h
    FidelityFX/FSRBridge.h
)

add_library(AetherPulseCore SHARED
    ${CORE_SOURCES}
    ${CORE_HEADERS}
    ${MINHOOK_SOURCES}
    ${MINHOOK_HEADERS}
    AetherPulseCore.def
)

target_include_directories(AetherPulseCore PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_CURRENT_SOURCE_DIR}/Shared
    ${CMAKE_CURRENT_SOURCE_DIR}/DXGI
    ${CMAKE_CURRENT_SOURCE_DIR}/Streamline
    ${CMAKE_CURRENT_SOURCE_DIR}/FidelityFX
    ${CMAKE_CURRENT_SOURCE_DIR}/Vendor/MinHook
)

target_link_libraries(AetherPulseCore PRIVATE
    d3d11
    d3d12
    dxgi
    d3dcompiler
    dxguid
)

if(MSVC)
    set_property(TARGET AetherPulseCore PROPERTY
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
    )
    target_compile_options(AetherPulseCore PRIVATE
        /W4
        /permissive-
        /EHsc
        /Zc:__cplusplus
        $<$<CONFIG:Release>:/GL>
        $<$<CONFIG:Release>:/O2>
        $<$<CONFIG:Release>:/Oi>
    )
    target_link_options(AetherPulseCore PRIVATE
        $<$<CONFIG:Release>:/LTCG>
        $<$<CONFIG:Release>:/OPT:REF>
        $<$<CONFIG:Release>:/OPT:ICF>
    )
endif()

set_target_properties(AetherPulseCore PROPERTIES
    OUTPUT_NAME "AetherPulseCore"
    PREFIX ""
)

# Modular Shader Precompilation Target (CS_6_6 / CS_6_0) emitting to bin/Redist/Shaders/
set(SHADER_OUTPUT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../bin/Redist/Shaders")
file(MAKE_DIRECTORY ${SHADER_OUTPUT_DIR})

find_program(DXC_EXECUTABLE dxc HINTS "$ENV{WindowsSdkDir}/bin/$ENV{WindowsSDKVersion}/x64" "$ENV{WindowsSdkVerBinPath}/x64")
find_program(FXC_EXECUTABLE fxc HINTS "$ENV{WindowsSdkDir}/bin/$ENV{WindowsSDKVersion}/x64" "$ENV{WindowsSdkVerBinPath}/x64")

set(SHADER_SOURCES
    "${CMAKE_CURRENT_SOURCE_DIR}/FidelityFX/Shaders/RCAS_CS.hlsl"
)

set(COMPILED_SHADERS "")
foreach(SHADER ${SHADER_SOURCES})
    get_filename_component(SHADER_NAME ${SHADER} NAME_WE)
    set(CSO_OUTPUT "${SHADER_OUTPUT_DIR}/${SHADER_NAME}.cso")
    
    if(DXC_EXECUTABLE)
        add_custom_command(
            OUTPUT ${CSO_OUTPUT}
            COMMAND ${DXC_EXECUTABLE} -T cs_6_6 -E CSMain -Fo ${CSO_OUTPUT} ${SHADER}
            MAIN_DEPENDENCY ${SHADER}
            COMMENT "Compiling HLSL Compute Shader (SM 6.6): ${SHADER_NAME}.hlsl -> ${SHADER_NAME}.cso"
        )
    elseif(FXC_EXECUTABLE)
        add_custom_command(
            OUTPUT ${CSO_OUTPUT}
            COMMAND ${FXC_EXECUTABLE} /T cs_5_0 /E CSMain /Fo ${CSO_OUTPUT} ${SHADER}
            MAIN_DEPENDENCY ${SHADER}
            COMMENT "Compiling HLSL Compute Shader (SM 5.0 fallback): ${SHADER_NAME}.hlsl -> ${SHADER_NAME}.cso"
        )
    endif()
    list(APPEND COMPILED_SHADERS ${CSO_OUTPUT})
endforeach()

if(COMPILED_SHADERS)
    add_custom_target(AetherPulseShaders ALL DEPENDS ${COMPILED_SHADERS})
    add_dependencies(AetherPulseCore AetherPulseShaders)
endif()
