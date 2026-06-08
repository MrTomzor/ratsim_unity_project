Shader "IMP/Imposter" {
	Properties {
		_Color ("Color", Color) = (1,1,1,1)
		_Glossiness ("Smoothness", Range(0,1)) = 0.5
		_Metallic ("Metallic", Range(0,1)) = 0.0
		_Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.3
		
		_ImposterAlbedoMap ("Imposter Base", 2D) = "black" {}
		_ImposterNormalMap ("WorldNormal+Depth", 2D) = "black" {}
		_ImposterFrames ("Frames",  float) = 8
		_ImposterSize ("Radius", float) = 1
		_ImposterOffset ("Offset", Vector) = (0,0,0,0)
		_ImposterIsHalfSphere ("Is Half Sphere", float) = 1
		_ImposterBorderClamp ("Border Clamp", float) = 2.0
	}
	SubShader {  
		Tags { "RenderType"="Opaque" "Queue"="AlphaTest" }
		LOD 200
        ZTest LEqual
        ZWrite on
        cull back 
        blend off
        
		CGPROGRAM
		#define TEXTURE2D(x) sampler2D x
		#define _ImposterWorldNormalDepthMap _ImposterNormalMap
		#include "ImposterCommon.hlsl"
		
		#pragma surface surf Standard fullforwardshadows vertex:vert dithercrossfade 
		#pragma target 4.0
			
		half _Glossiness;
		half _Metallic;
		half _Cutoff;
		fixed4 _Color;
		
        struct Input 
        {
            float4 texCoord; 
            float4 plane0;
            float4 plane1;
            float4 plane2;
            float3 tangentWorld;
            float3 bitangentWorld;
            float3 normalWorld;
            float4 screenPos;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            ImposterData imp;
            imp.vertex = v.vertex;
            imp.uv = v.texcoord.xy;
             
            ImposterVertex(imp); 
            
            v.vertex = imp.vertex;
            
            float3 normalWorld = UnityObjectToWorldDir(v.normal.xyz);
            float3 tangentWorld = UnityObjectToWorldDir(v.tangent.xyz);
            float3x3 tangentToWorld = CreateTangentToWorldPerVertex(normalWorld, tangentWorld, v.tangent.w);
            o.tangentWorld = tangentToWorld[0];
            o.bitangentWorld = tangentToWorld[1];
            o.normalWorld = tangentToWorld[2];
            
            o.texCoord.xy = imp.uv;
            o.texCoord.zw = imp.grid;
            o.plane0 = imp.frame0;
            o.plane1 = imp.frame1;
            o.plane2 = imp.frame2;
        }

		void surf (Input IN, inout SurfaceOutputStandard o)
		{
		    UNITY_APPLY_DITHER_CROSSFADE(IN.screenPos.xy);
		    
		    ImposterData imp;
		    imp.uv = IN.texCoord.xy;
		    imp.grid = IN.texCoord.zw;
		    imp.frame0 = IN.plane0; 
		    imp.frame1 = IN.plane1;
		    imp.frame2 = IN.plane2;
		    
		    half4 baseTex;
		    half4 normalTex;
		    
		    ImposterSample(imp, baseTex, normalTex );
            baseTex.a = saturate( pow(baseTex.a, _Cutoff) );
            clip(baseTex.a - _Cutoff);
            
            half3 worldNormal = normalTex.xyz * 2 - 1;
            worldNormal = mul( unity_ObjectToWorld, half4(worldNormal, 0) ).xyz;
            
            half3 t = IN.tangentWorld;
            half3 b = IN.bitangentWorld;
            half3 n = IN.normalWorld;
        
            #if UNITY_TANGENT_ORTHONORMALIZE
                n = normalize(n);
                t = normalize (t - n * dot(t, n));
                half3 newB = cross(n, t);
                b = newB * sign (dot (newB, b));
            #endif
            half3x3 tangentToWorld = half3x3(t, b, n); 
            
            o.Normal = normalize(mul(tangentToWorld, worldNormal));
            
			o.Albedo = baseTex.rgb * _Color.rgb;
			o.Alpha = baseTex.a;
			o.Metallic = _Metallic;
			o.Smoothness = _Glossiness;
			o.Occlusion = 1;	
		}
		ENDCG
	}
	FallBack "Diffuse"
}
