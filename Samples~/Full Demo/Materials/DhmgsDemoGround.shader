Shader "Dhmgs/DemoGround"
{
	Properties
	{
		_BaseColor ("Base Color", Color) = (0.38, 0.38, 0.38, 0.65)
		_GridColor ("Grid Color", Color) = (0.26, 0.26, 0.26, 1)
		_MajorGridColor ("Major Grid Color", Color) = (0.16, 0.16, 0.16, 1)
		_GridSpacing ("Grid Spacing", Float) = 0.5
		_MajorGridSpacing ("Major Grid Spacing", Float) = 1
		_GridWidth ("Grid Width", Float) = 0.008
		_MajorGridWidth ("Major Grid Width", Float) = 0.015
		_ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.75
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent"
			"Queue" = "Transparent+10"
			"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "GroundForward"
			Tags { "LightMode" = "UniversalForward" }
			Cull Off
			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#pragma multi_compile_fog
			#define _SURFACE_TYPE_TRANSPARENT 1
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			CBUFFER_START(UnityPerMaterial)
				half4 _BaseColor;
				half4 _GridColor;
				half4 _MajorGridColor;
				float _GridSpacing;
				float _MajorGridSpacing;
				float _GridWidth;
				float _MajorGridWidth;
				half _ShadowStrength;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half fogFactor : TEXCOORD1;
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
				OUT.positionHCS = positionInputs.positionCS;
				OUT.positionWS = positionInputs.positionWS;
				OUT.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
				return OUT;
			}

			half GridLine(float2 position, float spacing, float width)
			{
				spacing = max(spacing, 0.001);
				float2 distanceToLine = abs(frac(position / spacing + 0.5) - 0.5) * spacing;
				float2 pixelWidth = max(fwidth(position), 0.0001);
				float halfWidth = max(width, 0.0) * 0.5;
				float2 coverage = 1.0 - smoothstep(halfWidth, halfWidth + pixelWidth, distanceToLine);
				return max(coverage.x, coverage.y);
			}

			half4 frag(Varyings IN) : SV_Target
			{
				half minorGrid = GridLine(IN.positionWS.xz, _GridSpacing, _GridWidth);
				half majorGrid = GridLine(IN.positionWS.xz, _MajorGridSpacing, _MajorGridWidth);
				half3 color = lerp(_BaseColor.rgb, _GridColor.rgb, minorGrid);
				color = lerp(color, _MajorGridColor.rgb, majorGrid);
				Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
				color *= lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);
				return half4(MixFog(color, IN.fogFactor), _BaseColor.a);
			}
			ENDHLSL
		}
	}
}
