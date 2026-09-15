Shader "Hidden/VolumetricSmoke"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // --------------------------------------------------------
        // PASS 0: Raymarch
        // --------------------------------------------------------
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
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION; // screen position
                float3 viewRay : TEXCOORD1;  // ray from camera to far clip plane
            };

            sampler2D _CameraDepthTexture;
            sampler2D _GPUInstancerDepthTex;
            
            float4 _MainTex_TexelSize;
            float3 _CameraWorldPos;
            
            float3 _FrustumBL;
            float3 _FrustumTL;
            float3 _FrustumTR;
            float3 _FrustumBR;
            
            float4 _SmokeColor;
            float4 _FarSmokeColor;
            float _FarSmokeColorDist;
            int _StepCount;
            float4 _MinDensityByStep[64];
            float _PreliminaryRayStep;
            int _FrameCount;
            float _UseDither;
            int _StepCount2;
            float _MaxDrawDistance2;
            float _MaxDrawDistance;
            float _DensityFadeStart;
            float _DensityFadeEnd;
            float _MaxDistance;
            sampler2D _DensityCurveMap;
            float _SmokeHeight;
            float _SmokeHeightFade;
            float _GlobalDensityMultiplier;
            
            int _ShadowOversample;
            int _ShadowOversampleStartStep;
            int _Phase2Shadows;
            float _ShadowJitter;
            int _ShadowJitterSamples;
            int _ShadowJitterMaxSteps;
            float _ShadowThreshold;
            float _EdgeShadowWeight;
            float4 _ShadowColor;
            float _ShadowDensityMultiplier;
            int _SmokeSelfShadow;
            float _SelfShadowStepSize;
            int _SelfShadowStepsPhase1;
            float _SelfShadowStrengthPhase1;
            int _SelfShadowStepsPhase2;
            float _SelfShadowStrengthPhase2;
            float3 _SunDirection;
            UNITY_DECLARE_SHADOWMAP(_GlobalShadowMap);
            
            // Global variables from RealTerrain
            sampler2D _GlobalWaterDistanceMap;
            float4 _GlobalBiomeMap_Bounds; // xy = center, zw = size
            
            #include "../RealTerrain/RealTerrainHeight.cginc"

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4(v.vertex.xy, 0.0, 1.0);
                o.uv = (v.vertex.xy + 1.0) * 0.5;
                
                // Interpolate the view ray for this vertex based on its UV
                float3 rayX1 = lerp(_FrustumBL, _FrustumBR, o.uv.x);
                float3 rayX2 = lerp(_FrustumTL, _FrustumTR, o.uv.x);
                o.viewRay = lerp(rayX1, rayX2, o.uv.y);
                
                return o;
            }



            float4 frag (v2f i) : SV_Target
            {
                // 1. Get Scene Depth
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                
                // 1b. Get GPUInstancer Depth and combine them
                float instancerDepth = tex2D(_GPUInstancerDepthTex, i.uv).r;
                
                #if defined(UNITY_REVERSED_Z)
                    // Reversed Z: near plane is 1, far plane is 0
                    rawDepth = max(rawDepth, instancerDepth);
                #else
                    // Standard Z: near plane is 0, far plane is 1
                    rawDepth = min(rawDepth, instancerDepth);
                #endif
                
                // Linearize the depth to a 0..1 value (0 = camera, 1 = far clip plane)
                float linear01Depth = Linear01Depth(rawDepth);
                
                // 2. Reconstruct World Position of the Scene Pixel
                float3 sceneWorldPos = _CameraWorldPos + (i.viewRay * linear01Depth);
                
                // 3. Setup Ray
                float3 rayOrigin = _CameraWorldPos;
                float3 rayDir = sceneWorldPos - rayOrigin;
                float totalDistance = length(rayDir);
                rayDir /= totalDistance; // normalize
                
                // --- SETUP PHASES ---
                float startDist = min(_PreliminaryRayStep, totalDistance);
                
                float endPhase1 = max(startDist, min(totalDistance, _MaxDrawDistance));
                float distPhase1 = endPhase1 - startDist;
                float stepSize1 = distPhase1 / max(1.0, float(_StepCount));
                
                float endPhase2 = max(endPhase1, min(totalDistance, _MaxDrawDistance2));
                float distPhase2 = endPhase2 - endPhase1;
                float stepSize2 = distPhase2 / max(1.0, float(_StepCount2));
                
                // Interleaved Gradient Noise for dithering (hides banding)
                float2 ditherPos = i.vertex.xy + float2(float(_FrameCount) * 137.0, float(_FrameCount) * 189.0);
                float dither = frac(52.9829189 * frac(dot(ditherPos, float2(0.06711056, 0.00583715))));
                dither *= _UseDither;
                
                float transmittance = 1.0;
                float3 totalColor = float3(0,0,0);
                int unskippedSteps = 0;
                
                // -----------------------------------------------------------------
                // PHASE 1
                // -----------------------------------------------------------------
                if (distPhase1 > 0.001 && _StepCount > 0) {
                    float stepSize = stepSize1;
                    float currentDist = startDist + stepSize * dither;
                    float3 currentPos = rayOrigin + (rayDir * currentDist);
                    
                    for(int step = 0; step < _StepCount; step++)
                    {
                        // 1. CHEAP EARLY OUT: Water Distance
                        float2 uv = (currentPos.xz - _GlobalBiomeMap_Bounds.xy) / _GlobalBiomeMap_Bounds.zw + 0.5;
                        float waterDist = tex2Dlod(_GlobalWaterDistanceMap, float4(uv, 0, 0)).r;
                        float localDensity = tex2Dlod(_DensityCurveMap, float4(saturate(waterDist / max(0.0001, _MaxDistance)), 0.5, 0, 0)).r;
                        
                        if (localDensity < 0.001) {
                            currentPos += rayDir * stepSize;
                            currentDist += stepSize;
                            continue;
                        }
                        
                        // 2. CHEAP EARLY OUT: Height Fade
                        float fade = 1.0 - smoothstep(_DensityFadeStart, _DensityFadeEnd, currentDist);
                        localDensity *= fade;
                        
                        float terrainHeight = GetTerrainHeightCheap(currentPos.xz);
                        float heightAboveTerrain = currentPos.y - terrainHeight;
                        float heightFade = 1.0 - smoothstep(max(0.0, _SmokeHeight - _SmokeHeightFade), _SmokeHeight, heightAboveTerrain);
                        localDensity *= heightFade;
                        
                        int clampedStep = min(unskippedSteps, 255);
                        float minAcceptedDensity = _MinDensityByStep[clampedStep / 4][clampedStep % 4];
                        
                        if (localDensity < max(0.01, minAcceptedDensity)) {
                            currentPos += rayDir * stepSize;
                            currentDist += stepSize;
                            continue;
                        }

                        // --- WE ARE IN SMOKE. DO HEAVY CALCULATIONS ---
                        unskippedSteps++;

                        float3 stepColor = lerp(_SmokeColor.rgb, _FarSmokeColor.rgb, saturate(currentDist / _FarSmokeColorDist));
                        float colorDensityMult = 1.0;

                        float maxShadowWeight = 0.0;
                        int actualOversample = (step >= _ShadowOversampleStartStep) ? _ShadowOversample : 1;
                        
                        [loop]
                        for (int s = 0; s < actualOversample; s++) 
                        {
                            int evalIndex = s;
                            if (actualOversample == 3) {
                                if (s == 0) evalIndex = 1;
                                else if (s == 1) evalIndex = 0;
                                else evalIndex = 2;
                            } else if (actualOversample == 4) {
                                if (s == 0) evalIndex = 1;
                                else if (s == 1) evalIndex = 2;
                                else if (s == 2) evalIndex = 0;
                                else evalIndex = 3;
                            }
                            float offsetMult = -0.5 + ((float(evalIndex) + 0.5) / float(actualOversample));
                            float3 baseShadowPos = currentPos + (rayDir * (stepSize * offsetMult));
                            float accumulatedShadowHits = 0.0;
                            int actualJitterSamples = (step < _ShadowJitterMaxSteps) ? _ShadowJitterSamples : 1;
                            
                            for (int j = 0; j < actualJitterSamples; j++) {
                                float3 shadowPos = baseShadowPos;
                                if (_ShadowJitter > 0.001) {
                                    float3 jitter = frac(sin(shadowPos * 12.9898 + float3(12.3, 45.6, 78.9) * dither + s * 1.3 + j * 7.1) * 43758.5453);
                                    shadowPos += (jitter * 2.0 - 1.0) * _ShadowJitter;
                                }
                                
                                float3 fromCenter0 = shadowPos - unity_ShadowSplitSpheres[0].xyz;
                                float3 fromCenter1 = shadowPos - unity_ShadowSplitSpheres[1].xyz;
                                float3 fromCenter2 = shadowPos - unity_ShadowSplitSpheres[2].xyz;
                                float3 fromCenter3 = shadowPos - unity_ShadowSplitSpheres[3].xyz;
                                
                                float4 distances2 = float4(dot(fromCenter0, fromCenter0), dot(fromCenter1, fromCenter1), 
                                                           dot(fromCenter2, fromCenter2), dot(fromCenter3, fromCenter3));
                                                           
                                float4 weights = distances2 < float4(unity_ShadowSplitSpheres[0].w, unity_ShadowSplitSpheres[1].w, 
                                                                     unity_ShadowSplitSpheres[2].w, unity_ShadowSplitSpheres[3].w);
                                                                     
                                weights.yzw = saturate(weights.yzw - weights.xxx);
                                weights.zw = saturate(weights.zw - weights.yyy);
                                weights.w = saturate(weights.w - weights.zzz);
                                
                                float4 shadowCoord = mul(unity_WorldToShadow[0], float4(shadowPos, 1.0)) * weights.x +
                                                     mul(unity_WorldToShadow[1], float4(shadowPos, 1.0)) * weights.y +
                                                     mul(unity_WorldToShadow[2], float4(shadowPos, 1.0)) * weights.z +
                                                     mul(unity_WorldToShadow[3], float4(shadowPos, 1.0)) * weights.w;
                                                     
                                half shadowAtten = UNITY_SAMPLE_SHADOW(_GlobalShadowMap, shadowCoord.xyz);
                                half inCascade = saturate(dot(weights, float4(1.0, 1.0, 1.0, 1.0)));
                                shadowAtten = lerp(1.0, shadowAtten, inCascade);
                                
                                if (shadowAtten < _ShadowThreshold) {
                                    accumulatedShadowHits += 1.0;
                                }
                            }
                            
                            float shadowRatio = accumulatedShadowHits / float(actualJitterSamples);
                            if (shadowRatio > 0.0) {
                                float weight = lerp(1.0, _EdgeShadowWeight, abs(offsetMult) * 2.0) * shadowRatio;
                                maxShadowWeight = max(maxShadowWeight, weight);
                                if (shadowRatio > 0.99) break;
                            }
                        }
                        
                        if (maxShadowWeight > 0.0) {
                            stepColor = lerp(stepColor, _ShadowColor.rgb, maxShadowWeight);
                            colorDensityMult = lerp(colorDensityMult, _ShadowDensityMultiplier, maxShadowWeight);
                        }
                        
                        // --- SMOKE SELF-SHADOW ---
                        if (_SmokeSelfShadow) {
                            float totalSSDensity = 0.0;
                            for (int ss = 1; ss <= _SelfShadowStepsPhase1; ss++) {
                                float3 ssPos = currentPos + _SunDirection * (_SelfShadowStepSize * float(ss));
                                float2 ssUV = (ssPos.xz - _GlobalBiomeMap_Bounds.xy) / _GlobalBiomeMap_Bounds.zw + 0.5;
                                float ssWaterDist = tex2Dlod(_GlobalWaterDistanceMap, float4(ssUV, 0, 0)).r;
                                float ssDensity = tex2Dlod(_DensityCurveMap, float4(saturate(ssWaterDist / max(0.0001, _MaxDistance)), 0.5, 0, 0)).r;
                                
                                float ssTerrainHeight = GetTerrainHeightCheap(ssPos.xz);
                                float ssHeightAboveTerrain = ssPos.y - ssTerrainHeight;
                                float ssHeightFade = 1.0 - smoothstep(max(0.0, _SmokeHeight - _SmokeHeightFade), _SmokeHeight, ssHeightAboveTerrain);
                                totalSSDensity += ssDensity * ssHeightFade;
                            }
                            
                            float ssAmount = saturate(totalSSDensity * _SelfShadowStrengthPhase1);
                            stepColor = lerp(stepColor, _ShadowColor.rgb, ssAmount);
                        }

                        localDensity *= _GlobalDensityMultiplier * colorDensityMult;
                        
                        float stepTransmittance = exp(-localDensity * stepSize);
                        
                        if (transmittance < 0.01) break;
                            
                        float stepOpacity = 1.0 - stepTransmittance;
                        totalColor += stepColor * stepOpacity * transmittance;
                        transmittance *= stepTransmittance;
                        
                        currentPos += rayDir * stepSize;
                        currentDist += stepSize;
                    }
                }

                // -----------------------------------------------------------------
                // PHASE 2 (Distant)
                // -----------------------------------------------------------------
                if (distPhase2 > 0.001 && transmittance > 0.01 && _StepCount2 > 0) {
                    float stepSize = stepSize2;
                    float currentDist = endPhase1 + stepSize * dither;
                    float3 currentPos = rayOrigin + (rayDir * currentDist);
                    
                    for(int step2 = 0; step2 < _StepCount2; step2++)
                    {
                        // 1. CHEAP EARLY OUT: Water Distance
                        float2 uv = (currentPos.xz - _GlobalBiomeMap_Bounds.xy) / _GlobalBiomeMap_Bounds.zw + 0.5;
                        float waterDist = tex2Dlod(_GlobalWaterDistanceMap, float4(uv, 0, 0)).r;
                        float localDensity = tex2Dlod(_DensityCurveMap, float4(saturate(waterDist / max(0.0001, _MaxDistance)), 0.5, 0, 0)).r;
                        
                        if (localDensity < 0.001) {
                            currentPos += rayDir * stepSize;
                            currentDist += stepSize;
                            continue;
                        }
                        
                        // 2. CHEAP EARLY OUT: Height Fade
                        float fade = 1.0 - smoothstep(_DensityFadeStart, _DensityFadeEnd, currentDist);
                        localDensity *= fade;
                        
                        float terrainHeight = GetTerrainHeightCheap(currentPos.xz);
                        float heightAboveTerrain = currentPos.y - terrainHeight;
                        float heightFade = 1.0 - smoothstep(max(0.0, _SmokeHeight - _SmokeHeightFade), _SmokeHeight, heightAboveTerrain);
                        localDensity *= heightFade;
                        
                        int clampedStep = min(unskippedSteps, 255);
                        float minAcceptedDensity = _MinDensityByStep[clampedStep / 4][clampedStep % 4];
                        
                        if (localDensity < max(0.01, minAcceptedDensity)) {
                            currentPos += rayDir * stepSize;
                            currentDist += stepSize;
                            continue;
                        }

                        // --- WE ARE IN SMOKE. DO HEAVY CALCULATIONS ---
                        unskippedSteps++;

                        float3 stepColor = lerp(_SmokeColor.rgb, _FarSmokeColor.rgb, saturate(currentDist / _FarSmokeColorDist));
                        float colorDensityMult = 1.0;

                        // --- SHADOW SAMPLING (optional, toggled by _Phase2Shadows) ---
                        if (_Phase2Shadows) {
                            float accumulatedShadowHits = 0.0;
                            int actualJitterSamples = (step2 + _StepCount < _ShadowJitterMaxSteps) ? _ShadowJitterSamples : 1;
                            
                            for (int j = 0; j < actualJitterSamples; j++) {
                                float3 shadowPos = currentPos;
                                if (_ShadowJitter > 0.001) {
                                    float3 jitter = frac(sin(shadowPos * 12.9898 + float3(12.3, 45.6, 78.9) * dither + step2 * 1.3 + j * 7.1) * 43758.5453);
                                    shadowPos += (jitter * 2.0 - 1.0) * _ShadowJitter;
                                }
                                
                                float3 fromCenter0 = shadowPos - unity_ShadowSplitSpheres[0].xyz;
                                float3 fromCenter1 = shadowPos - unity_ShadowSplitSpheres[1].xyz;
                                float3 fromCenter2 = shadowPos - unity_ShadowSplitSpheres[2].xyz;
                                float3 fromCenter3 = shadowPos - unity_ShadowSplitSpheres[3].xyz;
                                
                                float4 distances2 = float4(dot(fromCenter0, fromCenter0), dot(fromCenter1, fromCenter1), 
                                                           dot(fromCenter2, fromCenter2), dot(fromCenter3, fromCenter3));
                                                           
                                float4 weights = distances2 < float4(unity_ShadowSplitSpheres[0].w, unity_ShadowSplitSpheres[1].w, 
                                                                     unity_ShadowSplitSpheres[2].w, unity_ShadowSplitSpheres[3].w);
                                                                     
                                weights.yzw = saturate(weights.yzw - weights.xxx);
                                weights.zw = saturate(weights.zw - weights.yyy);
                                weights.w = saturate(weights.w - weights.zzz);
                                
                                float4 shadowCoord = mul(unity_WorldToShadow[0], float4(shadowPos, 1.0)) * weights.x +
                                                     mul(unity_WorldToShadow[1], float4(shadowPos, 1.0)) * weights.y +
                                                     mul(unity_WorldToShadow[2], float4(shadowPos, 1.0)) * weights.z +
                                                     mul(unity_WorldToShadow[3], float4(shadowPos, 1.0)) * weights.w;
                                                     
                                half shadowAtten = UNITY_SAMPLE_SHADOW(_GlobalShadowMap, shadowCoord.xyz);
                                half inCascade = saturate(dot(weights, float4(1.0, 1.0, 1.0, 1.0)));
                                shadowAtten = lerp(1.0, shadowAtten, inCascade);
                                
                                if (shadowAtten < _ShadowThreshold) {
                                    accumulatedShadowHits += 1.0;
                                }
                            }
                            
                            float shadowRatio = accumulatedShadowHits / float(actualJitterSamples);
                            if (shadowRatio > 0.0) {
                                stepColor = lerp(stepColor, _ShadowColor.rgb, shadowRatio);
                                colorDensityMult = lerp(colorDensityMult, _ShadowDensityMultiplier, shadowRatio);
                            }
                        }
                        
                        // --- SMOKE SELF-SHADOW ---
                        if (_SmokeSelfShadow) {
                            float totalSSDensity = 0.0;
                            for (int ss = 1; ss <= _SelfShadowStepsPhase2; ss++) {
                                float3 ssPos = currentPos + _SunDirection * (_SelfShadowStepSize * float(ss));
                                float2 ssUV = (ssPos.xz - _GlobalBiomeMap_Bounds.xy) / _GlobalBiomeMap_Bounds.zw + 0.5;
                                float ssWaterDist = tex2Dlod(_GlobalWaterDistanceMap, float4(ssUV, 0, 0)).r;
                                float ssDensity = tex2Dlod(_DensityCurveMap, float4(saturate(ssWaterDist / max(0.0001, _MaxDistance)), 0.5, 0, 0)).r;
                                
                                float ssTerrainHeight = GetTerrainHeightCheap(ssPos.xz);
                                float ssHeightAboveTerrain = ssPos.y - ssTerrainHeight;
                                float ssHeightFade = 1.0 - smoothstep(max(0.0, _SmokeHeight - _SmokeHeightFade), _SmokeHeight, ssHeightAboveTerrain);
                                totalSSDensity += ssDensity * ssHeightFade;
                            }
                            
                            float ssAmount = saturate(totalSSDensity * _SelfShadowStrengthPhase2);
                            stepColor = lerp(stepColor, _ShadowColor.rgb, ssAmount);
                        }

                        localDensity *= _GlobalDensityMultiplier * colorDensityMult;
                        
                        float stepTransmittance = exp(-localDensity * stepSize);
                        if (transmittance < 0.01) break;
                            
                        float stepOpacity = 1.0 - stepTransmittance;
                        totalColor += stepColor * stepOpacity * transmittance;
                        transmittance *= stepTransmittance;
                        
                        currentPos += rayDir * stepSize;
                        currentDist += stepSize;
                    }
                }
                
                float finalAlpha = 1.0 - transmittance;
                
                return float4(totalColor, finalAlpha);
            }
            ENDCG
        }
        
        // --------------------------------------------------------
        // PASS 1: Composite Full Screen (Depth-Aware Upsample)
        // --------------------------------------------------------
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
            sampler2D _SmokeTex;
            float4 _SmokeTex_TexelSize;
            
            // Low-res depth produced by Pass 2 — matches exactly what Pass 0 used
            sampler2D _SmokeDepthTex;
            
            // Full-res depth for the current high-res pixel
            sampler2D _CameraDepthTexture;
            sampler2D _GPUInstancerDepthTex;
            
            int _UpsampleSamples;
            float _FarClipPlane;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4(v.vertex.xy, 0.0, 1.0);
                o.uv = (v.vertex.xy + 1.0) * 0.5;
                return o;
            }
            
            float GetFullResLinearDepth(float2 uv)
            {
                float2 uvFixed = uv;
                uvFixed.y = 1.0 - uvFixed.y;
                float rawDepth = tex2Dlod(_CameraDepthTexture, float4(uvFixed, 0, 0)).r;
                float instancerDepth = tex2Dlod(_GPUInstancerDepthTex, float4(uvFixed, 0, 0)).r;
                
                #if defined(UNITY_REVERSED_Z)
                    rawDepth = max(rawDepth, instancerDepth);
                #else
                    rawDepth = min(rawDepth, instancerDepth);
                #endif
                
                return Linear01Depth(rawDepth);
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 sceneUV = i.uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y > 0.0) {
                    sceneUV.y = 1.0 - sceneUV.y;
                }
                #endif
                float4 sceneColor = tex2D(_MainTex, sceneUV);
                
                // Get the depth of the exact high-res pixel in meters
                float eyeDepthFull = GetFullResLinearDepth(i.uv) * _FarClipPlane;
                
                // Find the center of the nearest low-res pixel
                float2 lowResUV = i.uv * _SmokeTex_TexelSize.zw; 
                float2 center = floor(lowResUV) + 0.5; 
                float2 uvStart = center * _SmokeTex_TexelSize.xy;
                
                static const float2 manhattanOffsets[25] = {
                    float2(0, 0),
                    float2(1, 0), float2(0, 1), float2(-1, 0), float2(0, -1),
                    float2(1, 1), float2(-1, 1), float2(-1, -1), float2(1, -1),
                    float2(2, 0), float2(0, 2), float2(-2, 0), float2(0, -2),
                    float2(2, 1), float2(1, 2), float2(-1, 2), float2(-2, 1),
                    float2(-2, -1), float2(-1, -2), float2(1, -2), float2(2, -1),
                    float2(3, 0), float2(0, 3), float2(-3, 0), float2(0, -3)
                };
                
                float4 totalSmokeColor = float4(0, 0, 0, 0);
                float totalWeight = 0.0;
                
                int sampleCount = min(_UpsampleSamples, 25);
                
                [loop]
                for (int step = 0; step < sampleCount; step++)
                {
                    float2 currentUV = uvStart + manhattanOffsets[step] * _SmokeTex_TexelSize.xy;
                    
                    // Read the depth from the LOW-RES depth map
                    float dSearch = tex2Dlod(_SmokeDepthTex, float4(currentUV, 0, 0)).r * _FarClipPlane;
                    float diff = abs(eyeDepthFull - dSearch);
                    
                    // Depth weighing logic
                    float depthWeight = 1.0 / (diff + 0.00001);
                    
                    // Spatial weighing logic (smooths pixelated blocks against sky)
                    float2 distInPixels = (i.uv - currentUV) * _SmokeTex_TexelSize.zw;
                    float spatialWeight = exp(-dot(distInPixels, distInPixels));
                    
                    float weight = depthWeight * spatialWeight;
                    
                    float4 sampleColor = tex2Dlod(_SmokeTex, float4(currentUV, 0, 0));
                    totalSmokeColor += sampleColor * weight;
                    totalWeight += weight;
                }
                
                float4 smokeColor = totalSmokeColor / totalWeight;

                // Standard alpha blend over scene
                float3 finalColor = sceneColor.rgb * (1.0 - smokeColor.a) + smokeColor.rgb;
                
                return float4(finalColor, sceneColor.a);
            }
            ENDCG
        }
        
        // --------------------------------------------------------
        // PASS 2: Low-Res Depth Output
        // Renders at the same resolution as Pass 0, outputting
        // the combined linear01 depth so Pass 1 can use it.
        // --------------------------------------------------------
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

            sampler2D _CameraDepthTexture;
            sampler2D _GPUInstancerDepthTex;
            float4 _MainTex_TexelSize;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4(v.vertex.xy, 0.0, 1.0);
                o.uv = (v.vertex.xy + 1.0) * 0.5;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                float instancerDepth = tex2D(_GPUInstancerDepthTex, i.uv).r;
                
                #if defined(UNITY_REVERSED_Z)
                    rawDepth = max(rawDepth, instancerDepth);
                #else
                    rawDepth = min(rawDepth, instancerDepth);
                #endif
                
                return Linear01Depth(rawDepth);
            }
            ENDCG
        }

    }
}
