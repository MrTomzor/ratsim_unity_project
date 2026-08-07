Shader "RealLifeEnvironment/GPUInstancerShader"
{
    Properties
    {
        _BaseColorTextureArray("BaseColor Texture Array", 2DArray) = "" {}
        _TextureCount("Texture Count", Float) = 1
        _AlphaCutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        _ColorA("ColorA", Color) = (0,0,0,1)
        _ColorB("ColorB", Color) = (1,1,1,1)
        _AOColor("AO Color", Color) = (0.5,0.5,0.5)

        [Header(Shape)][Space]
        _BaseScale("Base Scale (X=Width/Depth, Y=Height)", Vector) = (1, 1, 0, 0)
        _InstanceScaleRandomness("Instance Scale Randomness", Range(0, 1)) = 0.25
        _DistanceA("Distance A", Float) = 10.0
        _ScaleMultiplierA("Scale Multiplier at Distance A (X=Width/Depth, Y=Height)", Vector) = (1, 1, 0, 0)
        _DistanceB("Distance B", Float) = 100.0
        _ScaleMultiplierB("Scale Multiplier at Distance B (X=Width/Depth, Y=Height)", Vector) = (1, 1, 0, 0)
        
        [Header(Placement)][Space]
        _TerrainAlignment("Terrain Alignment", Range(0, 1)) = 0.5

        [Header(Water Influence)][Space]
        [Toggle] _EnableWaterInfluence("Enable Water Influence", Float) = 0
        _MinDistFromWater("Min Distance From Water", Float) = 0.0
        _MaxDistFromWater("Max Distance From Water", Float) = 50.0
        _TransitionSmoothness("Transition Smoothness (Units)", Float) = 10.0
        _HueToChangeTo("Hue To Change To", Range(0, 1)) = 0.3
        _SaturationToChangeTo("Saturation To Change To", Range(0, 1)) = 0.8

        [Header(Wind)][Space]
        _WindTexture("Wind Texture", 2D) = "white" {}
        _WindScroll("Wind Scroll", Vector) = (1, 1, 0, 0)
        _WindStrength("Wind Strength", Float) = 1

        [Header(Lighting)][Space]
        _Smoothness("Smoothness", Range(1, 256)) = 16.0
        _SpecularIntensity("Specular Intensity", Range(0, 1)) = 0.12
        _RandomNormal("Random Normal", Range(0, 1)) = 0.1
        [Toggle] _ReceiveShadows("Receive Shadows", Float) = 1
        _ShadowAmbientDarkness("Shadow Ambient Darkness", Range(0, 1)) = 0.5
        
        [Header(Rendering Options)][Space]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode (0=Off, 2=Back)", Float) = 0
        [Toggle] _UseBiomeClipping("Pixel Accurate Biome Clipping", Float) = 0
        _EdgeCullThreshold("Edge Culling (0=Off)", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "Transparent" }

        Pass
        {
            Cull [_Cull]
            ZWrite On
            ZTest Less
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 albedo : COLOR0;
                half3 normal : TEXCOORD0;
                half3 realNormal : TEXCOORD4;
                half3 viewDir : TEXCOORD3;
                half heightY : TEXCOORD5;
                float3 worldPos : TEXCOORD1;
                float2 meshUV : TEXCOORD8;
                float texIndex : TEXCOORD9;
                UNITY_FOG_COORDS(6)
                UNITY_SHADOW_COORDS(7)
            };

            half3 _ColorA;
            half3 _ColorB;
            float4 _BaseColorTextureArray_ST;
            half3 _AOColor;

            float4 _BaseScale;
            float _InstanceScaleRandomness;
            float _DistanceA;
            float4 _ScaleMultiplierA;
            float _DistanceB;
            float4 _ScaleMultiplierB;

            float _MeshHeight;

            float4 _WindTexture_ST;
            float _WindStrength;
            float2 _WindScroll;

            half _Smoothness;
            half _SpecularIntensity;

            half _RandomNormal;
            float _ReceiveShadows;
            float _ShadowAmbientDarkness;
            float _AlphaCutoff;
            float _EdgeCullThreshold;

            float _TerrainAlignment;
            float _DrawDistance;

            float _UseBiomeClipping;
            sampler2D _GlobalBiomeMap;
            float4 _GlobalBiomeMap_Bounds;
            float4 _GlobalBiomeMap_TexelSize;
            int _AllowedBiomes;

            float _EnableWaterInfluence;
            sampler2D _GlobalWaterDistanceMap;
            float _MinDistFromWater;
            float _MaxDistFromWater;
            float _TransitionSmoothness;
            float _HueToChangeTo;
            float _SaturationToChangeTo;

            struct InstanceData
            {
                float3 position;
                float3 normal;
                float texIndex;
            };

            StructuredBuffer<InstanceData> _InstancePositions;

            UNITY_DECLARE_TEX2DARRAY(_BaseColorTextureArray);
            float _TextureCount;
            sampler2D _WindTexture;

            uint murmurHash3(float input) {
                uint h = abs(input);
                h ^= h >> 16;
                h *= 0x85ebca6b;
                h ^= h >> 13;
                h *= 0xc2b2ae3d;
                h ^= h >> 16;
                return h;
            }

            float random(float input) {
                return murmurHash3(input) / 4294967295.0;
            }

            float srandom(float input) {
                return (murmurHash3(input) / 4294967295.0) * 2 - 1;
            }

            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            half3 CalculateLighting(half3 albedo, half3 N, half3 V, half heightY, half atten)
            {
                half ambientDarken = lerp(1.0 - _ShadowAmbientDarkness, 1.0, atten);
                half3 ambient = ShadeSH9(half4(N, 1.0)) * albedo * ambientDarken;

                half3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                half3 lightColor = _LightColor0.rgb;

                half3 H = normalize(lightDir + V);
                half directDiffuse = dot(N, lightDir) * 0.5 + 0.5;

                float directSpecular = saturate(dot(N, H));
                directSpecular = pow(directSpecular, _Smoothness); 
                directSpecular *= heightY * _SpecularIntensity;

                half3 lightingColor = lightColor * atten;
                half3 direct = (albedo * directDiffuse + directSpecular) * lightingColor;

                return ambient + direct;
            }

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f OUT;
                UNITY_INITIALIZE_OUTPUT(v2f, OUT);
                UNITY_SETUP_INSTANCE_ID(v);

                float3 pivot = _InstancePositions[instanceID].position;
                float3 terrainNormal = _InstancePositions[instanceID].normal;

                float heightFactor = saturate(v.vertex.y / _MeshHeight);

                float randScale = 1.0 - random(pivot.x * 950.0 + pivot.z * 10.0) * _InstanceScaleRandomness;

                float distToCamera = length(_WorldSpaceCameraPos - pivot);
                
                float t = saturate((distToCamera - _DistanceA) / max(0.0001, _DistanceB - _DistanceA));
                float2 distScale = lerp(_ScaleMultiplierA.xy, _ScaleMultiplierB.xy, t);

                float2 finalScale = _BaseScale.xy * randScale * distScale;

                float rand1 = random(pivot.x * 391.0 + pivot.z * 10.0);
                float angle = rand1 * 6.283185;
                float cosA = cos(angle);
                float sinA = sin(angle);

                float3 up = normalize(lerp(float3(0, 1, 0), terrainNormal, _TerrainAlignment));
                float3 baseForward = float3(-sinA, 0.0, cosA);
                float3 right = normalize(cross(up, baseForward));
                float3 forward = cross(right, up);

                float3 scaledVertex = v.vertex.xyz;
                scaledVertex.xz *= finalScale.x;
                scaledVertex.y *= finalScale.y;

                float3 rotatedPos = scaledVertex.x * right + scaledVertex.y * up + scaledVertex.z * forward;

                // Wind animation logic
                float4 windUV = float4(pivot.xz * _WindTexture_ST.xy + _WindScroll * _Time.y, 0.0, 0.0);
                half3 windTex = tex2Dlod(_WindTexture, windUV);
                float2 wind = (windTex.rg * 2.0 - 1.0) * _WindStrength;

                rotatedPos.xz += wind * heightFactor;

                float3 positionWS = rotatedPos + pivot;

                float3 rotatedNormal = v.normal.x * right + v.normal.y * up + v.normal.z * forward;

                OUT.pos = UnityWorldToClipPos(positionWS);

                half3 tintColor = lerp(_ColorA, _ColorB, heightFactor);
                half3 ao = lerp(_AOColor, half3(1.0, 1.0, 1.0), heightFactor);
                half3 albedo = tintColor * ao;

                half3 N = normalize(rotatedNormal + _RandomNormal * half3(srandom(pivot.x * 314.0 + pivot.z * 10.0), 0.0, srandom(pivot.z * 677.0 + pivot.x * 10.0)));
                half3 V = normalize(_WorldSpaceCameraPos - positionWS);

                OUT.albedo = albedo;
                OUT.normal = N;
                OUT.realNormal = normalize(rotatedNormal);
                OUT.viewDir = V;
                OUT.heightY = heightFactor;
                OUT.worldPos = positionWS;
                OUT.meshUV = v.uv;
                
                OUT.texIndex = _InstancePositions[instanceID].texIndex;

                float4 actualPos = OUT.pos;
                OUT.pos = UnityWorldToClipPos(pivot);
                v.vertex = float4(pivot, 1.0);
                OUT.pos = actualPos;

                UNITY_TRANSFER_FOG(OUT, OUT.pos);

                return OUT;
            }

            UNITY_DECLARE_SHADOWMAP(_GlobalShadowMap);

            fixed4 frag(v2f i, float facing : VFACE) : SV_Target
            {
                float3 wpos = i.worldPos;

                float2 worldXZ = wpos.xz;
                float2 center = _GlobalBiomeMap_Bounds.xy;
                float2 size = _GlobalBiomeMap_Bounds.zw;
                float2 biomeUV = (worldXZ - center) / size + 0.5;

                if (_UseBiomeClipping > 0.5)
                {
                    float biomeVal = tex2D(_GlobalBiomeMap, biomeUV).r * 255.0;
                    int biomeIndex = round(biomeVal);
                    
                    int bitIndex = -1;
                    if (biomeIndex == 10) bitIndex = 0;
                    else if (biomeIndex == 20) bitIndex = 1;
                    else if (biomeIndex == 30) bitIndex = 2;
                    else if (biomeIndex == 40) bitIndex = 3;
                    else if (biomeIndex == 50) bitIndex = 4;
                    else if (biomeIndex == 60) bitIndex = 5;
                    else if (biomeIndex == 70) bitIndex = 6;
                    else if (biomeIndex == 80) bitIndex = 7;
                    else if (biomeIndex == 90) bitIndex = 8;
                    else if (biomeIndex == 100) bitIndex = 9;
                    else if (biomeIndex == 110) bitIndex = 10;
                    else if (biomeIndex == 120) bitIndex = 11;
                    else if (biomeIndex == 130) bitIndex = 12;
                    else if (biomeIndex == 140) bitIndex = 13;
                    else if (biomeIndex == 150) bitIndex = 14;
                    else if (biomeIndex == 160) bitIndex = 15;
                    else if (biomeIndex == 170) bitIndex = 16;
                    else if (biomeIndex == 180) bitIndex = 17;
                    else if (biomeIndex == 190) bitIndex = 18;
                    else if (biomeIndex == 200) bitIndex = 19;

                    bool inMask = false;
                    if (bitIndex >= 0) {
                        inMask = (_AllowedBiomes & (1 << bitIndex)) != 0;
                    }
                    clip(inMask ? 1.0 : -1.0);
                }

                float3 fromCenter0 = wpos - unity_ShadowSplitSpheres[0].xyz;
                float3 fromCenter1 = wpos - unity_ShadowSplitSpheres[1].xyz;
                float3 fromCenter2 = wpos - unity_ShadowSplitSpheres[2].xyz;
                float3 fromCenter3 = wpos - unity_ShadowSplitSpheres[3].xyz;
                
                float4 distances2 = float4(dot(fromCenter0, fromCenter0), 
                                           dot(fromCenter1, fromCenter1), 
                                           dot(fromCenter2, fromCenter2), 
                                           dot(fromCenter3, fromCenter3));
                                           
                float4 weights = distances2 < float4(unity_ShadowSplitSpheres[0].w, 
                                                     unity_ShadowSplitSpheres[1].w, 
                                                     unity_ShadowSplitSpheres[2].w, 
                                                     unity_ShadowSplitSpheres[3].w);
                                                     
                weights.yzw = saturate(weights.yzw - weights.xxx);
                weights.zw = saturate(weights.zw - weights.yyy);
                weights.w = saturate(weights.w - weights.zzz);

                float4 shadowCoord = mul(unity_WorldToShadow[0], float4(wpos, 1.0)) * weights.x +
                                     mul(unity_WorldToShadow[1], float4(wpos, 1.0)) * weights.y +
                                     mul(unity_WorldToShadow[2], float4(wpos, 1.0)) * weights.z +
                                     mul(unity_WorldToShadow[3], float4(wpos, 1.0)) * weights.w;

                half shadowAtten = UNITY_SAMPLE_SHADOW(_GlobalShadowMap, shadowCoord.xyz);
                
                half inCascade = saturate(dot(weights, float4(1.0, 1.0, 1.0, 1.0)));
                shadowAtten = lerp(1.0, shadowAtten, inCascade);

                half atten = lerp(1.0, shadowAtten, _ReceiveShadows);

                half3 realN = normalize(i.realNormal) * (facing > 0 ? 1.0 : -1.0);
                half3 N = normalize(i.normal) * (facing > 0 ? 1.0 : -1.0);
                half3 V = normalize(i.viewDir);

                clip(abs(dot(realN, V)) - _EdgeCullThreshold);

                float3 uvArray = float3(i.meshUV * _BaseColorTextureArray_ST.xy + _BaseColorTextureArray_ST.zw, i.texIndex);
                half4 texSample = UNITY_SAMPLE_TEX2DARRAY(_BaseColorTextureArray, uvArray);

                clip(texSample.a - _AlphaCutoff);

                half3 finalAlbedo = i.albedo * texSample.rgb;

                // Apply water distance influence
                if (_EnableWaterInfluence > 0.5)
                {
                    float waterDist = tex2D(_GlobalWaterDistanceMap, biomeUV).r;
                    float influence = smoothstep(_MinDistFromWater - _TransitionSmoothness, _MinDistFromWater, waterDist) 
                                    - smoothstep(_MaxDistFromWater, _MaxDistFromWater + _TransitionSmoothness, waterDist);
                    influence = saturate(influence);

                    if (influence > 0.0)
                    {
                        float3 hsv = rgb2hsv(finalAlbedo);
                        hsv.x = lerp(hsv.x, _HueToChangeTo, influence);
                        hsv.y = lerp(hsv.y, _SaturationToChangeTo, influence);
                        finalAlbedo = hsv2rgb(hsv);
                    }
                }

                half3 lighting = CalculateLighting(finalAlbedo, N, V, i.heightY, atten);

                UNITY_APPLY_FOG(i.fogCoord, lighting);

                return half4(lighting, 1.0);
            }
            ENDCG
        }
    }
}
