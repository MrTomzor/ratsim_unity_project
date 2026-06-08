// ImpostorCrossBIRP.shader
Shader "Roundy/Vegetation/ImpostorCrossBIRP"
{
    Properties
    {
        _MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
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
        }
        Cull [_Cull]
        ZWrite On
        
        Pass
        {
            AlphaToMask [_AlphaToCoverage]
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ ALPHA_TO_COVERAGE
            #pragma multi_compile_instancing
            #pragma target 2.0
            #pragma dithercrossfade
            
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 screenPos : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed _Cutoff;
            half _AlphaCoverageStrength;
            
            static const half4x4 bayerMatrix = half4x4(
                0.0h, 0.5h, 0.125h, 0.625h,
                0.75h, 0.25h, 0.875h, 0.375h,
                0.1875h, 0.6875h, 0.0625h, 0.5625h,
                0.9375h, 0.4375h, 0.8125h, 0.3125h
            );
            
            UNITY_INSTANCING_BUFFER_START(Props)
                // Add instanced properties here if needed
            UNITY_INSTANCING_BUFFER_END(Props)
            
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.screenPos = ComputeScreenPos(o.pos);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                
                fixed4 col = tex2D(_MainTex, i.uv);
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
                    half2 screenPos = i.screenPos.xy / i.screenPos.w * _ScreenParams.xy * 0.5h;
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
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #pragma target 2.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed _Cutoff;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed alpha = tex2D(_MainTex, i.uv).a * _Color.a;
                clip(alpha - _Cutoff);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
}