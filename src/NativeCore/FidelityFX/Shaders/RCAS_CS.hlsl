// FidelityFX RCAS (Robust Contrast Adaptive Sharpening) HLSL Compute Shader
// Based on AMD FidelityFX RCAS architecture

cbuffer RCASConstants : register(b0)
{
    uint2 g_Resolution;
    float g_Sharpness;
    float g_Padding;
};

Texture2D<float4>   g_InputTexture   : register(t0);
RWTexture2D<float4> g_OutputTexture  : register(u0);

// Convert RGB to perceptual luma
float RgbToLuma(float3 rgb)
{
    return dot(rgb, float3(0.2126f, 0.7152f, 0.0722f));
}

[numthreads(8, 8, 1)]
void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (dispatchThreadId.x >= g_Resolution.x || dispatchThreadId.y >= g_Resolution.y)
        return;

    int2 pos = int2(dispatchThreadId.xy);

    // Sample 5-tap cross neighborhood (e = center, b = top, d = left, f = right, h = bottom)
    float4 e = g_InputTexture.Load(int3(pos, 0));
    float4 b = g_InputTexture.Load(int3(clamp(pos + int2(0, -1), int2(0, 0), int2(g_Resolution) - 1), 0));
    float4 d = g_InputTexture.Load(int3(clamp(pos + int2(-1, 0), int2(0, 0), int2(g_Resolution) - 1), 0));
    float4 f = g_InputTexture.Load(int3(clamp(pos + int2(1, 0), int2(0, 0), int2(g_Resolution) - 1), 0));
    float4 h = g_InputTexture.Load(int3(clamp(pos + int2(0, 1), int2(0, 0), int2(g_Resolution) - 1), 0));

    // Calculate perceptual luminance
    float bL = RgbToLuma(b.rgb);
    float dL = RgbToLuma(d.rgb);
    float eL = RgbToLuma(e.rgb);
    float fL = RgbToLuma(f.rgb);
    float hL = RgbToLuma(h.rgb);

    // Min and Max local contrast limits
    float minL = min(eL, min(min(bL, dL), min(fL, hL)));
    float maxL = max(eL, max(max(bL, dL), max(fL, hL)));

    // Contrast adaptive weight calculation
    float contrast = max(maxL - minL, 0.001f);
    float amp = saturate(minL / contrast);
    float peak = -1.0f / lerp(8.0f, 5.0f, saturate(g_Sharpness));
    float w = amp * peak * g_Sharpness;

    // Apply adaptive sharpening filter
    float3 sharpenedRgb = (b.rgb * w + d.rgb * w + f.rgb * w + h.rgb * w + e.rgb) / (1.0f + 4.0f * w);

    // Anti-ringing clamp
    float3 minRgb = min(e.rgb, min(min(b.rgb, d.rgb), min(f.rgb, h.rgb)));
    float3 maxRgb = max(e.rgb, max(max(b.rgb, d.rgb), max(f.rgb, h.rgb)));
    sharpenedRgb = clamp(sharpenedRgb, minRgb, maxRgb);

    g_OutputTexture[pos] = float4(sharpenedRgb, e.a);
}
