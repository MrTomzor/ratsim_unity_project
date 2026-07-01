Shader "Custom/Terrain4WayBlend" {
    Properties {
        _UVScale ("Global Tiling Scale", Float) = 1.0

        [Header(LayerRed)]
        _LayerR ("Albedo R", 2D) = "white" {}
        [Normal] _NormalR ("Normal R", 2D) = "bump" {}
        _GlossR ("Smoothness R", Range(0,1)) = 0.1
        _MetalR ("Metallic R", Range(0,1)) = 0.0
        _OcclusionR ("Occlusion R", 2D) = "white" {}
        _OcclusionStrengthR ("Occlusion Strength R", Range(0,1)) = 1.0

        [Header(LayerGreen)]
        _LayerG ("Albedo G", 2D) = "white" {}
        [Normal] _NormalG ("Normal G", 2D) = "bump" {}
        _GlossG ("Smoothness G", Range(0,1)) = 0.1
        _MetalG ("Metallic G", Range(0,1)) = 0.0
        _OcclusionG ("Occlusion G", 2D) = "white" {}
        _OcclusionStrengthG ("Occlusion Strength G", Range(0,1)) = 1.0

        [Header(LayerBlue)]
        _LayerB ("Albedo B", 2D) = "white" {}
        [Normal] _NormalB ("Normal B", 2D) = "bump" {}
        _GlossB ("Smoothness B", Range(0,1)) = 0.2
        _MetalB ("Metallic B", Range(0,1)) = 0.0
        _OcclusionB ("Occlusion B", 2D) = "white" {}
        _OcclusionStrengthB ("Occlusion Strength B", Range(0,1)) = 1.0

        [Header(LayerAlpha)]
        _LayerA ("Albedo A", 2D) = "white" {}
        [Normal] _NormalA ("Normal A", 2D) = "bump" {}
        _GlossA ("Smoothness A", Range(0,1)) = 0.5
        _MetalA ("Metallic A", Range(0,1)) = 0.0
        _OcclusionA ("Occlusion A", 2D) = "white" {}
        _OcclusionStrengthA ("Occlusion Strength A", Range(0,1)) = 1.0
    }
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0

        // Textures
        sampler2D _LayerR, _LayerG, _LayerB, _LayerA;
        sampler2D _NormalR, _NormalG, _NormalB, _NormalA;
        sampler2D _OcclusionR, _OcclusionG, _OcclusionB, _OcclusionA;
        
        // Material Properties
        half _GlossR, _GlossG, _GlossB, _GlossA;
        half _MetalR, _MetalG, _MetalB, _MetalA;
        half _OcclusionStrengthR, _OcclusionStrengthG, _OcclusionStrengthB, _OcclusionStrengthA;
        float _UVScale;

        struct Input {
            float2 uv_LayerR; // Reused for all layers since tiling is global
            float4 color : COLOR; // Vertex Color Weights
            float heightRatio;
        };
        
        #include "Assets/ClipmapTerrain/ClipmapDisplacement.cginc"

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            DisplaceClipmapVertex(v, o.heightRatio);;
        }

        void surf (Input IN, inout SurfaceOutputStandard o) {
            float2 tiledUV = IN.uv_LayerR * _UVScale;
            float4 weights = IN.color;

            // 1. Normalize weights safely to avoid rendering artifacts
            float totalWeight = weights.r + weights.g + weights.b + weights.a;
            if (totalWeight > 0.0) {
                weights /= totalWeight;
            } else {
                weights = float4(1, 0, 0, 0);
            }

            // 2. Sample and Blend Albedo Colors
            fixed4 colR = tex2D(_LayerR, tiledUV);
            fixed4 colG = tex2D(_LayerG, tiledUV);
            fixed4 colB = tex2D(_LayerB, tiledUV);
            fixed4 colA = tex2D(_LayerA, tiledUV);
            
            fixed3 finalAlbedo = (colR.rgb * weights.r) + 
                                 (colG.rgb * weights.g) + 
                                 (colB.rgb * weights.b) + 
                                 (colA.rgb * weights.a);

            // 3. Sample and Unpack Tangent Space Normals
            fixed3 normR = UnpackNormal(tex2D(_NormalR, tiledUV));
            fixed3 normG = UnpackNormal(tex2D(_NormalG, tiledUV));
            fixed3 normB = UnpackNormal(tex2D(_NormalB, tiledUV));
            fixed3 normA = UnpackNormal(tex2D(_NormalA, tiledUV));

            // Blend the normal vectors linearly based on weights
            fixed3 finalNormal = (normR * weights.r) + 
                                 (normG * weights.g) + 
                                 (normB * weights.b) + 
                                 (normA * weights.a);

            // 4. Blend Numerical Properties (Smoothness, Metallic, Occlusion)
            half finalSmoothness = (_GlossR * weights.r) + 
                                   (_GlossG * weights.g) + 
                                   (_GlossB * weights.b) + 
                                   (_GlossA * weights.a);

            half finalMetallic = (_MetalR * weights.r) + 
                                 (_MetalG * weights.g) + 
                                 (_MetalB * weights.b) + 
                                 (_MetalA * weights.a);

            half aoR = lerp(1.0, tex2D(_OcclusionR, tiledUV).g, _OcclusionStrengthR);
            half aoG = lerp(1.0, tex2D(_OcclusionG, tiledUV).g, _OcclusionStrengthG);
            half aoB = lerp(1.0, tex2D(_OcclusionB, tiledUV).g, _OcclusionStrengthB);
            half aoA = lerp(1.0, tex2D(_OcclusionA, tiledUV).g, _OcclusionStrengthA);

            half finalOcclusion = (aoR * weights.r) + 
                                  (aoG * weights.g) + 
                                  (aoB * weights.b) + 
                                  (aoA * weights.a);

            // 5. Output to the Standard Lighting Model
            o.Albedo = finalAlbedo;
            o.Normal = normalize(finalNormal); // Normalizing fixes math stretching at transitions
            o.Metallic = finalMetallic;
            o.Smoothness = finalSmoothness;
            o.Occlusion = finalOcclusion;
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}