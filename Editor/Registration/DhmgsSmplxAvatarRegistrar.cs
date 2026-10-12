using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mnnh.Dhmgs.Editor
{
	internal sealed class DhmgsSmplxAvatarRegistrationOptions
	{
		internal GameObject SourceFbx { get; set; }
		internal string AvatarName { get; set; }
		internal string PrefabPath { get; set; }
		internal DhmgsSmplxSkeletonReference SkeletonReference { get; set; } =
			DhmgsSmplxSkeletonReference.Neutral;
		internal bool AddToFullDemo { get; set; } = true;
	}

	internal sealed class DhmgsSmplxAvatarRegistrationResult
	{
		internal GameObject Prefab { get; set; }
		internal string PrefabPath { get; set; }
		internal string FullDemoScenePath { get; set; }
		internal bool AddedToFullDemo { get; set; }
		internal bool AlreadyInFullDemo { get; set; }
		internal DhmgsSmplxAvatarValidationResult Validation { get; set; }
	}

	internal static class DhmgsSmplxAvatarRegistrar
	{
		internal const string DefaultAvatarFolder = "Assets/Mnnh.Dhmgs/Content/Avatars";
		private static readonly HashSet<string> WindowsReservedNames = new HashSet<string>(
			new[]
			{
				"CON", "PRN", "AUX", "NUL",
				"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
				"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
			},
			StringComparer.OrdinalIgnoreCase);

		internal static DhmgsSmplxAvatarRegistrationResult Register(
			DhmgsSmplxAvatarRegistrationOptions options)
		{
			if (options == null)
			{
				throw new InvalidOperationException("아바타 등록 설정이 없습니다.");
			}
			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				throw new InvalidOperationException("아바타를 등록하기 전에 플레이 모드를 종료하세요.");
			}

			string avatarName = ValidateAvatarName(options.AvatarName);
			string prefabPath = ValidatePrefabPath(options.PrefabPath);
			DhmgsSmplxAvatarValidationResult validation =
				DhmgsSmplxAvatarValidator.Validate(options.SourceFbx, options.SkeletonReference);
			if (!validation.CanRegister)
			{
				string errors = string.Join("\n", validation.Diagnostics
					.Where(item => item.Severity == DhmgsAvatarValidationSeverity.Error)
					.Select(item => "- " + item.Message));
				throw new InvalidOperationException("이 FBX는 DHMGS 재생 구조와 호환되지 않습니다:\n" + errors);
			}

			string fullDemoPath = string.Empty;
			if (options.AddToFullDemo)
			{
				if (!TryResolveFullDemoScene(out fullDemoPath, out string reason))
				{
					throw new InvalidOperationException(reason);
				}
				EnsureSceneCanBeModified(fullDemoPath);
			}

			UnityEngine.Object existingPrefab = AssetDatabase.LoadMainAssetAtPath(prefabPath);
			if (existingPrefab != null && !(existingPrefab is GameObject))
			{
				throw new InvalidOperationException($"'{prefabPath}' 경로에 다른 에셋이 이미 있습니다.");
			}
			byte[] previousPrefabContents = existingPrefab != null
				? File.ReadAllBytes(ToAbsolutePath(prefabPath))
				: null;
			string[] previousLabels = existingPrefab != null
				? AssetDatabase.GetLabels(existingPrefab)
				: Array.Empty<string>();

			try
			{
				GameObject prefab = CreateWrapperPrefab(
					options.SourceFbx,
					avatarName,
					prefabPath,
					validation);

				DhmgsSmplxAvatarRegistrationResult result = new DhmgsSmplxAvatarRegistrationResult
				{
					Prefab = prefab,
					PrefabPath = prefabPath,
					FullDemoScenePath = fullDemoPath,
					Validation = validation,
				};
				if (options.AddToFullDemo)
				{
					result.AddedToFullDemo = AddToFullDemoScene(
						prefab,
						avatarName,
						fullDemoPath,
						out bool alreadyRegistered);
					result.AlreadyInFullDemo = alreadyRegistered;
				}
				return result;
			}
			catch (Exception registrationException)
			{
				Exception rollbackException = TryRestorePrefab(
					prefabPath,
					previousPrefabContents,
					previousLabels);
				if (rollbackException != null)
				{
					throw new AggregateException(
						"SMPL-X 아바타 등록에 실패했으며 기존 래퍼 프리팹도 복원하지 못했습니다.",
						registrationException,
						rollbackException);
				}
				throw;
			}
		}

		internal static GameObject CreateWrapperPrefab(
			GameObject sourceFbx,
			string avatarName,
			string prefabPath,
			DhmgsSmplxAvatarValidationResult validation)
		{
			if (sourceFbx == null)
			{
				throw new InvalidOperationException("원본 FBX를 선택하세요.");
			}
			if (validation == null || !validation.CanRegister)
			{
				throw new InvalidOperationException("래퍼를 만들기 전에 SMPL-X 검증을 통과해야 합니다.");
			}

			avatarName = ValidateAvatarName(avatarName);
			prefabPath = ValidatePrefabPath(prefabPath);
			EnsureAssetFolder(Path.GetDirectoryName(prefabPath)?.Replace('\\', '/'));
			UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(prefabPath);
			if (existing != null && !(existing is GameObject))
			{
				throw new InvalidOperationException($"'{prefabPath}' 경로에 다른 에셋이 이미 있습니다.");
			}

			bool editingExisting = existing is GameObject;
			Scene previewScene = default;
			GameObject wrapperRoot = null;
			try
			{
				if (editingExisting)
				{
					wrapperRoot = PrefabUtility.LoadPrefabContents(prefabPath);
					foreach (Transform child in wrapperRoot.transform.Cast<Transform>().ToArray())
					{
						UnityEngine.Object.DestroyImmediate(child.gameObject);
					}
				}
				else
				{
					previewScene = EditorSceneManager.NewPreviewScene();
					wrapperRoot = new GameObject(avatarName);
					SceneManager.MoveGameObjectToScene(wrapperRoot, previewScene);
				}
				wrapperRoot.name = avatarName;

				GameObject modelInstance = PrefabUtility.InstantiatePrefab(
					sourceFbx,
					wrapperRoot.scene) as GameObject;
				if (modelInstance == null)
				{
					throw new InvalidOperationException("선택한 FBX 모델의 인스턴스를 만들 수 없습니다.");
				}

				modelInstance.name = "DHMGS Source Model";
				modelInstance.transform.SetParent(wrapperRoot.transform, false);
				DhmgsRigDriver[] existingDrivers = wrapperRoot.GetComponents<DhmgsRigDriver>();
				DhmgsRigDriver driver = existingDrivers.FirstOrDefault()
					?? wrapperRoot.AddComponent<DhmgsRigDriver>();
				foreach (DhmgsRigDriver duplicate in existingDrivers.Skip(1))
				{
					UnityEngine.Object.DestroyImmediate(duplicate);
				}
				Transform rigRoot = string.IsNullOrEmpty(validation.SkeletonRootPath)
					? modelInstance.transform
					: modelInstance.transform.Find(validation.SkeletonRootPath);
				if (rigRoot == null)
				{
					throw new InvalidOperationException(
						$"검증된 골격 루트 '{validation.SkeletonRootPath}'를 FBX 인스턴스에서 찾을 수 없습니다.");
				}

				SerializedObject driverObject = new SerializedObject(driver);
				SerializedProperty rigRootProperty = driverObject.FindProperty("_rigRoot");
				if (rigRootProperty == null)
				{
					throw new InvalidOperationException("DhmgsRigDriver의 골격 루트 필드를 찾을 수 없습니다.");
				}
				rigRootProperty.objectReferenceValue = rigRoot;
				driverObject.ApplyModifiedPropertiesWithoutUndo();

				DhmgsSkinOpacity[] opacityComponents = wrapperRoot.GetComponents<DhmgsSkinOpacity>();
				if (validation.SupportsOpacity && opacityComponents.Length == 0)
				{
					wrapperRoot.AddComponent<DhmgsSkinOpacity>();
				}
				else if (!validation.SupportsOpacity)
				{
					foreach (DhmgsSkinOpacity opacity in opacityComponents)
					{
						UnityEngine.Object.DestroyImmediate(opacity);
					}
				}
				else
				{
					foreach (DhmgsSkinOpacity duplicate in opacityComponents.Skip(1))
					{
						UnityEngine.Object.DestroyImmediate(duplicate);
					}
				}

				GameObject saved = PrefabUtility.SaveAsPrefabAsset(wrapperRoot, prefabPath, out bool success);
				if (!success || saved == null)
				{
					throw new InvalidOperationException($"래퍼 프리팹을 '{prefabPath}'에 저장할 수 없습니다.");
				}
			}
			finally
			{
				if (editingExisting)
				{
					if (wrapperRoot != null)
					{
						PrefabUtility.UnloadPrefabContents(wrapperRoot);
					}
				}
				else if (previewScene.IsValid())
				{
					EditorSceneManager.ClosePreviewScene(previewScene);
				}
			}

			AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
			DhmgsSmplxAvatarRemoval.MarkGeneratedWrapper(prefabPath);
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
			if (prefab == null)
			{
				throw new InvalidOperationException($"저장한 래퍼 프리팹을 '{prefabPath}'에서 불러올 수 없습니다.");
			}
			return prefab;
		}

		internal static bool AddToFullDemoScene(
			GameObject wrapperPrefab,
			string avatarName,
			string scenePath,
			out bool alreadyRegistered)
		{
			alreadyRegistered = false;
			if (wrapperPrefab == null)
			{
				throw new InvalidOperationException("등록할 래퍼 프리팹이 없습니다.");
			}
			if (string.IsNullOrEmpty(scenePath))
			{
				throw new InvalidOperationException("전체 데모 씬 경로가 필요합니다.");
			}

			Scene scene = SceneManager.GetSceneByPath(scenePath);
			bool openedForRegistration = !scene.IsValid() || !scene.isLoaded;
			if (openedForRegistration)
			{
				scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			}

			try
			{
				DhmgsMotionPlayer[] players = scene.GetRootGameObjects()
					.SelectMany(root => root.GetComponentsInChildren<DhmgsMotionPlayer>(true))
					.ToArray();
				if (players.Length != 1)
				{
					throw new InvalidOperationException(
						$"전체 데모 씬에는 DhmgsMotionPlayer가 정확히 하나 있어야 합니다. 현재 {players.Length}개가 있습니다.");
				}

				DhmgsMotionPlayer player = players[0];
				SerializedObject playerObject = new SerializedObject(player);
				SerializedProperty avatars = playerObject.FindProperty("_avatars");
				if (avatars == null || !avatars.isArray)
				{
					throw new InvalidOperationException("DhmgsMotionPlayer의 아바타 목록 필드를 찾을 수 없습니다.");
				}

				string prefabPath = AssetDatabase.GetAssetPath(wrapperPrefab);
				Transform avatarParent = null;
				for (int index = 0; index < avatars.arraySize; index++)
				{
					DhmgsRigDriver existing = avatars.GetArrayElementAtIndex(index).objectReferenceValue as DhmgsRigDriver;
					if (existing == null)
					{
						continue;
					}
					avatarParent ??= existing.transform.parent;
					string existingPrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(existing.gameObject);
					if (existingPrefabPath.Equals(prefabPath, StringComparison.OrdinalIgnoreCase))
					{
						GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(existing.gameObject)
							?? existing.gameObject;
						if (!string.Equals(instanceRoot.name, avatarName, StringComparison.Ordinal))
						{
							instanceRoot.name = avatarName;
							EditorSceneManager.MarkSceneDirty(scene);
							if (!EditorSceneManager.SaveScene(scene))
							{
								throw new InvalidOperationException(
									$"전체 데모 씬 '{scenePath}'을(를) 저장할 수 없습니다.");
							}
						}
						alreadyRegistered = true;
						return false;
					}
				}

				GameObject instance = null;
				try
				{
					instance = PrefabUtility.InstantiatePrefab(wrapperPrefab, scene) as GameObject;
					if (instance == null)
					{
						throw new InvalidOperationException("전체 데모 씬에 래퍼 프리팹 인스턴스를 만들 수 없습니다.");
					}
					instance.name = avatarName;
					if (avatarParent != null)
					{
						instance.transform.SetParent(avatarParent, false);
					}
					instance.transform.localPosition = Vector3.zero;
					instance.transform.localRotation = Quaternion.identity;
					instance.transform.localScale = Vector3.one;
					instance.SetActive(false);

					DhmgsRigDriver driver = instance.GetComponent<DhmgsRigDriver>();
					if (driver == null)
					{
						UnityEngine.Object.DestroyImmediate(instance);
						throw new InvalidOperationException("생성된 래퍼의 루트에 DhmgsRigDriver가 없습니다.");
					}

					playerObject.Update();
					avatars = playerObject.FindProperty("_avatars");
					int newIndex = avatars.arraySize;
					avatars.InsertArrayElementAtIndex(newIndex);
					avatars.GetArrayElementAtIndex(newIndex).objectReferenceValue = driver;
					playerObject.ApplyModifiedPropertiesWithoutUndo();
					EditorUtility.SetDirty(player);
					EditorSceneManager.MarkSceneDirty(scene);
					if (!EditorSceneManager.SaveScene(scene))
					{
						throw new InvalidOperationException($"전체 데모 씬 '{scenePath}'을 저장할 수 없습니다.");
					}
					return true;
				}
				catch
				{
					if (instance != null)
					{
						playerObject.Update();
						SerializedProperty rollbackAvatars = playerObject.FindProperty("_avatars");
						for (int index = rollbackAvatars.arraySize - 1; index >= 0; index--)
						{
							DhmgsRigDriver listed = rollbackAvatars.GetArrayElementAtIndex(index).objectReferenceValue as DhmgsRigDriver;
							if (listed != null && listed.gameObject == instance)
							{
								RemoveObjectReferenceAt(rollbackAvatars, index);
							}
						}
						playerObject.ApplyModifiedPropertiesWithoutUndo();
						UnityEngine.Object.DestroyImmediate(instance);
					}
					throw;
				}
			}
			finally
			{
				if (openedForRegistration && scene.IsValid() && scene.isLoaded)
				{
					EditorSceneManager.CloseScene(scene, true);
				}
			}
		}

		internal static bool TryResolveFullDemoScene(out string scenePath, out string reason)
		{
			string[] candidates = AssetDatabase.FindAssets("DhmgsFullDemo t:Scene", new[] { "Assets/Samples" })
				.Select(AssetDatabase.GUIDToAssetPath)
				.Where(path => path.StartsWith("Assets/Samples/", StringComparison.OrdinalIgnoreCase)
					&& Path.GetFileName(path).Equals("DhmgsFullDemo.unity", StringComparison.OrdinalIgnoreCase)
					&& File.Exists(ToAbsolutePath(path)))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			if (candidates.Length == 1)
			{
				scenePath = candidates[0];
				reason = string.Empty;
				return true;
			}

			scenePath = string.Empty;
			reason = candidates.Length > 1
				? "임포트된 DHMGS 전체 데모 씬이 여러 개 있습니다. 아바타를 등록하기 전에 이전 버전의 샘플을 제거하세요."
				: "아바타를 씬에 추가하려면 먼저 MNNH DHMGS 전체 데모 샘플을 임포트하세요.";
			return false;
		}

		internal static string BuildDefaultPrefabPath(string avatarName)
		{
			string fileName = SanitizeFileName(string.IsNullOrWhiteSpace(avatarName) ? "New SMPLX Avatar" : avatarName.Trim());
			return $"{DefaultAvatarFolder}/{fileName}.prefab";
		}

		internal static void EnsureSceneCanBeModified(string scenePath)
		{
			Scene loaded = SceneManager.GetSceneByPath(scenePath);
			if (loaded.IsValid() && loaded.isLoaded && loaded.isDirty)
			{
				throw new InvalidOperationException(
					"임포트된 전체 데모 씬에 저장하지 않은 변경 사항이 있습니다. 아바타를 등록하기 전에 저장하거나 되돌리세요.");
			}
		}

		private static string ValidateAvatarName(string value)
		{
			string name = value?.Trim();
			if (string.IsNullOrEmpty(name))
			{
				throw new InvalidOperationException("아바타 이름을 입력하세요.");
			}
			if (name.Any(char.IsControl))
			{
				throw new InvalidOperationException("아바타 이름에는 제어 문자를 사용할 수 없습니다.");
			}
			return name;
		}

		private static string ValidatePrefabPath(string value)
		{
			string path = value?.Trim().Replace('\\', '/');
			if (string.IsNullOrEmpty(path)
				|| !path.StartsWith("Assets/", StringComparison.Ordinal)
				|| !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("프리팹 저장 경로는 Assets/...로 시작하고 .prefab으로 끝나야 합니다.");
			}
			string[] segments = path.Split('/');
			if (path.Contains("//") || segments.Any(IsInvalidPathSegment))
			{
				throw new InvalidOperationException("프리팹 저장 경로에 사용할 수 없는 경로 구간이 있습니다.");
			}

			string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
				?? throw new InvalidOperationException("Unity 프로젝트 루트 경로를 확인할 수 없습니다.");
			string absolute = Path.GetFullPath(Path.Combine(projectRoot, path));
			string assetsRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
			if (!absolute.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("프리팹은 현재 프로젝트의 Assets 폴더 안에 저장해야 합니다.");
			}

			string packageRoot = ResolveEmbeddedPackageRoot();
			if (!string.IsNullOrEmpty(packageRoot)
				&& (path.Equals(packageRoot, StringComparison.OrdinalIgnoreCase)
					|| path.StartsWith(packageRoot + "/", StringComparison.OrdinalIgnoreCase)))
			{
				throw new InvalidOperationException("등록 아바타는 읽기 전용 DHMGS 패키지 밖에 저장해야 합니다.");
			}
			return path;
		}

		internal static string ResolveEmbeddedPackageRoot()
		{
			foreach (string guid in AssetDatabase.FindAssets("Mnnh.Dhmgs.Editor t:asmdef"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
				const string suffix = "/Editor/Mnnh.Dhmgs.Editor.asmdef";
				if (path.EndsWith(suffix, StringComparison.Ordinal))
				{
					return path.Substring(0, path.Length - suffix.Length);
				}
			}
			return string.Empty;
		}

		private static void EnsureAssetFolder(string folder)
		{
			if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets", StringComparison.Ordinal))
			{
				throw new InvalidOperationException("올바른 Assets 폴더 경로가 필요합니다.");
			}
			string[] parts = folder.Split('/');
			string current = parts[0];
			for (int index = 1; index < parts.Length; index++)
			{
				string next = current + "/" + parts[index];
				if (!AssetDatabase.IsValidFolder(next))
				{
					string guid = AssetDatabase.CreateFolder(current, parts[index]);
					if (string.IsNullOrEmpty(guid))
					{
						throw new InvalidOperationException($"에셋 폴더 '{next}'을 만들 수 없습니다.");
					}
				}
				current = next;
			}
		}

		private static string SanitizeFileName(string value)
		{
			HashSet<char> invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
			char[] characters = value.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
			string result = new string(characters).Trim().TrimEnd('.');
			if (string.IsNullOrEmpty(result))
			{
				return "New SMPLX Avatar";
			}
			return IsWindowsReservedName(result) ? "_" + result : result;
		}

		private static bool IsInvalidPathSegment(string segment)
		{
			return string.IsNullOrWhiteSpace(segment)
				|| segment == "."
				|| segment == ".."
				|| segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
				|| !segment.Equals(segment.TrimEnd(' ', '.'), StringComparison.Ordinal)
				|| IsWindowsReservedName(segment);
		}

		private static bool IsWindowsReservedName(string segment)
		{
			string stem = segment.Split('.')[0];
			return WindowsReservedNames.Contains(stem);
		}

		internal static void RemoveObjectReferenceAt(SerializedProperty array, int index)
		{
			int oldSize = array.arraySize;
			array.DeleteArrayElementAtIndex(index);
			if (array.arraySize == oldSize)
			{
				array.DeleteArrayElementAtIndex(index);
			}
		}

		private static Exception TryRestorePrefab(
			string prefabPath,
			byte[] previousContents,
			string[] previousLabels)
		{
			try
			{
				if (previousContents == null)
				{
					if ((AssetDatabase.LoadMainAssetAtPath(prefabPath) != null
							|| File.Exists(ToAbsolutePath(prefabPath)))
						&& !AssetDatabase.DeleteAsset(prefabPath))
					{
						throw new IOException($"일부만 생성된 프리팹 '{prefabPath}'을 제거할 수 없습니다.");
					}
					return null;
				}

				File.WriteAllBytes(ToAbsolutePath(prefabPath), previousContents);
				AssetDatabase.ImportAsset(
					prefabPath,
					ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
				GameObject restoredPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
				if (restoredPrefab != null)
				{
					AssetDatabase.SetLabels(restoredPrefab, previousLabels ?? Array.Empty<string>());
				}
				if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
				{
					throw new IOException($"복원한 프리팹 '{prefabPath}'을 불러올 수 없습니다.");
				}
				return null;
			}
			catch (Exception exception)
			{
				return exception;
			}
		}

		internal static string ToAbsolutePath(string assetPath)
		{
			string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
			return Path.GetFullPath(Path.Combine(projectRoot ?? string.Empty, assetPath));
		}
	}
}
