using UnityEngine;

namespace Mnnh.Dhmgs
{
	public class DhmgsMotionAsset : ScriptableObject
	{
		public string attemptId;
		public float fps;
		public int frameCount;
		public int boneCount;

		public string[] boneNames;

		public Quaternion[] boneRotations;

		public Vector3[] pelvisPositions;

		public Quaternion[] pelvisRotations;

		public int imageWidth;
		public int imageHeight;
		public Vector2 focalLength;
		public Vector2 principalPoint;

		public bool HasProjection =>
			imageWidth > 0 && imageHeight > 0 && focalLength.x > 0f && focalLength.y > 0f;

		public bool IsValid =>
			frameCount > 0
			&& boneCount > 0
			&& boneNames != null && boneNames.Length == boneCount
			&& boneRotations != null && boneRotations.Length == frameCount * boneCount
			&& pelvisPositions != null && pelvisPositions.Length == frameCount
			&& pelvisRotations != null && pelvisRotations.Length == frameCount;
	}
}
