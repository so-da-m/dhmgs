Shader "Dhmgs/ClothesTransparent"
{
	// 의상용 투명 셰이더. 내용은 Dhmgs/SkinTransparent와 동일하나 렌더 큐만 한 단계 뒤(Transparent+100).
	// 이렇게 하면 피부보다 항상 나중에 그려져, 피부가 버퍼에 먼저 있고 그 위로 블렌드된다(피부가 비침).
	// _Opacity 프로퍼티명은 피부와 동일하므로 DhmgsSkinOpacity가 그대로 동작한다.
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

		// 1) 깊이 prime: 가장 앞면의 깊이만 기록 → 뒷면/안쪽이 색칠에서 탈락(단일 레이어).
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

			struct Attributes
			{
				float4 positionOS : POSITION;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				return half4(0, 0, 0, 0);
			}
			ENDHLSL
		}

		// 2) 색상: prime이 기록한 앞면만 통과(ZTest LEqual), 뒤(피부) 위로 알파 블렌드.
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
				half3 lighting = mainLight.color * nl + SampleSH(normalWS);

				half3 rgb = baseColor.rgb * lighting;
				half alpha = baseColor.a * _Opacity;
				return half4(rgb, alpha);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
