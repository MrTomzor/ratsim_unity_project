Shader "InfiniteGrass/GrassBladeShader"
{
    
    Properties
    {
        [MainTexture] _BaseColorTexture("BaseColor Texture", 2D) = "white" {}
        _ColorA("ColorA", Color) = (0,0,0,1)
        _ColorB("ColorB", Color) = (1,1,1,1)
        _AOColor("AO Color", Color) = (0.5,0.5,0.5)

        [Header(Grass Shape)][Space]
        _GrassWidth("Grass Width", Float) = 1
        _GrassHeight("Grass Height", Float) = 1
        _GrassWidthRandomness("Grass Width Randomness", Range(0, 1)) = 0.25
        _GrassHeightRandomness("Grass Height Randomness", Range(0, 1)) = 0.5

        _GrassCurving("Grass Curving", Float) = 0.1
        [Space]
        _ExpandDistantGrassWidth("Expand Distant Grass Width", Float) = 1
        _ExpandDistantGrassRange("Expand Distant Grass Range", Vector) = (50, 200, 0, 0)

        [Header(Wind)][Space]
        _WindTexture("Wind Texture", 2D) = "white" {}
        _WindScroll("Wind Scroll", Vector) = (1, 1, 0, 0)
        _WindStrength("Wind Strength", Float) = 1

        [Header(Lighting)][Space]
        _RandomNormal("Random Normal", Range(0, 1)) = 0.1
        [Toggle] _ReceiveShadows("Receive Shadows", Float) = 1
        _ShadowAmbientDarkness("Shadow Ambient Darkness", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Cull Back
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 albedo : COLOR0;
                half3 normal : TEXCOORD0;
                half3 viewDir : TEXCOORD3;
                half mask : TEXCOORD4;
                half heightY : TEXCOORD5;
                float3 worldPos : TEXCOORD1;
                UNITY_FOG_COORDS(6)
                UNITY_SHADOW_COORDS(7)
            };

            half3 _ColorA;
            half3 _ColorB;
            float4 _BaseColorTexture_ST;
            half3 _AOColor;

            float _GrassWidth;
            float _GrassHeight;
            float _GrassCurving;
            float _GrassWidthRandomness;
            float _GrassHeightRandomness;

            float _ExpandDistantGrassWidth;
            float2 _ExpandDistantGrassRange;

            float4 _WindTexture_ST;
            float _WindStrength;
            float2 _WindScroll;

            half _RandomNormal;
            float _ReceiveShadows;
            float _ShadowAmbientDarkness;

            float2 _CenterPos;

            float _DrawDistance;
            float _TextureUpdateThreshold;

            StructuredBuffer<float3> _GrassPositions;

            sampler2D _BaseColorTexture;
            sampler2D _WindTexture;

            sampler2D _GrassColorRT;
            sampler2D _GrassSlopeRT;

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

            float Remap(float In, float2 InMinMax, float2 OutMinMax)
            {
                return OutMinMax.x + (In - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
            }

            half3 CalculateLighting(half3 albedo, half3 N, half3 V, half mask, half heightY, half atten)
            {
                // Ambient (Spherical Harmonics + Global Ambient) shadowed occlusion
                half ambientDarken = lerp(1.0 - _ShadowAmbientDarkness, 1.0, atten);
                half3 ambient = ShadeSH9(half4(N, 1.0)) * albedo * ambientDarken;

                // Main Direct Light Direction
                half3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                half3 lightColor = _LightColor0.rgb;

                // Specular calculations
                half3 H = normalize(lightDir + V);
                half directDiffuse = dot(N, lightDir) * 0.5 + 0.5;

                float directSpecular = saturate(dot(N, H));
                directSpecular = pow(directSpecular, 16.0); // Pow to the 16th is identical to 4 iterative squares
                directSpecular *= heightY * 0.12;

                // Attenuation combined with light color
                half3 lightingColor = lightColor * atten;

                half3 direct = (albedo * directDiffuse + directSpecular * (1.0 - mask)) * lightingColor;

                return ambient + direct;
            }

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);

                // Fetch procedural position from structured buffer
                float3 pivot = _GrassPositions[instanceID];

                // Calculate UV for the prepass data textures
                float2 uv = (pivot.xz - _CenterPos) / (_DrawDistance + _TextureUpdateThreshold);
                uv = uv * 0.5 + 0.5;

                // Grass blade width and height with random variation
                float grassWidth = _GrassWidth * (1.0 - random(pivot.x * 950.0 + pivot.z * 10.0) * _GrassWidthRandomness);
                float distToCamera = length(_WorldSpaceCameraPos - pivot);

                // Expand grass width in distance for better visibility
                grassWidth += saturate(Remap(distToCamera, _ExpandDistantGrassRange, float2(0.0, 1.0))) * _ExpandDistantGrassWidth;
                grassWidth *= (1.0 - v.vertex.y);

                float grassHeight = _GrassHeight * (1.0 - random(pivot.x * 230.0 + pivot.z * 10.0) * _GrassHeightRandomness);

                // Billboarding logic: face the active camera
                float3 cameraTransformRightWS = UNITY_MATRIX_V[0].xyz;
                float3 cameraTransformUpWS = UNITY_MATRIX_V[1].xyz;
                float3 cameraTransformForwardWS = -UNITY_MATRIX_V[2].xyz;

                // Sample and reconstruct direction from terrain/trampling slope map
                float4 slope = tex2Dlod(_GrassSlopeRT, float4(uv, 0.0, 0.0));
                float xSlope = slope.r * 2.0 - 1.0;
                float zSlope = slope.g * 2.0 - 1.0;

                float3 slopeDirection = normalize(float3(xSlope, 1.0 - (max(abs(xSlope), abs(zSlope)) * 0.5), zSlope));
                float3 bladeDirection = normalize(lerp(float3(0.0, 1.0, 0.0), slopeDirection, slope.a));

                // Wind animation logic
                float4 windUV = float4(pivot.xz * _WindTexture_ST.xy + _WindScroll * _Time.y, 0.0, 0.0);
                half3 windTex = tex2Dlod(_WindTexture, windUV);
                float2 wind = (windTex.rg * 2.0 - 1.0) * _WindStrength * (1.0 - slope.a);

                bladeDirection.xz += wind * v.vertex.y;
                bladeDirection = normalize(bladeDirection);

                float3 rightTangent = normalize(cross(bladeDirection, cameraTransformForwardWS));

                // Final object-space vertex construction
                float3 positionOS = bladeDirection * v.vertex.y * grassHeight 
                                    + rightTangent * v.vertex.x * grassWidth;

                // Add curving
                positionOS.xz += (v.vertex.y * v.vertex.y) * float2(srandom(pivot.x * 851.0 + pivot.z * 10.0), srandom(pivot.z * 647.0 + pivot.x * 10.0)) * _GrassCurving;

                float3 positionWS = positionOS + pivot;

                // Transform to clip space
                OUT.pos = UnityWorldToClipPos(positionWS);

                // Base albedo color and ambient occlusion
                float4 baseUV = float4(pivot.xz * _BaseColorTexture_ST.xy, 0.0, 0.0);
                half3 baseColor = lerp(_ColorA, _ColorB, tex2Dlod(_BaseColorTexture, baseUV).r);
                half3 albedo = lerp(_AOColor, baseColor, v.vertex.y);

                float4 colorRTVal = tex2Dlod(_GrassColorRT, float4(uv, 0.0, 0.0));
                albedo = lerp(albedo, colorRTVal.rgb, colorRTVal.a);

                // Normal vector calculation
                half3 N = normalize(bladeDirection + cameraTransformForwardWS * -0.5 + _RandomNormal * half3(srandom(pivot.x * 314.0 + pivot.z * 10.0), 0.0, srandom(pivot.z * 677.0 + pivot.x * 10.0)));
                half3 V = normalize(_WorldSpaceCameraPos - positionWS);

                // Output properties to fragment shader for per-pixel lighting
                OUT.albedo = albedo;
                OUT.normal = N;
                OUT.viewDir = V;
                OUT.mask = colorRTVal.a;
                OUT.heightY = v.vertex.y;
                OUT.worldPos = pivot;

                // Set input vertex to pivot position and temporarily override OUT.pos with pivot clip-space position.
                // This forces standard and screen-space shadows to evaluate at the pivot coordinate instead of per-pixel.
                float4 actualPos = OUT.pos;
                OUT.pos = UnityWorldToClipPos(pivot);
                v.vertex = float4(pivot, 1.0);
                
                TRANSFER_SHADOW(OUT);
                
                // Restore the actual clip-space position for rendering the geometry in the correct spot
                OUT.pos = actualPos;

                UNITY_TRANSFER_FOG(OUT, OUT.pos);

                return OUT;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half atten = 1.0;
                #if defined(SHADOWS_SCREEN) || defined(SHADOWS_SHADOWMAP) || defined(SHADOWS_DEPTH) || defined(SHADOWS_CUBE)
                    UNITY_LIGHT_ATTENUATION(shadowAtten, i, i.worldPos);
                    atten = lerp(1.0, shadowAtten, _ReceiveShadows);
                #endif

                half3 N = normalize(i.normal);
                half3 V = normalize(i.viewDir);

                // Compute ambient and direct specular/diffuse lighting
                half3 lighting = CalculateLighting(i.albedo, N, V, i.mask, i.heightY, atten);

                UNITY_APPLY_FOG(i.fogCoord, lighting);

                return half4(lighting, 1.0);
            }
            ENDCG
        }
    }
}