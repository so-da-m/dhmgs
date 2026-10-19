Shader "Dhmgs/SkinTransparent"
{
	Properties
	{
		_BaseMap ("Base Map", 2D) = "white" {}
		_BaseColor ("Base Color", Color) = (1, 1, 1, 1)
		_Opacity ("Opacity", Range(0, 1)) = 1
		_Smoothness ("Smoothness", Range(0, 1)) = 0.25
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent"
			"Queue" = "Transparent"
			"RenderPipeline" = "UniversalPipeline"
		}

		Cull Back

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

		TEXTURE2D(_BaseMap);
		SAMPLER(sampler_BaseMap);

		CBUFFER_START(UnityPerMaterial)
			float4 _BaseMap_ST;
			half4 _BaseColor;
			half _Opacity;
			half _Smoothness;
		CBUFFER_END
		ENDHLSL

		Pass
		{
			Name "SkinDepthPrime"
			Tags { "LightMode" = "SRPDefaultUnlit" }

			ZWrite On
			ZTest LEqual
			ColorMask 0

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a *
					_BaseColor.a * _Opacity;
				clip(alpha - 0.001h);
				return half4(0, 0, 0, 0);
			}
			ENDHLSL
		}

		Pass
		{
			Name "SkinForward"
			Tags { "LightMode" = "UniversalForward" }

			ZWrite Off
			ZTest LEqual
			Blend SrcAlpha OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#if UNITY_VERSION >= 600010
				#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#else
				#pragma multi_compile _ _FORWARD_PLUS
			#endif
			#pragma multi_compile_fog

			#define _SURFACE_TYPE_TRANSPARENT 1
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
				float3 positionWS : TEXCOORD2;
				half4 fogAndVertexLight : TEXCOORD3;
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
				OUT.positionHCS = positionInputs.positionCS;
				OUT.positionWS = positionInputs.positionWS;
				OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
				OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
				OUT.fogAndVertexLight = half4(
					ComputeFogFactor(positionInputs.positionCS.z),
					VertexLighting(positionInputs.positionWS, OUT.normalWS));
				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
				half alpha = baseColor.a * _Opacity;
				clip(alpha - 0.001h);

				InputData inputData = (InputData)0;
				inputData.positionWS = IN.positionWS;
				inputData.normalWS = NormalizeNormalPerPixel(IN.normalWS);
				inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
				inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
				inputData.fogCoord = IN.fogAndVertexLight.x;
				inputData.vertexLighting = IN.fogAndVertexLight.yzw;
				inputData.bakedGI = SampleSH(inputData.normalWS);
				inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
				inputData.shadowMask = unity_ProbesOcclusion;

				SurfaceData surfaceData = (SurfaceData)0;
				surfaceData.albedo = baseColor.rgb;
				surfaceData.metallic = 0;
				surfaceData.smoothness = _Smoothness;
				surfaceData.normalTS = half3(0, 0, 1);
				surfaceData.occlusion = 1;
				surfaceData.alpha = alpha;

				half4 color = UniversalFragmentPBR(inputData, surfaceData);
				color.rgb = MixFog(color.rgb, inputData.fogCoord);
				return color;
			}
			ENDHLSL
		}

		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			ZWrite On
			ZTest LEqual
			ColorMask 0

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

			float3 _LightDirection;
			float3 _LightPosition;

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
				float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
				#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
					float3 lightDirectionWS = normalize(_LightPosition - positionWS);
				#else
					float3 lightDirectionWS = _LightDirection;
				#endif
				OUT.positionHCS = TransformWorldToHClip(
					ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
				#if !defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
					#if UNITY_REVERSED_Z
						OUT.positionHCS.z = min(OUT.positionHCS.z, UNITY_NEAR_CLIP_VALUE);
					#else
						OUT.positionHCS.z = max(OUT.positionHCS.z, UNITY_NEAR_CLIP_VALUE);
					#endif
				#endif
				OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a *
					_BaseColor.a * _Opacity;
				clip(alpha - 0.001h);
				return half4(0, 0, 0, 0);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
