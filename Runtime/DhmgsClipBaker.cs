#if UNITY_EDITOR
using System;

using UnityEditor;
using UnityEditor.Animations;

using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 베이크된 DhmgsMotionAsset을 모델 위에서 구동해 AnimationClip으로 굽는다.
	/// 카메라 정합 배치는 제외하고 DhmgsAvatarPlayer.ApplyPose로 범용 관절 포즈만 기록한다.
	/// GameObjectRecorder/HumanPoseHandler는 에디터 전용이라 #if UNITY_EDITOR로 감싼다(에디터 전용 도구).
	/// - Generic : GameObjectRecorder로 본 트랜스폼 녹화(그 리그 전용).
	/// - Humanoid: HumanPoseHandler로 muscle 추출(휴머노이드 리타게팅, jaw/eye 일부 소실).
	/// </summary>
	public static class DhmgsClipBaker
	{
		private sealed class HumanoidClipPair
		{
			public AnimationClip inPlace;
			public AnimationClip rootMotion;
			public string error;
		}

		public static AnimationClip BakeGeneric(DhmgsMotionAsset asset, GameObject model, out string error)
		{
			if (!Validate(asset, model, out error))
			{
				return null;
			}
			return BakeOnInstance(asset, model, (player, instance) => RecordGeneric(asset, player, instance));
		}

		public static AnimationClip BakeHumanoid(DhmgsMotionAsset asset, GameObject model, out string error)
		{
			if (!BakeHumanoidVariants(asset, model, out AnimationClip inPlace, out _, out error))
			{
				return null;
			}
			return inPlace;
		}

		/// <summary>
		/// 같은 Humanoid 샘플에서 제자리용과 Root Motion용 클립을 함께 만든다.
		/// 두 클립 모두 첫 프레임의 Avatar 발바닥을 Y=0으로 정렬하며,
		/// Root Motion 클립만 카메라 절대 위치를 제거한 pelvis 상대 이동을 보존한다.
		/// </summary>
		public static bool BakeHumanoidVariants(DhmgsMotionAsset asset, GameObject model,
			out AnimationClip inPlace, out AnimationClip rootMotion, out string error)
		{
			inPlace = null;
			rootMotion = null;
			if (!Validate(asset, model, out error))
			{
				return false;
			}
			Avatar avatar = ResolveHumanoidAvatar(model, out error);
			if (avatar == null)
			{
				return false;
			}

			HumanoidClipPair pair = BakeOnInstance(asset, model,
				(player, instance) => RecordHumanoidVariants(asset, player, instance, avatar));
			if (pair == null || pair.inPlace == null || pair.rootMotion == null)
			{
				error = pair != null && !string.IsNullOrEmpty(pair.error)
					? pair.error
					: "Humanoid 클립 생성에 실패했습니다.";
				return false;
			}

			inPlace = pair.inPlace;
			rootMotion = pair.rootMotion;
			error = null;
			return true;
		}

		private static bool Validate(DhmgsMotionAsset asset, GameObject model, out string error)
		{
			error = null;
			if (asset == null || !asset.IsValid)
			{
				error = "모션 자산이 비었거나 유효하지 않음";
				return false;
			}
			if (model == null)
			{
				error = "모델 GameObject가 없음";
				return false;
			}
			if (asset.fps <= 0f)
			{
				error = $"fps가 유효하지 않음: {asset.fps}";
				return false;
			}
			return true;
		}

		private static Avatar ResolveHumanoidAvatar(GameObject model, out string error)
		{
			error = null;
			Animator animator = model.GetComponentInChildren<Animator>(true);
			Avatar avatar = animator != null ? animator.avatar : null;
			if (avatar == null || !avatar.isValid || !avatar.isHuman)
			{
				error = "모델이 Humanoid가 아닙니다. FBX의 Rig를 Humanoid로 임포트했는지 확인하세요.";
				return null;
			}
			return avatar;
		}

		private static T BakeOnInstance<T>(DhmgsMotionAsset asset, GameObject model, Func<DhmgsAvatarPlayer, GameObject, T> record)
		{
			GameObject instance = UnityEngine.Object.Instantiate(model);
			try
			{
				instance.transform.position = Vector3.zero;
				instance.transform.rotation = Quaternion.identity;
				instance.transform.localScale = Vector3.one;

				DhmgsAvatarPlayer player = instance.AddComponent<DhmgsAvatarPlayer>();
				player.Bind(instance.transform);
				return record(player, instance);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(instance);
			}
		}

		private static AnimationClip RecordGeneric(DhmgsMotionAsset asset, DhmgsAvatarPlayer player, GameObject instance)
		{
			GameObjectRecorder recorder = new GameObjectRecorder(instance);
			recorder.BindComponentsOfType<Transform>(instance, true);

			float frameDuration = 1f / asset.fps;
			for (int f = 0; f < asset.frameCount; f++)
			{
				player.ApplyPose(asset, f);
				recorder.TakeSnapshot(frameDuration);
			}

			AnimationClip clip = new AnimationClip { frameRate = asset.fps };
			recorder.SaveToClip(clip);
			return clip;
		}

		private static HumanoidClipPair RecordHumanoidVariants(DhmgsMotionAsset asset, DhmgsAvatarPlayer player,
			GameObject instance, Avatar avatar)
		{
			Animator animator = instance.GetComponentInChildren<Animator>(true);
			if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
			{
				return new HumanoidClipPair { error = "베이크 인스턴스에서 유효한 Humanoid Animator를 찾지 못했습니다." };
			}

			Transform root = player.Root;
			root.position = Vector3.zero;
			root.rotation = Quaternion.identity;
			player.ApplyPose(asset, 0);
			if (!TryGetLowestFootBottomY(animator, out float firstFrameGroundY))
			{
				return new HumanoidClipPair { error = "Humanoid Avatar에서 왼발/오른발 Transform을 찾지 못했습니다." };
			}

			// 첫 프레임의 가장 낮은 발바닥을 Unity 지면(Y=0)에 놓는 클립 공통 오프셋.
			// 매 프레임 다시 맞추지 않으므로 점프와 pelvis 상하 이동은 그대로 유지된다.
			float groundOffsetY = -firstFrameGroundY;
			Vector3 firstPelvisPosition = asset.pelvisPositions[0];
			HumanPoseHandler poseHandler = new HumanPoseHandler(avatar, instance.transform);
			HumanPose humanPose = new HumanPose();

			int frameCount = asset.frameCount;
			int muscleCount = HumanTrait.MuscleCount;
			float frameDuration = 1f / asset.fps;

			Keyframe[] inPlacePositionX = new Keyframe[frameCount];
			Keyframe[] inPlacePositionY = new Keyframe[frameCount];
			Keyframe[] inPlacePositionZ = new Keyframe[frameCount];
			Keyframe[] rootMotionPositionX = new Keyframe[frameCount];
			Keyframe[] rootMotionPositionY = new Keyframe[frameCount];
			Keyframe[] rootMotionPositionZ = new Keyframe[frameCount];
			Keyframe[] rootRotationX = new Keyframe[frameCount];
			Keyframe[] rootRotationY = new Keyframe[frameCount];
			Keyframe[] rootRotationZ = new Keyframe[frameCount];
			Keyframe[] rootRotationW = new Keyframe[frameCount];
			Keyframe[][] muscleKeyframes = new Keyframe[muscleCount][];
			for (int muscleIndex = 0; muscleIndex < muscleKeyframes.Length; muscleIndex++)
			{
				muscleKeyframes[muscleIndex] = new Keyframe[frameCount];
			}

			for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
			{
				float time = frameIndex * frameDuration;
				Vector3 relativePelvisPosition = asset.pelvisPositions[frameIndex] - firstPelvisPosition;

				// In-Place는 XZ 이동만 제거한다. Y 변화는 앉기/점프/운동 자세에 필요하므로 보존한다.
				root.position = new Vector3(0f, groundOffsetY + relativePelvisPosition.y, 0f);
				root.rotation = Quaternion.identity;
				player.ApplyPose(asset, frameIndex);
				poseHandler.GetHumanPose(ref humanPose);

				Vector3 bodyPosition = humanPose.bodyPosition;
				Quaternion bodyRotation = humanPose.bodyRotation;
				inPlacePositionX[frameIndex] = new Keyframe(time, bodyPosition.x);
				inPlacePositionY[frameIndex] = new Keyframe(time, bodyPosition.y);
				inPlacePositionZ[frameIndex] = new Keyframe(time, bodyPosition.z);
				rootRotationX[frameIndex] = new Keyframe(time, bodyRotation.x);
				rootRotationY[frameIndex] = new Keyframe(time, bodyRotation.y);
				rootRotationZ[frameIndex] = new Keyframe(time, bodyRotation.z);
				rootRotationW[frameIndex] = new Keyframe(time, bodyRotation.w);
				for (int muscleIndex = 0; muscleIndex < muscleCount; muscleIndex++)
				{
					muscleKeyframes[muscleIndex][frameIndex] = new Keyframe(time, humanPose.muscles[muscleIndex]);
				}

				// Root Motion은 카메라로부터의 절대 위치를 버리고 첫 프레임 기준 상대 XZ 이동만 추가한다.
				root.position = new Vector3(relativePelvisPosition.x,
					groundOffsetY + relativePelvisPosition.y, relativePelvisPosition.z);
				poseHandler.GetHumanPose(ref humanPose);
				bodyPosition = humanPose.bodyPosition;
				rootMotionPositionX[frameIndex] = new Keyframe(time, bodyPosition.x);
				rootMotionPositionY[frameIndex] = new Keyframe(time, bodyPosition.y);
				rootMotionPositionZ[frameIndex] = new Keyframe(time, bodyPosition.z);
			}

			AnimationClip inPlaceClip = CreateHumanoidClip(asset.fps,
				inPlacePositionX, inPlacePositionY, inPlacePositionZ,
				rootRotationX, rootRotationY, rootRotationZ, rootRotationW, muscleKeyframes, true);
			AnimationClip rootMotionClip = CreateHumanoidClip(asset.fps,
				rootMotionPositionX, rootMotionPositionY, rootMotionPositionZ,
				rootRotationX, rootRotationY, rootRotationZ, rootRotationW, muscleKeyframes, false);

			return new HumanoidClipPair { inPlace = inPlaceClip, rootMotion = rootMotionClip };
		}

		private static AnimationClip CreateHumanoidClip(float frameRate,
			Keyframe[] positionX, Keyframe[] positionY, Keyframe[] positionZ,
			Keyframe[] rotationX, Keyframe[] rotationY, Keyframe[] rotationZ, Keyframe[] rotationW,
			Keyframe[][] muscleKeyframes, bool inPlace)
		{
			AnimationClip clip = new AnimationClip { frameRate = frameRate };
			SetAnimatorCurve(clip, "RootT.x", positionX);
			SetAnimatorCurve(clip, "RootT.y", positionY);
			SetAnimatorCurve(clip, "RootT.z", positionZ);
			SetAnimatorCurve(clip, "RootQ.x", rotationX);
			SetAnimatorCurve(clip, "RootQ.y", rotationY);
			SetAnimatorCurve(clip, "RootQ.z", rotationZ);
			SetAnimatorCurve(clip, "RootQ.w", rotationW);
			for (int muscleIndex = 0; muscleIndex < muscleKeyframes.Length; muscleIndex++)
			{
				SetAnimatorCurve(clip, MuscleCurveProperty(HumanTrait.MuscleName[muscleIndex]), muscleKeyframes[muscleIndex]);
			}

			clip.EnsureQuaternionContinuity();
			ConfigureRootTransform(clip, inPlace);
			return clip;
		}

		private static bool TryGetLowestFootBottomY(Animator animator, out float groundY)
		{
			groundY = float.PositiveInfinity;
			Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
			Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
			if (leftFoot != null)
			{
				Vector3 bottom = leftFoot.position + leftFoot.rotation *
					new Vector3(0f, -Mathf.Max(0f, animator.leftFeetBottomHeight), 0f);
				groundY = Mathf.Min(groundY, bottom.y);
			}
			if (rightFoot != null)
			{
				Vector3 bottom = rightFoot.position + rightFoot.rotation *
					new Vector3(0f, -Mathf.Max(0f, animator.rightFeetBottomHeight), 0f);
				groundY = Mathf.Min(groundY, bottom.y);
			}
			return !float.IsInfinity(groundY) && !float.IsNaN(groundY);
		}

		private static void ConfigureRootTransform(AnimationClip clip, bool inPlace)
		{
			// AnimationClipSettings에는 공개 setter가 없으므로 Unity가 직렬화하는 동일 필드를 설정한다.
			// m_LoopBlend*는 Animation Importer의 "Bake Into Pose"에 해당한다.
			SerializedObject serializedClip = new SerializedObject(clip);
			SerializedProperty settings = serializedClip.FindProperty("m_AnimationClipSettings");
			if (settings == null)
			{
				return;
			}

			SetBool(settings, "m_LoopBlendOrientation", inPlace);
			SetBool(settings, "m_LoopBlendPositionY", inPlace);
			SetBool(settings, "m_LoopBlendPositionXZ", inPlace);
			SetBool(settings, "m_KeepOriginalOrientation", false);
			// 이 클립은 ModelImporter를 거치지 않는 native .anim이며, RootT.y에 이미 지면 정렬된
			// Humanoid Body 높이가 기록되어 있다. Original Y를 보존하지 않으면 m_Level=0 기준으로
			// 첫 Body 높이가 제거되어 재생 시 아바타가 지면 아래로 내려간다.
			SetBool(settings, "m_KeepOriginalPositionY", true);
			SetBool(settings, "m_KeepOriginalPositionXZ", false);
			SetBool(settings, "m_HeightFromFeet", false);
			serializedClip.ApplyModifiedPropertiesWithoutUndo();
		}

		private static void SetBool(SerializedProperty parent, string relativeName, bool value)
		{
			SerializedProperty property = parent.FindPropertyRelative(relativeName);
			if (property != null)
			{
				property.boolValue = value;
			}
		}

		private static readonly string[] MuscleSides = { "Left", "Right" };
		private static readonly string[] MuscleFingers = { "Thumb", "Index", "Middle", "Ring", "Little" };

		// 손가락 muscle은 AnimationClip 곡선 바인딩 이름이 점 표기다.
		// HumanTrait.MuscleName "Left Thumb 1 Stretched" → 곡선명 "LeftHand.Thumb.1 Stretched".
		// 몸통 muscle은 이름이 일치하므로 그대로 둔다(이미 점 표기인 경우에도 "Left "로 시작 안 해 무변).
		private static string MuscleCurveProperty(string muscleName)
		{
			foreach (string side in MuscleSides)
			{
				string sidePrefix = side + " ";
				if (!muscleName.StartsWith(sidePrefix, StringComparison.Ordinal))
				{
					continue;
				}
				foreach (string finger in MuscleFingers)
				{
					string fingerPrefix = sidePrefix + finger + " ";
					if (muscleName.StartsWith(fingerPrefix, StringComparison.Ordinal))
					{
						string rest = muscleName.Substring(fingerPrefix.Length);
						return $"{side}Hand.{finger}.{rest}";
					}
				}
			}
			return muscleName;
		}

		private static void SetAnimatorCurve(AnimationClip clip, string property, Keyframe[] keys)
		{
			EditorCurveBinding binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), property);
			AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
		}
	}
}
#endif
