// AMD FidelityFX Robust Contrast-Adaptive Sharpening (RCAS)
cbuffer RcasConstants : register(b0) {
    uint4 rcasConfig; // Packed sharpness attenuation
};

Texture2D<float4> InputTexture : register(t0);
RWTexture2D<float4> OutputTexture : register(u0);

float3 Min3(float3 a, float3 b, float3 c) { return min(a, min(b, c)); }
float3 Max3(float3 a, float3 b, float3 c) { return max(a, max(b, c)); }

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID) {
    int2 pos = int2(id.xy);
    
    // Sample 5-tap cross neighborhood
    float3 e = InputTexture.Load(int3(pos, 0)).rgb;
    float3 b = InputTexture.Load(int3(pos + int2(0, -1), 0)).rgb;
    float3 d = InputTexture.Load(int3(pos + int2(-1, 0), 0)).rgb;
    float3 f = InputTexture.Load(int3(pos + int2(1, 0), 0)).rgb;
    float3 h = InputTexture.Load(int3(pos + int2(0, 1), 0)).rgb;
    
    // Attenuation factor unpack
    float sharpness = asfloat(rcasConfig.x);
    if (sharpness <= 0.001f) {
        OutputTexture[pos] = float4(e, 1.0f);
        return;
    }
    
    // Luma weights
    float3 mn = Min3(Min3(b, d, f), h, e);
    float3 mx = Max3(Max3(b, d, f), h, e);
    
    // Contrast calculation
    float3 nz = (b + d + f + h) * 0.25f;
    float3 hit = (mn + mx) * 0.5f;
    float3 diff = e - hit;
    
    float w = clamp(sharpness * 0.25f, 0.0f, 0.25f);
    float3 result = clamp(e + diff * (w * 4.0f), 0.0f, 1.0f);
    
    OutputTexture[pos] = float4(result, 1.0f);
}
