// ImpostorCrossURP.shader
Shader "Roundy/Vegetation/ImpostorCrossURP"
{
    Properties
    {
        [MainTexture] _MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
        [MainColor] _Color ("Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0.15,0.85)) = 0.5
        [Toggle(ALPHA_TO_COVERAGE)] _AlphaToCoverage("Alpha To Coverage", Float) = 0
        _AlphaCoverageStrength ("Alpha Coverage Strength", Range(0.1, 2.0)) = 1.0
        
        [Enum(Off,0,Front,1,Back,2)] _Cull ("Culling", Float) = 2
    }
    
    SubShader
    {
        Tags
        {
            "Queue"="AlphaTest"
            "RenderType"="TransparentCutout"
            "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline"
        }
        Cull [_Cull]
        ZWrite On
       
        Pass
        {
            Name "Pass"
            Tags { "LightMode" = "UniversalForwardOnly" }
            AlphaToMask [_AlphaToCoverage]
           
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ ALPHA_TO_COVERAGE
            #pragma multi_compile_instancing
            #pragma target 2.0
           
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
           
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Cutoff;
                half _AlphaCoverageStrength;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
           
            static const half4x4 bayerMatrix = half4x4(
                0.0h, 0.5h, 0.125h, 0.625h,
                0.75h, 0.25h, 0.875h, 0.375h,
                0.1875h, 0.6875h, 0.0625h, 0.5625h,
                0.9375h, 0.4375h, 0.8125h, 0.3125h
            );
           
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
           
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogCoord : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
           
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }
           
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                col.rgb *= _Color.rgb;
                
                // Process alpha differently based on alpha to coverage mode
                #if defined(ALPHA_TO_COVERAGE)
                    half processedAlpha = pow(col.a * _Color.a, _AlphaCoverageStrength);
                    half alpha = (processedAlpha - _Cutoff) / max(fwidth(processedAlpha), 0.0001) + 0.5;
                #else
                    half alpha = col.a * _Color.a;
                    clip(alpha - _Cutoff);
                #endif

                #if defined(LOD_FADE_CROSSFADE)
                    half2 screenPos = input.screenPos.xy / input.screenPos.w * _ScreenParams.xy * 0.5h;
                    uint2 ditherCoord = uint2(fmod(screenPos, 4));
                    half dither = bayerMatrix[ditherCoord.x][ditherCoord.y];
                    half fadeValue = unity_LODFade.x > 0 ?
                        unity_LODFade.x - dither :
                        unity_LODFade.x + dither;
                    
                    #if defined(ALPHA_TO_COVERAGE)
                        alpha *= saturate(fadeValue + 1);
                    #endif
                    
                    if (fadeValue < 0)
                    {
                        discard;
                    }
                #endif

                #if defined(ALPHA_TO_COVERAGE)
                    if (alpha < 0)
                    {
                        discard;
                    }
                #endif
                
                col.a = alpha;
                col.rgb = MixFog(col.rgb, input.fogCoord);
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Cutoff;
                half _AlphaCoverageStrength;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * _Color.a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}