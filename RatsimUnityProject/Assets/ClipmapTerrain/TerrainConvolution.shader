Shader "Hidden/TerrainConvolution"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
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

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 offset = _MainTex_TexelSize.xy;
                
                fixed4 original = tex2D(_MainTex, i.uv);
                
                fixed4 maxColor = original;
                maxColor = max(maxColor, tex2D(_MainTex, i.uv + float2(-offset.x, -offset.y)));
                maxColor = max(maxColor, tex2D(_MainTex, i.uv + float2(0, -offset.y)));
                maxColor = max(maxColor, tex2D(_MainTex, i.uv + float2(offset.x, -offset.y)));
                
                maxColor = max(maxColor, tex2D(_MainTex, i.uv + float2(-offset.x, 0)));
                maxColor = max(maxColor, tex2D(_MainTex, i.uv + float2(offset.x, 0)));
                
                maxColor = max(maxColor, tex2D(_MainTex, i.uv + float2(-offset.x, offset.y)));
                maxColor = max(maxColor, tex2D(_MainTex, i.uv + float2(0, offset.y)));
                maxColor = max(maxColor, tex2D(_MainTex, i.uv + float2(offset.x, offset.y)));
                
                fixed4 convolved = maxColor*maxColor * 0.98;
                
                return max(original, convolved);
            }
            ENDCG
        }
    }
}
