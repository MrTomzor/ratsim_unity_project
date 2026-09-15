Shader "Hidden/GPUInstancerDepthCombine"
{
    Properties
    {
        _MainTex ("Instancer Depth", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;

            float4 frag (v2f i) : SV_Target
            {
                float sceneDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                float gpuDepth = tex2D(_MainTex, i.uv).r;

                #if UNITY_REVERSED_Z
                    float combined = max(sceneDepth, gpuDepth);
                #else
                    float combined = min(sceneDepth, gpuDepth);
                #endif

                return combined;
            }
            ENDCG
        }
    }
}

