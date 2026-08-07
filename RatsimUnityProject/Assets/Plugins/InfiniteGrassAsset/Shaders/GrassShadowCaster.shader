Shader "InfiniteGrassAsset/GrassShadowCaster"
{
    Properties
    {
        _BaseColorTextureArray("BaseColor Texture Array", 2DArray) = "" {}
        _TextureCount("Texture Count", Float) = 1
        _AlphaCutoff("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Grass Shape)][Space]
        _GrassScale("Grass Scale", Float) = 1
        _GrassScaleRandomness("Grass Scale Randomness", Range(0, 1)) = 0.25
        _DistanceScaleMultiplier("Distance Scale Multiplier", Float) = 1.0

        [Header(Wind)][Space]
        _WindTexture("Wind Texture", 2D) = "white" {}
        _WindScroll("Wind Scroll", Vector) = (1, 1, 0, 0)
        _WindStrength("Wind Strength", Float) = 1

        [Header(Rendering Options)][Space]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode (0=Off, 2=Back)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }

        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma target 4.5
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_shadow
            {
                float4 pos : SV_POSITION;
                float2 meshUV : TEXCOORD0;
                float texIndex : TEXCOORD1;
                #if defined(SHADOWS_CUBE) && !defined(SHADOWS_CUBE_IN_DEPTH_TEX)
                float3 lightVec : TEXCOORD2;
                #endif
            };

            float _GrassScale;
            float _GrassScaleRandomness;
            float _DistanceScaleMultiplier;
            float _MeshHeight;
            float4 _WindTexture_ST;
            float _WindStrength;
            float2 _WindScroll;
            float _AlphaCutoff;
            float2 _CenterPos;
            float _DrawDistance;
            float _TextureUpdateThreshold;
            float3 _MainCameraPosition;

            StructuredBuffer<float3> _GrassPositions;
            UNITY_DECLARE_TEX2DARRAY(_BaseColorTextureArray);
            float4 _BaseColorTextureArray_ST;
            float _TextureCount;
            float _CumulativeTextureWeights[32];
            sampler2D _WindTexture;
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

            v2f_shadow vertShadow(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f_shadow OUT;
                UNITY_SETUP_INSTANCE_ID(v);

                float3 pivot = _GrassPositions[instanceID];
                float2 uv = (pivot.xz - _CenterPos) / (_DrawDistance + _TextureUpdateThreshold);
                uv = uv * 0.5 + 0.5;

                float heightFactor = saturate(v.vertex.y / _MeshHeight);
                float scale = _GrassScale * (1.0 - random(pivot.x * 950.0 + pivot.z * 10.0) * _GrassScaleRandomness);

                float distToCamera = length(_MainCameraPosition - pivot);
                float distRatio = saturate(distToCamera / _DrawDistance);
                scale *= lerp(1.0, _DistanceScaleMultiplier, distRatio);

                float rand1 = random(pivot.x * 391.0 + pivot.z * 10.0);
                float angle = rand1 * 6.283185;
                float cosA = cos(angle);
                float sinA = sin(angle);

                float3 rotatedPos = float3(
                    v.vertex.x * cosA - v.vertex.z * sinA,
                    v.vertex.y,
                    v.vertex.x * sinA + v.vertex.z * cosA
                );

                rotatedPos *= scale;

                float4 slope = tex2Dlod(_GrassSlopeRT, float4(uv, 0.0, 0.0));
                float xSlope = slope.r * 2.0 - 1.0;
                float zSlope = slope.g * 2.0 - 1.0;

                float3 slopeDirection = normalize(float3(xSlope, 1.0 - (max(abs(xSlope), abs(zSlope)) * 0.5), zSlope));
                float3 bendDirection = normalize(lerp(float3(0.0, 1.0, 0.0), slopeDirection, slope.a));

                float4 windUV = float4(pivot.xz * _WindTexture_ST.xy + _WindScroll * _Time.y, 0.0, 0.0);
                half3 windTex = tex2Dlod(_WindTexture, windUV);
                float2 wind = (windTex.rg * 2.0 - 1.0) * _WindStrength * (1.0 - slope.a);

                float randomVertFactor = random(v.vertex.x * 123.0 + v.vertex.y * 456.0 + pivot.x * 789.0);
                rotatedPos.xz += wind * randomVertFactor * heightFactor;
                rotatedPos.xz += (bendDirection.xz - float2(0, 0)) * slope.a * heightFactor;

                float3 positionWS = rotatedPos + pivot;

                // Shadow projection — compute clip pos from world space directly
                // During shadow pass, UNITY_MATRIX_VP is the light's view-projection
                #if defined(SHADOWS_CUBE) && !defined(SHADOWS_CUBE_IN_DEPTH_TEX)
                    // Point light shadows: output world-to-light vector for distance encoding
                    OUT.pos = UnityWorldToClipPos(positionWS);
                    OUT.lightVec = positionWS - _LightPositionRange.xyz;
                #else
                    // Directional light shadows: project + apply depth bias
                    float4 clipPos = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));
                    OUT.pos = UnityApplyLinearShadowBias(clipPos);
                #endif

                float randVal = random(pivot.x * 219.0 + pivot.z * 133.0);
                float texIndex = 0;
                for (int j = 0; j < (int)_TextureCount && j < 32; j++) {
                    if (randVal <= _CumulativeTextureWeights[j]) {
                        texIndex = j;
                        break;
                    }
                }
                OUT.texIndex = texIndex;
                OUT.meshUV = v.uv;

                return OUT;
            }

            fixed4 fragShadow(v2f_shadow i) : SV_Target
            {
                float3 uvArray = float3(i.meshUV * _BaseColorTextureArray_ST.xy + _BaseColorTextureArray_ST.zw, i.texIndex);
                half4 texSample = UNITY_SAMPLE_TEX2DARRAY(_BaseColorTextureArray, uvArray);
                clip(texSample.a - _AlphaCutoff);

                #if defined(SHADOWS_CUBE) && !defined(SHADOWS_CUBE_IN_DEPTH_TEX)
                    // Point light: encode distance from light
                    return UnityEncodeCubeShadowDepth(
                        (length(i.lightVec) + unity_LightShadowBias.x) * _LightPositionRange.w
                    );
                #else
                    return 0;
                #endif
            }
            ENDCG
        }
    }
}
