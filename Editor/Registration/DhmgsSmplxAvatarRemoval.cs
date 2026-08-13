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
	internal enum DhmgsSmplxAvatarRemovalKind
	{
		Wrapper,
		BrokenSceneRegistration,
	}

	internal sealed class DhmgsSmplxAvatarRemovalEntry
	{
		internal DhmgsSmplxAvatarRemovalKind Kind { get; set; }
		internal string AvatarName { get; set; }
		internal string PrefabPath { get; set; }
		internal string PrefabGuid { get; set; }
		internal string ScenePath { get; set; }
		internal string SceneObjectName { get; set; }
		internal string SceneObjectId { get; set; }
		internal int AvatarEntryCount { get; set; }
		internal int SceneInstanceCount { get; set; }

		internal bool IsBroken => Kind == DhmgsSmplxAvatarRemovalKind.BrokenSceneRegistration;
	}

	internal sealed class DhmgsSmplxAvatarRemovalResult
	{
		internal string AvatarName { get; set; }
		internal string PrefabPath { get; set; }
		internal int RemovedAvatarEntries { get; set; }
		internal int RemovedSceneInstances { get; set; }
		internal bool WrapperMovedToTrash { get; set; }
		internal bool WrapperKept { get; set; }
	}

	internal sealed class DhmgsSmplxAvatarExternalReferenceException : InvalidOperationException
	{
		internal DhmgsSmplxAvatarExternalReferenceException(
			string prefabPath,
			IReadOnlyList<string> referencePaths)
			: base(
				$"래퍼 프리팹 '{prefabPath}'을(를) 참조하는 에셋이 남아 있어 삭제할 수 없습니다.")
		{
			ReferencePaths = referencePaths;
		}

		internal IReadOnlyList<string> ReferencePaths { get; }
	}

	/// <summary>
	/// Removes project-owned avatar wrapper registrations from the imported Full Demo.
	/// Source FBXs and wrapper dependencies are never deleted.
	/// </summary>
	internal static class DhmgsSmplxAvatarRemoval
	{
		internal static IReadOnlyList<DhmgsSmplxAvatarRemovalEntry> FindRemovableRegistrations(
			out string scenePath,
			out string reason)
		{
			if (!DhmgsSmplxAvatarRegistrar.TryResolveFullDemoScene(out scenePath, out reason))
			{
				return Array.Empty<DhmgsSmplxAvatarRemovalEntry>();
			}

			try
			{
				IReadOnlyList<DhmgsSmplxAvatarRemovalEntry> entries =
					FindRemovableRegistrations(scenePath);
				reason = entries.Count == 0
					? "Full Demo에 사용자가 등록한 SMPL-X 아바타가 없습니다."
					: string.Empty;
				return entries;
			}
			catch (Exception exception)
			{
				reason = exception.Message;
				return Array.Empty<DhmgsSmplxAvatarRemovalEntry>();
			}
		}

		internal static IReadOnlyList<DhmgsSmplxAvatarRemovalEntry> FindRemovableRegistrations(
			string scenePath)
		{
			ValidateScenePath(scenePath);
			Scene scene = SceneManager.GetSceneByPath(scenePath);
			bool openedForInspection = !scene.IsValid() || !scene.isLoaded;
			if (openedForInspection)
			{
				scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			}

			try
			{
				DhmgsMotionPlayer player = FindPlayer(scene);
				SerializedObject playerObject = new SerializedObject(player);
				SerializedProperty avatars = RequireAvatarList(playerObject);
				Dictionary<string, DhmgsSmplxAvatarRemovalEntry> wrappers =
					new Dictionary<string, DhmgsSmplxAvatarRemovalEntry>(StringComparer.OrdinalIgnoreCase);
				Transform avatarParent = null;
				HashSet<string> listedBrokenIds = new HashSet<string>(StringComparer.Ordinal);
				int nullEntries = 0;
				Dictionary<string, DhmgsSmplxAvatarRemovalEntry> brokenByName =
					new Dictionary<string, DhmgsSmplxAvatarRemovalEntry>(StringComparer.Ordinal);

				for (int index = 0; index < avatars.arraySize; index++)
				{
					DhmgsRigDriver driver = avatars.GetArrayElementAtIndex(index).objectReferenceValue
						as DhmgsRigDriver;
					if (driver == null)
					{
						nullEntries++;
						continue;
					}

					avatarParent ??= driver.transform.parent;
					GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(driver.gameObject);
					if (instanceRoot != null && PrefabUtility.IsPrefabAssetMissing(instanceRoot))
					{
						string brokenId = GetSceneObjectId(instanceRoot);
						listedBrokenIds.Add(brokenId);
						if (!brokenByName.TryGetValue(
							brokenId,
							out DhmgsSmplxAvatarRemovalEntry broken))
						{
							broken = new DhmgsSmplxAvatarRemovalEntry
							{
								Kind = DhmgsSmplxAvatarRemovalKind.BrokenSceneRegistration,
								AvatarName = instanceRoot.name,
								SceneObjectName = instanceRoot.name,
								SceneObjectId = brokenId,
								PrefabPath = string.Empty,
								ScenePath = scenePath,
							};
							brokenByName.Add(brokenId, broken);
						}
						broken.AvatarEntryCount++;
						continue;
					}

					string prefabPath = NormalizePath(
						PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(driver.gameObject));
					if (!IsGeneratedWrapperPrefab(prefabPath))
					{
						continue;
					}

					if (!wrappers.TryGetValue(prefabPath, out DhmgsSmplxAvatarRemovalEntry entry))
					{
						entry = new DhmgsSmplxAvatarRemovalEntry
						{
							Kind = DhmgsSmplxAvatarRemovalKind.Wrapper,
							AvatarName = instanceRoot != null ? instanceRoot.name : driver.gameObject.name,
							PrefabPath = prefabPath,
							PrefabGuid = AssetDatabase.AssetPathToGUID(prefabPath),
							ScenePath = scenePath,
						};
						wrappers.Add(prefabPath, entry);
					}
					entry.AvatarEntryCount++;
				}

				foreach (DhmgsSmplxAvatarRemovalEntry entry in wrappers.Values)
				{
					entry.SceneInstanceCount = FindWrapperInstanceRoots(scene, entry.PrefabPath).Count;
				}

				List<DhmgsSmplxAvatarRemovalEntry> result = wrappers.Values
					.OrderBy(item => item.AvatarName, StringComparer.CurrentCultureIgnoreCase)
					.ToList();
				foreach (DhmgsSmplxAvatarRemovalEntry broken in brokenByName.Values)
				{
					broken.SceneInstanceCount = FindBrokenAvatarRoots(
						scene,
						avatarParent,
						broken.SceneObjectId).Count;
					result.Add(broken);
				}
				foreach (GameObject missingRoot in FindBrokenAvatarRoots(scene, avatarParent, null))
				{
					string missingId = GetSceneObjectId(missingRoot);
					if (listedBrokenIds.Contains(missingId))
					{
						continue;
					}
					result.Add(new DhmgsSmplxAvatarRemovalEntry
					{
						Kind = DhmgsSmplxAvatarRemovalKind.BrokenSceneRegistration,
						AvatarName = missingRoot.name,
						SceneObjectName = missingRoot.name,
						SceneObjectId = missingId,
						PrefabPath = string.Empty,
						ScenePath = scenePath,
						AvatarEntryCount = 0,
						SceneInstanceCount = 1,
					});
				}
				if (nullEntries > 0)
				{
					result.Add(new DhmgsSmplxAvatarRemovalEntry
					{
						Kind = DhmgsSmplxAvatarRemovalKind.BrokenSceneRegistration,
						AvatarName = $"대상을 확인할 수 없는 빈 목록 {nullEntries}개",
						SceneObjectName = string.Empty,
						PrefabPath = string.Empty,
						ScenePath = scenePath,
						AvatarEntryCount = nullEntries,
						SceneInstanceCount = 0,
					});
				}
				return result;
			}
			finally
			{
				if (openedForInspection && scene.IsValid() && scene.isLoaded)
				{
					EditorSceneManager.CloseScene(scene, true);
				}
			}
		}

		internal static DhmgsSmplxAvatarRemovalResult Remove(
			DhmgsSmplxAvatarRemovalEntry requestedEntry,
			bool removeWrapperPrefab)
		{
			if (requestedEntry == null)
			{
				throw new InvalidOperationException("제거할 아바타를 선택하세요.");
			}
			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				throw new InvalidOperationException("아바타를 제거하려면 먼저 플레이 모드를 종료하세요.");
			}
			if (PrefabStageUtility.GetCurrentPrefabStage() != null)
			{
				throw new InvalidOperationException("프리팹 모드를 종료한 뒤 아바타를 제거하세요.");
			}

			ValidateScenePath(requestedEntry.ScenePath);
			EnsureSceneCanBeRemovedFrom(requestedEntry.ScenePath);
			DhmgsSmplxAvatarRemovalEntry current = ResolveCurrentEntry(requestedEntry);
			if (!current.IsBroken)
			{
				if (!string.IsNullOrEmpty(requestedEntry.PrefabGuid)
					&& !string.Equals(
						requestedEntry.PrefabGuid,
						current.PrefabGuid,
						StringComparison.Ordinal))
				{
					throw new InvalidOperationException(
						"선택한 래퍼 프리팹이 다른 에셋으로 교체되었습니다. 목록을 새로고침하세요.");
				}
				ValidateWrapperPrefab(current.PrefabPath);
				if (removeWrapperPrefab)
				{
					EnsureNoOtherDirtyScenes(current.ScenePath);
					EnsureNoDirtyPersistentAssets(current.PrefabPath, current.ScenePath);
					IReadOnlyList<string> references = FindExternalReferences(
						current.PrefabPath,
						current.ScenePath);
					if (references.Count > 0)
					{
						throw new DhmgsSmplxAvatarExternalReferenceException(
							current.PrefabPath,
							references);
					}
				}
			}

			byte[] sceneSnapshot = File.ReadAllBytes(
				DhmgsSmplxAvatarRegistrar.ToAbsolutePath(current.ScenePath));
			Scene preexistingScene = SceneManager.GetSceneByPath(current.ScenePath);
			bool wasLoaded = preexistingScene.IsValid() && preexistingScene.isLoaded;
			bool wasActive = wasLoaded && SceneManager.GetActiveScene() == preexistingScene;
			DhmgsSmplxAvatarRemovalResult result;
			try
			{
				result = RemoveFromScene(current, removeWrapperPrefab);
			}
			catch (Exception removalException)
			{
				Exception rollbackException = TryRestoreScene(
					current.ScenePath,
					sceneSnapshot,
					wasLoaded,
					wasActive);
				if (rollbackException != null)
				{
					throw new AggregateException(
						"아바타 제거와 Full Demo 원상 복구가 모두 실패했습니다.",
						removalException,
						rollbackException);
				}
				throw;
			}

			result.AvatarName = current.AvatarName;
			result.PrefabPath = current.PrefabPath;
			if (current.IsBroken || !removeWrapperPrefab)
			{
				result.WrapperKept = !current.IsBroken;
				return result;
			}

			// This is the final commit. No fallible work is performed after the asset is trashed.
			bool movedToTrash;
			try
			{
				movedToTrash = AssetDatabase.MoveAssetToTrash(current.PrefabPath);
			}
			catch (Exception trashException)
			{
				throw RollBackAfterTrashFailure(
					current,
					sceneSnapshot,
					wasLoaded,
					wasActive,
					trashException);
			}
			if (!movedToTrash)
			{
				InvalidOperationException deleteException = new InvalidOperationException(
					$"래퍼 프리팹 '{current.PrefabPath}'을(를) 휴지통으로 이동하지 못했습니다.");
				throw RollBackAfterTrashFailure(
					current,
					sceneSnapshot,
					wasLoaded,
					wasActive,
					deleteException);
			}

			result.WrapperMovedToTrash = true;
			return result;
		}

		private static DhmgsSmplxAvatarRemovalResult RemoveFromScene(
			DhmgsSmplxAvatarRemovalEntry entry,
			bool requireNoRemainingWrapperReferences)
		{
			Scene scene = SceneManager.GetSceneByPath(entry.ScenePath);
			bool openedForRemoval = !scene.IsValid() || !scene.isLoaded;
			if (openedForRemoval)
			{
				scene = EditorSceneManager.OpenScene(entry.ScenePath, OpenSceneMode.Additive);
			}

			try
			{
				DhmgsMotionPlayer player = FindPlayer(scene);
				SerializedObject playerObject = new SerializedObject(player);
				SerializedProperty avatars = RequireAvatarList(playerObject);
				SerializedProperty defaultAvatar = playerObject.FindProperty("_defaultAvatar")
					?? throw new InvalidOperationException(
						"DhmgsMotionPlayer에서 기본 아바타 필드를 찾을 수 없습니다.");
				int oldDefaultIndex = defaultAvatar.intValue;
				DhmgsRigDriver oldDefaultDriver = oldDefaultIndex >= 0 && oldDefaultIndex < avatars.arraySize
					? avatars.GetArrayElementAtIndex(oldDefaultIndex).objectReferenceValue as DhmgsRigDriver
					: null;
				Transform avatarParent = avatars.arraySize > 0
					? Enumerable.Range(0, avatars.arraySize)
						.Select(index => avatars.GetArrayElementAtIndex(index).objectReferenceValue as DhmgsRigDriver)
						.FirstOrDefault(driver => driver != null)?.transform.parent
					: null;

				List<int> removedIndices = new List<int>();
				for (int index = avatars.arraySize - 1; index >= 0; index--)
				{
					DhmgsRigDriver driver = avatars.GetArrayElementAtIndex(index).objectReferenceValue
						as DhmgsRigDriver;
					bool remove = entry.IsBroken
						? string.IsNullOrEmpty(entry.SceneObjectName)
							? driver == null
							: driver != null
								&& IsMissingPrefabDriver(driver)
								&& string.Equals(
									GetSceneObjectId(
										PrefabUtility.GetNearestPrefabInstanceRoot(driver.gameObject)
											?? driver.gameObject),
									entry.SceneObjectId,
									StringComparison.Ordinal)
						: driver != null && PathsEqual(
							PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(driver.gameObject),
							entry.PrefabPath);
					if (!remove)
					{
						continue;
					}
					removedIndices.Add(index);
					DhmgsSmplxAvatarRegistrar.RemoveObjectReferenceAt(avatars, index);
				}

				List<GameObject> roots = entry.IsBroken
					? string.IsNullOrEmpty(entry.SceneObjectId)
						? new List<GameObject>()
						: FindBrokenAvatarRoots(scene, avatarParent, entry.SceneObjectId)
					: FindWrapperInstanceRoots(scene, entry.PrefabPath);
				defaultAvatar.intValue = ResolveDefaultAvatarIndex(
					avatars,
					oldDefaultIndex,
					oldDefaultDriver,
					removedIndices);
				playerObject.ApplyModifiedPropertiesWithoutUndo();
				foreach (GameObject root in roots)
				{
					UnityEngine.Object.DestroyImmediate(root);
				}

				EditorUtility.SetDirty(player);
				EditorSceneManager.MarkSceneDirty(scene);
				if (!EditorSceneManager.SaveScene(scene))
				{
					throw new IOException($"Full Demo 씬 '{entry.ScenePath}'을(를) 저장하지 못했습니다.");
				}
				if (requireNoRemainingWrapperReferences
					&& AssetDatabase.GetDependencies(entry.ScenePath, false)
						.Any(dependency => PathsEqual(dependency, entry.PrefabPath)))
				{
					throw new DhmgsSmplxAvatarExternalReferenceException(
						entry.PrefabPath,
						new[] { entry.ScenePath });
				}
				if (openedForRemoval && !EditorSceneManager.CloseScene(scene, true))
				{
					throw new IOException($"Full Demo 씬 '{entry.ScenePath}'을(를) 닫지 못했습니다.");
				}

				return new DhmgsSmplxAvatarRemovalResult
				{
					RemovedAvatarEntries = removedIndices.Count,
					RemovedSceneInstances = roots.Count,
				};
			}
			catch
			{
				if (openedForRemoval && scene.IsValid() && scene.isLoaded)
				{
					EditorSceneManager.CloseScene(scene, true);
				}
				throw;
			}
		}

		private static DhmgsSmplxAvatarRemovalEntry ResolveCurrentEntry(
			DhmgsSmplxAvatarRemovalEntry requested)
		{
			IReadOnlyList<DhmgsSmplxAvatarRemovalEntry> current =
				FindRemovableRegistrations(requested.ScenePath);
			DhmgsSmplxAvatarRemovalEntry resolved = requested.IsBroken
				? current.FirstOrDefault(item => item.IsBroken
					&& string.Equals(
						item.SceneObjectId,
						requested.SceneObjectId,
						StringComparison.Ordinal))
				: current.FirstOrDefault(item =>
					!item.IsBroken && PathsEqual(item.PrefabPath, requested.PrefabPath));
			return resolved ?? throw new InvalidOperationException(
				"선택한 아바타 등록이 이미 변경되었습니다. 목록을 새로고침하세요.");
		}

		private static DhmgsMotionPlayer FindPlayer(Scene scene)
		{
			DhmgsMotionPlayer[] players = scene.GetRootGameObjects()
				.SelectMany(root => root.GetComponentsInChildren<DhmgsMotionPlayer>(true))
				.ToArray();
			if (players.Length != 1)
			{
				throw new InvalidOperationException(
					$"Full Demo 씬에는 DhmgsMotionPlayer가 정확히 하나 있어야 합니다. 현재 {players.Length}개입니다.");
			}
			return players[0];
		}

		private static SerializedProperty RequireAvatarList(SerializedObject playerObject)
		{
			SerializedProperty avatars = playerObject.FindProperty("_avatars");
			if (avatars == null || !avatars.isArray)
			{
				throw new InvalidOperationException(
					"DhmgsMotionPlayer에서 아바타 목록 필드를 찾을 수 없습니다.");
			}
			return avatars;
		}

		private static GameObject ValidateWrapperPrefab(string prefabPath)
		{
			if (!IsProjectOwnedWrapperPath(prefabPath))
			{
				throw new InvalidOperationException(
					"패키지 내장 아바타나 Assets 밖의 에셋은 제거할 수 없습니다.");
			}
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
			if (prefab == null)
			{
				throw new InvalidOperationException(
					$"래퍼 프리팹 '{prefabPath}'이(가) 이미 없습니다. 목록을 새로고침하세요.");
			}
			if (!IsGeneratedWrapperPrefab(prefabPath))
			{
				throw new InvalidOperationException(
					$"'{prefabPath}'은(는) DHMGS가 생성한 아바타 래퍼 프리팹이 아닙니다.");
			}
			return prefab;
		}

		private static IReadOnlyList<string> FindExternalReferences(
			string prefabPath,
			string targetScenePath)
		{
			List<string> references = new List<string>();
			foreach (string path in AssetDatabase.GetAllAssetPaths())
			{
				if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
					|| PathsEqual(path, prefabPath)
					|| PathsEqual(path, targetScenePath))
				{
					continue;
				}
				if (AssetDatabase.IsValidFolder(path))
				{
					continue;
				}
				if (AssetDatabase.GetDependencies(path, false)
					.Any(dependency => PathsEqual(dependency, prefabPath)))
				{
					references.Add(path);
				}
			}
			return references;
		}

		private static Exception RollBackAfterTrashFailure(
			DhmgsSmplxAvatarRemovalEntry entry,
			byte[] sceneSnapshot,
			bool wasLoaded,
			bool wasActive,
			Exception failure)
		{
			// If the asset disappeared despite an exception, restoring the scene would create
			// a broken reference. Report that uncertain commit instead of making it worse.
			if (AssetDatabase.LoadMainAssetAtPath(entry.PrefabPath) == null
				&& !File.Exists(DhmgsSmplxAvatarRegistrar.ToAbsolutePath(entry.PrefabPath)))
			{
				return new InvalidOperationException(
					"래퍼 프리팹 이동 중 오류가 발생했으나 프리팹 파일은 이미 사라졌습니다. "
					+ "Full Demo는 정리된 상태로 유지합니다.",
					failure);
			}

			Exception rollbackException = TryRestoreScene(
				entry.ScenePath,
				sceneSnapshot,
				wasLoaded,
				wasActive);
			return rollbackException == null
				? failure
				: new AggregateException(
					"래퍼 프리팹 제거와 Full Demo 원상 복구가 모두 실패했습니다.",
					failure,
					rollbackException);
		}

		private static List<GameObject> FindWrapperInstanceRoots(Scene scene, string prefabPath)
		{
			HashSet<GameObject> roots = new HashSet<GameObject>();
			foreach (DhmgsRigDriver driver in scene.GetRootGameObjects()
				.SelectMany(root => root.GetComponentsInChildren<DhmgsRigDriver>(true)))
			{
				if (!PathsEqual(
					PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(driver.gameObject),
					prefabPath))
				{
					continue;
				}
				GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(driver.gameObject)
					?? driver.gameObject;
				roots.Add(root);
			}
			return roots.ToList();
		}

		private static List<GameObject> FindBrokenAvatarRoots(
			Scene scene,
			Transform avatarParent,
			string expectedId)
		{
			if (avatarParent == null || avatarParent.gameObject.scene != scene)
			{
				return new List<GameObject>();
			}
			List<GameObject> roots = new List<GameObject>();
			for (int index = 0; index < avatarParent.childCount; index++)
			{
				GameObject child = avatarParent.GetChild(index).gameObject;
				if (PrefabUtility.IsPrefabAssetMissing(child)
					&& (string.IsNullOrEmpty(expectedId)
						|| string.Equals(GetSceneObjectId(child), expectedId, StringComparison.Ordinal)))
				{
					roots.Add(PrefabUtility.GetNearestPrefabInstanceRoot(child) ?? child);
				}
			}
			return roots.Distinct().ToList();
		}

		private static string GetSceneObjectId(GameObject gameObject)
		{
			GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(gameObject);
			return id.ToString();
		}

		private static bool IsMissingPrefabDriver(DhmgsRigDriver driver)
		{
			if (driver == null)
			{
				return true;
			}
			GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(driver.gameObject);
			return root != null && PrefabUtility.IsPrefabAssetMissing(root);
		}

		private static int ResolveDefaultAvatarIndex(
			SerializedProperty avatars,
			int oldIndex,
			DhmgsRigDriver oldDriver,
			IReadOnlyCollection<int> removedIndices)
		{
			if (oldIndex < 0)
			{
				return -1;
			}
			if (avatars.arraySize == 0)
			{
				return -1;
			}
			if (oldDriver != null)
			{
				for (int index = 0; index < avatars.arraySize; index++)
				{
					if (avatars.GetArrayElementAtIndex(index).objectReferenceValue == oldDriver)
					{
						return index;
					}
				}
			}
			int removedBefore = removedIndices.Count(index => index < oldIndex);
			return Mathf.Clamp(oldIndex - removedBefore, 0, avatars.arraySize - 1);
		}

		private static Exception TryRestoreScene(
			string scenePath,
			byte[] snapshot,
			bool wasLoaded,
			bool wasActive)
		{
			Scene temporaryScene = default;
			try
			{
				Scene current = SceneManager.GetSceneByPath(scenePath);
				if (current.IsValid() && current.isLoaded)
				{
					if (SceneManager.sceneCount == 1)
					{
						temporaryScene = EditorSceneManager.NewScene(
							NewSceneSetup.EmptyScene,
							NewSceneMode.Additive);
					}
					if (!EditorSceneManager.CloseScene(current, true))
					{
						throw new IOException($"변경된 Full Demo 씬 '{scenePath}'을(를) 닫지 못했습니다.");
					}
				}

				File.WriteAllBytes(DhmgsSmplxAvatarRegistrar.ToAbsolutePath(scenePath), snapshot);
				AssetDatabase.ImportAsset(
					scenePath,
					ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
				if (wasLoaded)
				{
					Scene restored = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
					if (wasActive)
					{
						SceneManager.SetActiveScene(restored);
					}
				}
				if (temporaryScene.IsValid() && temporaryScene.isLoaded)
				{
					EditorSceneManager.CloseScene(temporaryScene, true);
				}
				return null;
			}
			catch (Exception exception)
			{
				return exception;
			}
			finally
			{
				if (temporaryScene.IsValid() && temporaryScene.isLoaded)
				{
					EditorSceneManager.CloseScene(temporaryScene, true);
				}
			}
		}

		private static void EnsureSceneCanBeRemovedFrom(string scenePath)
		{
			Scene loaded = SceneManager.GetSceneByPath(scenePath);
			if (loaded.IsValid() && loaded.isLoaded && loaded.isDirty)
			{
				throw new InvalidOperationException(
					"열려 있는 Full Demo 씬에 저장하지 않은 변경 사항이 있습니다. "
					+ "아바타를 제거하기 전에 씬을 저장하거나 변경을 되돌리세요.");
			}
		}

		private static void EnsureNoOtherDirtyScenes(string targetScenePath)
		{
			List<string> dirtyScenes = new List<string>();
			for (int index = 0; index < SceneManager.sceneCount; index++)
			{
				Scene scene = SceneManager.GetSceneAt(index);
				if (scene.IsValid()
					&& scene.isLoaded
					&& scene.isDirty
					&& !PathsEqual(scene.path, targetScenePath))
				{
					dirtyScenes.Add(string.IsNullOrEmpty(scene.path) ? "저장되지 않은 씬" : scene.path);
				}
			}
			if (dirtyScenes.Count > 0)
			{
				throw new InvalidOperationException(
					"다른 열린 씬에 저장하지 않은 참조가 있을 수 있어 래퍼 프리팹을 삭제하지 않습니다.\n"
					+ string.Join("\n", dirtyScenes.Select(path => "• " + path))
					+ "\n\n해당 씬을 먼저 저장하거나 닫은 뒤 다시 시도하세요.");
			}
		}

		private static void EnsureNoDirtyPersistentAssets(
			string prefabPath,
			string targetScenePath)
		{
			string[] dirtyAssetPaths = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
				.Where(asset => asset != null
					&& EditorUtility.IsPersistent(asset)
					&& EditorUtility.IsDirty(asset))
				.Where(asset => EditorUtility.CollectDependencies(new[] { asset })
					.Any(dependency => PathsEqual(
						AssetDatabase.GetAssetPath(dependency),
						prefabPath)))
				.Select(AssetDatabase.GetAssetPath)
				.Where(path => !string.IsNullOrEmpty(path)
					&& path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
					&& !PathsEqual(path, prefabPath)
					&& !PathsEqual(path, targetScenePath))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			if (dirtyAssetPaths.Length == 0)
			{
				return;
			}

			throw new InvalidOperationException(
				"저장되지 않은 프로젝트 에셋이 있어 래퍼 프리팹을 삭제할 수 없습니다.\n"
				+ string.Join("\n", dirtyAssetPaths.Select(path => "• " + path))
				+ "\n\n해당 에셋을 먼저 저장하거나 변경을 되돌린 뒤 다시 시도하세요.");
		}

		private static void ValidateScenePath(string scenePath)
		{
			if (string.IsNullOrWhiteSpace(scenePath)
				|| !scenePath.StartsWith("Assets/", StringComparison.Ordinal)
				|| !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
				|| !File.Exists(DhmgsSmplxAvatarRegistrar.ToAbsolutePath(scenePath)))
			{
				throw new InvalidOperationException("올바른 imported Full Demo 씬 경로가 필요합니다.");
			}
		}

		private static bool IsProjectOwnedWrapperPath(string path)
		{
			path = NormalizePath(path);
			if (string.IsNullOrEmpty(path)
				|| !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
				|| !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			string packageRoot = DhmgsSmplxAvatarRegistrar.ResolveEmbeddedPackageRoot();
			return string.IsNullOrEmpty(packageRoot)
				|| (!PathsEqual(path, packageRoot)
					&& !path.StartsWith(packageRoot + "/", StringComparison.OrdinalIgnoreCase));
		}

		private static bool IsGeneratedWrapperPrefab(string path)
		{
			if (!IsProjectOwnedWrapperPath(path))
			{
				return false;
			}
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			if (prefab == null)
			{
				return false;
			}
			bool hasMarker = AssetDatabase.GetLabels(prefab)
				.Contains(GeneratedWrapperMarker, StringComparer.Ordinal);
			bool isLegacyDefaultWrapper = path.StartsWith(
				DhmgsSmplxAvatarRegistrar.DefaultAvatarFolder + "/",
				StringComparison.OrdinalIgnoreCase);
			if (!hasMarker && !isLegacyDefaultWrapper)
			{
				return false;
			}
			if (prefab.GetComponents<DhmgsRigDriver>().Length != 1)
			{
				return false;
			}
			DhmgsRigDriver driver = prefab.GetComponent<DhmgsRigDriver>();
			SerializedObject driverObject = new SerializedObject(driver);
			Transform rigRoot = driverObject.FindProperty("_rigRoot")?.objectReferenceValue as Transform;
			if (rigRoot == null || rigRoot == prefab.transform || !rigRoot.IsChildOf(prefab.transform))
			{
				return false;
			}
			return AssetDatabase.GetDependencies(path, false)
				.Any(dependency => dependency.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase));
		}

		internal static void MarkGeneratedWrapper(string prefabPath)
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)
				?? throw new InvalidOperationException(
					$"래퍼 프리팹 '{prefabPath}'을(를) 찾을 수 없습니다.");
			List<string> labels = AssetDatabase.GetLabels(prefab).ToList();
			if (!labels.Contains(GeneratedWrapperMarker, StringComparer.Ordinal))
			{
				labels.Add(GeneratedWrapperMarker);
				AssetDatabase.SetLabels(prefab, labels.ToArray());
			}
		}

		private const string GeneratedWrapperMarker = "mnnh.dhmgs.smplx-avatar-wrapper.v1";

		private static bool PathsEqual(string left, string right)
		{
			return string.Equals(
				NormalizePath(left),
				NormalizePath(right),
				StringComparison.OrdinalIgnoreCase);
		}

		private static string NormalizePath(string path)
		{
			return path?.Replace('\\', '/') ?? string.Empty;
		}
	}
}
