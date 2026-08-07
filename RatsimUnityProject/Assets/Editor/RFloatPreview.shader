Shader "Hidden/RFloatPreview"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Min ("Min", Float) = 0
        _Max ("Max", Float) = 1
        _ColorMap ("ColorMap", Float) = 0
        _Repeat ("Repeat", Float) = 1
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
            float _Min;
            float _Max;
            float _ColorMap;
            float _Repeat;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float val = tex2D(_MainTex, i.uv).r;
                
                // Avoid division by zero
                float range = max(_Max - _Min, 0.00001);
                float t = saturate((val - _Min) / range);
                
                // Repeat the colormap
                t = frac(t * _Repeat);
                
                if (_ColorMap > 0.5) 
                {
                    // Heatmap (Jet Colormap) approximation
                    float r = saturate(1.5 - abs(4.0 * t - 3.0));
                    float g = saturate(1.5 - abs(4.0 * t - 2.0));
                    float b = saturate(1.5 - abs(4.0 * t - 1.0));
                    return fixed4(r, g, b, 1.0);
                }
                
                // Default Grayscale
                return fixed4(t, t, t, 1.0);
            }
            ENDCG
        }
    }
}
