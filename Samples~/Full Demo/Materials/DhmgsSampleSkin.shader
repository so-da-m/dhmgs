Shader "Mnnh/DHMGS Sample Skin"
{
	Properties
	{
		_Color ("Color", Color) = (0.72, 0.82, 0.92, 1)
		_Opacity ("Opacity", Range(0, 1)) = 1
	}

	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off

		Pass
		{
			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#include "UnityCG.cginc"

			struct AppData
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
			};

			struct Varyings
			{
				float4 position : SV_POSITION;
				float3 normal : TEXCOORD0;
			};

			fixed4 _Color;
			float _Opacity;

			Varyings Vert(AppData input)
			{
				Varyings output;
				output.position = UnityObjectToClipPos(input.vertex);
				output.normal = UnityObjectToWorldNormal(input.normal);
				return output;
			}

			fixed4 Frag(Varyings input) : SV_Target
			{
				float3 normal = normalize(input.normal);
				float diffuse = saturate(dot(normal, normalize(float3(-0.35, 0.75, -0.55))));
				float facing = pow(1.0 - saturate(abs(normal.z)), 2.0);
				float shade = 0.58 + diffuse * 0.34 + facing * 0.08;
				return fixed4(_Color.rgb * shade, _Color.a * saturate(_Opacity));
			}
			ENDCG
		}
	}
}
