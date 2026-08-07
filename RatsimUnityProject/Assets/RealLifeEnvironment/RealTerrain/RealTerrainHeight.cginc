#ifndef REAL_TERRAIN_HEIGHT_CGINC
#define REAL_TERRAIN_HEIGHT_CGINC

sampler2D _CustomTerrainHeightmap;
float4 _CustomTerrainHeightmap_TexelSize;
float4 _CustomTerrainHeightmap_Bounds;
float _CustomTerrainHeightmap_Multiplier;

// 4-tap optimized B-Spline bicubic filter for perfectly smooth C2 continuity
float SampleBicubicBSpline(sampler2D tex, float4 texSizeData, float2 uv)
{
    float2 texSize = texSizeData.zw;
    float2 invTexSize = texSizeData.xy;
    
    uv = uv * texSize - 0.5;
    
    float2 f = frac(uv);
    float2 i = floor(uv);
    
    float2 f2 = f * f;
    float2 f3 = f2 * f;
    
    float2 w0 = (1.0/6.0) * (1.0 - f) * (1.0 - f) * (1.0 - f);
    float2 w1 = (1.0/6.0) * (3.0 * f3 - 6.0 * f2 + 4.0);
    float2 w2 = (1.0/6.0) * (-3.0 * f3 + 3.0 * f2 + 3.0 * f + 1.0);
    float2 w3 = (1.0/6.0) * f3;
    
    float2 g0 = w0 + w1;
    float2 g1 = w2 + w3;
    
    float2 h0 = (w1 / g0) - 1.0;
    float2 h1 = (w3 / g1) + 1.0;
    
    float2 p0 = (i + h0 + 0.5) * invTexSize;
    float2 p1 = (i + h1 + 0.5) * invTexSize;
    
    float result = 
        g0.y * (g0.x * tex2Dlod(tex, float4(p0.x, p0.y, 0, 0)).r +
                g1.x * tex2Dlod(tex, float4(p1.x, p0.y, 0, 0)).r) +
        g1.y * (g0.x * tex2Dlod(tex, float4(p0.x, p1.y, 0, 0)).r +
                g1.x * tex2Dlod(tex, float4(p1.x, p1.y, 0, 0)).r);
                
    return result;
}

float GetTerrainHeightOriginal(float2 worldXZ)
{
    if (_CustomTerrainHeightmap_Bounds.z > 0.0) 
    {
        float2 center = _CustomTerrainHeightmap_Bounds.xy;
        float2 size = _CustomTerrainHeightmap_Bounds.zw;
        float2 uv = (worldXZ - center) / size + 0.5;
        return SampleBicubicBSpline(_CustomTerrainHeightmap, _CustomTerrainHeightmap_TexelSize, uv) * _CustomTerrainHeightmap_Multiplier;
    }
    return 0.0;
}

float GetTerrainHeight(float2 worldXZ)
{
    return GetTerrainHeightOriginal(worldXZ);
}

#endif // REAL_TERRAIN_HEIGHT_CGINC
