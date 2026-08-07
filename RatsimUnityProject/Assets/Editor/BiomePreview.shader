Shader "Hidden/BiomePreview"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _LUT ("LUT", 2D) = "black" {}
        _Multiplier ("Multiplier", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

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
            sampler2D _LUT;
            float _Multiplier;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Force Nearest-Neighbor (Point) sampling regardless of the texture's import settings
                float2 pixelPos = floor(i.uv * _MainTex_TexelSize.zw);
                float2 nearestUV = (pixelPos + 0.5) * _MainTex_TexelSize.xy;

                // Sample the biome texture (R channel)
                float val = tex2D(_MainTex, nearestUV).r;
                
                // If it's a normalized 8-bit texture, _Multiplier should be 255.
                // If it's a raw RFloat texture, _Multiplier should be 1.
                float index = round(val * _Multiplier);
                
                // Read from the 256x1 LUT
                // The LUT maps values 0-255 directly. We sample exactly at the pixel centers.
                // index 0 -> 0.5/256, index 255 -> 255.5/256
                float2 lutUV = float2((index + 0.5) / 256.0, 0.5);
                
                return tex2D(_LUT, lutUV);
            }
            ENDCG
        }
    }
}
