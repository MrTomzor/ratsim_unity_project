Shader "Custom/RealTerrain"
{

    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0.5, 0.5, 0.5, 1.0)
        [HideInInspector] _Level ("LOD Level", Float) = 0
        
        [Toggle] _DebugBiomeColors ("Debug: Show Biome Colors", Float) = 0
        _BiomeTiling ("Biome Texture Tiling", Float) = 0.1

        _BiomeTex10 ("Trees (10)", 2D) = "black" {}
        _BiomeSmoothness10 ("Trees (10) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic10 ("Trees (10) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity10 ("Trees (10) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex20 ("Shrubland (20)", 2D) = "black" {}
        _BiomeSmoothness20 ("Shrubland (20) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic20 ("Shrubland (20) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity20 ("Shrubland (20) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex30 ("Grassland (30)", 2D) = "black" {}
        _BiomeSmoothness30 ("Grassland (30) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic30 ("Grassland (30) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity30 ("Grassland (30) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex40 ("Cropland (40)", 2D) = "black" {}
        _BiomeSmoothness40 ("Cropland (40) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic40 ("Cropland (40) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity40 ("Cropland (40) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex50 ("Built-up (50)", 2D) = "black" {}
        _BiomeSmoothness50 ("Built-up (50) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic50 ("Built-up (50) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity50 ("Built-up (50) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex60 ("Bare (60)", 2D) = "black" {}
        _BiomeSmoothness60 ("Bare (60) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic60 ("Bare (60) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity60 ("Bare (60) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex70 ("Snow and Ice (70)", 2D) = "black" {}
        _BiomeSmoothness70 ("Snow and Ice (70) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic70 ("Snow and Ice (70) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity70 ("Snow and Ice (70) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex80 ("Water (80)", 2D) = "black" {}
        _BiomeSmoothness80 ("Water (80) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic80 ("Water (80) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity80 ("Water (80) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex90 ("Wetland (90)", 2D) = "black" {}
        _BiomeSmoothness90 ("Wetland (90) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic90 ("Wetland (90) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity90 ("Wetland (90) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex100 ("Mangroves (100)", 2D) = "black" {}
        _BiomeSmoothness100 ("Mangroves (100) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic100 ("Mangroves (100) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity100 ("Mangroves (100) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex110 ("Moss and Lichen (110)", 2D) = "black" {}
        _BiomeSmoothness110 ("Moss and Lichen (110) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic110 ("Moss and Lichen (110) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity110 ("Moss and Lichen (110) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex120 ("Orchard (120)", 2D) = "black" {}
        _BiomeSmoothness120 ("Orchard (120) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic120 ("Orchard (120) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity120 ("Orchard (120) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex130 ("Empty Orchard (130)", 2D) = "black" {}
        _BiomeSmoothness130 ("Empty Orchard (130) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic130 ("Empty Orchard (130) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity130 ("Empty Orchard (130) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex140 ("Road (140)", 2D) = "black" {}
        _BiomeSmoothness140 ("Road (140) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic140 ("Road (140) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity140 ("Road (140) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex150 ("Path (150)", 2D) = "black" {}
        _BiomeSmoothness150 ("Path (150) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic150 ("Path (150) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity150 ("Path (150) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex160 ("Cropland2 (160)", 2D) = "black" {}
        _BiomeSmoothness160 ("Cropland2 (160) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic160 ("Cropland2 (160) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity160 ("Cropland2 (160) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex170 ("Cropland3 (170)", 2D) = "black" {}
        _BiomeSmoothness170 ("Cropland3 (170) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic170 ("Cropland3 (170) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity170 ("Cropland3 (170) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex180 ("Cropland4 (180)", 2D) = "black" {}
        _BiomeSmoothness180 ("Cropland4 (180) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic180 ("Cropland4 (180) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity180 ("Cropland4 (180) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex190 ("Cropland5 (190)", 2D) = "black" {}
        _BiomeSmoothness190 ("Cropland5 (190) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic190 ("Cropland5 (190) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity190 ("Cropland5 (190) Specular Intensity", Range(0, 1)) = 1.0
        _BiomeTex200 ("WaterEdge (200)", 2D) = "black" {}
        _BiomeSmoothness200 ("WaterEdge (200) Smoothness", Range(0, 1)) = 0.0
        _BiomeMetallic200 ("WaterEdge (200) Metallic", Range(0, 1)) = 0.0
        _BiomeSpecularIntensity200 ("WaterEdge (200) Specular Intensity", Range(0, 1)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        // vertex:vert enables vertex displacement
        #pragma surface surf StandardSpecularCustom fullforwardshadows vertex:vert addshadow

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 4.5

        struct Input
        {
            float3 worldPos;
            float heightRatio;
        };

        fixed4 _ColorLow;
        fixed4 _ColorHigh;
        fixed4 _ShadowColor;

        #include "UnityPBSLighting.cginc"

        inline half4 LightingStandardSpecularCustom(SurfaceOutputStandardSpecular s, half3 viewDir, UnityGI gi)
        {
            return LightingStandardSpecular(s, viewDir, gi);
        }

        inline void LightingStandardSpecularCustom_GI(SurfaceOutputStandardSpecular s, UnityGIInput data, inout UnityGI gi)
        {
            LightingStandardSpecular_GI(s, data, gi);
            fixed3 shadowTint = lerp(_ShadowColor.rgb, fixed3(1, 1, 1), data.atten);
            gi.indirect.diffuse *= shadowTint;
            gi.indirect.specular *= shadowTint;
        }

        #include "RealTerrainDisplacement.cginc"

        sampler2D _GlobalBiomeMap;
        float4 _GlobalBiomeMap_Bounds;

        float _DebugBiomeColors;
        float _BiomeTiling;
        
        UNITY_DECLARE_TEX2D(_BiomeTex10); float _BiomeSmoothness10; float _BiomeMetallic10; float _BiomeSpecularIntensity10;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex20); float _BiomeSmoothness20; float _BiomeMetallic20; float _BiomeSpecularIntensity20;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex30); float _BiomeSmoothness30; float _BiomeMetallic30; float _BiomeSpecularIntensity30;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex40); float _BiomeSmoothness40; float _BiomeMetallic40; float _BiomeSpecularIntensity40;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex50); float _BiomeSmoothness50; float _BiomeMetallic50; float _BiomeSpecularIntensity50;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex60); float _BiomeSmoothness60; float _BiomeMetallic60; float _BiomeSpecularIntensity60;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex70); float _BiomeSmoothness70; float _BiomeMetallic70; float _BiomeSpecularIntensity70;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex80); float _BiomeSmoothness80; float _BiomeMetallic80; float _BiomeSpecularIntensity80;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex90); float _BiomeSmoothness90; float _BiomeMetallic90; float _BiomeSpecularIntensity90;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex100); float _BiomeSmoothness100; float _BiomeMetallic100; float _BiomeSpecularIntensity100;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex110); float _BiomeSmoothness110; float _BiomeMetallic110; float _BiomeSpecularIntensity110;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex120); float _BiomeSmoothness120; float _BiomeMetallic120; float _BiomeSpecularIntensity120;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex130); float _BiomeSmoothness130; float _BiomeMetallic130; float _BiomeSpecularIntensity130;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex140); float _BiomeSmoothness140; float _BiomeMetallic140; float _BiomeSpecularIntensity140;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex150); float _BiomeSmoothness150; float _BiomeMetallic150; float _BiomeSpecularIntensity150;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex160); float _BiomeSmoothness160; float _BiomeMetallic160; float _BiomeSpecularIntensity160;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex170); float _BiomeSmoothness170; float _BiomeMetallic170; float _BiomeSpecularIntensity170;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex180); float _BiomeSmoothness180; float _BiomeMetallic180; float _BiomeSpecularIntensity180;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex190); float _BiomeSmoothness190; float _BiomeMetallic190; float _BiomeSpecularIntensity190;
        UNITY_DECLARE_TEX2D_NOSAMPLER(_BiomeTex200); float _BiomeSmoothness200; float _BiomeMetallic200; float _BiomeSpecularIntensity200;

        void GetBiomeSurfaceProperties(float biomeValue, float2 uv, out float3 albedo, out float metallic, out float smoothness, out float specularIntensity)
        {
            albedo = float3(0.0, 0.0, 0.0); metallic = 0.0; smoothness = 0.0; specularIntensity = 1.0;
            int b = (int)(biomeValue);
            if (b <=  10+1 && b >=  10-1) { albedo = UNITY_SAMPLE_TEX2D(_BiomeTex10, uv).rgb; metallic = _BiomeMetallic10; smoothness = _BiomeSmoothness10; specularIntensity = _BiomeSpecularIntensity10; }
            if (b <=  20+1 && b >=  20-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex20, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic20; smoothness = _BiomeSmoothness20; specularIntensity = _BiomeSpecularIntensity20; }
            if (b <=  30+1 && b >=  30-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex30, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic30; smoothness = _BiomeSmoothness30; specularIntensity = _BiomeSpecularIntensity30; }
            if (b <=  40+1 && b >=  40-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex40, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic40; smoothness = _BiomeSmoothness40; specularIntensity = _BiomeSpecularIntensity40; }
            if (b <=  50+1 && b >=  50-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex50, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic50; smoothness = _BiomeSmoothness50; specularIntensity = _BiomeSpecularIntensity50; }
            if (b <=  60+1 && b >=  60-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex60, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic60; smoothness = _BiomeSmoothness60; specularIntensity = _BiomeSpecularIntensity60; }
            if (b <=  70+1 && b >=  70-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex70, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic70; smoothness = _BiomeSmoothness70; specularIntensity = _BiomeSpecularIntensity70; }
            if (b <=  80+1 && b >=  80-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex80, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic80; smoothness = _BiomeSmoothness80; specularIntensity = _BiomeSpecularIntensity80; }
            if (b <=  90+1 && b >=  90-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex90, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic90; smoothness = _BiomeSmoothness90; specularIntensity = _BiomeSpecularIntensity90; }
            if (b <= 100+1 && b >= 100-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex100, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic100; smoothness = _BiomeSmoothness100; specularIntensity = _BiomeSpecularIntensity100; }
            if (b <= 110+1 && b >= 110-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex110, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic110; smoothness = _BiomeSmoothness110; specularIntensity = _BiomeSpecularIntensity110; }
            if (b <= 120+1 && b >= 120-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex120, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic120; smoothness = _BiomeSmoothness120; specularIntensity = _BiomeSpecularIntensity120; }
            if (b <= 130+1 && b >= 130-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex130, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic130; smoothness = _BiomeSmoothness130; specularIntensity = _BiomeSpecularIntensity130; }
            if (b <= 140+1 && b >= 140-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex140, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic140; smoothness = _BiomeSmoothness140; specularIntensity = _BiomeSpecularIntensity140; }
            if (b <= 150+1 && b >= 150-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex150, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic150; smoothness = _BiomeSmoothness150; specularIntensity = _BiomeSpecularIntensity150; }
            if (b <= 160+1 && b >= 160-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex160, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic160; smoothness = _BiomeSmoothness160; specularIntensity = _BiomeSpecularIntensity160; }
            if (b <= 170+1 && b >= 170-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex170, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic170; smoothness = _BiomeSmoothness170; specularIntensity = _BiomeSpecularIntensity170; }
            if (b <= 180+1 && b >= 180-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex180, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic180; smoothness = _BiomeSmoothness180; specularIntensity = _BiomeSpecularIntensity180; }
            if (b <= 190+1 && b >= 190-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex190, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic190; smoothness = _BiomeSmoothness190; specularIntensity = _BiomeSpecularIntensity190; }
            if (b <= 200+1 && b >= 200-1) { albedo = UNITY_SAMPLE_TEX2D_SAMPLER(_BiomeTex200, _BiomeTex10, uv).rgb; metallic = _BiomeMetallic200; smoothness = _BiomeSmoothness200; specularIntensity = _BiomeSpecularIntensity200; }
        }

        float3 GetBiomeColor(float biomeValue)
        {
            int b = (int)(biomeValue);
            float3 c = float3(0.0, 0.0, 0.0);
            if (b <=  10+1 && b >=  10-1) c = float3(0.0, 0.3921, 0.0); // 10 Trees
            if (b <=  20+1 && b >=  20-1) c = float3(1.0, 0.7333, 0.1333); // 20 Shrubland
            if (b <=  30+1 && b >=  30-1) c = float3(1.0, 1.0, 0.2980); // 30 Grassland
            if (b <=  40+1 && b >=  40-1) c = float3(0.9411, 0.5882, 1.0); // 40 Cropland
            if (b <=  50+1 && b >=  50-1) c = float3(0.9803, 0.0, 0.0); // 50 Built-up
            if (b <=  60+1 && b >=  60-1) c = float3(0.7058, 0.7058, 0.7058); // 60 Bare
            if (b <=  70+1 && b >=  70-1) c = float3(0.9411, 0.9411, 0.9411); // 70 Snow and ice
            if (b <=  80+1 && b >=  80-1) c = float3(0.0, 0.3921, 0.7843); // 80 Water
            if (b <=  90+1 && b >=  90-1) c = float3(0.0, 0.5882, 0.6274); // 90 Wetland
            if (b <= 100+1 && b >= 100-1) c = float3(0.0, 0.8117, 0.4588); // 100 Mangroves
            if (b <= 110+1 && b >= 110-1) c = float3(0.9803, 0.9019, 0.6274); // 110 Moss and lichen
            if (b <= 120+1 && b >= 120-1) c = float3(0.4000, 0.8000, 0.2000); // 120 Orchard
            if (b <= 130+1 && b >= 130-1) c = float3(0.5000, 0.6000, 0.3000); // 130 Empty Orchard
            if (b <= 140+1 && b >= 140-1) c = float3(0.2000, 0.2000, 0.2000); // 140 Road
            if (b <= 150+1 && b >= 150-1) c = float3(0.6000, 0.4000, 0.2000); // 150 Path
            if (b <= 160+1 && b >= 160-1) c = float3(0.8000, 0.5000, 1.0000); // 160 Cropland2
            if (b <= 170+1 && b >= 170-1) c = float3(0.7000, 0.4000, 1.0000); // 170 Cropland3
            if (b <= 180+1 && b >= 180-1) c = float3(0.6000, 0.3000, 1.0000); // 180 Cropland4
            if (b <= 190+1 && b >= 190-1) c = float3(0.5000, 0.2000, 1.0000); // 190 Cropland5
            if (b <= 200+1 && b >= 200-1) c = float3(0.0000, 0.8000, 1.0000); // 200 WaterEdge
            
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

        void surf (Input IN, inout SurfaceOutputStandardSpecular o)
        {
            fixed4 c = lerp(_ColorLow, _ColorHigh, IN.heightRatio);
            float biomeMetallic = 0.0;
            float biomeSmoothness = 0.0;
            float biomeSpecularIntensity = 1.0;

            if (_GlobalBiomeMap_Bounds.z > 0.0)
            {
                float2 center = _GlobalBiomeMap_Bounds.xy;
                float2 size = _GlobalBiomeMap_Bounds.zw;
                float2 uv = (IN.worldPos.xz - center) / size + 0.5;
                
                float biome = tex2D(_GlobalBiomeMap, uv).r * 255.0;
                
                if (_DebugBiomeColors > 0.5)
                {
                    c.rgb = GetBiomeColor(biome);
                }
                else
                {
                    float2 tilingUV = IN.worldPos.xz * _BiomeTiling;
                    GetBiomeSurfaceProperties(biome, tilingUV, c.rgb, biomeMetallic, biomeSmoothness, biomeSpecularIntensity);
                }
            }

            float3 dielectricSpecular = float3(0.04, 0.04, 0.04) * biomeSpecularIntensity;

            o.Albedo = c.rgb * (1.0 - biomeMetallic);
            o.Specular = lerp(dielectricSpecular, c.rgb, biomeMetallic);
            o.Smoothness = biomeSmoothness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
