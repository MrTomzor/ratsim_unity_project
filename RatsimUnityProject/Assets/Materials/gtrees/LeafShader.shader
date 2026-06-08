Shader "Custom/LeafShader"
{
    Properties
    {
        _Color ("Color (Tint)", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {} // Added Normal Map property
        _BumpScale ("Normal Strength", Range(0, 2)) = 1.0
        
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Glossiness ("Smoothness", Range(0,1)) = 0.1
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "IgnoreProjector"="True" "RenderType"="TransparentCutout" }
        LOD 200
        
        Cull Off // Keeps leaf visible from both sides

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows alphatest:_Cutoff dithercrossfade

        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap; // Added sampler

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_BumpMap; // Added UV coordinates for the normal map
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;
        half _BumpScale;

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Sample our combined Albedo + Alpha texture
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            
            // Unpack and apply the Normal Map
            fixed3 normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
            normal.xy *= _BumpScale; // Apply the strength multiplier
            o.Normal = normalize(normal);

            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a; 
        }
        ENDCG
    }
    FallBack "Transparent/Cutout/Diffuse"
}