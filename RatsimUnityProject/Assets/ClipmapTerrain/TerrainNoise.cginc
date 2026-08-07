#ifndef TERRAIN_NOISE_CGINC
#define TERRAIN_NOISE_CGINC

float _HeightMax;
float _NoiseScale;
float _Pa;
float _Pb;
float _Pc;
float _Pd;
float _Pe;

sampler2D _TerrainTexture1;
float4 _TerrainTexture1_TexelSize;
float4 _TerrainTexture1_Bounds;



sampler2D _TerrainTexture2;
float4 _TerrainTexture2_TexelSize;
float4 _TerrainTexture2_Bounds;

// Deterministic PCG Hash for 2D inputs.
// Replaces the chaotic sin() hash which evaluates differently on CPU vs GPU.
float hash(float2 p)
{
    int2 i = int2(p.x, p.y);
    uint ux = asuint(i.x);
    uint uy = asuint(i.y);
    
    uint seed = ux * 73856093u ^ uy * 19349663u;
    uint state = seed * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    uint result = (word >> 22u) ^ word;
    
    return float(result) / 4294967295.0;
}

// Helper: Converts your 1D hash into a 2D random direction vector
float2 hash_dir(float2 p)
{
    // Get your hash, multiply by 2*PI to get a random angle
    float h = hash(p) * 6.28318530718; 
    return float2(cos(h), sin(h));
}

// Returns float3: x = noise value, y = x-derivative, z = y-derivative
float3 noised(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    
    // Quintic interpolant AND its exact mathematical derivative (du)
    float2 u = f*f*f*(f*(f*6.0-15.0)+10.0);
    float2 du = 30.0*f*f*(f*(f-2.0)+1.0);

    // Hash vectors
    float2 ga = hash_dir(i + float2(0.0,0.0));
    float2 gb = hash_dir(i + float2(1.0,0.0));
    float2 gc = hash_dir(i + float2(0.0,1.0));
    float2 gd = hash_dir(i + float2(1.0,1.0));

    // Dot products
    float va = dot(ga, f - float2(0.0,0.0));
    float vb = dot(gb, f - float2(1.0,0.0));
    float vc = dot(gc, f - float2(0.0,1.0));
    float vd = dot(gd, f - float2(1.0,1.0));

    // 1. Calculate the value
    float value = va + u.x*(vb-va) + u.y*(vc-va) + u.x*u.y*(va-vb-vc+vd);
    
    // 2. Calculate the exact analytical gradient!
    float2 grad = ga + u.x*(gb-ga) + u.y*(gc-ga) + u.x*u.y*(ga-gb-gc+gd) + 
                  du * (u.yx*(va-vb-vc+vd) + float2(vb,vc) - va);

    // Remap value to 0-1 range, and scale gradient to match the remap
    return float3(value * 0.5 + 0.5, grad * 0.5);
}

// Fractional Brownian Motion (fBm)
// Requires _Pa, _Pb, _Pc to be defined globally
float fbm(float2 p)
{
    float f = 0.0;
    float w = 0.5;
    float2 g_acc = float2(0.0,0.0);
    float e = 2.71828182846;
    
    float2x2 m = float2x2( 0.8,  0.6, 
                          -0.6,  0.8);

    for (int i = 0; i < 5; i++)
    {
        // ONE call gets both the noise value AND the exact gradient!
        float3 n = noised(p);
        float val = n.x;     // The height value
        float2 g = n.yz;     // The gradient

        g_acc += g;
        float g_s = length(g_acc);
        
        f += w * 1/(1+g_s*_Pa) * val;
        
        p = mul(m, p) * 2.0;
        w *= 0.5;
    }
    return pow(f*_Pc, _Pb);
}

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

float GetTextureFlattenAmount(float2 worldXZ, sampler2D tex, float4 bounds, float4 texelSize, float innerDist, float outerDist)
{
    float2 center = bounds.xy;
    float size = bounds.z;
    if (size == 0.0) size = 4000.0;
    
    float2 uv = (worldXZ - center) / size + 0.5;
    float dist = SampleBicubicBSpline(tex, texelSize, uv);
    
    if (outerDist <= 0.001) {
        return dist < 0.01 ? 1.0 : 0.0;
    }
    
    // smoothstep(min, max, x) returns 0 if x <= min, 1 if x >= max.
    // Inverting it gives 1 when dist <= innerDist, and 0 when dist >= outerDist.
    return 1.0 - smoothstep(innerDist, outerDist, dist);
}

float smax(float a, float b, float k) 
{
    float fac = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
    return lerp(a, b, fac) + k * fac * (1.0 - fac);
}

float smin(float a, float b, float k) 
{
    float fac = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
    return lerp(b, a, fac) - k * fac * (1.0 - fac);
}


float GetTerrainHeightOriginal(float2 worldXZ)
{

    float noiseScaleScaled = _NoiseScale / 100.0;
    float h = fbm(worldXZ * noiseScaleScaled) * _HeightMax - _Pe;
    h = smax(h, 10.0, 10.0);
    h = smin(h, _HeightMax, _Pd);
    return h;
}

float GetTerrainHeight(float2 worldXZ)
{
    float flattenAmount1 = GetTextureFlattenAmount(worldXZ, _TerrainTexture1, _TerrainTexture1_Bounds, _TerrainTexture1_TexelSize, 5.0, 20.0);
    float flattenAmount2 = GetTextureFlattenAmount(worldXZ, _TerrainTexture2, _TerrainTexture2_Bounds, _TerrainTexture2_TexelSize, 0.0, 100.0);
    float h = GetTerrainHeightOriginal(worldXZ);
    h = lerp(h, 10.0, flattenAmount2);
    h = lerp(h, 0.0, flattenAmount1);
    return h;
}


#endif // TERRAIN_NOISE_CGINC
