using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// SMPL-X(Y-down, 오른손, axis-angle rad) → Unity 본 회전 변환의 단일 진실원.
	/// DHGSConventions.SmplxYDownRhsCoordinate + SmplxPoseMath에서 검증된 수학을 그대로 옮겼다.
	/// manifest + sequence를 받아 DhmgsMotionAsset을 채운다(변환은 여기서 1회만).
	/// </summary>
	public static class SmplxToUnity
	{
		// SMPL-X 리그 본 이름 (SMPLX.cs JOINT_NAMES와 일치). 눈은 _smplhf 접미사.
		private const string JawBone = "jaw";
		private const string LeftEyeBone = "left_eye_smplhf";
		private const string RightEyeBone = "right_eye_smplhf";

		// body(21) + jaw + leftEye + rightEye + lhand(15) + rhand(15)
		public const int BoneCount = 54;

		// 적용 순서상 face 본 시작 인덱스
		private const int JawIndex = 21;
		private const int LeftEyeIndex = 22;
		private const int RightEyeIndex = 23;
		private const int LeftHandStart = 24;
		private const int RightHandStart = 39;

		private static readonly Quaternion RootCorrection = Quaternion.Euler(180f, 180f, 0f);

		/// <summary>축-각(라디안)을 Unity 좌표로: (x, -y, -z).</summary>
		public static Vector3 AxisAngleToUnity(Vector3 raw)
		{
			return new Vector3(raw.x, -raw.y, -raw.z);
		}

		/// <summary>위치를 Unity 좌표로: (x, -y, z).</summary>
		private static Vector3 PositionToUnity(Vector3 raw)
		{
			return new Vector3(raw.x, -raw.y, raw.z);
		}

		/// <summary>Unity 좌표 축-각(rad)을 quaternion으로.</summary>
		private static Quaternion AxisAngleToQuat(Vector3 unityAxisAngle)
		{
			float angle = unityAxisAngle.magnitude;
			if (angle < 1e-6f)
			{
				return Quaternion.identity;
			}
			Vector3 axis = unityAxisAngle / angle;
			return Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis);
		}

		/// <summary>manifest + sequence로 SO를 채운다. 호출 전 asset은 빈 ScriptableObject.</summary>
		public static void Fill(DhmgsMotionAsset asset, string attemptId, DhmgsManifest manifest, DhmgsSequence sequence)
		{
			int frameCount = sequence.frames.Length;

			asset.attemptId = attemptId;
			asset.fps = manifest.fps;
			asset.frameCount = frameCount;
			asset.boneCount = BoneCount;
			asset.boneNames = BuildBoneNames(manifest);

			asset.boneRotations = new Quaternion[frameCount * BoneCount];
			asset.pelvisPositions = new Vector3[frameCount];
			asset.pelvisRotations = new Quaternion[frameCount];

			for (int f = 0; f < frameCount; f++)
			{
				DhmgsRawFrame frame = sequence.frames[f];
				int baseIndex = f * BoneCount;

				for (int i = 0; i < 21; i++)
				{
					asset.boneRotations[baseIndex + i] = JointQuat(frame.body[i]);
				}
				asset.boneRotations[baseIndex + JawIndex] = JointQuat(frame.jaw);
				asset.boneRotations[baseIndex + LeftEyeIndex] = JointQuat(frame.leftEye);
				asset.boneRotations[baseIndex + RightEyeIndex] = JointQuat(frame.rightEye);

				for (int j = 0; j < 15; j++)
				{
					asset.boneRotations[baseIndex + LeftHandStart + j] = HandQuat(frame.leftHand[j], manifest.leftHandOffset, j);
					asset.boneRotations[baseIndex + RightHandStart + j] = HandQuat(frame.rightHand[j], manifest.rightHandOffset, j);
				}

				asset.pelvisRotations[f] = RootCorrection * JointQuat(frame.root);
				asset.pelvisPositions[f] = PositionToUnity(frame.translation);
			}

			// 카메라 오버레이용 intrinsics는 변환 없이 픽셀 단위 그대로 운반.
			asset.imageWidth = manifest.imageWidth;
			asset.imageHeight = manifest.imageHeight;
			asset.focalLength = manifest.focalLength;
			asset.principalPoint = manifest.principalPoint;
		}

		private static string[] BuildBoneNames(DhmgsManifest manifest)
		{
			string[] names = new string[BoneCount];
			for (int i = 0; i < 21; i++)
			{
				names[i] = manifest.bodyJointNames[i];
			}
			names[JawIndex] = JawBone;
			names[LeftEyeIndex] = LeftEyeBone;
			names[RightEyeIndex] = RightEyeBone;
			for (int j = 0; j < 15; j++)
			{
				names[LeftHandStart + j] = manifest.leftHandJointNames[j];
				names[RightHandStart + j] = manifest.rightHandJointNames[j];
			}
			return names;
		}

		/// <summary>body/jaw/eye 회전: 좌표 변환 후 quaternion.</summary>
		private static Quaternion JointQuat(Vector3 rawAxisAngle)
		{
			return AxisAngleToQuat(AxisAngleToUnity(rawAxisAngle));
		}

		/// <summary>손 회전: native 공간에서 hand_pose_offset를 더한 뒤(변환 전) 좌표 변환.</summary>
		private static Quaternion HandQuat(Vector3 rawAxisAngle, float[] offset, int jointIndex)
		{
			int b = jointIndex * 3;
			Vector3 nativeWithOffset = new Vector3(
				rawAxisAngle.x + offset[b],
				rawAxisAngle.y + offset[b + 1],
				rawAxisAngle.z + offset[b + 2]);
			return AxisAngleToQuat(AxisAngleToUnity(nativeWithOffset));
		}
	}
}
