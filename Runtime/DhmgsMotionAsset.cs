using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 베이크된 SMPL-X 모션. 변환이 끝난 Unity 본 회전만 담는 순수 데이터(허브).
	/// 플레이어와 클립 베이커가 이 자산 하나만 소비하므로 좌표 변환 로직은 런타임에 없다.
	/// boneRotations는 평탄 배열: index = frame * boneCount + bone.
	/// </summary>
	public class DhmgsMotionAsset : ScriptableObject
	{
		public string attemptId;
		public float fps;
		public int frameCount;
		public int boneCount;

		/// <summary>적용 순서대로의 본 이름. 길이 = boneCount. body/hands는 manifest joint_names, jaw/eye는 고정명.</summary>
		public string[] boneNames;

		/// <summary>평탄 본 로컬 회전. 길이 = frameCount * boneCount. (pelvis 제외)</summary>
		public Quaternion[] boneRotations;

		/// <summary>프레임별 pelvis 로컬 위치. 길이 = frameCount.</summary>
		public Vector3[] pelvisPositions;

		/// <summary>프레임별 pelvis 로컬 회전(RootCorrection 포함). 길이 = frameCount.</summary>
		public Quaternion[] pelvisRotations;

		// === 카메라 오버레이용 intrinsics (manifest.projection, 픽셀 단위 raw) ===
		public int imageWidth;
		public int imageHeight;
		public Vector2 focalLength;     // fx, fy
		public Vector2 principalPoint;  // cx, cy

		/// <summary>오버레이에 쓸 projection 정보가 유효한지.</summary>
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
