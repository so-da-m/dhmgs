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
		private const float SamplingProgressShare = 0.72f;
		private const float ClipProgressShare = 0.14f;

		private sealed class HumanoidSamples
		{
			internal float FrameRate;
			internal Keyframe[] InPlacePositionX;
			internal Keyframe[] InPlacePositionY;
			internal Keyframe[] InPlacePositionZ;
			internal Keyframe[] RootMotionPositionX;
			internal Keyframe[] RootMotionPositionY;
			internal Keyframe[] RootMotionPositionZ;
			internal Keyframe[][] RootRotation;
			internal Keyframe[][] Muscles;
		}

		internal static async Task<DhmgsHumanoidBakeArtifacts> BakeHumanoidVariantsAsync(
			DhmgsMotionAsset asset,
			Action<float> onProgress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			GameObject model = DhmgsPackageDefaults.LoadNeutralSmplx();
			if (!ValidateSource(asset, model, out string error))
			{
				return new DhmgsHumanoidBakeArtifacts { Error = error };
			}
			Avatar avatar = ResolveHumanoidAvatar(model, out error);
			if (avatar == null)
			{
				return new DhmgsHumanoidBakeArtifacts { Error = error };
			}

			Scene previewScene = EditorSceneManager.NewPreviewScene();
			GameObject instance = null;
			try
			{
				instance = InstantiateBakeSource(model, previewScene);
				DhmgsRigDriver player = instance.GetComponent<DhmgsRigDriver>();
				if (player == null)
				{
					player = instance.AddComponent<DhmgsRigDriver>();
				}
				player.Bind(instance.transform);

				Animator animator = ResolveHumanoidAnimator(instance, avatar, out error);
				if (animator == null)
				{
					return new DhmgsHumanoidBakeArtifacts { Error = error };
				}
				if (!TryMeasureRestSolePlaneY(instance, out float restSolePlaneY, out error))
				{
					return new DhmgsHumanoidBakeArtifacts { Error = error };
				}

				cancellationToken.ThrowIfCancellationRequested();
				HumanoidSamples samples = await SampleHumanoidAsync(
					asset,
					player,
					animator,
					avatar,
					-restSolePlaneY,
					onProgress,
					cancellationToken);
				return await CreateVariantClipsAsync(samples, onProgress, cancellationToken);
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

		private static bool ValidateSource(DhmgsMotionAsset asset, GameObject model, out string error)
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
			if (!IsHumanoid(avatar))
			{
				string modelPath = AssetDatabase.GetAssetPath(model);
				if (!string.IsNullOrEmpty(modelPath))
				{
					foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(modelPath))
					{
						if (candidate is Avatar imported && IsHumanoid(imported))
						{
							avatar = imported;
							break;
						}
					}
				}
			}
			if (!IsHumanoid(avatar))
			{
				error = "모델이 Humanoid가 아닙니다. FBX의 Rig를 Humanoid로 임포트했는지 확인하세요.";
				return null;
			}
			return avatar;
		}

		private static bool IsHumanoid(Avatar avatar)
		{
			return avatar != null && avatar.isValid && avatar.isHuman;
		}

		private static GameObject InstantiateBakeSource(GameObject model, Scene previewScene)
		{
			GameObject instance = UnityEngine.Object.Instantiate(model);
			SceneManager.MoveGameObjectToScene(instance, previewScene);
			foreach (Transform transform in instance.GetComponentsInChildren<Transform>(true))
			{
				transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
			}
			instance.transform.position = Vector3.zero;
			instance.transform.rotation = Quaternion.identity;
			instance.transform.localScale = Vector3.one;
			return instance;
		}

		private static Animator ResolveHumanoidAnimator(GameObject instance, Avatar avatar, out string error)
		{
			error = null;
			Animator animator = instance.GetComponentInChildren<Animator>(true);
			if (animator == null)
			{
				animator = instance.AddComponent<Animator>();
			}
			animator.avatar = avatar;
			if (!IsHumanoid(animator.avatar))
			{
				error = "The bake source did not produce a valid Humanoid Animator.";
				return null;
			}
			return animator;
		}

		private static bool TryMeasureRestSolePlaneY(GameObject instance, out float solePlaneY, out string error)
		{
			error = null;
			solePlaneY = float.PositiveInfinity;
			Mesh bakedMesh = new Mesh();
			try
			{
				foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
				{
					renderer.BakeMesh(bakedMesh);
					Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;
					foreach (Vector3 vertex in bakedMesh.vertices)
					{
						solePlaneY = Mathf.Min(solePlaneY, localToWorld.MultiplyPoint3x4(vertex).y);
					}
				}
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(bakedMesh);
			}
			if (float.IsInfinity(solePlaneY))
			{
				error = "베이크 소스에서 발바닥 높이를 측정할 스킨드 메시를 찾을 수 없음";
				return false;
			}
			return true;
		}

		private static async Task<HumanoidSamples> SampleHumanoidAsync(
			DhmgsMotionAsset asset,
			DhmgsRigDriver player,
			Animator animator,
			Avatar avatar,
			float groundOffsetY,
			Action<float> onProgress,
			CancellationToken cancellationToken)
		{
			DhmgsEditorYield editorYield = new DhmgsEditorYield();
			Transform root = animator.transform;
			root.position = Vector3.zero;
			root.rotation = Quaternion.identity;

			Vector3 firstPelvisPosition = asset.pelvisPositions[0];
			using HumanPoseHandler poseHandler = new HumanPoseHandler(avatar, animator.transform);
			HumanPose humanPose = new HumanPose();

			int frameCount = asset.frameCount;
			int muscleCount = HumanTrait.MuscleCount;
			float frameDuration = 1f / asset.fps;
			HumanoidSamples samples = new HumanoidSamples
			{
				FrameRate = asset.fps,
				InPlacePositionX = new Keyframe[frameCount],
				InPlacePositionY = new Keyframe[frameCount],
				InPlacePositionZ = new Keyframe[frameCount],
				RootMotionPositionX = new Keyframe[frameCount],
				RootMotionPositionY = new Keyframe[frameCount],
				RootMotionPositionZ = new Keyframe[frameCount],
				Muscles = new Keyframe[muscleCount][],
			};
			Quaternion[] rootRotations = new Quaternion[frameCount];
			for (int muscleIndex = 0; muscleIndex < muscleCount; muscleIndex++)
			{
				samples.Muscles[muscleIndex] = new Keyframe[frameCount];
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
				rootRotations[frameIndex] = humanPose.bodyRotation;
				samples.InPlacePositionX[frameIndex] = new Keyframe(time, bodyPosition.x);
				samples.InPlacePositionY[frameIndex] = new Keyframe(time, bodyPosition.y);
				samples.InPlacePositionZ[frameIndex] = new Keyframe(time, bodyPosition.z);
				for (int muscleIndex = 0; muscleIndex < muscleCount; muscleIndex++)
				{
					samples.Muscles[muscleIndex][frameIndex] = new Keyframe(time, humanPose.muscles[muscleIndex]);
				}

				root.position = relativePelvisPosition + Vector3.up * groundOffsetY;
				poseHandler.GetHumanPose(ref humanPose);
				bodyPosition = humanPose.bodyPosition;
				samples.RootMotionPositionX[frameIndex] = new Keyframe(time, bodyPosition.x);
				samples.RootMotionPositionY[frameIndex] = new Keyframe(time, bodyPosition.y);
				samples.RootMotionPositionZ[frameIndex] = new Keyframe(time, bodyPosition.z);
				onProgress?.Invoke(frameCount > 0
					? (float)frameIndex / frameCount * SamplingProgressShare
					: SamplingProgressShare);
			}

			DhmgsCurveTangents.ApplyLinear(samples.InPlacePositionX, frameDuration, cancellationToken);
			DhmgsCurveTangents.ApplyLinear(samples.InPlacePositionY, frameDuration, cancellationToken);
			DhmgsCurveTangents.ApplyLinear(samples.InPlacePositionZ, frameDuration, cancellationToken);
			DhmgsCurveTangents.ApplyLinear(samples.RootMotionPositionX, frameDuration, cancellationToken);
			DhmgsCurveTangents.ApplyLinear(samples.RootMotionPositionY, frameDuration, cancellationToken);
			DhmgsCurveTangents.ApplyLinear(samples.RootMotionPositionZ, frameDuration, cancellationToken);
			foreach (Keyframe[] muscleKeys in samples.Muscles)
			{
				DhmgsCurveTangents.ApplyLinear(muscleKeys, frameDuration, cancellationToken);
			}
			samples.RootRotation = DhmgsQuaternionCurves.CreateKeys(rootRotations, asset.fps, cancellationToken);
			return samples;
		}

		private static async Task<DhmgsHumanoidBakeArtifacts> CreateVariantClipsAsync(
			HumanoidSamples samples,
			Action<float> onProgress,
			CancellationToken cancellationToken)
		{
			AnimationClip inPlaceClip = null;
			AnimationClip rootMotionClip = null;
			try
			{
				inPlaceClip = await CreateHumanoidClipAsync(
					samples,
					samples.InPlacePositionX,
					samples.InPlacePositionY,
					samples.InPlacePositionZ,
					true,
					value => onProgress?.Invoke(SamplingProgressShare + value * ClipProgressShare),
					cancellationToken);
				rootMotionClip = await CreateHumanoidClipAsync(
					samples,
					samples.RootMotionPositionX,
					samples.RootMotionPositionY,
					samples.RootMotionPositionZ,
					false,
					value => onProgress?.Invoke(SamplingProgressShare + ClipProgressShare + value * ClipProgressShare),
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
				DestroyIfCreated(inPlaceClip);
				DestroyIfCreated(rootMotionClip);
				throw;
			}
		}

		private static async Task<AnimationClip> CreateHumanoidClipAsync(
			HumanoidSamples samples,
			Keyframe[] positionX,
			Keyframe[] positionY,
			Keyframe[] positionZ,
			bool inPlace,
			Action<float> onProgress,
			CancellationToken cancellationToken)
		{
			DhmgsEditorYield editorYield = new DhmgsEditorYield();
			cancellationToken.ThrowIfCancellationRequested();
			AnimationClip clip = new AnimationClip { frameRate = samples.FrameRate };
			try
			{
				SetAnimatorCurve(clip, "RootT.x", positionX);
				SetAnimatorCurve(clip, "RootT.y", positionY);
				SetAnimatorCurve(clip, "RootT.z", positionZ);
				SetAnimatorCurve(clip, "RootQ.x", samples.RootRotation[0]);
				SetAnimatorCurve(clip, "RootQ.y", samples.RootRotation[1]);
				SetAnimatorCurve(clip, "RootQ.z", samples.RootRotation[2]);
				SetAnimatorCurve(clip, "RootQ.w", samples.RootRotation[3]);
				for (int muscleIndex = 0; muscleIndex < samples.Muscles.Length; muscleIndex++)
				{
					if ((muscleIndex & 7) == 0)
					{
						await editorYield.CheckpointAsync(cancellationToken);
					}
					SetAnimatorCurve(
						clip,
						MuscleCurveProperty(HumanTrait.MuscleName[muscleIndex]),
						samples.Muscles[muscleIndex]);
					onProgress?.Invoke((float)(muscleIndex + 1) / samples.Muscles.Length);
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

		private static void DestroyIfCreated(AnimationClip clip)
		{
			if (clip != null)
			{
				UnityEngine.Object.DestroyImmediate(clip);
			}
		}
	}
}
#endif
