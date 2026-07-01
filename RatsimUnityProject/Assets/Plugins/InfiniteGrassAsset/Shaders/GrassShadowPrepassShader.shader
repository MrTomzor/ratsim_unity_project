Shader "InfiniteGrassAsset/GrassShadowPrepassShader"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "LightMode" = "ForwardBase" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            UNITY_DECLARE_SHADOWMAP(_ShadowMapTexture);

            fixed4 frag(v2f i) : SV_Target
            {
                float3 wpos = i.worldPos;

                // Determine which shadow cascade the world position belongs to
                float3 fromCenter0 = wpos.xyz - unity_ShadowSplitSpheres[0].xyz;
                float3 fromCenter1 = wpos.xyz - unity_ShadowSplitSpheres[1].xyz;
                float3 fromCenter2 = wpos.xyz - unity_ShadowSplitSpheres[2].xyz;
                float3 fromCenter3 = wpos.xyz - unity_ShadowSplitSpheres[3].xyz;
                
                float4 distances2 = float4(dot(fromCenter0, fromCenter0), 
                                           dot(fromCenter1, fromCenter1), 
                                           dot(fromCenter2, fromCenter2), 
                                           dot(fromCenter3, fromCenter3));
                                           
                float4 weights = distances2 < float4(unity_ShadowSplitSpheres[0].w, 
                                                     unity_ShadowSplitSpheres[1].w, 
                                                     unity_ShadowSplitSpheres[2].w, 
                                                     unity_ShadowSplitSpheres[3].w);
                                                     
                // Select only the first active cascade
                weights.yzw = saturate(weights.yzw - weights.xxx);
                weights.zw = saturate(weights.zw - weights.yyy);
                weights.w = saturate(weights.w - weights.zzz);

                // Fallback to cascade 3 if outside all bounds
                float4 shadowCoord = mul(unity_WorldToShadow[0], float4(wpos, 1.0)) * weights.x +
                                     mul(unity_WorldToShadow[1], float4(wpos, 1.0)) * weights.y +
                                     mul(unity_WorldToShadow[2], float4(wpos, 1.0)) * weights.z +
                                     mul(unity_WorldToShadow[3], float4(wpos, 1.0)) * weights.w;

                // Sample the raw shadow map
                half shadow = UNITY_SAMPLE_SHADOW(_ShadowMapTexture, shadowCoord.xyz);

                // Return cascade weights as colors (Red = Cascade 0, Green = Cascade 1, Blue = Cascade 2)
                return fixed4(weights.rgb, 1.0);
            }
            ENDCG
        }
    }
}
