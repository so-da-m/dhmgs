using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	public static class DhmgsHandMeanReference
	{
		public static readonly float[] LeftHandMean =
		{
			0.11167871f, -0.04289218f, 0.41644183f,
			0.10881133f, 0.06598568f, 0.75621998f,
			-0.09639297f, 0.09091566f, 0.18845929f,
			-0.11809504f, -0.05094385f, 0.52958453f,
			-0.14369841f, -0.05524170f, 0.70485711f,
			-0.01918292f, 0.09233685f, 0.33791351f,
			-0.45703298f, 0.19628395f, 0.62545753f,
			-0.21465237f, 0.06599829f, 0.50689423f,
			-0.36972436f, 0.06034463f, 0.07949023f,
			-0.14186969f, 0.08585263f, 0.63552827f,
			-0.30334160f, 0.05788098f, 0.63138920f,
			-0.17612089f, 0.13209307f, 0.37335458f,
			0.85096431f, -0.27692273f, 0.09154807f,
			-0.49983943f, -0.02655647f, -0.05288088f,
			0.53555918f, -0.04596104f, 0.27735803f,
		};

		public static readonly float[] RightHandMean =
		{
			0.11167871f, 0.04289218f, -0.41644183f,
			0.10881133f, -0.06598568f, -0.75621998f,
			-0.09639297f, -0.09091566f, -0.18845929f,
			-0.11809504f, 0.05094385f, -0.52958453f,
			-0.14369841f, 0.05524170f, -0.70485711f,
			-0.01918292f, -0.09233685f, -0.33791351f,
			-0.45703298f, -0.19628395f, -0.62545753f,
			-0.21465237f, -0.06599829f, -0.50689423f,
			-0.36972436f, -0.06034463f, -0.07949023f,
			-0.14186969f, -0.08585263f, -0.63552827f,
			-0.30334160f, -0.05788098f, -0.63138920f,
			-0.17612089f, -0.13209307f, -0.37335458f,
			0.85096431f, 0.27692273f, -0.09154807f,
			-0.49983943f, 0.02655647f, 0.05288088f,
			0.53555918f, 0.04596104f, -0.27735803f,
		};

		public static float MaxDiff(float[] nativeOffset, float[] reference)
		{
			float maxDiff = 0f;
			int jointCount = Mathf.Min(nativeOffset.Length, reference.Length) / 3;
			for (int j = 0; j < jointCount; j++)
			{
				int b = j * 3;
				Vector3 native = new Vector3(nativeOffset[b], nativeOffset[b + 1], nativeOffset[b + 2]);
				Vector3 unity = DhmgsMotionDecoder.AxisAngleToUnity(native);
				maxDiff = Mathf.Max(maxDiff, Mathf.Abs(unity.x - reference[b]));
				maxDiff = Mathf.Max(maxDiff, Mathf.Abs(unity.y - reference[b + 1]));
				maxDiff = Mathf.Max(maxDiff, Mathf.Abs(unity.z - reference[b + 2]));
			}
			return maxDiff;
		}
	}
}
