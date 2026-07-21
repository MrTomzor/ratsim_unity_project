Shader "Hidden/Custom/SkyboxFog"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertDefault
            #pragma fragment Frag
            #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"

            TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
            TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);

            samplerCUBE _Skybox;
            float _UseSkybox;
            
            float _StartDistance;
            float _EndDistance;
            float _MaxDensity;

            float4x4 _InverseViewProj;
            float4 _CameraPos;

            // Properly declare Unity's global reflection probe for D3D11
            TextureCube unity_SpecCube0;
            SamplerState samplerunity_SpecCube0;
            float4 unity_SpecCube0_HDR;

            float3 CustomDecodeHDR(float4 data, float4 decodeInstructions)
            {
                float alpha = decodeInstructions.w * (data.a - 1.0) + 1.0;
                return (decodeInstructions.x * pow(abs(alpha), decodeInstructions.y)) * data.rgb;
            }

            float4 Frag(VaryingsDefault i) : SV_Target
            {
                // Sample original color
                float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.texcoord);
                
                // Sample depth
                float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.texcoord);

                // Calculate NDC position to reconstruct world position
                #if defined(UNITY_REVERSED_Z)
                float z = depth;
                #else
                float z = depth * 2.0 - 1.0;
                #endif

                float4 ndc = float4(i.texcoord.x * 2.0 - 1.0, i.texcoord.y * 2.0 - 1.0, z, 1.0);
                float4 worldPos = mul(_InverseViewProj, ndc);
                worldPos /= worldPos.w;

                // Unity's standard fog is radial (distance from camera to pixel in world space)
                float radialDistance = length(worldPos.xyz - _CameraPos.xyz);
                float3 viewDir = (worldPos.xyz - _CameraPos.xyz) / max(0.0001, radialDistance);

                // Determine sky color based on view direction
                float3 skyColor = float3(0, 0, 0);
                if (_UseSkybox > 0.5)
                {
                    skyColor = texCUBE(_Skybox, viewDir).rgb;
                }
                else
                {
                    // Fallback to Unity's global environment reflection probe
                    float4 envSample = unity_SpecCube0.SampleLevel(samplerunity_SpecCube0, viewDir, 0);
                    skyColor = CustomDecodeHDR(envSample, unity_SpecCube0_HDR);
                }

                // Calculate linear fog factor exactly like Unity's built-in linear fog
                float fogFactor = saturate((radialDistance - _StartDistance) / max(0.001, _EndDistance - _StartDistance));
                fogFactor *= _MaxDensity;

                // Do not apply fog to the actual skybox (pixels at the far clipping plane)
                // In Unity, depth texture clear value is 0 for REVERSED_Z and 1 for standard
                #if defined(UNITY_REVERSED_Z)
                if (depth < 0.000001) fogFactor = 0.0;
                #else
                if (depth > 0.999999) fogFactor = 0.0;
                #endif

                // Blend original color with sky color based on fog factor
                color.rgb = lerp(color.rgb, skyColor, fogFactor);

                return color;
            }
            ENDHLSL
        }
    }
}
