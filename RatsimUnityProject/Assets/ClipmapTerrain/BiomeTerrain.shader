Shader "Custom/BiomeTerrain"
{

    Properties
    {
        [HideInInspector] _Level ("LOD Level", Float) = 0
        
        [Toggle] _DebugBiomeColors ("Debug: Show Biome Colors", Float) = 0
        _BiomeTiling ("Biome Texture Tiling", Float) = 0.1

        _BiomeTex10 ("Trees (10)", 2D) = "black" {}
        _BiomeTex20 ("Shrubland (20)", 2D) = "black" {}
        _BiomeTex30 ("Grassland (30)", 2D) = "black" {}
        _BiomeTex40 ("Cropland (40)", 2D) = "black" {}
        _BiomeTex50 ("Built-up (50)", 2D) = "black" {}
        _BiomeTex60 ("Bare (60)", 2D) = "black" {}
        _BiomeTex70 ("Snow and Ice (70)", 2D) = "black" {}
        _BiomeTex80 ("Water (80)", 2D) = "black" {}
        _BiomeTex90 ("Wetland (90)", 2D) = "black" {}
        _BiomeTex100 ("Mangroves (100)", 2D) = "black" {}
        _BiomeTex110 ("Moss and Lichen (110)", 2D) = "black" {}
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

        sampler2D _GlobalBiomeMap;
        float4 _GlobalBiomeMap_Bounds;

        float _DebugBiomeColors;
        float _BiomeTiling;
        
        sampler2D _BiomeTex10;
        sampler2D _BiomeTex20;
        sampler2D _BiomeTex30;
        sampler2D _BiomeTex40;
        sampler2D _BiomeTex50;
        sampler2D _BiomeTex60;
        sampler2D _BiomeTex70;
        sampler2D _BiomeTex80;
        sampler2D _BiomeTex90;
        sampler2D _BiomeTex100;
        sampler2D _BiomeTex110;

        float3 GetBiomeAlbedo(float biomeValue, float2 uv)
        {
            int b = (int)(biomeValue);
            float3 c;
            if      (b <=  10+1 && b >=  10-1) c = tex2D(_BiomeTex10, uv).rgb;
            else if (b <=  20+1 && b >=  20-1) c = tex2D(_BiomeTex20, uv).rgb;
            else if (b <=  30+1 && b >=  30-1) c = tex2D(_BiomeTex30, uv).rgb;
            else if (b <=  40+1 && b >=  40-1) c = tex2D(_BiomeTex40, uv).rgb;
            else if (b <=  50+1 && b >=  50-1) c = tex2D(_BiomeTex50, uv).rgb;
            else if (b <=  60+1 && b >=  60-1) c = tex2D(_BiomeTex60, uv).rgb;
            else if (b <=  70+1 && b >=  70-1) c = tex2D(_BiomeTex70, uv).rgb;
            else if (b <=  80+1 && b >=  80-1) c = tex2D(_BiomeTex80, uv).rgb;
            else if (b <=  90+1 && b >=  90-1) c = tex2D(_BiomeTex90, uv).rgb;
            else if (b <= 100+1 && b >= 100-1) c = tex2D(_BiomeTex100, uv).rgb;
            else if (b <= 110+1 && b >= 110-1) c = tex2D(_BiomeTex110, uv).rgb;
            else c = float3(0.0, 0.0, 0.0);
            
            return c;
        }

        float3 GetBiomeColor(float biomeValue)
        {
            int b = (int)(biomeValue);
            float3 c;
            if      (b <=  10+1 && b >=  10-1) c = float3(0.0, 0.3921, 0.0); // 10 Trees
            else if (b <=  20+1 && b >=  20-1) c = float3(1.0, 0.7333, 0.1333); // 20 Shrubland
            else if (b <=  30+1 && b >=  30-1) c = float3(1.0, 1.0, 0.2980); // 30 Grassland
            else if (b <=  40+1 && b >=  40-1) c = float3(0.9411, 0.5882, 1.0); // 40 Cropland
            else if (b <=  50+1 && b >=  50-1) c = float3(0.9803, 0.0, 0.0); // 50 Built-up
            else if (b <=  60+1 && b >=  60-1) c = float3(0.7058, 0.7058, 0.7058); // 60 Bare
            else if (b <=  70+1 && b >=  70-1) c = float3(0.9411, 0.9411, 0.9411); // 70 Snow and ice
            else if (b <=  80+1 && b >=  80-1) c = float3(0.0, 0.3921, 0.7843); // 80 Water
            else if (b <=  90+1 && b >=  90-1) c = float3(0.0, 0.5882, 0.6274); // 90 Wetland
            else if (b <= 100+1 && b >= 100-1) c = float3(0.0, 0.8117, 0.4588); // 100 Mangroves
            else if (b <= 110+1 && b >= 110-1) c = float3(0.9803, 0.9019, 0.6274); // 110 Moss and lichen
            else c = float3(0.0, 0.0, 0.0);
            
            // Unity stores colors in linear space internally for PBR
            #ifdef UNITY_COLORSPACE_GAMMA
                return c;
            #else
                return GammaToLinearSpace(c);
            #endif
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            DisplaceClipmapVertex(v, o.heightRatio);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = lerp(_ColorLow, _ColorHigh, IN.heightRatio);

            if (_GlobalBiomeMap_Bounds.z > 0.0)
            {
                float2 center = _GlobalBiomeMap_Bounds.xy;
                float2 size = _GlobalBiomeMap_Bounds.zw;
                float2 uv = (IN.worldPos.xz - center) / size + 0.5;
                
                float biome = tex2D(_GlobalBiomeMap, uv).r;
                
                if (_DebugBiomeColors > 0.5)
                {
                    c.rgb = GetBiomeColor(biome);
                }
                else
                {
                    float2 tilingUV = IN.worldPos.xz * _BiomeTiling;
                    c.rgb = GetBiomeAlbedo(biome, tilingUV);
                }
            }

            o.Albedo = c.rgb;
            o.Metallic = 0.0;
            o.Smoothness = 0.0;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
