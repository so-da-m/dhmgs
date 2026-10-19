using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal static class DhmgsPackageDefaults
	{
		internal const string NeutralSmplxGuid = "615fc734a2d080f4d954f707b0aaa53e";

		internal static GameObject LoadNeutralSmplx()
		{
			string path = AssetDatabase.GUIDToAssetPath(NeutralSmplxGuid);
			return string.IsNullOrEmpty(path)
				? null
				: AssetDatabase.LoadAssetAtPath<GameObject>(path);
		}
	}

	internal static class DhmgsBakeSourceValidator
	{
		private static readonly string[] CanonicalMotionBones =
		{
			"left_hip", "right_hip", "spine1", "left_knee", "right_knee", "spine2",
			"left_ankle", "right_ankle", "spine3", "left_foot", "right_foot", "neck",
			"left_collar", "right_collar", "head", "left_shoulder", "right_shoulder",
			"left_elbow", "right_elbow", "left_wrist", "right_wrist", "jaw",
			"left_eye_smplhf", "right_eye_smplhf",
			"left_index1", "left_index2", "left_index3",
			"left_middle1", "left_middle2", "left_middle3",
			"left_pinky1", "left_pinky2", "left_pinky3",
			"left_ring1", "left_ring2", "left_ring3",
			"left_thumb1", "left_thumb2", "left_thumb3",
			"right_index1", "right_index2", "right_index3",
			"right_middle1", "right_middle2", "right_middle3",
			"right_pinky1", "right_pinky2", "right_pinky3",
			"right_ring1", "right_ring2", "right_ring3",
			"right_thumb1", "right_thumb2", "right_thumb3",
		};

		internal static void ValidatePreflight(
			GameObject model,
			DhmgsBakeOutputs outputs,
			List<DhmgsImportDiagnostic> diagnostics)
		{
			if (outputs == DhmgsBakeOutputs.None)
			{
				return;
			}
			if (model == null)
			{
				diagnostics.Add(Error("bake.source.missing",
					"The bundled Neutral SMPL-X bake source could not be found."));
				return;
			}

			string path = AssetDatabase.GetAssetPath(model);
			if (string.IsNullOrEmpty(path) || !AssetDatabase.Contains(model))
			{
				diagnostics.Add(Error("bake.source.not-asset",
					"The SMPL-X bake source must be a persistent model or prefab asset."));
				return;
			}

			ValidateBoneNames(model, CanonicalMotionBones, diagnostics);
			if ((outputs & DhmgsBakeOutputs.Humanoid) != 0)
			{
				Avatar avatar = ResolveHumanoidAvatar(model);
				if (avatar == null)
				{
					diagnostics.Add(Error("bake.source.not-humanoid",
						"Humanoid baking requires a valid Human Avatar on the SMPL-X bake source."));
				}
				else
				{
					ValidateHumanoidAvatarMapping(model, avatar, diagnostics);
				}
			}
		}

		internal static void ValidateForMotion(
			GameObject model,
			DhmgsMotionAsset motion,
			List<DhmgsImportDiagnostic> diagnostics)
		{
			if (model == null || motion == null || motion.boneNames == null)
			{
				return;
			}
			ValidateBoneNames(model, motion.boneNames, diagnostics);
		}

		internal static Avatar ResolveHumanoidAvatar(GameObject model)
		{
			if (model == null)
			{
				return null;
			}
			Animator animator = model.GetComponentInChildren<Animator>(true);
			Avatar avatar = animator != null ? animator.avatar : null;
			if (avatar != null && avatar.isValid && avatar.isHuman)
			{
				return avatar;
			}

			string path = AssetDatabase.GetAssetPath(model);
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			return AssetDatabase.LoadAllAssetsAtPath(path)
				.OfType<Avatar>()
				.FirstOrDefault(candidate => candidate.isValid && candidate.isHuman);
		}

		private static void ValidateBoneNames(
			GameObject model,
			IEnumerable<string> requiredNames,
			List<DhmgsImportDiagnostic> diagnostics)
		{
			Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (Transform transform in model.GetComponentsInChildren<Transform>(true))
			{
				counts.TryGetValue(transform.name, out int count);
				counts[transform.name] = count + 1;
			}

			List<string> missing = new List<string>();
			List<string> duplicates = new List<string>();
			if (!counts.TryGetValue("pelvis", out int pelvisCount) || pelvisCount == 0)
			{
				missing.Add("pelvis");
			}
			else if (pelvisCount > 1)
			{
				duplicates.Add("pelvis");
			}

			foreach (string requiredName in requiredNames)
			{
				if (string.IsNullOrWhiteSpace(requiredName) || !counts.TryGetValue(requiredName, out int count) || count == 0)
				{
					missing.Add(string.IsNullOrWhiteSpace(requiredName) ? "<empty>" : requiredName);
				}
				else if (count > 1)
				{
					duplicates.Add(requiredName);
				}
			}

			if (missing.Count > 0)
			{
				diagnostics.Add(Error("bake.source.bones-missing",
					$"The bundled Neutral SMPL-X bake source is missing {missing.Count} required transform(s): {Preview(missing)}"));
			}
			if (duplicates.Count > 0)
			{
				diagnostics.Add(Error("bake.source.bones-duplicate",
					$"The bundled Neutral SMPL-X bake source has duplicate required transform names: {Preview(duplicates)}"));
			}
		}

		private static void ValidateHumanoidAvatarMapping(
			GameObject model,
			Avatar avatar,
			List<DhmgsImportDiagnostic> diagnostics)
		{
			HashSet<string> transformNames = new HashSet<string>(
				model.GetComponentsInChildren<Transform>(true).Select(transform => transform.name),
				StringComparer.Ordinal);
			HumanBone[] humanBones = avatar.humanDescription.human ?? Array.Empty<HumanBone>();
			string hipsHumanName = HumanTrait.BoneName[(int)HumanBodyBones.Hips];
			HumanBone hips = humanBones.FirstOrDefault(bone => bone.humanName == hipsHumanName);
			List<string> missingMappings = humanBones
				.Where(bone => string.IsNullOrWhiteSpace(bone.boneName) || !transformNames.Contains(bone.boneName))
				.Select(bone => string.IsNullOrWhiteSpace(bone.boneName) ? "<empty>" : bone.boneName)
				.Distinct(StringComparer.Ordinal)
				.ToList();

			if (humanBones.Length == 0
				|| string.IsNullOrEmpty(hips.boneName)
				|| !hips.boneName.Equals("pelvis", StringComparison.Ordinal)
				|| missingMappings.Count > 0)
			{
				string detail = missingMappings.Count > 0
					? $" Missing mapped transform(s): {Preview(missingMappings)}"
					: string.Empty;
				diagnostics.Add(Error(
					"bake.source.avatar-mismatch",
					"The Human Avatar does not describe this SMPL-X hierarchy (Hips must map to pelvis)." + detail));
			}
		}

		private static string Preview(IReadOnlyList<string> names)
		{
			const int max = 6;
			string value = string.Join(", ", names.Take(max));
			return names.Count > max ? $"{value}, ..." : value;
		}

		private static DhmgsImportDiagnostic Error(string code, string message)
		{
			return new DhmgsImportDiagnostic(code, DhmgsImportSeverity.Error, message);
		}
	}
}
