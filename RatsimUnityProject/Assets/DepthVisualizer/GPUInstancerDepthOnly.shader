Shader "Custom/GPUInstancerDepthOnly"
{
    Properties
    {
        _BaseColorTextureArray("BaseColor Texture Array", 2DArray) = "" {}
        _TextureCount("Texture Count", Float) = 1
        _AlphaCutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle(_DITHERED_TRANSPARENCY)] _DitherTransparency("Use Dithered Transparency", Float) = 0
        [Toggle(_USE_DITHER_TEXTURE)] _UseDitherTexture("Use Dither Texture", Float) = 0
        _DitherTexture("Dither Texture (Blue Noise)", 2D) = "white" {}
        _DitherTextureScale("Dither Texture Scale", Float) = 64.0
        _DitherMeshUVInfluence("Dither Mesh UV Influence", Float) = 0.0
        _DitherInstanceInfluence("Dither Instance XYZ Influence", Float) = 0.0
        _DitherAnimationSpeed("Dither Animation Speed", Float) = 0.0
        _DitherSolidThreshold("Solid Alpha Threshold", Range(0, 1)) = 1.0
        _GlobalAlphaMultiplier("Global Alpha Multiplier", Float) = 1.0

        [Header(Shape)][Space]
        _BaseScale("Base Scale (X=Width/Depth, Y=Height)", Vector) = (1, 1, 0, 0)
        _InstanceScaleRandomness("Instance Scale Randomness", Range(0, 1)) = 0.25
        _DistanceA("Distance A", Float) = 10.0
        _ScaleMultiplierA("Scale Multiplier at Distance A (X=Width/Depth, Y=Height)", Vector) = (1, 1, 0, 0)
        _DistanceB("Distance B", Float) = 100.0
        _ScaleMultiplierB("Scale Multiplier at Distance B (X=Width/Depth, Y=Height)", Vector) = (1, 1, 0, 0)
        
        [Header(Placement)][Space]
        _TerrainAlignment("Terrain Alignment", Range(0, 1)) = 0.5

        [Header(Wind)][Space]
        _WindTexture("Wind Texture", 2D) = "white" {}
        _WindScroll("Wind Scroll", Vector) = (1, 1, 0, 0)
        _WindStrength("Wind Strength", Float) = 1

        [Header(Rendering Options)][Space]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode (0=Off, 2=Back)", Float) = 0
        [Toggle] _UseBiomeClipping("Pixel Accurate Biome Clipping", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }

        Pass
        {
            ZWrite On 
            ZTest LEqual
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma shader_feature_local _DITHERED_TRANSPARENCY
            #pragma shader_feature_local _USE_DITHER_TEXTURE

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_depth
            {
                float4 pos : SV_POSITION;
                float2 meshUV : TEXCOORD0;
                float texIndex : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float ditherOffset : TEXCOORD3;
            };

            float4 _BaseScale;
            float _InstanceScaleRandomness;
            float _DistanceA;
            float4 _ScaleMultiplierA;
            float _DistanceB;
            float4 _ScaleMultiplierB;
            float _MeshHeight;
            float4 _WindTexture_ST;
            float _WindStrength;
            float2 _WindScroll;
            float _TerrainAlignment;
            float _AlphaCutoff;
            float _DrawDistance;
            float3 _MainCameraPosition;

            float _UseBiomeClipping;
            sampler2D _GlobalBiomeMap;
            float4 _GlobalBiomeMap_Bounds;
            float4 _GlobalBiomeMap_TexelSize;
            int _AllowedBiomes;

            struct InstanceData
            {
                float3 position;
                float3 normal;
                float texIndex;
            };

            StructuredBuffer<InstanceData> _InstancePositions;
            UNITY_DECLARE_TEX2DARRAY(_BaseColorTextureArray);
            float4 _BaseColorTextureArray_ST;
            float _TextureCount;
            sampler2D _WindTexture;
            sampler2D _DitherTexture;
            float _DitherTextureScale;
            float _DitherMeshUVInfluence;
            float _DitherInstanceInfluence;
            float _DitherAnimationSpeed;
            float _DitherSolidThreshold;
            float _GlobalAlphaMultiplier;

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

            v2f_depth vertDepth(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f_depth OUT;
                UNITY_INITIALIZE_OUTPUT(v2f_depth, OUT);
                UNITY_SETUP_INSTANCE_ID(v);

                float3 pivot = _InstancePositions[instanceID].position;
                float3 terrainNormal = _InstancePositions[instanceID].normal;

                float heightFactor = saturate(v.vertex.y / _MeshHeight);
                float randScale = 1.0 - random(pivot.x * 950.0 + pivot.z * 10.0) * _InstanceScaleRandomness;

                float distToCamera = length(_MainCameraPosition - pivot);
                
                float t = saturate((distToCamera - _DistanceA) / max(0.0001, _DistanceB - _DistanceA));
                float2 distScale = lerp(_ScaleMultiplierA.xy, _ScaleMultiplierB.xy, t);

                float2 finalScale = _BaseScale.xy * randScale * distScale;

                float rand1 = random(pivot.x * 391.0 + pivot.z * 10.0);
                float angle = rand1 * 6.283185;
                float cosA = cos(angle);
                float sinA = sin(angle);

                float3 up = normalize(lerp(float3(0, 1, 0), terrainNormal, _TerrainAlignment));
                float3 baseForward = float3(-sinA, 0.0, cosA);
                float3 right = normalize(cross(up, baseForward));
                float3 forward = cross(right, up);

                float3 scaledVertex = v.vertex.xyz;
                scaledVertex.xz *= finalScale.x;
                scaledVertex.y *= finalScale.y;

                float3 rotatedPos = scaledVertex.x * right + scaledVertex.y * up + scaledVertex.z * forward;

                float4 windUV = float4(pivot.xz * _WindTexture_ST.xy + _WindScroll * _Time.y, 0.0, 0.0);
                half3 windTex = tex2Dlod(_WindTexture, windUV);
                float2 wind = (windTex.rg * 2.0 - 1.0) * _WindStrength;

                rotatedPos.xz += wind * heightFactor;

                float3 positionWS = rotatedPos + pivot;
                OUT.worldPos = positionWS;

                OUT.pos = UnityWorldToClipPos(positionWS);

                OUT.texIndex = _InstancePositions[instanceID].texIndex;
                OUT.meshUV = v.uv;
                OUT.ditherOffset = pivot.x + pivot.y + pivot.z;

                return OUT;
            }

            float4 fragDepth(v2f_depth i) : SV_Target
            {
                if (_UseBiomeClipping > 0.5)
                {
                    float2 worldXZ = i.worldPos.xz;
                    float2 center = _GlobalBiomeMap_Bounds.xy;
                    float2 size = _GlobalBiomeMap_Bounds.zw;
                    float2 biomeUV = (worldXZ - center) / size + 0.5;
                    
                    float biomeVal = tex2D(_GlobalBiomeMap, biomeUV).r * 255.0;
                    int biomeIndex = round(biomeVal);
                    
                    int bitIndex = -1;
                    if (biomeIndex == 10) bitIndex = 0;
                    else if (biomeIndex == 20) bitIndex = 1;
                    else if (biomeIndex == 30) bitIndex = 2;
                    else if (biomeIndex == 40) bitIndex = 3;
                    else if (biomeIndex == 50) bitIndex = 4;
                    else if (biomeIndex == 60) bitIndex = 5;
                    else if (biomeIndex == 70) bitIndex = 6;
                    else if (biomeIndex == 80) bitIndex = 7;
                    else if (biomeIndex == 90) bitIndex = 8;
                    else if (biomeIndex == 100) bitIndex = 9;
                    else if (biomeIndex == 110) bitIndex = 10;
                    else if (biomeIndex == 120) bitIndex = 11;
                    else if (biomeIndex == 130) bitIndex = 12;
                    else if (biomeIndex == 140) bitIndex = 13;
                    else if (biomeIndex == 150) bitIndex = 14;
                    else if (biomeIndex == 160) bitIndex = 15;
                    else if (biomeIndex == 170) bitIndex = 16;
                    else if (biomeIndex == 180) bitIndex = 17;
                    else if (biomeIndex == 190) bitIndex = 18;
                    else if (biomeIndex == 200) bitIndex = 19;

                    bool inMask = false;
                    if (bitIndex >= 0) {
                        inMask = (_AllowedBiomes & (1 << bitIndex)) != 0;
                    }
                    clip(inMask ? 1.0 : -1.0);
                }

                float3 uvArray = float3(i.meshUV * _BaseColorTextureArray_ST.xy + _BaseColorTextureArray_ST.zw, i.texIndex);
                half4 texSample = UNITY_SAMPLE_TEX2DARRAY(_BaseColorTextureArray, uvArray);
                texSample.a = saturate(texSample.a * _GlobalAlphaMultiplier);
                
                #if _DITHERED_TRANSPARENCY
                    float2 ditherPixel = i.pos.xy;
                    ditherPixel += i.meshUV * _DitherMeshUVInfluence * _DitherTextureScale;
                    ditherPixel += i.ditherOffset * _DitherInstanceInfluence * _DitherTextureScale;
                    ditherPixel += floor(_Time.y * _DitherAnimationSpeed);

                    #if _USE_DITHER_TEXTURE
                        float2 ditherUV = ditherPixel / _DitherTextureScale;
                        float ditherThreshold = tex2D(_DitherTexture, ditherUV).r;
                    #else
                        float4x4 ditherMatrix = float4x4(
                            1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
                            13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
                            4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
                            16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
                        );
                        uint x = (uint)abs(ditherPixel.x) % 4;
                        uint y = (uint)abs(ditherPixel.y) % 4;
                        float ditherThreshold = ditherMatrix[x][y];
                    #endif
                    if (texSample.a <= _DitherSolidThreshold) {
                        clip(texSample.a - ditherThreshold);
                    }
                #else
                    clip(texSample.a - _AlphaCutoff);
                #endif

                return float4(i.pos.z, 0, 0, 1);
            }
            ENDCG
        }
    }
}
