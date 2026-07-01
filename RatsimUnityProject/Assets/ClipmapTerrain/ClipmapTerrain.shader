Shader "Custom/ClipmapTerrain"
{

    Properties
    {
        _ColorLow ("Color Low", Color) = (0.2, 0.4, 0.1, 1)
        _ColorHigh ("Color High", Color) = (0.8, 0.8, 0.8, 1)
        _HeightMin ("Height Min", Float) = 0
        _HeightMax ("Height Max", Float) = 50
        _NoiseScale ("Noise Scale", Float) = 1.0
        _Pa ("Pa", Float) = 1.0
        _Pb ("Pb", Float) = 1.0
        _Pc ("Pc", Float) = 1.0
        _Pd ("Pd", Float) = 1.0
        _Pe ("Pe", Float) = 1.0
        
        [HideInInspector] _Level ("LOD Level", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        // vertex:vert enables vertex displacement
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        struct Input
        {
            float3 worldPos;
            float heightRatio;
        };

        fixed4 _ColorLow;
        fixed4 _ColorHigh;

        #include "ClipmapDisplacement.cginc"

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            DisplaceClipmapVertex(v, o.heightRatio);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = lerp(_ColorLow, _ColorHigh, IN.heightRatio);
            o.Albedo = c.rgb;
            o.Metallic = 0.0;
            o.Smoothness = 0.1;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
