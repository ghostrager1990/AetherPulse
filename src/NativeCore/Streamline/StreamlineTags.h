#pragma once

#include <cstdint>
#include <d3d12.h>

namespace sl
{
    using Feature = uint32_t;
    using ResourceType = uint32_t;
    using BufferType = uint32_t;

    // Feature constants
    constexpr Feature kFeatureDLSS   = 0;
    constexpr Feature kFeatureNRD    = 1;
    constexpr Feature kFeatureNIS    = 2;
    constexpr Feature kFeatureReflex = 3;
    constexpr Feature kFeatureDLSS_G = 1000;
    constexpr Feature kFeatureDLSS_D = 1001; // Ray Reconstruction / Denoiser

    // Alias for enum naming conventions
    constexpr Feature eFeatureDLSS   = kFeatureDLSS;
    constexpr Feature eFeatureDLSS_G = kFeatureDLSS_G;
    constexpr Feature eFeatureDLSS_D = kFeatureDLSS_D;
    constexpr Feature eFeatureNRD    = kFeatureNRD;

    // Resource Tag IDs
    constexpr BufferType kBufferTypeDepth                   = 0;
    constexpr BufferType kBufferTypeMotionVectors            = 1;
    constexpr BufferType kBufferTypeHUDLessColor            = 2;
    constexpr BufferType kBufferTypeScalingInputColor       = 3;
    constexpr BufferType kBufferTypeScalingOutputColor      = 4;
    constexpr BufferType kBufferTypeNormals                 = 5;
    constexpr BufferType kBufferTypeRoughness               = 6;
    constexpr BufferType kBufferTypeAlbedo                  = 7;
    constexpr BufferType kBufferTypeSpecularHitDistance     = 8;
    constexpr BufferType kBufferTypeDiffuseRadiance         = 9;
    constexpr BufferType kBufferTypeSpecularRadiance        = 10;
    constexpr BufferType kBufferTypeRaytracingRadiance      = 10; // Combined / specular radiance alias
    constexpr BufferType kBufferTypeCount                   = 16;

    // Resource tag aliases
    constexpr BufferType SL_RESOURCE_TAG_DEPTH                   = kBufferTypeDepth;
    constexpr BufferType SL_RESOURCE_TAG_MOTION_VECTORS          = kBufferTypeMotionVectors;
    constexpr BufferType SL_RESOURCE_TAG_NORMALS                 = kBufferTypeNormals;
    constexpr BufferType SL_RESOURCE_TAG_ROUGHNESS               = kBufferTypeRoughness;
    constexpr BufferType SL_RESOURCE_TAG_ALBEDO                  = kBufferTypeAlbedo;
    constexpr BufferType SL_RESOURCE_TAG_SPECULAR_HIT_DISTANCE   = kBufferTypeSpecularHitDistance;
    constexpr BufferType SL_RESOURCE_TAG_COLOR_DIFFUSE_RADIANCE  = kBufferTypeDiffuseRadiance;
    constexpr BufferType SL_RESOURCE_TAG_COLOR_SPECULAR_RADIANCE = kBufferTypeSpecularRadiance;

    enum class Result : uint32_t
    {
        eOk = 0,
        eErrorIO,
        eErrorDriverOutOfDate,
        eErrorOSOutOfDate,
        eErrorGPUArchitectureNotSupported,
        eErrorFeatureNotSupportedOnDevice,
        eErrorFeatureMissing,
        eErrorFeatureNotSupported,
        eErrorFeatureDisabled,
        eErrorInvalidParameter,
        eErrorNotInitialized
    };

    struct Resource
    {
        ResourceType type;
        void*        nativeResource; // ID3D12Resource* for D3D12
        uint32_t     width;
        uint32_t     height;
        uint32_t     nativeFormat;
        uint32_t     state;
        uint32_t     flags;
    };

    struct ResourceTag
    {
        Resource*   resource;
        BufferType  type;
        uint32_t    lifecycle;
        void*       extent;
    };

    struct ViewportHandle
    {
        uint32_t id;
    };

    struct FrameToken
    {
        uint32_t frameIndex;
    };

    struct Extent
    {
        uint32_t top;
        uint32_t left;
        uint32_t width;
        uint32_t height;
    };

    struct FeatureRequirements
    {
        uint32_t flags;
        uint32_t maxSupportedArchitecture;
        uint32_t minOSVersionBuild;
        uint32_t minDriverVersionMajor;
        uint32_t minDriverVersionMinor;
    };

    struct Preferences
    {
        uint32_t flags;
        uint32_t targetWidth;
        uint32_t targetHeight;
        void*    renderDevice;
    };
}
