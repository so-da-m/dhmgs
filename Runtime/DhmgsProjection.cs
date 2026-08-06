using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 핀홀 intrinsics → Unity Camera.projectionMatrix 및 프러스텀 코너 계산(순수 수학, 씬/카메라 의존 없음).
	/// projection 레시피는 검증된 SmplxPlayer.ApplyCameraFOV(off-center cx/cy + y-flip)에서 포팅.
	/// </summary>
	public static class DhmgsProjection
	{
		/// <summary>fx,fy,cx,cy(픽셀) + 영상 w,h + near/far로 projection matrix 구성.</summary>
		public static Matrix4x4 BuildProjectionMatrix(float fx, float fy, float cx, float cy, float width, float height, float near, float far)
		{
			float cyUnity = height - cy; // OpenCV y-down → Unity y-up
			Matrix4x4 proj = Matrix4x4.zero;
			proj.m00 = 2f * fx / width;
			proj.m11 = 2f * fy / height;
			proj.m02 = (2f * cx / width) - 1f;
			proj.m12 = (2f * cyUnity / height) - 1f;
			proj.m22 = -(far + near) / (far - near);
			proj.m23 = -2f * far * near / (far - near);
			proj.m32 = -1f;
			return proj;
		}

		/// <summary>
		/// projection으로 정의된 프러스텀의 depth(카메라 앞 거리, &gt;0) 평면 4코너를 카메라 로컬좌표로.
		/// 순서: 좌하, 우하, 좌상, 우상. off-center면 비대칭으로 나와 프러스텀을 정확히 채운다.
		/// proj.inverse로 NDC 모서리를 view space로 unproject한 뒤 depth 평면에 투영, view(-Z) → local(+Z) 변환.
		/// </summary>
		public static Vector3[] FrustumCornersAtDepth(Matrix4x4 proj, float depth)
		{
			Matrix4x4 inv = proj.inverse;
			Vector2[] ndc =
			{
				new Vector2(-1f, -1f),
				new Vector2(1f, -1f),
				new Vector2(-1f, 1f),
				new Vector2(1f, 1f),
			};

			Vector3[] corners = new Vector3[4];
			for (int i = 0; i < 4; i++)
			{
				// near 평면(z_ndc = -1)에서 한 점을 view space로 unproject.
				Vector4 clip = new Vector4(ndc[i].x, ndc[i].y, -1f, 1f);
				Vector4 v = inv * clip;
				Vector3 view = new Vector3(v.x / v.w, v.y / v.w, v.z / v.w);

				// 원점에서의 광선을 view z = -depth 평면으로 스케일(view space는 -Z가 정면).
				float scale = -depth / view.z;
				Vector3 viewPoint = view * scale;

				// view(-Z 정면) → 카메라 로컬(+Z 정면): z 부호 반전.
				corners[i] = new Vector3(viewPoint.x, viewPoint.y, -viewPoint.z);
			}
			return corners;
		}
	}
}
