Shader "Dhmgs/SkinTransparent"
{
	// SMPL-X 피부용 투명 셰이더. 2-pass(깊이 prime + 색상)로 셀프 오버랩 없는 단일 레이어 투명.
	// _Opacity로 투명도 조절(슬라이더 연결용). 영상이 뒤에 있으면 낮출수록 영상이 비친다(onion-skin).
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
			"Queue" = "Transparent"
			"RenderPipeline" = "UniversalPipeline"
		}

		Cull Back

		// 1) 깊이 prime: 가장 앞면의 깊이만 기록 → 뒷면/안쪽이 색칠에서 탈락(단일 레이어).
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

		// 2) 색상: prime이 기록한 앞면만 통과(ZTest LEqual), 뒤(영상) 위로 알파 블렌드.
		Pass
		{
			Name "SkinForward"
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
