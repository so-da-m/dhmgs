using System;
using System.Linq;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Mnnh.Dhmgs.Editor.Tests
{
	public sealed class DhmgsSmplxAvatarRegistrationTests
	{
		private const string TemporaryRoot = "Assets/__DhmgsAvatarRegistrationTests";
		private const string TestHostPath = "Assets/__DhmgsAvatarRegistrationTestHost.unity";

		[TearDown]
		public void TearDown()
		{
			for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
			{
				Scene scene = SceneManager.GetSceneAt(index);
				if (scene.IsValid()
					&& scene.path.StartsWith(TemporaryRoot + "/", StringComparison.OrdinalIgnoreCase))
				{
					EditorSceneManager.CloseScene(scene, true);
				}
			}

			Scene testHost = SceneManager.GetSceneByPath(TestHostPath);
			if (testHost.IsValid() && testHost.isLoaded)
			{
				if (SceneManager.sceneCount == 1)
				{
					EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
				}
				else
				{
					Assert.That(EditorSceneManager.CloseScene(testHost, true), Is.True,
						"The registration test host scene could not be closed.");
				}
			}
			if (AssetDatabase.LoadMainAssetAtPath(TestHostPath) != null)
			{
				Assert.That(AssetDatabase.DeleteAsset(TestHostPath), Is.True,
					"The registration test host asset could not be removed.");
			}
			if (AssetDatabase.IsValidFolder(TemporaryRoot))
			{
				Assert.That(AssetDatabase.DeleteAsset(TemporaryRoot), Is.True,
					"Temporary registration test assets could not be removed.");
			}
		}

		[Test]
		public void BundledNeutral_SatisfiesRegistrationContract()
		{
			GameObject neutral = DhmgsSmplxAvatarValidator.LoadReference(
				DhmgsSmplxSkeletonReference.Neutral);

			DhmgsSmplxAvatarValidationResult result =
				DhmgsSmplxAvatarValidator.Validate(neutral, DhmgsSmplxSkeletonReference.Neutral);

			Assert.That(neutral, Is.Not.Null);
			Assert.That(result.CanRegister, Is.True, FormatDiagnostics(result));
			Assert.That(result.CanonicalJointCount, Is.EqualTo(56));
			Assert.That(result.MatchedJointCount, Is.EqualTo(56));
			Assert.That(result.StandardBlendShapeCount,
				Is.EqualTo(DhmgsSmplxAvatarValidator.OfficialBlendShapeCount));
			Assert.That(result.AdditionalBlendShapeCount, Is.Zero);
		}

		[Test]
		public void Candidate_AllowsExtraBranchWithoutChangingCanonicalHierarchy()
		{
			GameObject reference = DhmgsSmplxAvatarValidator.LoadReference(
				DhmgsSmplxSkeletonReference.Neutral);
			GameObject candidate = UnityEngine.Object.Instantiate(reference);
			try
			{
				Transform head = FindUnique(candidate, "head");
				GameObject accessory = new GameObject("CompanyHairRoot");
				accessory.transform.SetParent(head, false);

				DhmgsSmplxAvatarValidationResult result =
					DhmgsSmplxAvatarValidator.Validate(candidate, reference, false);

				Assert.That(result.CanRegister, Is.True, FormatDiagnostics(result));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(candidate);
			}
		}

		[Test]
		public void Candidate_MissingRequiredJointBlocksRegistration()
		{
			GameObject reference = DhmgsSmplxAvatarValidator.LoadReference(
				DhmgsSmplxSkeletonReference.Neutral);
			GameObject candidate = UnityEngine.Object.Instantiate(reference);
			try
			{
				FindUnique(candidate, "jaw").name = "CompanyJaw";

				DhmgsSmplxAvatarValidationResult result =
					DhmgsSmplxAvatarValidator.Validate(candidate, reference, false);

				Assert.That(result.CanRegister, Is.False);
				Assert.That(result.Diagnostics.Any(item => item.Code == "skeleton.missing"), Is.True,
					FormatDiagnostics(result));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(candidate);
			}
		}

		[Test]
		public void FixedShapeAvatar_MissingStandardBlendShapesRemainWarningOnly()
		{
			string path = AssetDatabase.GUIDToAssetPath("97941685cb9049043b8730426e785647");
			GameObject fixedShape = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			GameObject reference = DhmgsSmplxAvatarValidator.LoadReference(
				DhmgsSmplxSkeletonReference.Neutral);

			DhmgsSmplxAvatarValidationResult result =
				DhmgsSmplxAvatarValidator.Validate(fixedShape, reference, false);

			Assert.That(fixedShape, Is.Not.Null);
			Assert.That(result.CanRegister, Is.True, FormatDiagnostics(result));
			Assert.That(result.StandardBlendShapeCount,
				Is.LessThan(DhmgsSmplxAvatarValidator.OfficialBlendShapeCount));
			Assert.That(result.Diagnostics.Any(item =>
				item.Code == "blendshapes.missing"
				&& item.Severity == DhmgsAvatarValidationSeverity.Warning), Is.True);
		}

		[Test]
		public void Candidate_RejectsHelperInsertedBetweenCanonicalJoints()
		{
			GameObject reference = DhmgsSmplxAvatarValidator.LoadReference(
				DhmgsSmplxSkeletonReference.Neutral);
			GameObject candidate = UnityEngine.Object.Instantiate(reference);
			try
			{
				Transform wrist = FindUnique(candidate, "left_wrist");
				Transform elbow = wrist.parent;
				GameObject helper = new GameObject("CompanyHelper");
				helper.transform.SetParent(elbow, false);
				wrist.SetParent(helper.transform, true);

				DhmgsSmplxAvatarValidationResult result =
					DhmgsSmplxAvatarValidator.Validate(candidate, reference, false);

				Assert.That(result.CanRegister, Is.False);
				Assert.That(result.Diagnostics.Any(item => item.Code == "skeleton.parent"), Is.True,
					FormatDiagnostics(result));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(candidate);
			}
		}

		[Test]
		public void Candidate_ChangedCanonicalJointAxisWarnsWithoutBlockingStructure()
		{
			GameObject reference = DhmgsSmplxAvatarValidator.LoadReference(
				DhmgsSmplxSkeletonReference.Neutral);
			GameObject candidate = UnityEngine.Object.Instantiate(reference);
			try
			{
				Transform wrist = FindUnique(candidate, "right_wrist");
				wrist.localRotation *= Quaternion.Euler(1f, 0f, 0f);

				DhmgsSmplxAvatarValidationResult result =
					DhmgsSmplxAvatarValidator.Validate(candidate, reference, false);

				Assert.That(result.CanRegister, Is.True, FormatDiagnostics(result));
				Assert.That(result.Diagnostics.Any(item =>
					item.Code == "skeleton.rotation"
					&& item.Severity == DhmgsAvatarValidationSeverity.Warning), Is.True,
					FormatDiagnostics(result));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(candidate);
			}
		}

		[Test]
		public void Candidate_NonUnitEffectiveSkeletonRootScaleBlocksRegistration()
		{
			GameObject reference = DhmgsSmplxAvatarValidator.LoadReference(
				DhmgsSmplxSkeletonReference.Neutral);
			GameObject candidate = UnityEngine.Object.Instantiate(reference);
			try
			{
				Transform root = FindUnique(candidate, "root");
				Assert.That(root.parent, Is.Not.Null);
				root.parent.localScale *= 100f;

				DhmgsSmplxAvatarValidationResult result =
					DhmgsSmplxAvatarValidator.Validate(candidate, reference, false);

				Assert.That(result.CanRegister, Is.False, FormatDiagnostics(result));
				Assert.That(result.Diagnostics.Any(item =>
					item.Code == "skeleton.root-effective-scale"
					&& item.Severity == DhmgsAvatarValidationSeverity.Error), Is.True,
					FormatDiagnostics(result));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(candidate);
			}
		}

		[Test]
		public void Wrapper_PreservesFbxAndAddsOnlyPlaybackComponents()
		{
			EnsureTemporaryRoot();
			GameObject source = DhmgsSmplxAvatarValidator.LoadReference(
				DhmgsSmplxSkeletonReference.Neutral);
			string sourcePath = AssetDatabase.GetAssetPath(source);
			ModelImporter sourceImporter = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
			Assert.That(sourceImporter, Is.Not.Null);
			bool originalOptimize = sourceImporter.optimizeGameObjects;
			bool originalImportBlendShapes = sourceImporter.importBlendShapes;
			ModelImporterAnimationType originalAnimationType = sourceImporter.animationType;
			SkinnedMeshRenderer[] sourceRenderers = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			DhmgsSmplxAvatarValidationResult validation =
				DhmgsSmplxAvatarValidator.Validate(source, DhmgsSmplxSkeletonReference.Neutral);
			string prefabPath = TemporaryRoot + "/Registered Neutral.prefab";

			GameObject prefab = DhmgsSmplxAvatarRegistrar.CreateWrapperPrefab(
				source,
				"Registered Neutral",
				prefabPath,
				validation);

			Assert.That(prefab, Is.Not.Null);
			Assert.That(prefab.name, Is.EqualTo("Registered Neutral"));
			Assert.That(prefab.GetComponents<DhmgsRigDriver>(), Has.Length.EqualTo(1));
			Assert.That(prefab.GetComponentsInChildren<DhmgsRigDriver>(true), Has.Length.EqualTo(1));
			Assert.That(prefab.GetComponentsInChildren<SMPLXPlugin.SMPLX>(true), Is.Empty,
				"Fixed-shape registration must not add parametric SMPLX components.");
			Assert.That(AssetDatabase.GetDependencies(prefabPath, true),
				Does.Contain(sourcePath),
				"The wrapper must preserve a nested reference to the original FBX.");
			SkinnedMeshRenderer[] wrapperRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			Assert.That(wrapperRenderers, Has.Length.EqualTo(sourceRenderers.Length));
			foreach (SkinnedMeshRenderer sourceRenderer in sourceRenderers)
			{
				SkinnedMeshRenderer wrapped = wrapperRenderers.Single(renderer =>
					renderer.sharedMesh == sourceRenderer.sharedMesh);
				Assert.That(wrapped.sharedMaterials, Is.EqualTo(sourceRenderer.sharedMaterials));
				string[] sourceBlendShapes = Enumerable.Range(0, sourceRenderer.sharedMesh.blendShapeCount)
					.Select(sourceRenderer.sharedMesh.GetBlendShapeName)
					.ToArray();
				string[] wrappedBlendShapes = Enumerable.Range(0, wrapped.sharedMesh.blendShapeCount)
					.Select(wrapped.sharedMesh.GetBlendShapeName)
					.ToArray();
				Assert.That(wrappedBlendShapes, Is.EqualTo(sourceBlendShapes));
			}

			ModelImporter unchangedImporter = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
			Assert.That(unchangedImporter.optimizeGameObjects, Is.EqualTo(originalOptimize));
			Assert.That(unchangedImporter.importBlendShapes, Is.EqualTo(originalImportBlendShapes));
			Assert.That(unchangedImporter.animationType, Is.EqualTo(originalAnimationType));

			SerializedObject driver = new SerializedObject(prefab.GetComponent<DhmgsRigDriver>());
			Transform rigRoot = driver.FindProperty("_rigRoot").objectReferenceValue as Transform;
			Assert.That(rigRoot, Is.Not.Null);
			Assert.That(rigRoot.name, Is.EqualTo("root"));
		}

		[Test]
		public void FullDemoRegistration_AppendsOnceAndKeepsExistingDefault()
		{
			EnsureTemporaryRoot();
			EnsureAdditiveSceneHost();
			string wrapperPath = TemporaryRoot + "/Wrapper.prefab";
			string scenePath = TemporaryRoot + "/Demo.unity";
			GameObject wrapper = CreateTemporaryRegisteredWrapper(wrapperPath, "Registered Avatar");

			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
			GameObject playerObject = new GameObject("Motion Player");
			DhmgsMotionPlayer player = playerObject.AddComponent<DhmgsMotionPlayer>();
			GameObject baseRigs = new GameObject("Base Rigs");
			GameObject builtIn = new GameObject("Built In");
			builtIn.transform.SetParent(baseRigs.transform, false);
			DhmgsRigDriver builtInDriver = builtIn.AddComponent<DhmgsRigDriver>();
			SerializedObject playerData = new SerializedObject(player);
			SerializedProperty avatars = playerData.FindProperty("_avatars");
			avatars.arraySize = 1;
			avatars.GetArrayElementAtIndex(0).objectReferenceValue = builtInDriver;
			playerData.FindProperty("_defaultAvatar").intValue = 0;
			playerData.ApplyModifiedPropertiesWithoutUndo();
			Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);

			Assert.That(DhmgsSmplxAvatarRegistrar.AddToFullDemoScene(
				wrapper, "Registered Avatar", scenePath, out bool alreadyFirst), Is.True);
			Assert.That(alreadyFirst, Is.False);
			Assert.That(DhmgsSmplxAvatarRegistrar.AddToFullDemoScene(
				wrapper, "Registered Avatar", scenePath, out bool alreadySecond), Is.False);
			Assert.That(alreadySecond, Is.True);

			Scene reopened = SceneManager.GetSceneByPath(scenePath);
			if (!reopened.IsValid() || !reopened.isLoaded)
			{
				reopened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			}
			DhmgsMotionPlayer savedPlayer = reopened.GetRootGameObjects()
				.SelectMany(root => root.GetComponentsInChildren<DhmgsMotionPlayer>(true))
				.Single();
			SerializedObject savedData = new SerializedObject(savedPlayer);
			Assert.That(savedData.FindProperty("_avatars").arraySize, Is.EqualTo(2));
			Assert.That(savedData.FindProperty("_defaultAvatar").intValue, Is.Zero);
			Assert.That(EditorSceneManager.CloseScene(reopened, true), Is.True);

			DhmgsSmplxAvatarRemovalEntry removable =
				DhmgsSmplxAvatarRemoval.FindRemovableRegistrations(scenePath).Single();
			Assert.That(removable.AvatarName, Is.EqualTo("Registered Avatar"));
			Assert.That(removable.PrefabPath, Is.EqualTo(wrapperPath));
			DhmgsSmplxAvatarRemovalResult removal =
				DhmgsSmplxAvatarRemoval.Remove(removable, true);
			Assert.That(removal.RemovedAvatarEntries, Is.EqualTo(1));
			Assert.That(removal.RemovedSceneInstances, Is.EqualTo(1));
			Assert.That(removal.WrapperMovedToTrash, Is.True);
			Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath), Is.Null);

			Scene afterRemoval = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			DhmgsMotionPlayer remainingPlayer = afterRemoval.GetRootGameObjects()
				.SelectMany(root => root.GetComponentsInChildren<DhmgsMotionPlayer>(true))
				.Single();
			SerializedObject remainingData = new SerializedObject(remainingPlayer);
			Assert.That(remainingData.FindProperty("_avatars").arraySize, Is.EqualTo(1));
			Assert.That(remainingData.FindProperty("_defaultAvatar").intValue, Is.Zero);
			Assert.That(afterRemoval.GetRootGameObjects()
				.SelectMany(root => root.GetComponentsInChildren<DhmgsRigDriver>(true))
				.Select(driver => driver.gameObject.name), Does.Not.Contain("Registered Avatar"));
			Assert.That(EditorSceneManager.CloseScene(afterRemoval, true), Is.True);
		}

		[Test]
		public void BrokenWrapperRegistration_CanBeCleanedWithoutDeletingOtherAvatars()
		{
			LogAssert.ignoreFailingMessages = true;
			try
			{
				EnsureTemporaryRoot();
				EnsureAdditiveSceneHost();
				string wrapperPath = TemporaryRoot + "/Broken Wrapper.prefab";
				string scenePath = TemporaryRoot + "/Broken Demo.unity";
				GameObject wrapper = CreateTemporaryRegisteredWrapper(wrapperPath, "Broken Avatar");

				Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
			GameObject playerObject = new GameObject("Motion Player");
			DhmgsMotionPlayer player = playerObject.AddComponent<DhmgsMotionPlayer>();
			GameObject baseRigs = new GameObject("Base Rigs");
			GameObject builtIn = new GameObject("Built In");
			builtIn.transform.SetParent(baseRigs.transform, false);
			DhmgsRigDriver builtInDriver = builtIn.AddComponent<DhmgsRigDriver>();
			GameObject brokenInstance = PrefabUtility.InstantiatePrefab(wrapper, scene) as GameObject;
			Assert.That(brokenInstance, Is.Not.Null);
			brokenInstance.transform.SetParent(baseRigs.transform, false);
			brokenInstance.name = "Broken Avatar";
			DhmgsRigDriver brokenDriver = brokenInstance.GetComponent<DhmgsRigDriver>();

			SerializedObject playerData = new SerializedObject(player);
			SerializedProperty avatars = playerData.FindProperty("_avatars");
			avatars.arraySize = 2;
			avatars.GetArrayElementAtIndex(0).objectReferenceValue = builtInDriver;
			avatars.GetArrayElementAtIndex(1).objectReferenceValue = brokenDriver;
			playerData.FindProperty("_defaultAvatar").intValue = 0;
			playerData.ApplyModifiedPropertiesWithoutUndo();
			Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
			Assert.That(EditorSceneManager.CloseScene(scene, true), Is.True);

			Assert.That(AssetDatabase.DeleteAsset(wrapperPath), Is.True);
			AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
			DhmgsSmplxAvatarRemovalEntry broken =
				DhmgsSmplxAvatarRemoval.FindRemovableRegistrations(scenePath).Single(item => item.IsBroken);
			DhmgsSmplxAvatarRemovalResult removal =
				DhmgsSmplxAvatarRemoval.Remove(broken, false);
			Assert.That(removal.RemovedAvatarEntries, Is.GreaterThanOrEqualTo(1));
			Assert.That(removal.RemovedSceneInstances, Is.EqualTo(1));

			Scene cleaned = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			DhmgsMotionPlayer cleanedPlayer = cleaned.GetRootGameObjects()
				.SelectMany(root => root.GetComponentsInChildren<DhmgsMotionPlayer>(true))
				.Single();
			SerializedObject cleanedData = new SerializedObject(cleanedPlayer);
			Assert.That(cleanedData.FindProperty("_avatars").arraySize, Is.EqualTo(1));
			Assert.That(cleanedData.FindProperty("_avatars")
				.GetArrayElementAtIndex(0).objectReferenceValue, Is.Not.Null);
			Assert.That(cleanedData.FindProperty("_defaultAvatar").intValue, Is.Zero);
				Assert.That(EditorSceneManager.CloseScene(cleaned, true), Is.True);
			}
			finally
			{
				LogAssert.ignoreFailingMessages = false;
			}
		}

		[Test]
		public void WrapperRemoval_IgnoresUnrelatedDirtyPersistentAsset()
		{
			EnsureTemporaryRoot();
			string wrapperPath = TemporaryRoot + "/Unrelated Dirty Wrapper.prefab";
			string scenePath = TemporaryRoot + "/Unrelated Dirty Demo.unity";
			GameObject wrapper = CreateTemporaryRegisteredWrapper(
				wrapperPath,
				"Unrelated Dirty Avatar");
			CreateTemporaryDemoWithWrapper(wrapper, "Unrelated Dirty Avatar", scenePath);

			DhmgsRemovalReferenceTestAsset unrelated =
				ScriptableObject.CreateInstance<DhmgsRemovalReferenceTestAsset>();
			string unrelatedPath = TemporaryRoot + "/Unrelated Dirty.asset";
			AssetDatabase.CreateAsset(unrelated, unrelatedPath);
			unrelated.Revision = 1;
			EditorUtility.SetDirty(unrelated);
			Assert.That(EditorUtility.IsDirty(unrelated), Is.True);

			DhmgsSmplxAvatarRemovalEntry removable =
				DhmgsSmplxAvatarRemoval.FindRemovableRegistrations(scenePath).Single();
			DhmgsSmplxAvatarRemovalResult removal =
				DhmgsSmplxAvatarRemoval.Remove(removable, true);

			Assert.That(removal.WrapperMovedToTrash, Is.True);
			Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath), Is.Null);
		}

		[Test]
		public void WrapperRemoval_BlocksUnsavedPersistentReference()
		{
			EnsureTemporaryRoot();
			string wrapperPath = TemporaryRoot + "/Dirty Reference Wrapper.prefab";
			string scenePath = TemporaryRoot + "/Dirty Reference Demo.unity";
			GameObject wrapper = CreateTemporaryRegisteredWrapper(
				wrapperPath,
				"Dirty Reference Avatar");
			CreateTemporaryDemoWithWrapper(wrapper, "Dirty Reference Avatar", scenePath);

			DhmgsRemovalReferenceTestAsset reference =
				ScriptableObject.CreateInstance<DhmgsRemovalReferenceTestAsset>();
			string referencePath = TemporaryRoot + "/Unsaved Reference.asset";
			AssetDatabase.CreateAsset(reference, referencePath);
			Assert.That(AssetDatabase.GetDependencies(referencePath, false),
				Does.Not.Contain(wrapperPath));
			reference.Wrapper = wrapper;
			EditorUtility.SetDirty(reference);
			Assert.That(EditorUtility.IsDirty(reference), Is.True);
			Assert.That(EditorUtility.CollectDependencies(new UnityEngine.Object[] { reference })
				.Select(AssetDatabase.GetAssetPath), Does.Contain(wrapperPath));

			DhmgsSmplxAvatarRemovalEntry removable =
				DhmgsSmplxAvatarRemoval.FindRemovableRegistrations(scenePath).Single();
			InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
				DhmgsSmplxAvatarRemoval.Remove(removable, true));

			Assert.That(exception.Message, Does.Contain(referencePath));
			Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath), Is.Not.Null);
			Assert.That(DhmgsSmplxAvatarRemoval.FindRemovableRegistrations(scenePath),
				Has.Count.EqualTo(1));
		}

		[Test]
		public void WrapperRemoval_RollsBackWhenFullDemoStillReferencesWrapper()
		{
			EnsureTemporaryRoot();
			string wrapperPath = TemporaryRoot + "/Scene Reference Wrapper.prefab";
			string scenePath = TemporaryRoot + "/Scene Reference Demo.unity";
			GameObject wrapper = CreateTemporaryRegisteredWrapper(
				wrapperPath,
				"Scene Reference Avatar");
			CreateTemporaryDemoWithWrapper(wrapper, "Scene Reference Avatar", scenePath);

			Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			GameObject referenceObject = new GameObject("Additional Wrapper Reference");
			SceneManager.MoveGameObjectToScene(referenceObject, scene);
			Toggle reference = referenceObject.AddComponent<Toggle>();
			UnityEventTools.AddBoolPersistentListener(
				reference.onValueChanged,
				wrapper.SetActive,
				true);
			Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
			Assert.That(EditorSceneManager.CloseScene(scene, true), Is.True);

			DhmgsSmplxAvatarRemovalEntry removable =
				DhmgsSmplxAvatarRemoval.FindRemovableRegistrations(scenePath).Single();
			Assert.Throws<DhmgsSmplxAvatarExternalReferenceException>(() =>
				DhmgsSmplxAvatarRemoval.Remove(removable, true));

			Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath), Is.Not.Null);
			Scene restored = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			DhmgsMotionPlayer restoredPlayer = restored.GetRootGameObjects()
				.SelectMany(root => root.GetComponentsInChildren<DhmgsMotionPlayer>(true))
				.Single();
			SerializedObject restoredData = new SerializedObject(restoredPlayer);
			Assert.That(restoredData.FindProperty("_avatars").arraySize, Is.EqualTo(2));
			Toggle restoredReference = restored.GetRootGameObjects()
				.SelectMany(root => root.GetComponentsInChildren<Toggle>(true))
				.Single();
			Assert.That(restoredReference.onValueChanged.GetPersistentEventCount(), Is.EqualTo(1));
			Assert.That(AssetDatabase.GetAssetPath(
				restoredReference.onValueChanged.GetPersistentTarget(0)), Is.EqualTo(wrapperPath));
			Assert.That(EditorSceneManager.CloseScene(restored, true), Is.True);
		}

		[Test]
		public void FullDemoRegistration_UpdatesNameForExistingWrapper()
		{
			EnsureTemporaryRoot();
			string wrapperPath = TemporaryRoot + "/Renamed Wrapper.prefab";
			string scenePath = TemporaryRoot + "/Renamed Demo.unity";
			GameObject wrapper = CreateTemporaryRegisteredWrapper(wrapperPath, "Original Name");
			CreateTemporaryDemoWithWrapper(wrapper, "Original Name", scenePath);

			Assert.That(DhmgsSmplxAvatarRegistrar.AddToFullDemoScene(
				wrapper,
				"Updated Name",
				scenePath,
				out bool alreadyRegistered), Is.False);
			Assert.That(alreadyRegistered, Is.True);

			Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			DhmgsMotionPlayer player = scene.GetRootGameObjects()
				.SelectMany(root => root.GetComponentsInChildren<DhmgsMotionPlayer>(true))
				.Single();
			SerializedObject playerData = new SerializedObject(player);
			DhmgsRigDriver registered = playerData.FindProperty("_avatars")
				.GetArrayElementAtIndex(1).objectReferenceValue as DhmgsRigDriver;
			Assert.That(registered, Is.Not.Null);
			Assert.That(registered.gameObject.name, Is.EqualTo("Updated Name"));
			Assert.That(EditorSceneManager.CloseScene(scene, true), Is.True);
		}

		[Test]
		public void Registration_OverwritesAndRenamesWithoutBreakingSceneDriverReference()
		{
			EnsureTemporaryRoot();
			string wrapperPath = TemporaryRoot + "/Register Rename Wrapper.prefab";
			string scenePath = TemporaryRoot + "/Register Rename Demo.unity";
			GameObject source = DhmgsSmplxAvatarValidator.LoadReference(
				DhmgsSmplxSkeletonReference.Neutral);
			string originalName = source.name;
			string updatedName = originalName + " Updated";
			GameObject wrapper = CreateTemporaryRegisteredWrapper(wrapperPath, originalName);
			DhmgsRigDriver originalAssetDriver = wrapper.GetComponent<DhmgsRigDriver>();
			Assert.That(originalAssetDriver, Is.Not.Null);
			Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
				originalAssetDriver,
				out string originalGuid,
				out long originalDriverId), Is.True);
			CreateTemporaryDemoWithWrapper(wrapper, originalName, scenePath);

			DhmgsSmplxAvatarRegistrationResult result =
				DhmgsSmplxAvatarRegistrar.Register(new DhmgsSmplxAvatarRegistrationOptions
				{
					SourceFbx = source,
					AvatarName = updatedName,
					PrefabPath = wrapperPath,
					SkeletonReference = DhmgsSmplxSkeletonReference.Neutral,
					AddToFullDemo = false,
				});

			Assert.That(result.Prefab, Is.Not.Null);
			DhmgsRigDriver updatedAssetDriver = result.Prefab.GetComponent<DhmgsRigDriver>();
			Assert.That(updatedAssetDriver, Is.Not.Null);
			Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
				updatedAssetDriver,
				out string updatedGuid,
				out long updatedDriverId), Is.True);
			Assert.That(updatedGuid, Is.EqualTo(originalGuid));
			Assert.That(updatedDriverId, Is.EqualTo(originalDriverId),
				"Same-path re-registration must preserve the prefab DhmgsRigDriver local ID.");
			Assert.That(result.Prefab.transform.Cast<Transform>().Single().name,
				Is.EqualTo("DHMGS Source Model"));
			Assert.That(DhmgsSmplxAvatarRegistrar.AddToFullDemoScene(
				result.Prefab,
				updatedName,
				scenePath,
				out bool alreadyRegistered), Is.False);
			Assert.That(alreadyRegistered, Is.True,
				"The existing scene driver reference must survive a same-path wrapper overwrite.");

			Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			DhmgsMotionPlayer player = scene.GetRootGameObjects()
				.SelectMany(root => root.GetComponentsInChildren<DhmgsMotionPlayer>(true))
				.Single();
			SerializedObject playerData = new SerializedObject(player);
			SerializedProperty avatars = playerData.FindProperty("_avatars");
			Assert.That(avatars.arraySize, Is.EqualTo(2));
			DhmgsRigDriver registered = avatars.GetArrayElementAtIndex(1)
				.objectReferenceValue as DhmgsRigDriver;
			Assert.That(registered, Is.Not.Null);
			Assert.That(registered.gameObject.name, Is.EqualTo(updatedName));
			Assert.That(EditorSceneManager.CloseScene(scene, true), Is.True);
		}

		[Test]
		public void EditorSurface_UsesTheAgreedSimpleRegistrationContract()
		{
			Type windowType = typeof(DhmgsSmplxAvatarRegistrar).Assembly.GetType(
				"Mnnh.Dhmgs.Editor.DhmgsSmplxAvatarRegistrationWindow",
				true);
			System.Reflection.MethodInfo open = windowType.GetMethod(
				"Open",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			MenuItem menu = open.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>().Single();
			Assert.That(menu.menuItem, Is.EqualTo("DHMGS/새 SMPLX 아바타 등록..."));

			DhmgsSmplxAvatarRegistrationOptions defaults = new DhmgsSmplxAvatarRegistrationOptions();
			Assert.That(defaults.SkeletonReference, Is.EqualTo(DhmgsSmplxSkeletonReference.Neutral));
			Assert.That(defaults.AddToFullDemo, Is.True);
			Assert.That(DhmgsSmplxAvatarRegistrar.BuildDefaultPrefabPath("Company Avatar"),
				Is.EqualTo("Assets/Mnnh.Dhmgs/Content/Avatars/Company Avatar.prefab"));

			string windowSource = System.IO.File.ReadAllText(
				System.IO.Path.GetFullPath("Assets/Mnnh.Dhmgs/Package/Editor/Registration/DhmgsSmplxAvatarRegistrationWindow.cs"));
			Assert.That(windowSource, Does.Contain("new ObjectField(\"원본 FBX\")"));
			Assert.That(windowSource, Does.Contain("new TextField(\"아바타 이름\")"));
			Assert.That(windowSource, Does.Contain("new TextField(\"프리팹 저장 경로\")"));
			Assert.That(windowSource, Does.Contain("new Toggle(\"DHMGS 전체 데모에 추가\") { value = true }"));
			Assert.That(windowSource, Does.Contain("DhmgsSmplxSkeletonReference.Neutral"));
			Assert.That(windowSource, Does.Contain("new Foldout { text = \"고급 설정\""));
			Assert.That(windowSource, Does.Contain("new Button(Register) { text = \"등록\" }"));
			Assert.That(windowSource, Does.Contain("등록된 아바타 제거..."));
			Type removalWindowType = typeof(DhmgsSmplxAvatarRegistrar).Assembly.GetType(
				"Mnnh.Dhmgs.Editor.DhmgsSmplxAvatarRemovalWindow",
				true);
			System.Reflection.MethodInfo openRemoval = removalWindowType.GetMethod(
				"Open",
				System.Reflection.BindingFlags.Static
					| System.Reflection.BindingFlags.NonPublic
					| System.Reflection.BindingFlags.Public);
			MenuItem removalMenu = openRemoval.GetCustomAttributes(typeof(MenuItem), false)
				.Cast<MenuItem>()
				.Single();
			Assert.That(removalMenu.menuItem, Is.EqualTo("DHMGS/등록된 SMPLX 아바타 제거..."));
			string removalSource = System.IO.File.ReadAllText(
				System.IO.Path.GetFullPath("Assets/Mnnh.Dhmgs/Package/Editor/Registration/DhmgsSmplxAvatarRemovalWindow.cs"));
			Assert.That(removalSource, Does.Contain("원본 FBX, 메시, 머티리얼, 텍스처는 삭제하지 않습니다."));
			Assert.That(removalSource, Does.Contain("남은 씬 등록 정리..."));
			string validatorSource = System.IO.File.ReadAllText(
				System.IO.Path.GetFullPath("Assets/Mnnh.Dhmgs/Package/Editor/Registration/DhmgsSmplxAvatarValidator.cs"));
			Assert.That(validatorSource, Does.Contain("[InspectorName(\"중립형\")]"));
			Assert.That(validatorSource, Does.Contain("[InspectorName(\"여성형\")]"));
			Assert.That(validatorSource, Does.Contain("[InspectorName(\"남성형\")]"));
			Assert.That(windowSource, Does.Not.Contain("Parametric SMPL-X"));
			Assert.That(windowSource, Does.Not.Contain("Humanoid Avatar"));
		}

		private static void EnsureTemporaryRoot()
		{
			if (!AssetDatabase.IsValidFolder(TemporaryRoot))
			{
				string guid = AssetDatabase.CreateFolder("Assets", "__DhmgsAvatarRegistrationTests");
				Assert.That(guid, Is.Not.Empty);
			}
		}

		private static GameObject CreateTemporaryRegisteredWrapper(string path, string name)
		{
			GameObject source = DhmgsSmplxAvatarValidator.LoadReference(
				DhmgsSmplxSkeletonReference.Neutral);
			DhmgsSmplxAvatarValidationResult validation =
				DhmgsSmplxAvatarValidator.Validate(source, DhmgsSmplxSkeletonReference.Neutral);
			Assert.That(validation.CanRegister, Is.True, FormatDiagnostics(validation));
			return DhmgsSmplxAvatarRegistrar.CreateWrapperPrefab(source, name, path, validation);
		}

		private static void CreateTemporaryDemoWithWrapper(
			GameObject wrapper,
			string avatarName,
			string scenePath)
		{
			EnsureAdditiveSceneHost();
			Scene scene = EditorSceneManager.NewScene(
				NewSceneSetup.EmptyScene,
				NewSceneMode.Additive);
			GameObject playerObject = new GameObject("Motion Player");
			DhmgsMotionPlayer player = playerObject.AddComponent<DhmgsMotionPlayer>();
			GameObject baseRigs = new GameObject("Base Rigs");
			GameObject builtIn = new GameObject("Built In");
			builtIn.transform.SetParent(baseRigs.transform, false);
			DhmgsRigDriver builtInDriver = builtIn.AddComponent<DhmgsRigDriver>();
			SerializedObject playerData = new SerializedObject(player);
			SerializedProperty avatars = playerData.FindProperty("_avatars");
			avatars.arraySize = 1;
			avatars.GetArrayElementAtIndex(0).objectReferenceValue = builtInDriver;
			playerData.FindProperty("_defaultAvatar").intValue = 0;
			playerData.ApplyModifiedPropertiesWithoutUndo();
			Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
			Assert.That(EditorSceneManager.CloseScene(scene, true), Is.True);

			Assert.That(DhmgsSmplxAvatarRegistrar.AddToFullDemoScene(
				wrapper,
				avatarName,
				scenePath,
				out bool alreadyRegistered), Is.True);
			Assert.That(alreadyRegistered, Is.False);
		}

		private static void EnsureAdditiveSceneHost()
		{
			// Unity does not allow an additive scene while any loaded scene is untitled.
			// The Test Framework normally owns an empty untitled bootstrap scene, so save
			// only empty test-owned scenes under our temporary root. Never replace or save
			// a user's untitled scene that already contains objects.
			for (int index = 0; index < SceneManager.sceneCount; index++)
			{
				Scene scene = SceneManager.GetSceneAt(index);
				if (!scene.IsValid() || !scene.isLoaded || !string.IsNullOrEmpty(scene.path))
				{
					continue;
				}

				if (scene.rootCount > 0 || !Application.isBatchMode)
				{
					Assert.Ignore(
						"An untitled user scene is open. Save or close it before running "
						+ "the SMPL-X registration scene tests.");
				}

				Assert.That(EditorSceneManager.SaveScene(scene, TestHostPath), Is.True,
					"The empty Unity Test Framework bootstrap scene could not be saved.");
			}

			Assert.That(Enumerable.Range(0, SceneManager.sceneCount)
				.Select(SceneManager.GetSceneAt)
				.All(scene => !scene.IsValid() || !scene.isLoaded || !string.IsNullOrEmpty(scene.path)),
				Is.True,
				"An untitled scene still blocks additive registration tests.");
		}

		private static Transform FindUnique(GameObject root, string name)
		{
			Transform[] matches = root.GetComponentsInChildren<Transform>(true)
				.Where(transform => transform.name == name)
				.ToArray();
			Assert.That(matches, Has.Length.EqualTo(1), "Expected one transform named " + name);
			return matches[0];
		}

		private static string FormatDiagnostics(DhmgsSmplxAvatarValidationResult result)
		{
			return string.Join("\n", result.Diagnostics.Select(item =>
				$"[{item.Severity}] {item.Code}: {item.Message}"));
		}
	}
}
