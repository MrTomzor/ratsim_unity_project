Shader "IMP/ImposterBaker"
{
	Properties
	{
	}
	SubShader
	{
		ZTest LEqual
		ZWrite on
		Cull off

		// Pass 0: MinMax (pixels only pass used for min max frame computation)
		Pass
		{
			Blend one one
			CGPROGRAM
			#pragma target 4.0
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct Attributes
			{
				float4 positionOS 	: POSITION;
			};

			struct Varyings
			{
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				output.positionCS = UnityObjectToClipPos(input.positionOS);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				return 1;
			}
			ENDCG
		}
		
		// Pass 1: Alpha Copy
		Pass
		{
			Blend one one
			CGPROGRAM
			#pragma target 4.0
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _AlphaMap;
			float4 _Channels;
			
			struct Attributes
			{
				float4 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				output.positionCS = UnityObjectToClipPos(input.positionOS);
				output.uv = input.uv;
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				float depth = tex2Dlod(_AlphaMap, float4(input.uv, 0, 0)).r;
				float alpha = 0.0;
				#if defined(UNITY_REVERSED_Z)
					alpha = (depth > 0.00001) ? 1.0 : 0.0;
				#else
					alpha = (depth < 0.99999) ? 1.0 : 0.0;
				#endif
				return alpha * _Channels;
			}
			ENDCG
		}

		// Pass 2: Depth Copy
		Pass
		{
			Blend one one
			CGPROGRAM
			#pragma target 4.0
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _DepthMap;
			float4 _Channels;
			
			struct Attributes
			{
				float4 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				output.positionCS = UnityObjectToClipPos(input.positionOS);
				output.uv = input.uv;
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				float depth = tex2Dlod(_DepthMap, float4(input.uv, 0, 0)).r;
				return depth * _Channels;
			}
			ENDCG
		}

		// Pass 3: Merge Normals + Depth
		Pass
		{
			Blend one zero
			CGPROGRAM
			#pragma target 4.0
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _NormalMap;
			float4 _NormalMap_ST;
			float4 _NormalMap_TexelSize;

			sampler2D _DepthMap;
			float4 _DepthMap_ST;
			float4 _DepthMap_TexelSize;

			struct Attributes
			{
				float4 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				output.positionCS = UnityObjectToClipPos(input.positionOS);
				output.uv = TRANSFORM_TEX(input.uv, _NormalMap);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				// In BIRP deferred rendering, GBuffer 2 stores world-space normals already scaled/biased to 0..1
				float3 normalSample = tex2Dlod(_NormalMap, float4(input.uv, 0, 0)).rgb;
				float depthSample = tex2Dlod(_DepthMap, float4(input.uv, 0, 0)).r;

				// Since it is already world space and in [0, 1] range, we can return it directly.
				return float4(normalSample, depthSample);
			}
			ENDCG
		}

		// Pass 4: Dilate pass
		Pass
		{
			Blend one one
			CGPROGRAM
			#pragma target 4.0
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float4 _MainTex_TexelSize;

			sampler2D _DilateMask;
			float4 _DilateMask_ST;
			float4 _DilateMask_TexelSize;

			float4 _Channels;

			struct Attributes
			{
				float4 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				output.positionCS = UnityObjectToClipPos(input.positionOS);
				output.uv = TRANSFORM_TEX(input.uv, _MainTex);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				// Pixel colour
				float4 outColor = tex2Dlod(_MainTex, float4(input.uv, 0, 0));
				float mask = tex2Dlod(_DilateMask, float4(input.uv, 0, 0)).r;

				if (mask > 0) return outColor;

				float minDistance = sqrt(_MainTex_TexelSize.z * _MainTex_TexelSize.z + _MainTex_TexelSize.w * _MainTex_TexelSize.w);
				float4 closestColor = outColor;
				float2 uv = input.uv;

				UNITY_LOOP
				for (int i = 0; i < _MainTex_TexelSize.z; ++i) 
				{
					UNITY_LOOP
					for (int j = 0; j < _MainTex_TexelSize.z; ++j) 
					{
						float2 sampleUV = float2(i, j) * _MainTex_TexelSize.xy;

						if (sampleUV.x == uv.x && sampleUV.y == uv.y) continue;

						float texelDistance = distance(sampleUV, input.uv);
						
						float4 sample = tex2Dlod(_MainTex, float4(sampleUV, 0, 0));
						float sampleMask = tex2Dlod(_DilateMask, float4(sampleUV, 0, 0)).r;
						if (sampleMask > 0 && texelDistance < minDistance)
						{
							minDistance = texelDistance;
							closestColor = sample;
						}
					}
				}

				outColor = lerp(outColor, closestColor, _Channels);
				return outColor;
			}
			ENDCG
		}
	}
}
