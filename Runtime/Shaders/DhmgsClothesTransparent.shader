Shader "Dhmgs/ClothesTransparent"
{
	Properties
	{
		_BaseMap ("Base Map", 2D) = "white" {}
		_BaseColor ("Base Color", Color) = (1, 1, 1, 1)
		_Opacity ("Opacity", Range(0, 1)) = 1
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent"
			"Queue" = "Transparent+100"
			"RenderPipeline" = "UniversalPipeline"
		}

		Cull Back

		Pass
		{
			Name "ClothesDepthPrime"
			Tags { "LightMode" = "SRPDefaultUnlit" }

			ZWrite On
			ZTest LEqual
			ColorMask 0

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseMap_ST;
				half4 _BaseColor;
				half _Opacity;
			CBUFFER_END

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
			Name "ClothesForward"
			Tags { "LightMode" = "UniversalForward" }

			ZWrite Off
			ZTest LEqual
			Blend SrcAlpha OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseMap_ST;
				half4 _BaseColor;
				half _Opacity;
			CBUFFER_END

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
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
				OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

				half3 normalWS = normalize(IN.normalWS);
				Light mainLight = GetMainLight();
				half nl = saturate(dot(normalWS, mainLight.direction));
				half3 lighting = max(mainLight.color * nl + SampleSH(normalWS),
					half3(0.35h, 0.35h, 0.35h));

				half3 rgb = baseColor.rgb * lighting;
				half alpha = baseColor.a * _Opacity;
				return half4(rgb, alpha);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
