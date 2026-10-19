using UnityEngine;

namespace Mnnh.Dhmgs
{
	public static class DhmgsProjection
	{
		public static Matrix4x4 BuildProjectionMatrix(float fx, float fy, float cx, float cy, float width, float height, float near, float far)
		{
			float cyUnity = height - cy;
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
				Vector4 clip = new Vector4(ndc[i].x, ndc[i].y, -1f, 1f);
				Vector4 v = inv * clip;
				Vector3 view = new Vector3(v.x / v.w, v.y / v.w, v.z / v.w);

				float scale = -depth / view.z;
				Vector3 viewPoint = view * scale;

				corners[i] = new Vector3(viewPoint.x, viewPoint.y, -viewPoint.z);
			}
			return corners;
		}
	}
}
