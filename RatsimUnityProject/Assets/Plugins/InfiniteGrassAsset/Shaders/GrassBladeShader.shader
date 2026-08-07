Shader "InfiniteGrassAsset/GrassBladeShader"
{
    
    Properties
    {
        _BaseColorTextureArray("BaseColor Texture Array", 2DArray) = "" {}
        _TextureCount("Texture Count", Float) = 1
        _AlphaCutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        _ColorA("ColorA", Color) = (0,0,0,1)
        _ColorB("ColorB", Color) = (1,1,1,1)
        _AOColor("AO Color", Color) = (0.5,0.5,0.5)

        [Header(Grass Shape)][Space]
        _GrassScale("Grass Scale", Float) = 1
        _GrassScaleRandomness("Grass Scale Randomness", Range(0, 1)) = 0.25
        _DistanceScaleMultiplier("Distance Scale Multiplier", Float) = 1.0

        [Header(Wind)][Space]
        _WindTexture("Wind Texture", 2D) = "white" {}
        _WindScroll("Wind Scroll", Vector) = (1, 1, 0, 0)
        _WindStrength("Wind Strength", Float) = 1

        [Header(Lighting)][Space]
        _RandomNormal("Random Normal", Range(0, 1)) = 0.1
        [Toggle] _ReceiveShadows("Receive Shadows", Float) = 1
        _ShadowAmbientDarkness("Shadow Ambient Darkness", Range(0, 1)) = 0.5
        [Header(Rendering Options)][Space]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode (0=Off, 2=Back)", Float) = 0
        _EdgeCullThreshold("Edge Culling (0=Off)", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "Transparent" }

        Pass
        {
            Cull [_Cull]
            ZWrite On
            ZTest Less
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 albedo : COLOR0;
                half3 normal : TEXCOORD0;
                half3 viewDir : TEXCOORD3;
                half mask : TEXCOORD4;
                half heightY : TEXCOORD5;
                float3 worldPos : TEXCOORD1;
                float2 meshUV : TEXCOORD8;
                float texIndex : TEXCOORD9;
                UNITY_FOG_COORDS(6)
                UNITY_SHADOW_COORDS(7)
            };

            half3 _ColorA;
            half3 _ColorB;
            float4 _BaseColorTextureArray_ST;
            half3 _AOColor;

            float _GrassScale;
            float _GrassScaleRandomness;
            float _DistanceScaleMultiplier;

            float _MeshHeight; // Set from C# based on mesh bounds

            float4 _WindTexture_ST;
            float _WindStrength;
            float2 _WindScroll;

            half _RandomNormal;
            float _ReceiveShadows;
            float _ShadowAmbientDarkness;
            float _AlphaCutoff;
            float _EdgeCullThreshold;

            float2 _CenterPos;

            float _DrawDistance;
            float _TextureUpdateThreshold;

            StructuredBuffer<float3> _GrassPositions;

            UNITY_DECLARE_TEX2DARRAY(_BaseColorTextureArray);
            float _TextureCount;
            float _CumulativeTextureWeights[32];
            sampler2D _WindTexture;

            sampler2D _GrassColorRT;
            sampler2D _GrassSlopeRT;

            uint murmurHash3(float input) {
                uint h = abs(input);
                h ^= h >> 16;
                h *= 0x85ebca6b;
                h ^= h >> 13;
                h *= 0xc2b2ae3d;
                h ^= h >> 16;
                return h;
            }

            float random(float input) {
                return murmurHash3(input) / 4294967295.0;
            }

            float srandom(float input) {
                return (murmurHash3(input) / 4294967295.0) * 2 - 1;
            }

            float Remap(float In, float2 InMinMax, float2 OutMinMax)
            {
                return OutMinMax.x + (In - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
            }

            half3 CalculateLighting(half3 albedo, half3 N, half3 V, half mask, half heightY, half atten)
            {
                // Ambient (Spherical Harmonics + Global Ambient) shadowed occlusion
                half ambientDarken = lerp(1.0 - _ShadowAmbientDarkness, 1.0, atten);
                half3 ambient = ShadeSH9(half4(N, 1.0)) * albedo * ambientDarken;

                // Main Direct Light Direction
                half3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                half3 lightColor = _LightColor0.rgb;

                // Specular calculations
                half3 H = normalize(lightDir + V);
                half directDiffuse = dot(N, lightDir) * 0.5 + 0.5;

                float directSpecular = saturate(dot(N, H));
                directSpecular = pow(directSpecular, 16.0); // Pow to the 16th is identical to 4 iterative squares
                directSpecular *= heightY * 0.12;

                // Attenuation combined with light color
                half3 lightingColor = lightColor * atten;

                half3 direct = (albedo * directDiffuse + directSpecular * (1.0 - mask)) * lightingColor;

                return ambient + direct;
            }

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);

                // Fetch procedural position from structured buffer
                float3 pivot = _GrassPositions[instanceID];

                // Calculate UV for the prepass data textures
                float2 uv = (pivot.xz - _CenterPos) / (_DrawDistance + _TextureUpdateThreshold);
                uv = uv * 0.5 + 0.5;

                // Normalized height factor from mesh vertex position (0 at base, 1 at tip)
                float heightFactor = saturate(v.vertex.y / _MeshHeight);

                // Random scale per instance
                float scale = _GrassScale * (1.0 - random(pivot.x * 950.0 + pivot.z * 10.0) * _GrassScaleRandomness);

                // Scale based on distance from camera
                float distToCamera = length(_WorldSpaceCameraPos - pivot);
                float distRatio = saturate(distToCamera / _DrawDistance);
                scale *= lerp(1.0, _DistanceScaleMultiplier, distRatio);

                // Random Y-axis rotation per instance
                float rand1 = random(pivot.x * 391.0 + pivot.z * 10.0);
                float angle = rand1 * 6.283185; // 0 to 2*PI
                float cosA = cos(angle);
                float sinA = sin(angle);

                // Rotate vertex position around Y axis
                float3 rotatedPos = float3(
                    v.vertex.x * cosA - v.vertex.z * sinA,
                    v.vertex.y,
                    v.vertex.x * sinA + v.vertex.z * cosA
                );

                // Scale the mesh
                rotatedPos *= scale;

                // Sample and reconstruct direction from terrain/trampling slope map
                float4 slope = tex2Dlod(_GrassSlopeRT, float4(uv, 0.0, 0.0));
                float xSlope = slope.r * 2.0 - 1.0;
                float zSlope = slope.g * 2.0 - 1.0;

                float3 slopeDirection = normalize(float3(xSlope, 1.0 - (max(abs(xSlope), abs(zSlope)) * 0.5), zSlope));
                float3 bendDirection = normalize(lerp(float3(0.0, 1.0, 0.0), slopeDirection, slope.a));

                // Wind animation logic
                float4 windUV = float4(pivot.xz * _WindTexture_ST.xy + _WindScroll * _Time.y, 0.0, 0.0);
                half3 windTex = tex2Dlod(_WindTexture, windUV);
                float2 wind = (windTex.rg * 2.0 - 1.0) * _WindStrength * (1.0 - slope.a);

                float randomVertFactor = random(v.vertex.x * 123.0 + v.vertex.y * 456.0 + pivot.x * 789.0);
                // Apply wind and slope bending based on height
                rotatedPos.xz += wind * randomVertFactor * heightFactor;
                rotatedPos.xz += (bendDirection.xz - float2(0, 0)) * slope.a * heightFactor;

                float3 positionWS = rotatedPos + pivot;

                // Rotate normal by the same Y-axis rotation
                float3 rotatedNormal = float3(
                    v.normal.x * cosA - v.normal.z * sinA,
                    v.normal.y,
                    v.normal.x * sinA + v.normal.z * cosA
                );

                // Transform to clip space
                OUT.pos = UnityWorldToClipPos(positionWS);

                // Tint and AO based on height (texture sampled per-pixel in fragment shader)
                half3 tintColor = lerp(_ColorA, _ColorB, heightFactor);
                half3 albedo = lerp(_AOColor, tintColor, heightFactor);

                float4 colorRTVal = tex2Dlod(_GrassColorRT, float4(uv, 0.0, 0.0));
                albedo = lerp(albedo, colorRTVal.rgb, colorRTVal.a);

                // Normal vector calculation — use mesh normals with randomization
                half3 N = normalize(rotatedNormal + _RandomNormal * half3(srandom(pivot.x * 314.0 + pivot.z * 10.0), 0.0, srandom(pivot.z * 677.0 + pivot.x * 10.0)));
                half3 V = normalize(_WorldSpaceCameraPos - positionWS);

                // Output properties to fragment shader for per-pixel lighting
                OUT.albedo = albedo;
                OUT.normal = N;
                OUT.viewDir = V;
                OUT.mask = colorRTVal.a;
                OUT.heightY = heightFactor;
                OUT.worldPos = positionWS;
                OUT.meshUV = v.uv;
                
                float randVal = random(pivot.x * 219.0 + pivot.z * 133.0);
                float texIndex = 0;
                for (int j = 0; j < (int)_TextureCount && j < 32; j++) {
                    if (randVal <= _CumulativeTextureWeights[j]) {
                        texIndex = j;
                        break;
                    }
                }
                OUT.texIndex = texIndex;

                // Set input vertex to pivot position and temporarily override OUT.pos with pivot clip-space position.
                // This forces standard and screen-space shadows to evaluate at the pivot coordinate instead of per-pixel.
                float4 actualPos = OUT.pos;
                OUT.pos = UnityWorldToClipPos(pivot);
                v.vertex = float4(pivot, 1.0);
                
                // (Manual shadow sampling used)
                
                // Restore the actual clip-space position for rendering the geometry in the correct spot
                OUT.pos = actualPos;

                UNITY_TRANSFER_FOG(OUT, OUT.pos);

                return OUT;
            }

            UNITY_DECLARE_SHADOWMAP(_GlobalShadowMap);

            fixed4 frag(v2f i, float facing : VFACE) : SV_Target
            {
                // Manual shadow cascade sampling (Option A)
                float3 wpos = i.worldPos;
                float3 fromCenter0 = wpos - unity_ShadowSplitSpheres[0].xyz;
                float3 fromCenter1 = wpos - unity_ShadowSplitSpheres[1].xyz;
                float3 fromCenter2 = wpos - unity_ShadowSplitSpheres[2].xyz;
                float3 fromCenter3 = wpos - unity_ShadowSplitSpheres[3].xyz;
                
                float4 distances2 = float4(dot(fromCenter0, fromCenter0), 
                                           dot(fromCenter1, fromCenter1), 
                                           dot(fromCenter2, fromCenter2), 
                                           dot(fromCenter3, fromCenter3));
                                           
                float4 weights = distances2 < float4(unity_ShadowSplitSpheres[0].w, 
                                                     unity_ShadowSplitSpheres[1].w, 
                                                     unity_ShadowSplitSpheres[2].w, 
                                                     unity_ShadowSplitSpheres[3].w);
                                                     
                weights.yzw = saturate(weights.yzw - weights.xxx);
                weights.zw = saturate(weights.zw - weights.yyy);
                weights.w = saturate(weights.w - weights.zzz);

                float4 shadowCoord = mul(unity_WorldToShadow[0], float4(wpos, 1.0)) * weights.x +
                                     mul(unity_WorldToShadow[1], float4(wpos, 1.0)) * weights.y +
                                     mul(unity_WorldToShadow[2], float4(wpos, 1.0)) * weights.z +
                                     mul(unity_WorldToShadow[3], float4(wpos, 1.0)) * weights.w;

                half shadowAtten = UNITY_SAMPLE_SHADOW(_GlobalShadowMap, shadowCoord.xyz);
                
                // Force fully lit if outside the shadow map cascade radius
                half inCascade = saturate(dot(weights, float4(1.0, 1.0, 1.0, 1.0)));
                shadowAtten = lerp(1.0, shadowAtten, inCascade);

                half atten = lerp(1.0, shadowAtten, _ReceiveShadows);

                half3 N = normalize(i.normal) * (facing > 0 ? 1.0 : -1.0);
                half3 V = normalize(i.viewDir);

                clip(abs(dot(N, V)) - _EdgeCullThreshold);

                // Sample base color texture array per-pixel using mesh UVs and the instance's texture index
                float3 uvArray = float3(i.meshUV * _BaseColorTextureArray_ST.xy + _BaseColorTextureArray_ST.zw, i.texIndex);
                half4 texSample = UNITY_SAMPLE_TEX2DARRAY(_BaseColorTextureArray, uvArray);

                // Alpha cutout — discard pixels below threshold
                clip(texSample.a - _AlphaCutoff);

                half3 finalAlbedo = i.albedo * texSample.rgb;

                // Compute ambient and direct specular/diffuse lighting
                half3 lighting = CalculateLighting(finalAlbedo, N, V, i.mask, i.heightY, atten);

                UNITY_APPLY_FOG(i.fogCoord, lighting);

                return half4(lighting, 1.0);
            }
            ENDCG
        }
    }
}