using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal enum DhmgsSmplxSkeletonReference
	{
		[InspectorName("중립형")]
		Neutral,
		[InspectorName("여성형")]
		Female,
		[InspectorName("남성형")]
		Male,
	}

	internal enum DhmgsAvatarValidationSeverity
	{
		Info,
		Warning,
		Error,
	}

	internal sealed class DhmgsAvatarValidationDiagnostic
	{
		internal DhmgsAvatarValidationDiagnostic(
			string code,
			DhmgsAvatarValidationSeverity severity,
			string message)
		{
			Code = code;
			Severity = severity;
			Message = message;
		}

		internal string Code { get; }
		internal DhmgsAvatarValidationSeverity Severity { get; }
		internal string Message { get; }
	}

	internal sealed class DhmgsSmplxAvatarValidationResult
	{
		private readonly List<DhmgsAvatarValidationDiagnostic> _diagnostics =
			new List<DhmgsAvatarValidationDiagnostic>();

		internal GameObject Source { get; set; }
		internal GameObject Reference { get; set; }
		internal string SourcePath { get; set; }
		internal string SkeletonRootPath { get; set; }
		internal int CanonicalJointCount { get; set; }
		internal int MatchedJointCount { get; set; }
		internal int StandardBlendShapeCount { get; set; }
		internal int AdditionalBlendShapeCount { get; set; }
		internal bool SupportsOpacity { get; set; }
		internal int OpacityMaterialCount { get; set; }
		internal int MaterialCount { get; set; }
		internal IReadOnlyList<DhmgsAvatarValidationDiagnostic> Diagnostics => _diagnostics;
		internal bool CanRegister => !_diagnostics.Any(item => item.Severity == DhmgsAvatarValidationSeverity.Error);

		internal void Add(string code, DhmgsAvatarValidationSeverity severity, string message)
		{
			_diagnostics.Add(new DhmgsAvatarValidationDiagnostic(code, severity, message));
		}
	}

	internal static class DhmgsSmplxAvatarValidator
	{
		internal const string NeutralReferenceGuid = "615fc734a2d080f4d954f707b0aaa53e";
		internal const string FemaleReferenceGuid = "db0b900d854529d48abff28edee4a890";
		internal const string MaleReferenceGuid = "e76d27f2b19414b4b815356710530321";
		internal const int OfficialBlendShapeCount = 506;

		private const float RotationToleranceDegrees = 0.05f;
		private const float ScaleTolerance = 0.001f;
		private static readonly int OpacityProperty = Shader.PropertyToID("_Opacity");
		private static readonly string[] CanonicalJointNames =
		{
			"root", "pelvis", "left_hip", "right_hip", "spine1", "left_knee", "right_knee", "spine2",
			"left_ankle", "right_ankle", "spine3", "left_foot", "right_foot", "neck", "left_collar",
			"right_collar", "head", "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
			"left_wrist", "right_wrist", "jaw", "left_eye_smplhf", "right_eye_smplhf",
			"left_index1", "left_index2", "left_index3", "left_middle1", "left_middle2", "left_middle3",
			"left_pinky1", "left_pinky2", "left_pinky3", "left_ring1", "left_ring2", "left_ring3",
			"left_thumb1", "left_thumb2", "left_thumb3", "right_index1", "right_index2", "right_index3",
			"right_middle1", "right_middle2", "right_middle3", "right_pinky1", "right_pinky2",
			"right_pinky3", "right_ring1", "right_ring2", "right_ring3", "right_thumb1", "right_thumb2",
			"right_thumb3",
		};
		private static readonly string[] OfficialBlendShapeNames = BuildOfficialBlendShapeNames();

		internal static GameObject LoadReference(DhmgsSmplxSkeletonReference reference)
		{
			string guid;
			switch (reference)
			{
				case DhmgsSmplxSkeletonReference.Female:
					guid = FemaleReferenceGuid;
					break;
				case DhmgsSmplxSkeletonReference.Male:
					guid = MaleReferenceGuid;
					break;
				default:
					guid = NeutralReferenceGuid;
					break;
			}

			string path = AssetDatabase.GUIDToAssetPath(guid);
			return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
		}

		internal static DhmgsSmplxAvatarValidationResult Validate(
			GameObject source,
			DhmgsSmplxSkeletonReference reference = DhmgsSmplxSkeletonReference.Neutral)
		{
			GameObject referenceModel = LoadReference(reference);
			return Validate(source, referenceModel, true);
		}

		internal static DhmgsSmplxAvatarValidationResult Validate(
			GameObject source,
			GameObject referenceModel,
			bool requireFbxAsset)
		{
			DhmgsSmplxAvatarValidationResult result = new DhmgsSmplxAvatarValidationResult
			{
				Source = source,
				Reference = referenceModel,
				SourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty,
			};

			if (source == null)
			{
				result.Add("source.missing", DhmgsAvatarValidationSeverity.Error,
					"SMPL-X FBX 모델을 선택하세요.");
				return result;
			}

			if (requireFbxAsset && !ValidateSourceAsset(source, result))
			{
				return result;
			}

			if (referenceModel == null)
			{
				result.Add("reference.missing", DhmgsAvatarValidationSeverity.Error,
					"내장된 SMPL-X 골격 기준 에셋을 불러올 수 없습니다.");
				return result;
			}

			ValidateHierarchy(source, referenceModel, result, out Transform skeletonRoot,
				out HashSet<Transform> canonicalTransforms);
			ValidateSkinning(source, canonicalTransforms, result);
			ValidateBlendShapesAndMaterials(source, result);
			return result;
		}

		private static bool ValidateSourceAsset(
			GameObject source,
			DhmgsSmplxAvatarValidationResult result)
		{
			string path = result.SourcePath;
			if (string.IsNullOrEmpty(path)
				|| !AssetDatabase.Contains(source)
				|| !AssetDatabase.IsMainAsset(source)
				|| !Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
			{
				result.Add("source.not-fbx", DhmgsAvatarValidationSeverity.Error,
					"원본은 현재 프로젝트에 임포트된 FBX 파일의 루트 에셋이어야 합니다.");
				return false;
			}

			ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
			if (importer == null)
			{
				result.Add("source.no-model-importer", DhmgsAvatarValidationSeverity.Error,
					"선택한 에셋에 Unity ModelImporter가 없습니다.");
				return false;
			}

			if (importer.optimizeGameObjects)
			{
				result.Add("source.optimized-hierarchy", DhmgsAvatarValidationSeverity.Error,
					"DHMGS가 모든 SMPL-X 관절에 접근할 수 있도록 FBX Rig 임포트 설정에서 Optimize Game Objects를 끄세요.");
			}
			if (!importer.importBlendShapes)
			{
				result.Add("source.blendshapes-disabled", DhmgsAvatarValidationSeverity.Warning,
					"Import BlendShapes가 꺼져 있습니다. 골격 재생용 등록은 가능하지만, 이 FBX 임포트 설정을 켜기 전에는 기본 및 회사 전용 블렌드셰이프를 사용할 수 없습니다.");
			}
			return true;
		}

		private static void ValidateHierarchy(
			GameObject source,
			GameObject referenceModel,
			DhmgsSmplxAvatarValidationResult result,
			out Transform skeletonRoot,
			out HashSet<Transform> canonicalTransforms)
		{
			skeletonRoot = null;
			canonicalTransforms = new HashSet<Transform>();
			Transform referenceRoot = FindUniqueNamedTransform(referenceModel, "root", out int referenceRootCount);
			if (referenceRootCount != 1 || referenceRoot == null)
			{
				result.Add("reference.root", DhmgsAvatarValidationSeverity.Error,
					"내장 기준 에셋에 고유한 골격 루트가 하나만 존재하지 않습니다.");
				return;
			}

			Dictionary<string, List<Transform>> referenceGroups = referenceRoot
				.GetComponentsInChildren<Transform>(true)
				.GroupBy(transform => transform.name, StringComparer.Ordinal)
				.ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
			List<string> invalidReferenceJoints = CanonicalJointNames
				.Where(name => !referenceGroups.TryGetValue(name, out List<Transform> values) || values.Count != 1)
				.ToList();
			if (invalidReferenceJoints.Count > 0)
			{
				result.Add("reference.contract", DhmgsAvatarValidationSeverity.Error,
					$"내장 기준 에셋에서 모든 표준 관절을 각각 하나씩 찾을 수 없습니다: {Preview(invalidReferenceJoints)}");
				return;
			}
			Dictionary<string, Transform> referenceByName = CanonicalJointNames.ToDictionary(
				name => name,
				name => referenceGroups[name][0],
				StringComparer.Ordinal);

			result.CanonicalJointCount = CanonicalJointNames.Length;
			Transform[] sourceTransforms = source.GetComponentsInChildren<Transform>(true);
			Dictionary<string, List<Transform>> sourceByName = sourceTransforms
				.GroupBy(transform => transform.name, StringComparer.Ordinal)
				.ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

			Dictionary<string, Transform> matched = new Dictionary<string, Transform>(StringComparer.Ordinal);
			List<string> missing = new List<string>();
			List<string> duplicates = new List<string>();
			foreach (string name in CanonicalJointNames)
			{
				if (!sourceByName.TryGetValue(name, out List<Transform> values) || values.Count == 0)
				{
					missing.Add(name);
					continue;
				}
				if (values.Count != 1)
				{
					duplicates.Add(name);
					continue;
				}
				matched[name] = values[0];
			}

			result.MatchedJointCount = matched.Count;
			if (missing.Count > 0)
			{
				result.Add("skeleton.missing", DhmgsAvatarValidationSeverity.Error,
					$"필수 SMPL-X 관절 {missing.Count}개가 없습니다: {Preview(missing)}");
			}
			if (duplicates.Count > 0)
			{
				result.Add("skeleton.duplicates", DhmgsAvatarValidationSeverity.Error,
					$"필수 SMPL-X 관절 이름은 중복될 수 없습니다: {Preview(duplicates)}");
			}

			if (!matched.TryGetValue("root", out skeletonRoot))
			{
				return;
			}
			result.SkeletonRootPath = AnimationUtility.CalculateTransformPath(skeletonRoot, source.transform);
			Vector3 effectiveRootScale = skeletonRoot.lossyScale;
			if (!IsFinite(effectiveRootScale)
				|| !Approximately(effectiveRootScale, Vector3.one, ScaleTolerance))
			{
				result.Add("skeleton.root-effective-scale", DhmgsAvatarValidationSeverity.Error,
					$"표준 골격 root의 유효 스케일이 {effectiveRootScale}입니다. "
					+ "DHMGS 직접 재생에는 (1, 1, 1) 스케일이 필요합니다. "
					+ "DCC에서 스켈레톤 상위 Transform을 적용(Apply/Freeze)한 뒤 다시 내보내세요.");
			}

			bool hasUniformScaleDifference = false;
			foreach (string name in CanonicalJointNames)
			{
				if (!matched.TryGetValue(name, out Transform candidate))
				{
					continue;
				}
				Transform reference = referenceByName[name];
				canonicalTransforms.Add(candidate);

				if (candidate != skeletonRoot && !candidate.IsChildOf(skeletonRoot))
				{
					result.Add("skeleton.outside-root", DhmgsAvatarValidationSeverity.Error,
						$"관절 '{name}'이 표준 골격 루트 아래에 있지 않습니다.");
					continue;
				}

				if (reference != referenceRoot)
				{
					string expectedParentName = reference.parent != null ? reference.parent.name : "<없음>";
					Transform expectedParent = matched.TryGetValue(expectedParentName, out Transform parent)
						? parent
						: null;
					if (candidate.parent != expectedParent)
					{
						string actualParent = candidate.parent != null ? candidate.parent.name : "<없음>";
						result.Add("skeleton.parent", DhmgsAvatarValidationSeverity.Error,
							$"관절 '{name}'은 '{expectedParentName}'의 직계 자식이어야 하지만, 현재 부모는 '{actualParent}'입니다.");
					}
				}

				if (!IsFinite(candidate.localPosition)
					|| !IsFinite(candidate.localRotation)
					|| !IsFinite(candidate.localScale))
				{
					result.Add("skeleton.non-finite", DhmgsAvatarValidationSeverity.Error,
						$"관절 '{name}'의 Transform 값에 NaN 또는 Infinity가 포함되어 있습니다.");
					continue;
				}

				float angle = Quaternion.Angle(candidate.localRotation, reference.localRotation);
				if (angle > RotationToleranceDegrees)
				{
					result.Add("skeleton.rotation", DhmgsAvatarValidationSeverity.Warning,
						$"관절 '{name}'의 기본 축이 기준과 {angle:F3}도 다릅니다. 계층 구조는 호환되지만 직접 재생 결과를 눈으로 확인하세요.");
				}

				if (!IsPositiveUniformScaleRatio(candidate.localScale, reference.localScale, ScaleTolerance))
				{
					result.Add("skeleton.scale", DhmgsAvatarValidationSeverity.Warning,
						$"관절 '{name}'의 스케일이 기준 대비 0, 음수 또는 비균일입니다({candidate.localScale}). 계층 구조는 호환되지만 직접 재생 결과를 눈으로 확인하세요.");
				}
				else if (!Approximately(candidate.localScale, reference.localScale, ScaleTolerance))
				{
					hasUniformScaleDifference = true;
				}
			}
			if (hasUniformScaleDifference)
			{
				result.Add("skeleton.uniform-scale", DhmgsAvatarValidationSeverity.Info,
					"하나 이상의 표준 관절이 기준과 다른 양수 균일 스케일을 사용합니다. 고정 체형의 스케일은 그대로 유지됩니다.");
			}
			ValidateSkeletonAncestorChain(
				source.transform,
				skeletonRoot,
				referenceModel.transform,
				referenceRoot,
				result);
		}

		private static void ValidateSkinning(
			GameObject source,
			HashSet<Transform> canonicalTransforms,
			DhmgsSmplxAvatarValidationResult result)
		{
			SkinnedMeshRenderer[] renderers = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			if (renderers.Length == 0)
			{
				result.Add("skinning.missing", DhmgsAvatarValidationSeverity.Error,
					"FBX에 SkinnedMeshRenderer가 없습니다.");
				return;
			}

			HashSet<Transform> coveredCanonical = new HashSet<Transform>();
			foreach (SkinnedMeshRenderer renderer in renderers)
			{
				if (renderer.sharedMesh == null)
				{
					result.Add("skinning.mesh-null", DhmgsAvatarValidationSeverity.Error,
						$"SkinnedMeshRenderer '{renderer.name}'에 메시가 없습니다.");
					continue;
				}
				if (renderer.sharedMesh.vertexCount <= 0)
				{
					result.Add("skinning.mesh-empty", DhmgsAvatarValidationSeverity.Error,
						$"SkinnedMeshRenderer '{renderer.name}'의 메시가 비어 있습니다.");
				}

				Transform[] bones = renderer.bones ?? Array.Empty<Transform>();
				Matrix4x4[] bindposes = renderer.sharedMesh.bindposes ?? Array.Empty<Matrix4x4>();
				if (renderer.rootBone == null)
				{
					result.Add("skinning.root-bone-null", DhmgsAvatarValidationSeverity.Error,
						$"SkinnedMeshRenderer '{renderer.name}'에 루트 본이 없습니다.");
				}
				else if (!IsWithinSource(renderer.rootBone, source.transform))
				{
					result.Add("skinning.root-bone-outside", DhmgsAvatarValidationSeverity.Error,
						$"SkinnedMeshRenderer '{renderer.name}'가 선택한 FBX 밖의 루트 본을 사용합니다.");
				}
				if (bones.Length == 0)
				{
					result.Add("skinning.bones-empty", DhmgsAvatarValidationSeverity.Error,
						$"SkinnedMeshRenderer '{renderer.name}'에 본이 없습니다.");
				}
				if (bones.Length != bindposes.Length)
				{
					result.Add("skinning.bindpose-count", DhmgsAvatarValidationSeverity.Error,
						$"SkinnedMeshRenderer '{renderer.name}'의 본은 {bones.Length}개지만 바인드 포즈는 {bindposes.Length}개입니다.");
				}

				HashSet<Transform> uniqueBones = new HashSet<Transform>();
				for (int index = 0; index < bones.Length; index++)
				{
					Transform bone = bones[index];
					if (bone == null)
					{
						result.Add("skinning.bone-null", DhmgsAvatarValidationSeverity.Error,
							$"SkinnedMeshRenderer '{renderer.name}'의 {index}번 본 참조가 비어 있습니다.");
					}
					else if (!IsWithinSource(bone, source.transform))
					{
						result.Add("skinning.bone-outside", DhmgsAvatarValidationSeverity.Error,
							$"SkinnedMeshRenderer '{renderer.name}'가 선택한 FBX 밖의 본 '{bone.name}'을 참조합니다.");
					}
					else if (renderer.rootBone != null
						&& bone != renderer.rootBone
						&& !bone.IsChildOf(renderer.rootBone))
					{
						result.Add("skinning.root-not-ancestor", DhmgsAvatarValidationSeverity.Error,
							$"'{renderer.name}'의 루트 본 '{renderer.rootBone.name}'이 참조 본 '{bone.name}'의 상위 계층에 있지 않습니다.");
					}
					else if (!uniqueBones.Add(bone))
					{
						result.Add("skinning.bone-duplicate", DhmgsAvatarValidationSeverity.Error,
							$"SkinnedMeshRenderer '{renderer.name}'가 본 '{bone.name}'을 두 번 이상 참조합니다.");
					}
				}

				for (int index = 0; index < bindposes.Length; index++)
				{
					if (!IsFinite(bindposes[index]))
					{
						result.Add("skinning.bindpose-non-finite", DhmgsAvatarValidationSeverity.Error,
							$"SkinnedMeshRenderer '{renderer.name}'의 {index}번 바인드 포즈 값이 유효하지 않습니다.");
						break;
					}
				}

				foreach (Transform bone in bones)
				{
					if (bone != null && canonicalTransforms.Contains(bone))
					{
						coveredCanonical.Add(bone);
					}
				}
			}

			if (coveredCanonical.Count <= 0)
			{
				result.Add("skinning.not-smplx", DhmgsAvatarValidationSeverity.Error,
					"표준 SMPL-X 골격에 연결된 스킨 메시가 없습니다.");
				return;
			}
			if (coveredCanonical.Count < result.CanonicalJointCount)
			{
				result.Add("skinning.partial-coverage", DhmgsAvatarValidationSeverity.Warning,
					$"전체 스킨 메시가 표준 관절 {result.CanonicalJointCount}개 중 {coveredCanonical.Count}개를 사용합니다. 계층 구조는 호환되지만 사용하지 않는 관절은 메시 변형에 영향을 주지 않습니다.");
			}
		}

		private static void ValidateBlendShapesAndMaterials(
			GameObject source,
			DhmgsSmplxAvatarValidationResult result)
		{
			HashSet<string> blendShapes = new HashSet<string>(StringComparer.Ordinal);
			int materialCount = 0;
			int opacityMaterialCount = 0;
			foreach (SkinnedMeshRenderer renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				Mesh mesh = renderer.sharedMesh;
				if (mesh != null)
				{
					for (int index = 0; index < mesh.blendShapeCount; index++)
					{
						blendShapes.Add(mesh.GetBlendShapeName(index));
					}
				}

				foreach (Material material in renderer.sharedMaterials)
				{
					if (material == null)
					{
						continue;
					}
					materialCount++;
					if (material.HasProperty(OpacityProperty))
					{
						opacityMaterialCount++;
					}
				}
			}

			HashSet<string> official = new HashSet<string>(OfficialBlendShapeNames, StringComparer.Ordinal);
			List<string> missing = OfficialBlendShapeNames.Where(name => !blendShapes.Contains(name)).ToList();
			result.StandardBlendShapeCount = OfficialBlendShapeCount - missing.Count;
			result.AdditionalBlendShapeCount = blendShapes.Count(name => !official.Contains(name));
			result.MaterialCount = materialCount;
			result.OpacityMaterialCount = opacityMaterialCount;
			result.SupportsOpacity = opacityMaterialCount > 0;

			if (missing.Count > 0)
			{
				result.Add("blendshapes.missing", DhmgsAvatarValidationSeverity.Warning,
					$"기본 SMPL-X 블렌드셰이프 {OfficialBlendShapeCount}개 중 {missing.Count}개가 없습니다({Preview(missing)}). DHMGS 골격 재생에는 영향을 주지 않습니다.");
			}
			if (result.AdditionalBlendShapeCount > 0)
			{
				result.Add("blendshapes.additional", DhmgsAvatarValidationSeverity.Info,
					$"회사 전용 추가 블렌드셰이프 {result.AdditionalBlendShapeCount}개를 찾았습니다. 모두 그대로 유지됩니다.");
			}
			if (opacityMaterialCount == 0)
			{
				result.Add("opacity.unsupported", DhmgsAvatarValidationSeverity.Info,
					"원본 머터리얼에 _Opacity 속성이 없습니다. 등록은 가능하지만 데모의 불투명도 슬라이더가 이 아바타에는 적용되지 않습니다.");
			}
			else if (opacityMaterialCount < materialCount)
			{
				result.Add("opacity.partial", DhmgsAvatarValidationSeverity.Warning,
					$"머터리얼 슬롯 {materialCount}개 중 {opacityMaterialCount}개만 _Opacity 속성을 제공합니다. 데모의 불투명도 슬라이더는 해당 슬롯에만 적용됩니다.");
			}
		}

		private static Transform FindUniqueNamedTransform(GameObject root, string name, out int count)
		{
			Transform[] matches = root.GetComponentsInChildren<Transform>(true)
				.Where(transform => transform.name.Equals(name, StringComparison.Ordinal))
				.ToArray();
			count = matches.Length;
			return count == 1 ? matches[0] : null;
		}

		private static void ValidateSkeletonAncestorChain(
			Transform sourceRoot,
			Transform skeletonRoot,
			Transform referenceModelRoot,
			Transform referenceSkeletonRoot,
			DhmgsSmplxAvatarValidationResult result)
		{
			if (Quaternion.Angle(sourceRoot.localRotation, referenceModelRoot.localRotation)
				> RotationToleranceDegrees)
			{
				result.Add("skeleton.model-root-rotation", DhmgsAvatarValidationSeverity.Warning,
					"FBX 모델 루트의 방향이 공식 기준과 다릅니다. 계층 구조는 호환되지만 직접 재생 결과를 눈으로 확인하세요.");
			}
			Quaternion candidateRelativeRotation = Quaternion.Inverse(sourceRoot.rotation) * skeletonRoot.rotation;
			Quaternion referenceRelativeRotation =
				Quaternion.Inverse(referenceModelRoot.rotation) * referenceSkeletonRoot.rotation;
			if (Quaternion.Angle(candidateRelativeRotation, referenceRelativeRotation)
				> RotationToleranceDegrees)
			{
				result.Add("skeleton.root-axis", DhmgsAvatarValidationSeverity.Warning,
					"표준 골격 루트 축이 공식 기준과 다릅니다. 계층 구조는 호환되지만 직접 재생 결과를 눈으로 확인하세요.");
			}

			for (Transform current = skeletonRoot != null ? skeletonRoot.parent : null;
				current != null;
				current = current.parent)
			{
				if (!IsFinite(current.localPosition)
					|| !IsFinite(current.localRotation)
					|| !IsFinite(current.localScale))
				{
					result.Add("skeleton.container-transform", DhmgsAvatarValidationSeverity.Error,
						$"FBX 루트와 골격 루트 사이의 컨테이너 Transform '{current.name}'에 NaN 또는 Infinity 값이 포함되어 있습니다.");
				}
				else if (!IsPositiveUniformScaleRatio(current.localScale, Vector3.one, ScaleTolerance))
				{
					result.Add("skeleton.container-scale", DhmgsAvatarValidationSeverity.Warning,
						$"컨테이너 Transform '{current.name}'의 스케일이 0, 음수 또는 비균일입니다. 계층 구조는 호환되지만 직접 재생 결과를 눈으로 확인하세요.");
				}
				if (current == sourceRoot)
				{
					break;
				}
			}
		}

		private static string[] BuildOfficialBlendShapeNames()
		{
			List<string> names = new List<string>(OfficialBlendShapeCount);
			for (int index = 0; index < 10; index++)
			{
				names.Add($"Shape{index:D3}");
			}
			for (int index = 0; index < 10; index++)
			{
				names.Add($"Exp{index:D3}");
			}
			for (int index = 0; index < 486; index++)
			{
				names.Add($"Pose{index:D3}");
			}
			return names.ToArray();
		}

		private static bool Approximately(Vector3 left, Vector3 right, float tolerance)
		{
			return Mathf.Abs(left.x - right.x) <= tolerance
				&& Mathf.Abs(left.y - right.y) <= tolerance
				&& Mathf.Abs(left.z - right.z) <= tolerance;
		}

		private static bool IsPositiveUniformScaleRatio(Vector3 candidate, Vector3 reference, float tolerance)
		{
			if (candidate.x <= 0f || candidate.y <= 0f || candidate.z <= 0f
				|| Mathf.Abs(reference.x) <= Mathf.Epsilon
				|| Mathf.Abs(reference.y) <= Mathf.Epsilon
				|| Mathf.Abs(reference.z) <= Mathf.Epsilon)
			{
				return false;
			}
			float x = candidate.x / reference.x;
			float y = candidate.y / reference.y;
			float z = candidate.z / reference.z;
			return Mathf.Abs(x - y) <= tolerance && Mathf.Abs(x - z) <= tolerance;
		}

		private static bool IsWithinSource(Transform value, Transform sourceRoot)
		{
			return value != null && (value == sourceRoot || value.IsChildOf(sourceRoot));
		}

		private static bool IsFinite(Vector3 value)
		{
			return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
		}

		private static bool IsFinite(Quaternion value)
		{
			return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
		}

		private static bool IsFinite(Matrix4x4 value)
		{
			for (int row = 0; row < 4; row++)
			{
				for (int column = 0; column < 4; column++)
				{
					if (!IsFinite(value[row, column]))
					{
						return false;
					}
				}
			}
			return true;
		}

		private static bool IsFinite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}

		private static string Preview(IReadOnlyList<string> names)
		{
			const int max = 6;
			string value = string.Join(", ", names.Take(max));
			return names.Count > max ? value + ", ..." : value;
		}
	}
}
