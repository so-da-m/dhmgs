using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal static class DhmgsMotionDecoder
	{
		internal const int BoneCount = 54;

		private const int JawIndex = 21;
		private const int LeftEyeIndex = 22;
		private const int RightEyeIndex = 23;
		private const int LeftHandStart = 24;
		private const int RightHandStart = 39;

		private const string JawBone = "jaw";
		private const string LeftEyeBone = "left_eye_smplhf";
		private const string RightEyeBone = "right_eye_smplhf";

		private static readonly Quaternion RootCorrection = Quaternion.Euler(180f, 180f, 0f);

		public static DhmgsMotionAsset Decode(
			string sourceId,
			DhmgsManifestDto manifest,
			DhmgsSequenceDto sequence)
		{
			int frameCount = sequence.Frames.Length;
			DhmgsMotionAsset motion = ScriptableObject.CreateInstance<DhmgsMotionAsset>();
			motion.name = $"dhmgs-{sourceId}";
			motion.attemptId = sourceId;
			motion.fps = manifest.Fps;
			motion.frameCount = frameCount;
			motion.boneCount = BoneCount;
			motion.boneNames = BuildBoneNames(manifest);
			motion.boneRotations = new Quaternion[frameCount * BoneCount];
			motion.pelvisPositions = new Vector3[frameCount];
			motion.pelvisRotations = new Quaternion[frameCount];

			for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
			{
				SmplxFrameDto frame = sequence.Frames[frameIndex];
				int baseIndex = frameIndex * BoneCount;

				for (int bodyIndex = 0; bodyIndex < 21; bodyIndex++)
				{
					motion.boneRotations[baseIndex + bodyIndex] = JointRotation(frame.Body[bodyIndex]);
				}

				motion.boneRotations[baseIndex + JawIndex] = JointRotation(frame.Jaw);
				motion.boneRotations[baseIndex + LeftEyeIndex] = JointRotation(frame.LeftEye);
				motion.boneRotations[baseIndex + RightEyeIndex] = JointRotation(frame.RightEye);

				for (int handIndex = 0; handIndex < 15; handIndex++)
				{
					motion.boneRotations[baseIndex + LeftHandStart + handIndex] =
						HandRotation(frame.LeftHand[handIndex], manifest.LeftHandOffset, handIndex);
					motion.boneRotations[baseIndex + RightHandStart + handIndex] =
						HandRotation(frame.RightHand[handIndex], manifest.RightHandOffset, handIndex);
				}

				motion.pelvisRotations[frameIndex] = RootCorrection * JointRotation(frame.Root);
				motion.pelvisPositions[frameIndex] = PositionToUnity(frame.Translation);
			}

			motion.imageWidth = manifest.ImageWidth;
			motion.imageHeight = manifest.ImageHeight;
			motion.focalLength = manifest.FocalLength;
			motion.principalPoint = manifest.PrincipalPoint;
			return motion;
		}

		internal static Vector3 AxisAngleToUnity(Vector3 raw)
		{
			return new Vector3(raw.x, -raw.y, -raw.z);
		}

		private static Vector3 PositionToUnity(Vector3 raw)
		{
			return new Vector3(raw.x, -raw.y, raw.z);
		}

		private static Quaternion JointRotation(Vector3 rawAxisAngle)
		{
			return AxisAngleToQuaternion(AxisAngleToUnity(rawAxisAngle));
		}

		private static Quaternion HandRotation(Vector3 rawAxisAngle, float[] offset, int jointIndex)
		{
			int offsetIndex = jointIndex * 3;
			Vector3 nativeWithOffset = new Vector3(
				rawAxisAngle.x + offset[offsetIndex],
				rawAxisAngle.y + offset[offsetIndex + 1],
				rawAxisAngle.z + offset[offsetIndex + 2]);
			return AxisAngleToQuaternion(AxisAngleToUnity(nativeWithOffset));
		}

		private static Quaternion AxisAngleToQuaternion(Vector3 axisAngle)
		{
			float angle = axisAngle.magnitude;
			if (angle < 1e-6f)
			{
				return Quaternion.identity;
			}
			return Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axisAngle / angle);
		}

		private static string[] BuildBoneNames(DhmgsManifestDto manifest)
		{
			string[] names = new string[BoneCount];
			for (int bodyIndex = 0; bodyIndex < 21; bodyIndex++)
			{
				names[bodyIndex] = manifest.BodyJointNames[bodyIndex];
			}
			names[JawIndex] = JawBone;
			names[LeftEyeIndex] = LeftEyeBone;
			names[RightEyeIndex] = RightEyeBone;
			for (int handIndex = 0; handIndex < 15; handIndex++)
			{
				names[LeftHandStart + handIndex] = manifest.LeftHandJointNames[handIndex];
				names[RightHandStart + handIndex] = manifest.RightHandJointNames[handIndex];
			}
			return names;
		}
	}
}
