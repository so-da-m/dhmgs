#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mnnh.Dhmgs.Editor
{
	internal sealed class DhmgsHumanoidBakeArtifacts
	{
		internal AnimationClip InPlace { get; set; }
		internal AnimationClip RootMotion { get; set; }
		internal string Error { get; set; }
	}

	/// <summary>
	/// 베이크된 DhmgsMotionAsset을 모델 위에서 구동해 AnimationClip으로 굽는다.
	/// 카메라 정합 배치는 제외하고 DhmgsRigDriver.ApplyPose로 범용 관절 포즈만 기록한다.
	/// AnimationUtility/HumanPoseHandler are Editor-only, so this authoring utility is Editor-only.
	/// - Generic: writes quaternion curves only for local rotations driven by DhmgsRigDriver.ApplyPose.
	/// - Humanoid: HumanPoseHandler로 muscle 추출(휴머노이드 리타게팅, jaw/eye 일부 소실).
	/// </summary>
	internal static class DhmgsAnimationClipBaker
	{
		internal static async Task<AnimationClip> BakeGenericAsync(
			DhmgsMotionAsset asset,
			GameObject model,
			Action<float> onProgress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Validate(asset, model, out string error))
			{
				throw new InvalidOperationException(error);
			}
			return await BakeOnInstanceAsync(
				asset,
				model,
				(player, instance) => RecordGenericAsync(
					asset,
					instance,
					onProgress,
					cancellationToken),
				cancellationToken);
		}

		internal static async Task<DhmgsHumanoidBakeArtifacts> BakeHumanoidVariantsAsync(
			DhmgsMotionAsset asset,
			GameObject model,
			Action<float> onProgress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Validate(asset, model, out string error))
			{
				return new DhmgsHumanoidBakeArtifacts { Error = error };
			}
			Avatar avatar = ResolveHumanoidAvatar(model, out error);
			if (avatar == null)
			{
				return new DhmgsHumanoidBakeArtifacts { Error = error };
			}

			return await BakeOnInstanceAsync(
				asset,
				model,
				(player, instance) => RecordHumanoidVariantsAsync(
					asset,
					player,
					instance,
					avatar,
					onProgress,
					cancellationToken),
				cancellationToken);
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
				string modelPath = AssetDatabase.GetAssetPath(model);
				if (!string.IsNullOrEmpty(modelPath))
				{
					foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(modelPath))
					{
						if (candidate is Avatar imported && imported.isValid && imported.isHuman)
						{
							avatar = imported;
							break;
						}
					}
				}
			}
			if (avatar == null || !avatar.isValid || !avatar.isHuman)
			{
				error = "모델이 Humanoid가 아닙니다. FBX의 Rig를 Humanoid로 임포트했는지 확인하세요.";
				return null;
			}
			return avatar;
		}

		private static void MarkAsTransient(GameObject root)
		{
			foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
			{
				transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
			}
		}

		private static async Task<T> BakeOnInstanceAsync<T>(
			DhmgsMotionAsset asset,
			GameObject model,
			Func<DhmgsRigDriver, GameObject, Task<T>> record,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Scene previewScene = EditorSceneManager.NewPreviewScene();
			GameObject instance = null;
			try
			{
				instance = UnityEngine.Object.Instantiate(model);
				SceneManager.MoveGameObjectToScene(instance, previewScene);
				MarkAsTransient(instance);
				instance.transform.position = Vector3.zero;
				instance.transform.rotation = Quaternion.identity;
				instance.transform.localScale = Vector3.one;

				DhmgsRigDriver player = instance.GetComponent<DhmgsRigDriver>();
				if (player == null)
				{
					player = instance.AddComponent<DhmgsRigDriver>();
				}
				player.Bind(instance.transform);
				cancellationToken.ThrowIfCancellationRequested();
				return await record(player, instance);
			}
			finally
			{
				if (instance != null)
				{
					UnityEngine.Object.DestroyImmediate(instance);
				}
				if (previewScene.IsValid())
				{
					EditorSceneManager.ClosePreviewScene(previewScene);
				}
			}
		}

		private static async Task<AnimationClip> RecordGenericAsync(
			DhmgsMotionAsset asset,
			GameObject instance,
			Action<float> onProgress,
			CancellationToken cancellationToken)
		{
			DhmgsEditorYield editorYield = new DhmgsEditorYield();
			Dictionary<string, Transform> transformsByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
			foreach (Transform candidate in instance.GetComponentsInChildren<Transform>(true))
			{
				if (!transformsByName.ContainsKey(candidate.name))
				{
					transformsByName.Add(candidate.name, candidate);
				}
			}

			AnimationClip clip = new AnimationClip { frameRate = asset.fps };
			try
			{
				Dictionary<Transform, int> drivenTransforms = new Dictionary<Transform, int>();
				if (transformsByName.TryGetValue("pelvis", out Transform pelvis))
				{
					drivenTransforms[pelvis] = -1;
				}
				for (int boneIndex = 0; boneIndex < asset.boneCount; boneIndex++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					string boneName = asset.boneNames[boneIndex];
					if (!string.IsNullOrEmpty(boneName) && transformsByName.TryGetValue(boneName, out Transform bone))
					{
						drivenTransforms[bone] = boneIndex;
					}
				}

				if (drivenTransforms.Count == 0)
				{
					throw new InvalidOperationException("The SMPL-X bake source has no transforms driven by this motion.");
				}

				int drivenIndex = 0;
				foreach (KeyValuePair<Transform, int> driven in drivenTransforms)
				{
					cancellationToken.ThrowIfCancellationRequested();
					string path = AnimationUtility.CalculateTransformPath(driven.Key, instance.transform);
					Quaternion[] rotations = new Quaternion[asset.frameCount];
					Quaternion previous = Quaternion.identity;
					for (int frameIndex = 0; frameIndex < asset.frameCount; frameIndex++)
					{
						if ((frameIndex & 31) == 0)
						{
							await editorYield.CheckpointAsync(cancellationToken);
						}
						Quaternion rotation = driven.Value < 0
							? asset.pelvisRotations[frameIndex]
							: asset.boneRotations[frameIndex * asset.boneCount + driven.Value];
						if (frameIndex > 0 && Quaternion.Dot(previous, rotation) < 0f)
						{
							rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
						}
						rotations[frameIndex] = rotation;
						previous = rotation;
					}

					SetLocalRotationCurves(clip, path, rotations, asset.fps, cancellationToken);
					drivenIndex++;
					onProgress?.Invoke((float)drivenIndex / drivenTransforms.Count);
					await editorYield.CheckpointAsync(cancellationToken);
				}

				clip.EnsureQuaternionContinuity();
				cancellationToken.ThrowIfCancellationRequested();
				onProgress?.Invoke(1f);
				return clip;
			}
			catch
			{
				UnityEngine.Object.DestroyImmediate(clip);
				throw;
			}
		}

		private static async Task<DhmgsHumanoidBakeArtifacts> RecordHumanoidVariantsAsync(
			DhmgsMotionAsset asset,
			DhmgsRigDriver player,
			GameObject instance,
			Avatar avatar,
			Action<float> onProgress,
			CancellationToken cancellationToken)
		{
			DhmgsEditorYield editorYield = new DhmgsEditorYield();
			cancellationToken.ThrowIfCancellationRequested();
			Animator animator = instance.GetComponentInChildren<Animator>(true);
			if (animator == null)
			{
				animator = instance.AddComponent<Animator>();
			}
			animator.avatar = avatar;
			if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
			{
				return new DhmgsHumanoidBakeArtifacts
				{
					Error = "The bake source did not produce a valid Humanoid Animator.",
				};
			}

			Transform root = animator.transform;
			root.position = Vector3.zero;
			root.rotation = Quaternion.identity;
			player.ApplyPose(asset, 0);
			if (!TryGetLowestFootBottomY(animator, out float firstFrameGroundY))
			{
				return new DhmgsHumanoidBakeArtifacts
				{
					Error = "Could not resolve the Humanoid left/right foot transforms.",
				};
			}

			float groundOffsetY = -firstFrameGroundY;
			Vector3 firstPelvisPosition = asset.pelvisPositions[0];
			using HumanPoseHandler poseHandler = new HumanPoseHandler(avatar, animator.transform);
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
				if ((frameIndex & 7) == 0)
				{
					await editorYield.CheckpointAsync(cancellationToken);
				}
				float time = frameIndex * frameDuration;
				Vector3 relativePelvisPosition = asset.pelvisPositions[frameIndex] - firstPelvisPosition;

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
					muscleKeyframes[muscleIndex][frameIndex] =
						new Keyframe(time, humanPose.muscles[muscleIndex]);
				}

				root.position = new Vector3(
					relativePelvisPosition.x,
					groundOffsetY + relativePelvisPosition.y,
					relativePelvisPosition.z);
				poseHandler.GetHumanPose(ref humanPose);
				bodyPosition = humanPose.bodyPosition;
				rootMotionPositionX[frameIndex] = new Keyframe(time, bodyPosition.x);
				rootMotionPositionY[frameIndex] = new Keyframe(time, bodyPosition.y);
				rootMotionPositionZ[frameIndex] = new Keyframe(time, bodyPosition.z);
				onProgress?.Invoke(frameCount > 0 ? (float)frameIndex / frameCount * 0.72f : 0.72f);
			}

			AnimationClip inPlaceClip = null;
			AnimationClip rootMotionClip = null;
			try
			{
				inPlaceClip = await CreateHumanoidClipAsync(
					asset.fps,
					inPlacePositionX,
					inPlacePositionY,
					inPlacePositionZ,
					rootRotationX,
					rootRotationY,
					rootRotationZ,
					rootRotationW,
					muscleKeyframes,
					true,
					value => onProgress?.Invoke(0.72f + value * 0.14f),
					cancellationToken);
				rootMotionClip = await CreateHumanoidClipAsync(
					asset.fps,
					rootMotionPositionX,
					rootMotionPositionY,
					rootMotionPositionZ,
					rootRotationX,
					rootRotationY,
					rootRotationZ,
					rootRotationW,
					muscleKeyframes,
					false,
					value => onProgress?.Invoke(0.86f + value * 0.14f),
					cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				onProgress?.Invoke(1f);
				return new DhmgsHumanoidBakeArtifacts
				{
					InPlace = inPlaceClip,
					RootMotion = rootMotionClip,
				};
			}
			catch
			{
				if (inPlaceClip != null)
				{
					UnityEngine.Object.DestroyImmediate(inPlaceClip);
				}
				if (rootMotionClip != null)
				{
					UnityEngine.Object.DestroyImmediate(rootMotionClip);
				}
				throw;
			}
		}

		private static async Task<AnimationClip> CreateHumanoidClipAsync(
			float frameRate,
			Keyframe[] positionX,
			Keyframe[] positionY,
			Keyframe[] positionZ,
			Keyframe[] rotationX,
			Keyframe[] rotationY,
			Keyframe[] rotationZ,
			Keyframe[] rotationW,
			Keyframe[][] muscleKeyframes,
			bool inPlace,
			Action<float> onProgress,
			CancellationToken cancellationToken)
		{
			DhmgsEditorYield editorYield = new DhmgsEditorYield();
			cancellationToken.ThrowIfCancellationRequested();
			AnimationClip clip = new AnimationClip { frameRate = frameRate };
			try
			{
				SetAnimatorCurve(clip, "RootT.x", positionX);
				SetAnimatorCurve(clip, "RootT.y", positionY);
				SetAnimatorCurve(clip, "RootT.z", positionZ);
				SetAnimatorCurve(clip, "RootQ.x", rotationX);
				SetAnimatorCurve(clip, "RootQ.y", rotationY);
				SetAnimatorCurve(clip, "RootQ.z", rotationZ);
				SetAnimatorCurve(clip, "RootQ.w", rotationW);
				for (int muscleIndex = 0; muscleIndex < muscleKeyframes.Length; muscleIndex++)
				{
					if ((muscleIndex & 7) == 0)
					{
						await editorYield.CheckpointAsync(cancellationToken);
					}
					SetAnimatorCurve(
						clip,
						MuscleCurveProperty(HumanTrait.MuscleName[muscleIndex]),
						muscleKeyframes[muscleIndex]);
					onProgress?.Invoke((float)(muscleIndex + 1) / muscleKeyframes.Length);
				}

				clip.EnsureQuaternionContinuity();
				ConfigureRootTransform(clip, inPlace);
				cancellationToken.ThrowIfCancellationRequested();
				return clip;
			}
			catch
			{
				UnityEngine.Object.DestroyImmediate(clip);
				throw;
			}
		}

		private static void SetLocalRotationCurves(
			AnimationClip clip,
			string path,
			Quaternion[] rotations,
			float fps,
			CancellationToken cancellationToken)
		{
			SetTransformCurve(clip, path, "m_LocalRotation.x",
				CreateLinearKeys(rotations, value => value.x, fps, cancellationToken), cancellationToken);
			SetTransformCurve(clip, path, "m_LocalRotation.y",
				CreateLinearKeys(rotations, value => value.y, fps, cancellationToken), cancellationToken);
			SetTransformCurve(clip, path, "m_LocalRotation.z",
				CreateLinearKeys(rotations, value => value.z, fps, cancellationToken), cancellationToken);
			SetTransformCurve(clip, path, "m_LocalRotation.w",
				CreateLinearKeys(rotations, value => value.w, fps, cancellationToken), cancellationToken);
		}

		private static Keyframe[] CreateLinearKeys(
			Quaternion[] rotations,
			Func<Quaternion, float> component,
			float fps,
			CancellationToken cancellationToken)
		{
			Keyframe[] keys = new Keyframe[rotations.Length];
			float frameDuration = 1f / fps;
			for (int index = 0; index < rotations.Length; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				float value = component(rotations[index]);
				float inTangent = index > 0
					? (value - component(rotations[index - 1])) / frameDuration
					: index + 1 < rotations.Length
						? (component(rotations[index + 1]) - value) / frameDuration
						: 0f;
				float outTangent = index + 1 < rotations.Length
					? (component(rotations[index + 1]) - value) / frameDuration
					: inTangent;
				keys[index] = new Keyframe(index * frameDuration, value, inTangent, outTangent);
			}
			return keys;
		}

		private static void SetTransformCurve(
			AnimationClip clip,
			string path,
			string property,
			Keyframe[] keys,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			EditorCurveBinding binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), property);
			AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
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
