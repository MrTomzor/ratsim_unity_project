Shader "Hidden/BiomeOverrideShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _CenterUV ("Center UV", Vector) = (0.5, 0.5, 0, 0)
        _RadiusUV ("Radius UV", Vector) = (0.1, 0.1, 0, 0)
        _BiomeValue ("Biome Value", Float) = 0
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

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _CenterUV;
            float4 _RadiusUV;
            float _BiomeValue;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 col = tex2D(_MainTex, i.uv);
                
                // Calculate distance in normalized radius space
                float2 diff = (i.uv - _CenterUV.xy) / _RadiusUV.xy;
                
                if (dot(diff, diff) <= 1.0)
                {
                    col.r = _BiomeValue;
                }
                
                return col;
            }
            ENDCG
        }
    }
}
