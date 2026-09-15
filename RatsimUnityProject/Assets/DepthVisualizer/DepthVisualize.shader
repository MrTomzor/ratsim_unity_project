Shader "Custom/DepthVisualize"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _NearDist("Near Distance", Float) = 1.0
        _FarDist("Far Distance", Float) = 100.0
        _DepthMultiplier("Depth Multiplier", Float) = 10.0
        _DepthPower("Depth Power", Float) = 2.0
        _LogCompression("Log Compression", Float) = 10000.0
        _DepthMode("Depth Mode", Int) = 1
        _ColorMapMode("Color Map Mode", Int) = 1
        _RepeatColorMap("Repeat Color Map", Int) = 0
        _RepeatFrequency("Repeat Frequency", Float) = 10.0
        _UseRadialDistance("Use Radial Distance", Int) = 1
        _FlipColorMap("Flip Color Map", Int) = 0
        _CustomColorMap("Custom Color Map", 2D) = "white" {}
        _NormalizeOnScreen("Normalize On Screen", Int) = 0
        _MultiplyWithSceneColor("Multiply With Scene Color", Int) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            sampler2D _GPUInstancerDepthTex; // RFloat color texture, not a depth-format texture

            float _NearDist;
            float _FarDist;
            float _DepthMultiplier;
            float _DepthPower;
            float _LogCompression;
            int _DepthMode;
            int _ColorMapMode;
            int _RepeatColorMap;
            float _RepeatFrequency;
            int _UseRadialDistance;
            float4x4 _CamProj;
            int _FlipColorMap;
            sampler2D _CustomColorMap;
            int _NormalizeOnScreen;
            StructuredBuffer<uint> _MinMaxBuffer;
            int _MultiplyWithSceneColor;

            // Google's Turbo Colormap Approximation
            float3 turbo(float x) {
                const float4 kRedVec4 = float4(0.13572138, 4.61539260, -42.66032258, 132.13108234);
                const float4 kGreenVec4 = float4(0.09140261, 2.19418839, 4.84296658, -14.18503333);
                const float4 kBlueVec4 = float4(0.10667330, 12.64194608, -60.58204836, 110.36276771);
                const float2 kRedVec2 = float2(-152.94239396, 59.28637943);
                const float2 kGreenVec2 = float2(4.27729857, 2.82956604);
                const float2 kBlueVec2 = float2(-89.90310912, 27.34824973);

                x = saturate(x);
                float4 v4 = float4(1.0, x, x * x, x * x * x);
                float2 v2 = v4.zw * v4.z;

                return float3(
                    saturate(dot(v4, kRedVec4)   + dot(v2, kRedVec2)),
                    saturate(dot(v4, kGreenVec4) + dot(v2, kGreenVec2)),
                    saturate(dot(v4, kBlueVec4)  + dot(v2, kBlueVec2))
                );
            }

            // Viridis Colormap Approximation
            float3 viridis(float t) {
                t = saturate(t);
                const float3 c0 = float3(0.2777273272234177, 0.005407344544966578, 0.3340998053353061);
                const float3 c1 = float3(0.1050930431085774, 1.404613529898575, 1.38029335556315);
                const float3 c2 = float3(-0.3308618287255563, 0.214847559468213, -6.367098242202206);
                const float3 c3 = float3(-4.634230498983486, -5.799100973351585, 10.87768997576566);
                const float3 c4 = float3(6.228269936347081, 14.17993336680509, -4.358253106198953);
                const float3 c5 = float3(4.776384997670288, -13.74514537774601, -3.98595992984534);
                const float3 c6 = float3(-5.43545585594025, 4.645852612178535, 1.942738222956554);
                return saturate(c0+t*(c1+t*(c2+t*(c3+t*(c4+t*(c5+t*c6))))));
            }

            // Magma Colormap Approximation
            float3 magma(float t) {
                t = saturate(t);
                const float3 c0 = float3(0.0002986237510787011, 0.0006248981295982846, 0.001925916053315802);
                const float3 c1 = float3(0.2458022718309101, -0.2287959062332616, 1.258450125816912);
                const float3 c2 = float3(3.295286469608107, 4.417277884100518, 5.093414902047391);
                const float3 c3 = float3(-2.898628038740529, -15.11894451730036, -11.96639535308696);
                const float3 c4 = float3(-5.201824316140685, 23.36186835017551, 13.91684344247508);
                const float3 c5 = float3(8.530396001402288, -15.0298816738492, -8.761214041724626);
                const float3 c6 = float3(-2.95155160867746, 2.585215039294966, 1.472535091703623);
                return saturate(c0+t*(c1+t*(c2+t*(c3+t*(c4+t*(c5+t*c6))))));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float sceneDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                float gpuDepth = tex2D(_GPUInstancerDepthTex, i.uv).r;

                float depth = 0;
                bool isSkybox = false;

                #if UNITY_REVERSED_Z
                    depth = max(sceneDepth, gpuDepth);
                    if (depth == 0.0) isSkybox = true;
                #else
                    depth = min(sceneDepth, gpuDepth);
                    if (depth == 1.0) isSkybox = true;
                #endif

                float finalVal = 0.0;
                
                if (!isSkybox)
                {
                    float linearDepth = Linear01Depth(depth);

                    // Convert planar Z-depth to true radial (Euclidean) distance
                    if (_UseRadialDistance > 0)
                    {
                        float x = (i.uv.x * 2.0 - 1.0 - _CamProj[0][2]) / _CamProj[0][0];
                        float y = (i.uv.y * 2.0 - 1.0 - _CamProj[1][2]) / _CamProj[1][1];
                        float3 viewRay = float3(x, y, 1.0);
                        linearDepth *= length(viewRay);
                    }

                    // Get absolute world distance in meters
                    float actualDist = linearDepth * _ProjectionParams.z;
                    
                    // Clamp to user's desired range
                    float dist = clamp(actualDist, max(0.001, _NearDist), max(0.002, _FarDist));
                    
                    // Calculate x / dist directly
                    float normalizedVal = _DepthMultiplier / dist;
                    
                    // If requested, normalize this value strictly between the min and max currently visible on screen
                    if (_NormalizeOnScreen > 0)
                    {
                        float minVal = asfloat(_MinMaxBuffer[0]);
                        float maxVal = asfloat(_MinMaxBuffer[1]);
                        if (maxVal > minVal)
                        {
                            normalizedVal = (normalizedVal - minVal) / (maxVal - minVal);
                        }
                        else
                        {
                            normalizedVal = 0.0;
                        }
                    }

                    // Apply user curve
                    float val = 0.0;
                    if (_DepthMode == 0) // Linear Mode
                    {
                        val = normalizedVal;
                    }
                    else if (_DepthMode == 1) // Power Mode
                    {
                        val = pow(normalizedVal, _DepthPower);
                    }
                    else // Logarithmic Mode
                    {
                        val = log(1.0 + _LogCompression * normalizedVal) / log(1.0 + _LogCompression);
                    }

                    finalVal = val;

                    if (_RepeatColorMap > 0)
                    {
                        finalVal = frac(finalVal * _RepeatFrequency);
                    }
                    else
                    {
                        finalVal = saturate(finalVal);
                    }
                }

                if (_FlipColorMap > 0)
                {
                    finalVal = 1.0 - finalVal;
                }

                float3 color = float3(finalVal, finalVal, finalVal);

                if (_ColorMapMode == 1)      color = turbo(finalVal);
                else if (_ColorMapMode == 2) color = viridis(finalVal);
                else if (_ColorMapMode == 3) color = magma(finalVal);
                else if (_ColorMapMode == 4) color = tex2D(_CustomColorMap, float2(finalVal, 0.5)).rgb;
                
                if (_MultiplyWithSceneColor > 0)
                {
                    float4 sceneColor = tex2D(_MainTex, i.uv);
                    color *= sceneColor.rgb;
                }
                
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
}
