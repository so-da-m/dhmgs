#if UNITY_EDITOR
using System;
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

	internal static class DhmgsAnimationClipBaker
	{
		internal static async Task<DhmgsHumanoidBakeArtifacts> BakeHumanoidVariantsAsync(
			DhmgsMotionAsset asset,
			Action<float> onProgress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			GameObject model = DhmgsPackageDefaults.LoadNeutralSmplx();
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
				error = "번들 Neutral SMPL-X 베이크 모델을 찾을 수 없음";
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
			Quaternion[] rootRotations = new Quaternion[frameCount];
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

				root.position = new Vector3(0f, relativePelvisPosition.y, 0f);
				root.rotation = Quaternion.identity;
				player.ApplyPose(asset, frameIndex);
				poseHandler.GetHumanPose(ref humanPose);

				Vector3 bodyPosition = humanPose.bodyPosition;
				rootRotations[frameIndex] = humanPose.bodyRotation;
				inPlacePositionX[frameIndex] = new Keyframe(time, bodyPosition.x);
				inPlacePositionY[frameIndex] = new Keyframe(time, bodyPosition.y);
				inPlacePositionZ[frameIndex] = new Keyframe(time, bodyPosition.z);
				for (int muscleIndex = 0; muscleIndex < muscleCount; muscleIndex++)
				{
					muscleKeyframes[muscleIndex][frameIndex] =
						new Keyframe(time, humanPose.muscles[muscleIndex]);
				}

				root.position = relativePelvisPosition;
				poseHandler.GetHumanPose(ref humanPose);
				bodyPosition = humanPose.bodyPosition;
				rootMotionPositionX[frameIndex] = new Keyframe(time, bodyPosition.x);
				rootMotionPositionY[frameIndex] = new Keyframe(time, bodyPosition.y);
				rootMotionPositionZ[frameIndex] = new Keyframe(time, bodyPosition.z);
				onProgress?.Invoke(frameCount > 0 ? (float)frameIndex / frameCount * 0.72f : 0.72f);
			}

			DhmgsCurveTangents.ApplyLinear(inPlacePositionX, frameDuration, cancellationToken);
			DhmgsCurveTangents.ApplyLinear(inPlacePositionY, frameDuration, cancellationToken);
			DhmgsCurveTangents.ApplyLinear(inPlacePositionZ, frameDuration, cancellationToken);
			DhmgsCurveTangents.ApplyLinear(rootMotionPositionX, frameDuration, cancellationToken);
			DhmgsCurveTangents.ApplyLinear(rootMotionPositionY, frameDuration, cancellationToken);
			DhmgsCurveTangents.ApplyLinear(rootMotionPositionZ, frameDuration, cancellationToken);
			for (int muscleIndex = 0; muscleIndex < muscleKeyframes.Length; muscleIndex++)
			{
				DhmgsCurveTangents.ApplyLinear(muscleKeyframes[muscleIndex], frameDuration, cancellationToken);
			}

			Keyframe[][] rootRotationKeys = DhmgsQuaternionCurves.CreateKeys(
				rootRotations, asset.fps, cancellationToken);
			AnimationClip inPlaceClip = null;
			AnimationClip rootMotionClip = null;
			try
			{
				inPlaceClip = await CreateHumanoidClipAsync(
					asset.fps,
					inPlacePositionX,
					inPlacePositionY,
					inPlacePositionZ,
					rootRotationKeys[0],
					rootRotationKeys[1],
					rootRotationKeys[2],
					rootRotationKeys[3],
					muscleKeyframes,
					true,
					value => onProgress?.Invoke(0.72f + value * 0.14f),
					cancellationToken);
				rootMotionClip = await CreateHumanoidClipAsync(
					asset.fps,
					rootMotionPositionX,
					rootMotionPositionY,
					rootMotionPositionZ,
					rootRotationKeys[0],
					rootRotationKeys[1],
					rootRotationKeys[2],
					rootRotationKeys[3],
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

		private static void ConfigureRootTransform(AnimationClip clip, bool inPlace)
		{
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
