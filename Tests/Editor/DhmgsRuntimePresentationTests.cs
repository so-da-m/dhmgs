using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

using SMPLXPlugin;

namespace Mnnh.Dhmgs.Editor.Tests
{
	public sealed class DhmgsRuntimePresentationTests
	{
		private static readonly string PackageRoot = ResolvePackageRoot();
		private static readonly string RigRoot = PackageRoot + "/SmplxRig";
		private static readonly string GarmentAvatarPrefabPath = RigRoot + "/SmplxChatGarmentFemale.prefab";
		private static readonly string ClothesPrefabPath = RigRoot + "/SmplxChatGarmentClothes.prefab";
		private static readonly string YBotSourcePath = PackageRoot + "/Samples~/Full Demo/Rig/YBot.fbx";
		private static readonly string FullDemoScenePath =
			PackageRoot + "/Samples~/Full Demo/Scenes/DhmgsFullDemo.unity";
		private static readonly string FullDemoControllerPath =
			PackageRoot + "/Samples~/Full Demo/Scripts/DhmgsFullDemoController.cs";
		private static readonly string FullDemoEditorPreviewPath =
			PackageRoot + "/Samples~/Full Demo/Scripts/DhmgsFullDemoEditorPreview.cs";
		private static readonly string FullDemoImportBridgePath =
			PackageRoot + "/Samples~/Full Demo/Editor/DhmgsFullDemoEditorImportBridge.cs";
		private static readonly string ProjectValidatorPath =
			PackageRoot + "/Editor/DhmgsProjectValidator.cs";

		[TestCase("SmplxFemale")]
		[TestCase("SmplxMale")]
		[TestCase("SmplxNeutral")]
		public void BaseRigPrefab_HasExpectedDriversAndOpacityMaterials(string prefabName)
		{
			string path = RigRoot + "/" + prefabName + ".prefab";
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			Assert.That(prefab, Is.Not.Null, "Missing base rig prefab: " + path);

			Assert.That(prefab.GetComponentsInChildren<DhmgsRigDriver>(true), Has.Length.EqualTo(1),
				prefabName + " must contain exactly one DhmgsRigDriver.");
			Assert.That(prefab.GetComponentsInChildren<DhmgsSkinOpacity>(true), Has.Length.EqualTo(1),
				prefabName + " must contain exactly one DhmgsSkinOpacity.");

			SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			Assert.That(renderers, Is.Not.Empty, prefabName + " must contain a SkinnedMeshRenderer.");
			foreach (SkinnedMeshRenderer renderer in renderers)
			{
				Material[] materials = renderer.sharedMaterials;
				Assert.That(materials, Is.Not.Empty, renderer.name + " has no shared materials.");
				for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
				{
					Material material = materials[materialIndex];
					Assert.That(material, Is.Not.Null,
						$"{prefabName}/{renderer.name} material slot {materialIndex} is empty.");
					Assert.That(material.HasProperty("_Opacity"), Is.True,
						$"{prefabName}/{renderer.name} material '{material.name}' does not support _Opacity.");
				}
			}
		}

		[Test]
		public void GarmentAvatarPrefab_IsStandaloneFixedShapeBody()
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GarmentAvatarPrefabPath);
			Assert.That(prefab, Is.Not.Null, "Missing clothes-matched Garment Female prefab.");
			Assert.That(prefab.GetComponentsInChildren<DhmgsRigDriver>(true), Has.Length.EqualTo(1));
			Assert.That(prefab.GetComponentsInChildren<DhmgsSkinOpacity>(true), Has.Length.EqualTo(1));
			Assert.That(prefab.GetComponentsInChildren<SMPLX>(true), Is.Empty,
				"The fixed-shape SMPL-X Body must not retain the shape/corrective runtime component.");

			Animator animator = prefab.GetComponentInChildren<Animator>(true);
			Assert.That(animator, Is.Not.Null);
			Assert.That(animator.avatar, Is.Null,
				"The direct-playback avatar must not retain the source FBX Avatar subasset.");

			SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			Assert.That(renderers, Has.Length.EqualTo(1));
			SkinnedMeshRenderer renderer = renderers[0];
			Assert.That(renderer.sharedMesh, Is.Not.Null);
			Assert.That(renderer.sharedMesh.vertexCount, Is.EqualTo(11314));
			Assert.That(renderer.sharedMesh.blendShapeCount, Is.Zero,
				"The redistributable fixed-shape Body must not contain source model blendshapes.");
			Assert.That(renderer.bones, Has.Length.EqualTo(56));
			Assert.That(renderer.rootBone, Is.Not.Null);
			GameObject clothesPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClothesPrefabPath);
			Assert.That(clothesPrefab, Is.Not.Null);
			SkinnedMeshRenderer clothesRenderer =
				clothesPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
			Assert.That(clothesRenderer, Is.Not.Null);
			CollectionAssert.AreEqual(
				clothesRenderer.bones.Select(bone => bone.name).ToArray(),
				renderer.bones.Select(bone => bone.name).ToArray(),
				"Garment Female and its clothes must retain the same ordered skinning skeleton.");
			for (int boneIndex = 0; boneIndex < renderer.bones.Length; boneIndex++)
			{
				Transform avatarBone = renderer.bones[boneIndex];
				Transform clothesBone = clothesRenderer.bones[boneIndex];
				Assert.That(Vector3.Distance(avatarBone.localPosition, clothesBone.localPosition),
					Is.LessThan(0.00001f), avatarBone.name + " rest position no longer matches the clothes rig.");
			}
			foreach (Material material in renderer.sharedMaterials)
			{
				Assert.That(material, Is.Not.Null);
				Assert.That(material.shader.name, Is.EqualTo("Dhmgs/SkinTransparent"));
				Assert.That(material.HasProperty("_Opacity"), Is.True);
			}

			foreach (string dependency in AssetDatabase.GetDependencies(GarmentAvatarPrefabPath, true))
			{
				Assert.That(Path.GetExtension(dependency), Is.Not.EqualTo(".fbx").IgnoreCase,
					"Standalone Garment Female must not depend on an FBX: " + dependency);
				Assert.That(dependency.Replace('\\', '/'), Does.Not.Contain("/Content/"),
					"Standalone Garment Female must not depend on project-local Content: " + dependency);
			}

			string yaml = File.ReadAllText(Path.GetFullPath(GarmentAvatarPrefabPath));
			Assert.That(yaml, Does.Not.Contain("35625701d562c0b46ae707147ad6b37e"),
				"Standalone Garment Female must not retain the source model GUID.");
		}

		[Test]
		public void ClothesPrefab_IsStandaloneAndUsesClothesShader()
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClothesPrefabPath);
			if (prefab == null)
			{
				Assert.Ignore("Standalone clothes prefab has not been generated yet: " + ClothesPrefabPath);
			}

			Assert.That(prefab.GetComponentsInChildren<DhmgsRigDriver>(true), Has.Length.EqualTo(1),
				"Clothes prefab must contain exactly one DhmgsRigDriver.");
			Assert.That(prefab.GetComponentsInChildren<DhmgsSkinOpacity>(true), Has.Length.EqualTo(1),
				"Clothes prefab must contain exactly one DhmgsSkinOpacity.");
			Assert.That(prefab.GetComponentsInChildren<SMPLX>(true), Is.Empty,
				"Standalone clothes must not retain an SMPLX component.");

			Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
			Assert.That(renderers, Is.Not.Empty, "Clothes prefab must contain a renderer.");
			foreach (Renderer renderer in renderers)
			{
				if (renderer is SkinnedMeshRenderer skinnedRenderer)
				{
					Assert.That(skinnedRenderer.sharedMesh, Is.Not.Null,
						skinnedRenderer.name + " must reference the standalone native Mesh.");
					Assert.That(skinnedRenderer.bones, Is.Not.Empty,
						skinnedRenderer.name + " must retain its skinning bones.");
					Assert.That(skinnedRenderer.rootBone, Is.Not.Null,
						skinnedRenderer.name + " must retain its root bone.");
				}

				foreach (Material material in renderer.sharedMaterials)
				{
					Assert.That(material, Is.Not.Null, renderer.name + " has an empty material slot.");
					Assert.That(material.shader, Is.Not.Null, material.name + " has a missing shader.");
					Assert.That(material.shader.name, Is.EqualTo("Dhmgs/ClothesTransparent"),
						material.name + " must use the package clothes shader.");
				}
			}

			string[] dependencies = AssetDatabase.GetDependencies(ClothesPrefabPath, true);
			foreach (string dependency in dependencies)
			{
				Assert.That(Path.GetExtension(dependency), Is.Not.EqualTo(".fbx").IgnoreCase,
					"Standalone clothes prefab must not depend on an FBX: " + dependency);
			}

			string yaml = File.ReadAllText(Path.GetFullPath(ClothesPrefabPath));
			string modelSearchRoot = PackageRoot == "Assets/Mnnh.Dhmgs/Package"
				? "Assets/Mnnh.Dhmgs"
				: PackageRoot;
			string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { modelSearchRoot });
			foreach (string modelGuid in modelGuids)
			{
				Assert.That(yaml, Does.Not.Contain("guid: " + modelGuid),
					"Standalone clothes prefab YAML must not reference FBX model GUID " + modelGuid + ".");
			}
		}

		[Test]
		public void FullDemo_IncludesClothesMatchedGarmentFemale()
		{
			string scene = File.ReadAllText(Path.GetFullPath(FullDemoScenePath))
				.Replace("\r\n", "\n");
			string player = GetYamlDocument(scene, 1147522662);
			const string avatarList =
				"  _avatars:\n" +
				"  - {fileID: 444694584}\n" +
				"  - {fileID: 29406781}\n" +
				"  - {fileID: 1557838068}\n" +
				"  - {fileID: 2130640016}";
			Assert.That(player, Does.Contain(avatarList));
			Assert.That(player, Does.Contain("  _defaultAvatar: 2"),
				"Adding Garment Female must not change the default Neutral avatar.");

			string clothesInstance = GetYamlDocument(scene, 867258590);
			const string clothesDisplayName =
				"    - target: {fileID: 5478779261099414881, guid: 34701e122a2f36c478e86d9ca5e21f19, type: 3}\n" +
				"      propertyPath: m_Name\n" +
				"      value: Garment Clothes";
			Assert.That(clothesInstance, Does.Contain(clothesDisplayName),
				"The paired dropdown entries must share the Garment display name.");

			string instance = GetYamlDocument(scene, 2130640015);
			Assert.That(instance, Does.Contain("    m_TransformParent: {fileID: 2042756165}"));
			const string avatarDisplayName =
				"    - target: {fileID: 1306481656450799373, guid: 97941685cb9049043b8730426e785647, type: 3}\n" +
				"      propertyPath: m_Name\n" +
				"      value: Garment Female";
			Assert.That(instance, Does.Contain(avatarDisplayName));
			Assert.That(instance, Does.Contain(
				"  m_SourcePrefab: {fileID: 100100000, guid: 97941685cb9049043b8730426e785647, type: 3}"));

			string driver = GetYamlDocument(scene, 2130640016);
			Assert.That(driver, Does.Contain(
				"  m_CorrespondingSourceObject: {fileID: 5713619894650008075, guid: 97941685cb9049043b8730426e785647, type: 3}"));
		}

		[Test]
		public void FullDemoYBot_IsBundledWithoutGitLfsOrExternalFiles()
		{
			string absoluteFbxPath = Path.GetFullPath(YBotSourcePath);
			string absoluteMetaPath = absoluteFbxPath + ".meta";
			Assert.That(File.Exists(absoluteFbxPath), Is.True, "Missing Full Demo YBot FBX.");
			Assert.That(File.Exists(absoluteMetaPath), Is.True, "Missing Full Demo YBot meta file.");
			Assert.That(new FileInfo(absoluteFbxPath).Length, Is.LessThan(100L * 1024L * 1024L),
				"YBot must stay below GitHub's regular-file limit and must not require Git LFS.");

			string meta = File.ReadAllText(absoluteMetaPath);
			Assert.That(meta, Does.Contain("guid: 121420fccd4c4bba93c676f768fdf0a5"));
			Assert.That(meta, Does.Contain("animationType: 3"),
				"YBot must import as a Humanoid target.");
			Assert.That(meta, Does.Contain("externalObjects: {}"),
				"YBot must not depend on project-local material remaps.");
		}

		[Test]
		public void ProjectSetup_AutoConfiguresOncePerPackageVersionAndKeepsManualCommands()
		{
			const string currentVersion = "0.7.1";
			string projectA = Path.Combine(Path.GetTempPath(), "DhmgsAutoSetupProjectA");
			string projectB = Path.Combine(Path.GetTempPath(), "DhmgsAutoSetupProjectB");
			string first = global::Mnnh.Dhmgs.Editor.DhmgsProjectValidator.BuildAutoConfigurationKey(
				projectA,
				currentVersion);

			Assert.That(first, Is.EqualTo(
				global::Mnnh.Dhmgs.Editor.DhmgsProjectValidator.BuildAutoConfigurationKey(
					projectA,
					currentVersion)));
			Assert.That(first, Does.StartWith("Mnnh.Dhmgs.AutoProjectConfiguration."));
			Assert.That(first, Does.EndWith("." + currentVersion));
			Assert.That(first, Is.Not.EqualTo(
				global::Mnnh.Dhmgs.Editor.DhmgsProjectValidator.BuildAutoConfigurationKey(
					projectB,
					currentVersion)),
				"Different Unity projects must not share the automatic-setup marker.");
			Assert.That(first, Is.Not.EqualTo(
				global::Mnnh.Dhmgs.Editor.DhmgsProjectValidator.BuildAutoConfigurationKey(
					projectA,
					"0.7.0")),
				"A package update must get a fresh automatic-setup marker.");
			Assert.That(global::Mnnh.Dhmgs.Editor.DhmgsProjectValidator.FallbackPackageVersion,
				Is.EqualTo(currentVersion));

			string source = File.ReadAllText(Path.GetFullPath(ProjectValidatorPath))
				.Replace("\r\n", "\n");
			Assert.That(source, Does.Contain("[InitializeOnLoad]"));
			Assert.That(source, Does.Contain(
				"EditorApplication.delayCall += AutoConfigureAfterImport;"));
			Assert.That(source, Does.Contain("AssetDatabase.IsAssetImportWorkerProcess()"));
			Assert.That(
				source.Split(new[] { "AssetDatabase.IsAssetImportWorkerProcess()" },
					System.StringSplitOptions.None).Length - 1,
				Is.EqualTo(2),
				"Both initialization and the delayed callback must reject import-worker processes.");
			Assert.That(source, Does.Contain(
				"if (EditorApplication.isCompiling || EditorApplication.isUpdating)"));
			Assert.That(source, Does.Contain(
				"if (EditorPrefs.GetBool(configurationKey, false))"));
			Assert.That(source, Does.Contain(
				"EditorPrefs.SetBool(configurationKey, true);"));
			int autoMethod = source.IndexOf(
				"private static void AutoConfigureAfterImport()",
				System.StringComparison.Ordinal);
			Assert.That(autoMethod, Is.GreaterThanOrEqualTo(0));
			int constructor = source.IndexOf(
				"static DhmgsProjectValidator()",
				System.StringComparison.Ordinal);
			int constructorWorkerGuard = source.IndexOf(
				"AssetDatabase.IsAssetImportWorkerProcess()",
				constructor,
				System.StringComparison.Ordinal);
			int constructorReturn = source.IndexOf(
				"return;",
				constructorWorkerGuard,
				System.StringComparison.Ordinal);
			int constructorSchedule = source.IndexOf(
				"EditorApplication.delayCall += AutoConfigureAfterImport;",
				constructor,
				System.StringComparison.Ordinal);
			Assert.That(constructorWorkerGuard, Is.GreaterThan(constructor));
			Assert.That(constructorReturn, Is.GreaterThan(constructorWorkerGuard));
			Assert.That(constructorSchedule, Is.GreaterThan(constructorReturn),
				"Import workers must be rejected before automatic setup is scheduled.");
			int callbackWorkerGuard = source.IndexOf(
				"AssetDatabase.IsAssetImportWorkerProcess()",
				autoMethod,
				System.StringComparison.Ordinal);
			int callbackReturn = source.IndexOf(
				"return;",
				callbackWorkerGuard,
				System.StringComparison.Ordinal);
			int compilingGuard = source.IndexOf(
				"if (EditorApplication.isCompiling || EditorApplication.isUpdating)",
				autoMethod,
				System.StringComparison.Ordinal);
			Assert.That(callbackWorkerGuard, Is.GreaterThan(autoMethod));
			Assert.That(callbackReturn, Is.GreaterThan(callbackWorkerGuard));
			Assert.That(compilingGuard, Is.GreaterThan(callbackReturn),
				"A delayed callback must reject import workers before it can reschedule or mutate settings.");
			Assert.That(source.IndexOf(
				"ConfigureProject(packageVersion, configurationKey);",
				autoMethod,
				System.StringComparison.Ordinal), Is.GreaterThan(autoMethod));
			int configureMethod = source.IndexOf(
				"internal static void ConfigureProject(string packageVersion, string configurationKey)",
				System.StringComparison.Ordinal);
			Assert.That(configureMethod, Is.GreaterThan(autoMethod));
			int ensureRequiredSetting = source.IndexOf(
				"bool changed = EnsureInsecureHttpAllowed();",
				configureMethod,
				System.StringComparison.Ordinal);
			int markerGate = source.IndexOf(
				"if (EditorPrefs.GetBool(configurationKey, false))",
				configureMethod,
				System.StringComparison.Ordinal);
			int oneTimeValidation = source.IndexOf(
				"bool valid = ValidateProject(logSuccess: false);",
				configureMethod,
				System.StringComparison.Ordinal);
			Assert.That(ensureRequiredSetting, Is.GreaterThan(configureMethod));
			Assert.That(markerGate, Is.GreaterThan(ensureRequiredSetting),
				"The required HTTP setting must be repaired even when the validation marker exists.");
			Assert.That(oneTimeValidation, Is.GreaterThan(markerGate),
				"Validation must remain behind the one-time marker gate.");
			Assert.That(source, Does.Contain("[MenuItem(\"DHMGS/Project/Validate\")]"));
			Assert.That(source, Does.Contain(
				"[MenuItem(\"DHMGS/Project/Allow Insecure HTTP\")]"));
			Assert.That(source, Does.Contain(
				"TMP Essential Resources are not imported."),
				"TMP must remain a validation warning instead of an automatic import.");
			Assert.That(source, Does.Not.Contain("AssetDatabase.SaveAssets()"),
				"Automatic setup must not save unrelated dirty user assets.");
		}

		[TestCase(false, false, true)]
		[TestCase(true, false, false)]
		[TestCase(false, true, false)]
		[TestCase(true, true, false)]
		public void ProjectSetup_AutomaticConfigurationRunsOnlyInInteractiveMainEditor(
			bool isBatchMode,
			bool isAssetImportWorkerProcess,
			bool expected)
		{
			Assert.That(
				global::Mnnh.Dhmgs.Editor.DhmgsProjectValidator.ShouldAutoConfigure(
					isBatchMode,
					isAssetImportWorkerProcess),
				Is.EqualTo(expected));
		}

		[Test]
		public void ProjectSetup_InsecureHttpConfigurationIsIdempotent()
		{
			InsecureHttpOption original = PlayerSettings.insecureHttpOption;
			string markerKey = "Mnnh.Dhmgs.Tests.ProjectSetup." + System.Guid.NewGuid().ToString("N");
			try
			{
				PlayerSettings.insecureHttpOption = InsecureHttpOption.NotAllowed;
				global::Mnnh.Dhmgs.Editor.DhmgsProjectValidator.ConfigureProject(
					"test-version",
					markerKey);
				Assert.That(PlayerSettings.insecureHttpOption,
					Is.EqualTo(InsecureHttpOption.AlwaysAllowed));
				Assert.That(EditorPrefs.GetBool(markerKey, false), Is.True,
					"The initial validation must consume its one-time marker.");

				PlayerSettings.insecureHttpOption = InsecureHttpOption.NotAllowed;
				global::Mnnh.Dhmgs.Editor.DhmgsProjectValidator.ConfigureProject(
					"test-version",
					markerKey);
				Assert.That(PlayerSettings.insecureHttpOption,
					Is.EqualTo(InsecureHttpOption.AlwaysAllowed),
					"The required setting must be repaired even after validation has run once.");
				Assert.That(
					global::Mnnh.Dhmgs.Editor.DhmgsProjectValidator.EnsureInsecureHttpAllowed(),
					Is.False,
					"A repeated setup must not rewrite an already-correct project setting.");
			}
			finally
			{
				EditorPrefs.DeleteKey(markerKey);
				PlayerSettings.insecureHttpOption = original;
			}
		}

		[Test]
		public void FullDemoCameras_UseEquivalentFrontFacingConditions()
		{
			string scene = File.ReadAllText(Path.GetFullPath(FullDemoScenePath))
				.Replace("\r\n", "\n");
			AssertPreviewCameraSettings(scene, 1597169320, 1147522662, 180, "SMPL-X");
			AssertPreviewCameraSettings(scene, 829823346, 0, 0, "Unity Humanoid");
		}

		[Test]
		public void FixedAvatarCamera_ResetViewRestoresAuthoredHomeView()
		{
			GameObject root = new GameObject(
				"Camera Reset Test",
				typeof(RectTransform),
				typeof(CanvasRenderer),
				typeof(RawImage));
			root.SetActive(false);
			DhmgsFixedAvatarCamera camera = root.AddComponent<DhmgsFixedAvatarCamera>();
			SetPrivateField(camera, "_yaw", 180f);
			SetPrivateField(camera, "_pitch", 20f);
			SetPrivateField(camera, "_distance", 3f);
			SetPrivateField(camera, "_initialViewCaptured", false);
			camera.ResetView();

			try
			{
				root.SetActive(true);
				SetPrivateField(camera, "_yaw", 247f);
				SetPrivateField(camera, "_pitch", -31f);
				SetPrivateField(camera, "_distance", 7f);
				SetPrivateField(camera, "_pan", new Vector3(1f, 2f, 3f));
				SetPrivateField(camera, "_pivotOffset", new Vector3(4f, 5f, 6f));
				SetPrivateField(camera, "_pivotLocked", true);

				camera.ResetView();

				Assert.That(GetPrivateField<float>(camera, "_yaw"), Is.EqualTo(180f));
				Assert.That(GetPrivateField<float>(camera, "_pitch"), Is.EqualTo(20f));
				Assert.That(GetPrivateField<float>(camera, "_distance"), Is.EqualTo(3f));
				Assert.That(GetPrivateField<Vector3>(camera, "_pan"), Is.EqualTo(Vector3.zero));
				Assert.That(GetPrivateField<Vector3>(camera, "_pivotOffset"), Is.EqualTo(Vector3.zero));
				Assert.That(GetPrivateField<bool>(camera, "_pivotLocked"), Is.False,
					"Reset must unlock the old frame so the authored view is fitted again.");
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void FullDemoPreviewPanels_HaveIndependentResetCameraButtons()
		{
			string scene = File.ReadAllText(Path.GetFullPath(FullDemoScenePath))
				.Replace("\r\n", "\n");
			string controller = GetYamlDocument(scene, 1104551308);
			Assert.That(controller, Does.Contain(
				"  _directPreviewCamera: {fileID: 1597169320}\n" +
				"  _humanoidPreviewCamera: {fileID: 829823346}\n" +
				"  _directResetCameraButton: {fileID: 1553524884}\n" +
				"  _humanoidResetCameraButton: {fileID: 1043193371}"),
				"Each comparison panel must reference its own reset control.");

			AssertCameraResetButton(
				scene,
				"SMPL-X",
				1553524883,
				1553524884,
				1553524885,
				1491333803,
				1491333804,
				-24);
			AssertCameraResetButton(
				scene,
				"Unity Humanoid",
				1043193370,
				1043193371,
				1043193372,
				1468639581,
				1468639582,
				-384);

			Assert.That(GetYamlDocument(scene, 1593716991),
				Does.Contain("  m_SizeDelta: {x: 230, y: 30}"));
			Assert.That(GetYamlDocument(scene, 1426311810),
				Does.Contain("  m_SizeDelta: {x: 230, y: 30}"));
			const string previewSiblingOrder =
				"  - {fileID: 1597169319}\n" +
				"  - {fileID: 829823345}\n" +
				"  - {fileID: 1553524883}\n" +
				"  - {fileID: 1043193370}";
			Assert.That(GetYamlDocument(scene, 1935608393), Does.Contain(previewSiblingOrder),
				"Reset controls must render after both RawImages without becoming their input children.");

			string source = File.ReadAllText(Path.GetFullPath(FullDemoControllerPath))
				.Replace("\r\n", "\n");
			Assert.That(source, Does.Contain(
				"_directResetCameraButton?.onClick.AddListener(ResetDirectPreviewCamera);"));
			Assert.That(source, Does.Contain(
				"_humanoidResetCameraButton?.onClick.AddListener(ResetHumanoidPreviewCamera);"));
			Assert.That(source, Does.Contain(
				"_directResetCameraButton?.onClick.RemoveListener(ResetDirectPreviewCamera);"));
			Assert.That(source, Does.Contain(
				"_humanoidResetCameraButton?.onClick.RemoveListener(ResetHumanoidPreviewCamera);"));
			const string directReset =
				"\t\tprivate void ResetDirectPreviewCamera()\n" +
				"\t\t{\n" +
				"\t\t\t_directPreviewCamera?.ResetView();\n" +
				"\t\t}";
			const string humanoidReset =
				"\t\tprivate void ResetHumanoidPreviewCamera()\n" +
				"\t\t{\n" +
				"\t\t\t_humanoidPreviewCamera?.ResetView();\n" +
				"\t\t}";
			Assert.That(source, Does.Contain(directReset));
			Assert.That(source, Does.Contain(humanoidReset));
		}

		[Test]
		public void FullDemoClothesSelection_PreservesDirectFraming()
		{
			string controller = File.ReadAllText(Path.GetFullPath(FullDemoControllerPath))
				.Replace("\r\n", "\n");
			const string clothesHandler =
				"\t\tprivate void OnClothesChanged(int value)\n" +
				"\t\t{\n" +
				"\t\t\t_player?.SetClothesIndex(value - 1);\n" +
				"\t\t\tOnClothesOpacityChanged(_clothesOpacitySlider != null ? _clothesOpacitySlider.value : 1f);\n" +
				"\t\t}";
			Assert.That(controller, Does.Contain(clothesHandler),
				"Changing optional clothes must not refit the direct camera at a different animation pose.");
		}

		[Test]
		public void FullDemoVideoPreparation_RemainsBoundedWhenUnityLosesFocus()
		{
			string bridge = File.ReadAllText(Path.GetFullPath(FullDemoImportBridgePath))
				.Replace("\r\n", "\n");
			const string backgroundGuard =
				"bool previousRunInBackground = Application.runInBackground;\n" +
				"\t\t\t\ttry\n" +
				"\t\t\t\t{\n";
			Assert.That(bridge, Does.Contain(backgroundGuard));
			Assert.That(bridge, Does.Contain(
				"\t\t\t\t\tApplication.runInBackground = true;\n"));
			const string backgroundRestore =
				"\t\t\t\tfinally\n" +
				"\t\t\t\t{\n" +
				"\t\t\t\t\tApplication.runInBackground = previousRunInBackground;\n" +
				"\t\t\t\t}";
			Assert.That(bridge, Does.Contain(backgroundRestore),
				"The Editor-only focus override must be restored on success, failure, and cancellation.");

			string preview = File.ReadAllText(Path.GetFullPath(FullDemoEditorPreviewPath))
				.Replace("\r\n", "\n");
			Assert.That(preview, Does.Contain(
				"new TaskCompletionSource<IDhmgsPlaybackClock>(\n" +
				"\t\t\t\t\tTaskCreationOptions.RunContinuationsAsynchronously);"));
			Assert.That(preview, Does.Contain(
				"if (ReferenceEquals(_clockCompletion, completion))\n" +
				"\t\t\t{\n" +
				"\t\t\t\t_clockMonitor = monitor;\n" +
				"\t\t\t}\n" +
				"\t\t\treturn completion.Task;"),
				"A synchronously completed activation must not revive stale coroutine state or lose its Task.");
		}

		[Test]
		public void FullDemoComparisonPanels_UsePlaybackLabels()
		{
			string scene = File.ReadAllText(Path.GetFullPath(FullDemoScenePath))
				.Replace("\r\n", "\n");
			Assert.That(GetYamlDocument(scene, 1593716992),
				Does.EndWith("  m_Text: SMPL-X PLAYBACK"),
				"The upper comparison panel must describe its SMPL-X playback content.");
			Assert.That(GetYamlDocument(scene, 1426311811),
				Does.EndWith("  m_Text: UNITY HUMANOID PLAYBACK"),
				"The lower comparison panel must describe its Unity Humanoid playback content.");
		}

		[Test]
		public void FullDemoIntro_StartsWithoutInstructionCopy()
		{
			string scene = File.ReadAllText(Path.GetFullPath(FullDemoScenePath))
				.Replace("\r\n", "\n");
			Assert.That(GetYamlDocument(scene, 935676495), Does.EndWith("  m_Text:"),
				"The static Fetch/bake/playback description must start empty.");
			Assert.That(GetYamlDocument(scene, 1660494554), Does.EndWith("  m_Text:"),
				"The operation status must start empty until work begins.");

			string controller = File.ReadAllText(Path.GetFullPath(FullDemoControllerPath))
				.Replace("\r\n", "\n");
			Assert.That(controller, Does.Not.Contain("Ready. Enter an Attempt ID and Fetch."),
				"The controller must not restore the removed initial instruction at runtime.");
			const string initialStatus =
				"SetProgress(0f, available\n" +
				"\t\t\t\t? string.Empty\n" +
				"\t\t\t\t: \"Fetch, baking, and video preview are Editor-only.\");";
			Assert.That(controller, Does.Contain(initialStatus),
				"The Editor-ready path must remain empty while the unavailable path keeps its explanation.");
		}

		[Test]
		public void FullDemoControlPanel_UsesCompactLayoutAndContainedSliderHandles()
		{
			string scene = File.ReadAllText(Path.GetFullPath(FullDemoScenePath))
				.Replace("\r\n", "\n");

			AssertRect(scene, 1194733685, 20, -20, 430, 500, "Control Panel");
			AssertRect(scene, 935676494, 18, -43, 394, 0, "Removed description slot");
			AssertRect(scene, 845203382, 18, -46, 394, 20, "Server row");
			AssertRect(scene, 834354359, 18, -72, 86, 34, "Attempt ID label");
			AssertRect(scene, 1219108606, 104, -72, 126, 34, "Attempt ID input");
			AssertRect(scene, 2083564318, 238, -72, 174, 34, "Fetch button");
			AssertRect(scene, 2000084992, 18, -113, 394, 10, "Fetch progress");
			AssertRect(scene, 1660494553, 18, -127, 394, 22, "Operation status");
			AssertRect(scene, 1950418010, 18, -155, 394, 1, "Upper divider");
			AssertRect(scene, 1656304924, 18, -166, 124, 32, "Rig label");
			AssertRect(scene, 1993110571, 146, -166, 266, 32, "Rig dropdown");
			AssertRect(scene, 941369818, 18, -202, 124, 32, "Clothes label");
			AssertRect(scene, 443806443, 146, -202, 266, 32, "Clothes dropdown");
			AssertRect(scene, 589403728, 18, -240, 122, 26, "Avatar opacity label");
			AssertRect(scene, 1609728958, 143, -241, 210, 24, "Avatar opacity slider");
			AssertRect(scene, 499116788, 361, -240, 51, 26, "Avatar opacity value");
			AssertRect(scene, 587835256, 18, -272, 122, 26, "Clothes opacity label");
			AssertRect(scene, 790697361, 143, -273, 210, 24, "Clothes opacity slider");
			AssertRect(scene, 456937357, 361, -272, 51, 26, "Clothes opacity value");
			AssertRect(scene, 2050478462, 18, -304, 122, 26, "Video opacity label");
			AssertRect(scene, 598595543, 143, -305, 210, 24, "Video opacity slider");
			AssertRect(scene, 960203951, 361, -304, 51, 26, "Video opacity value");
			AssertRect(scene, 2140026618, 18, -336, 122, 26, "Play speed label");
			AssertRect(scene, 1866058379, 143, -337, 210, 24, "Play speed slider");
			AssertRect(scene, 292419785, 361, -336, 51, 26, "Play speed value");
			AssertRect(scene, 944199121, 18, -369, 394, 1, "Lower divider");
			AssertRect(scene, 1900638898, 18, -379, 394, 22, "Playback status");
			AssertRect(scene, 1909998407, 18, -407, 394, 77, "Log panel");

			AssertSliderHandle(scene, "Avatar opacity", 1609728959, 1609728958, 607842515, 59828390);
			AssertSliderHandle(scene, "Clothes opacity", 790697362, 790697361, 742654941, 1629121359);
			AssertSliderHandle(scene, "Video opacity", 598595544, 598595543, 238878688, 804265266);
			AssertSliderHandle(scene, "Play speed", 1866058380, 1866058379, 419068377, 572283857);
		}

		[Test]
		public void FullDemoDropdowns_UseHighContrastOptionRows()
		{
			string scene = File.ReadAllText(Path.GetFullPath(FullDemoScenePath))
				.Replace("\r\n", "\n");
			const string optionRowColors =
				"    m_NormalColor: {r: 0.1, g: 0.13, b: 0.18, a: 1}\n" +
				"    m_HighlightedColor: {r: 0.17, g: 0.42, b: 0.66, a: 1}\n" +
				"    m_PressedColor: {r: 0.12, g: 0.3, b: 0.5, a: 1}\n" +
				"    m_SelectedColor: {r: 0.14, g: 0.35, b: 0.58, a: 1}\n" +
				"    m_DisabledColor: {r: 0.08, g: 0.09, b: 0.11, a: 0.65}\n";
			const string selectedMarker =
				"  m_AnchoredPosition: {x: 8, y: 0}\n" +
				"  m_SizeDelta: {x: 4, y: 16}\n";
			const string selectedMarkerColor =
				"  m_Color: {r: 0.22, g: 0.72, b: 1, a: 1}\n";

			AssertDropdownTemplate(
				scene,
				"SMPL-X Rig",
				1623962146,
				1783227,
				1687078493,
				1623962145,
				1687078492,
				optionRowColors,
				selectedMarker,
				selectedMarkerColor);
			AssertDropdownTemplate(
				scene,
				"SMPL-X Clothes",
				981308389,
				1250377126,
				1988399354,
				981308388,
				1988399353,
				optionRowColors,
				selectedMarker,
				selectedMarkerColor);
		}

		private static void AssertDropdownTemplate(
			string scene,
			string label,
			long toggleId,
			long backgroundId,
			long markerImageId,
			long rowRectId,
			long markerRectId,
			string optionRowColors,
			string selectedMarker,
			string selectedMarkerColor)
		{
			string toggle = GetYamlDocument(scene, toggleId);
			Assert.That(toggle, Does.Contain(optionRowColors),
				label + " options must use readable dark rows and blue interaction states.");
			Assert.That(toggle, Does.Contain($"  m_TargetGraphic: {{fileID: {backgroundId}}}"),
				label + " option colors must target its Item Background.");
			Assert.That(toggle, Does.Contain($"  graphic: {{fileID: {markerImageId}}}"),
				label + " Toggle must use its selected-option marker.");

			string rowRect = GetYamlDocument(scene, rowRectId);
			Assert.That(rowRect, Does.Contain("  m_SizeDelta: {x: 0, y: 28}"),
				label + " options must retain the larger 28-pixel hit target.");

			string markerRect = GetYamlDocument(scene, markerRectId);
			Assert.That(markerRect, Does.Contain(selectedMarker),
				label + " must show a compact selected-option marker.");
			string markerImage = GetYamlDocument(scene, markerImageId);
			Assert.That(markerImage, Does.Contain(selectedMarkerColor),
				label + " selected-option marker must remain visible against the dark row.");
		}

		private static void AssertRect(
			string scene,
			long rectId,
			int x,
			int y,
			int width,
			int height,
			string label)
		{
			string expected =
				$"  m_AnchoredPosition: {{x: {x}, y: {y}}}\n" +
				$"  m_SizeDelta: {{x: {width}, y: {height}}}";
			Assert.That(GetYamlDocument(scene, rectId), Does.Contain(expected),
				label + " must retain the compact Full Demo layout.");
		}

		private static void AssertPreviewCameraSettings(
			string scene,
			long componentId,
			long targetId,
			int frontFacingYaw,
			string label)
		{
			string expected =
				$"  _targetSource: {{fileID: {targetId}}}\n" +
				"  _textureWidth: 500\n" +
				"  _textureHeight: 500\n" +
				$"  _yaw: {frontFacingYaw}\n" +
				"  _pitch: 20\n" +
				"  _distance: 3\n" +
				"  _autoFrame: 1\n" +
				"  _framePadding: 1.25\n" +
				"  _minPitch: -80\n" +
				"  _maxPitch: 80\n" +
				"  _minDistance: 0.8\n" +
				"  _maxDistance: 8\n" +
				"  _orbitSpeed: 0.3\n" +
				"  _zoomSpeed: 0.5\n" +
				"  _panSpeed: 0.002\n" +
				"  _fieldOfView: 60\n" +
				"  _nearClip: 0.05\n" +
				"  _farClip: 100\n" +
				"  _cullingMask:\n" +
				"    serializedVersion: 2\n" +
				"    m_Bits: 4294967291\n" +
				"  _background: {r: 0, g: 0, b: 0, a: 1}";
			Assert.That(GetYamlDocument(scene, componentId), Does.Contain(expected),
				label + " preview must use the shared camera preset with its rig-specific front-facing yaw.");
		}

		private static void AssertCameraResetButton(
			string scene,
			string label,
			long rectId,
			long buttonId,
			long imageId,
			long labelRectId,
			long labelTextId,
			int y)
		{
			string rect = GetYamlDocument(scene, rectId);
			const long canvasRectId = 1935608393;
			string expectedRect =
				$"  m_Father: {{fileID: {canvasRectId}}}\n" +
				"  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}\n" +
				"  m_AnchorMin: {x: 1, y: 1}\n" +
				"  m_AnchorMax: {x: 1, y: 1}\n" +
				$"  m_AnchoredPosition: {{x: -26, y: {y}}}\n" +
				"  m_SizeDelta: {x: 68, y: 22}\n" +
				"  m_Pivot: {x: 1, y: 1}";
			Assert.That(rect, Does.Contain(expectedRect),
				label + " reset must overlay the caption without becoming a camera-input child.");

			string button = GetYamlDocument(scene, buttonId);
			Assert.That(button, Does.Contain("  m_Navigation:\n    m_Mode: 0"));
			Assert.That(button, Does.Contain($"  m_TargetGraphic: {{fileID: {imageId}}}"));
			Assert.That(button, Does.Contain("  m_OnClick:\n    m_PersistentCalls:\n      m_Calls: []"),
				label + " reset must use the controller's lifecycle-safe runtime listener.");

			string image = GetYamlDocument(scene, imageId);
			Assert.That(image, Does.Contain("  m_Color: {r: 0.17, g: 0.42, b: 0.66, a: 1}"));
			Assert.That(image, Does.Contain("  m_RaycastTarget: 1"));

			string labelRect = GetYamlDocument(scene, labelRectId);
			Assert.That(labelRect, Does.Contain($"  m_Father: {{fileID: {rectId}}}"));
			Assert.That(labelRect, Does.Contain("  m_SizeDelta: {x: 0, y: 0}"));

			string text = GetYamlDocument(scene, labelTextId);
			Assert.That(text, Does.Contain("  m_RaycastTarget: 0"));
			Assert.That(text, Does.Contain("    m_FontSize: 12\n    m_FontStyle: 1"));
			Assert.That(text, Does.EndWith("  m_Text: RESET"));
		}

		private static void AssertSliderHandle(
			string scene,
			string label,
			long sliderId,
			long sliderRectId,
			long handleAreaId,
			long handleRectId)
		{
			string slider = GetYamlDocument(scene, sliderId);
			Assert.That(slider, Does.Contain($"  m_HandleRect: {{fileID: {handleRectId}}}"),
				label + " Slider must keep its intended Handle Rect.");
			Assert.That(slider, Does.Contain("  m_Direction: 0"),
				label + " Slider must remain horizontal so Unity drives the handle's vertical anchors.");
			Assert.That(GetYamlDocument(scene, sliderRectId),
				Does.Contain("  m_SizeDelta: {x: 210, y: 24}"),
				label + " Slider row must remain 24 pixels high.");

			string handleArea = GetYamlDocument(scene, handleAreaId);
			Assert.That(handleArea, Does.Contain($"  m_Father: {{fileID: {sliderRectId}}}"),
				label + " Handle Slide Area must remain under its Slider row.");
			const string stretchedHandleArea =
				"  m_AnchorMin: {x: 0, y: 0}\n" +
				"  m_AnchorMax: {x: 1, y: 1}\n" +
				"  m_AnchoredPosition: {x: 0, y: 0}\n" +
				"  m_SizeDelta: {x: -16, y: 0}";
			Assert.That(handleArea, Does.Contain(stretchedHandleArea),
				label + " Handle Slide Area must remain aligned to the 16-pixel handle width.");

			string handle = GetYamlDocument(scene, handleRectId);
			Assert.That(handle, Does.Contain($"  m_Father: {{fileID: {handleAreaId}}}"),
				label + " Handle Rect must remain under its stretched Handle Slide Area.");
			Assert.That(handle, Does.Contain("  m_SizeDelta: {x: 16, y: -8}"),
				label + " handle must resolve to 16 pixels high after Unity stretches it across the 24-pixel row.");
		}

		private static string GetYamlDocument(string yaml, long fileId)
		{
			string id = "&" + fileId;
			int idOffset = yaml.IndexOf(id + "\n", System.StringComparison.Ordinal);
			if (idOffset < 0)
			{
				idOffset = yaml.IndexOf(id + " stripped\n", System.StringComparison.Ordinal);
			}
			Assert.That(idOffset, Is.GreaterThanOrEqualTo(0), "Missing YAML component " + fileId + ".");
			int start = yaml.LastIndexOf("--- !u!", idOffset, System.StringComparison.Ordinal);
			int end = yaml.IndexOf("\n--- !u!", idOffset, System.StringComparison.Ordinal);
			return end >= 0 ? yaml.Substring(start, end - start) : yaml.Substring(start);
		}

		private static string ResolvePackageRoot()
		{
			UnityEditor.PackageManager.PackageInfo packageInfo =
				UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DhmgsRuntimePresentationTests).Assembly);
			return packageInfo != null ? packageInfo.assetPath : "Assets/Mnnh.Dhmgs/Package";
		}

		[Test]
		public void SkinOpacity_UsesPerRendererBlockWithoutMutatingSharedMaterial()
		{
			Shader shader = Shader.Find("Dhmgs/SkinTransparent");
			Assert.That(shader, Is.Not.Null);
			Material shared = new Material(shader);
			GameObject root = new GameObject("Opacity Test");
			GameObject child = new GameObject("Renderer");
			child.transform.SetParent(root.transform, false);
			SkinnedMeshRenderer renderer = child.AddComponent<SkinnedMeshRenderer>();
			renderer.sharedMaterial = shared;
			MaterialPropertyBlock initial = new MaterialPropertyBlock();
			int baseColor = Shader.PropertyToID("_BaseColor");
			initial.SetColor(baseColor, Color.magenta);
			renderer.SetPropertyBlock(initial, 0);

			try
			{
				DhmgsSkinOpacity opacity = root.AddComponent<DhmgsSkinOpacity>();
				opacity.SetOpacity(0.25f);

				Assert.That(renderer.sharedMaterial, Is.SameAs(shared));
				Assert.That(shared.GetFloat("_Opacity"), Is.EqualTo(1f));
				MaterialPropertyBlock actual = new MaterialPropertyBlock();
				renderer.GetPropertyBlock(actual, 0);
				Assert.That(actual.GetFloat("_Opacity"), Is.EqualTo(0.25f));
				Assert.That(actual.GetColor(baseColor), Is.EqualTo(Color.magenta));
			}
			finally
			{
				Object.DestroyImmediate(root);
				Object.DestroyImmediate(shared);
			}
		}

		[Test]
		public void FramingBounds_EncapsulatesActiveRenderersOnly()
		{
			GameObject root = new GameObject("Bounds Test");
			GameObject left = GameObject.CreatePrimitive(PrimitiveType.Cube);
			GameObject right = GameObject.CreatePrimitive(PrimitiveType.Cube);
			GameObject inactive = GameObject.CreatePrimitive(PrimitiveType.Cube);
			left.transform.SetParent(root.transform, false);
			right.transform.SetParent(root.transform, false);
			inactive.transform.SetParent(root.transform, false);
			left.transform.localPosition = new Vector3(-2f, 0f, 0f);
			right.transform.localPosition = new Vector3(2f, 0f, 0f);
			inactive.transform.localPosition = new Vector3(100f, 0f, 0f);
			inactive.SetActive(false);

			try
			{
				Assert.That(DhmgsFramingBoundsUtility.TryCalculate(root.transform, out Bounds bounds), Is.True);
				Assert.That(bounds.center.x, Is.EqualTo(0f).Within(0.001f));
				Assert.That(bounds.size.x, Is.EqualTo(5f).Within(0.001f));
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void MotionPlayerFraming_UsesAvatarBoundsInsteadOfClothes()
		{
			GameObject root = new GameObject("Player Framing Test");
			root.SetActive(false);
			DhmgsMotionPlayer player = root.AddComponent<DhmgsMotionPlayer>();

			GameObject avatarRoot = new GameObject("Avatar");
			avatarRoot.transform.SetParent(root.transform, false);
			DhmgsRigDriver avatar = avatarRoot.AddComponent<DhmgsRigDriver>();
			GameObject avatarMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
			avatarMesh.transform.SetParent(avatarRoot.transform, false);

			GameObject clothesRoot = new GameObject("Clothes");
			clothesRoot.transform.SetParent(root.transform, false);
			DhmgsRigDriver clothes = clothesRoot.AddComponent<DhmgsRigDriver>();
			GameObject clothesMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
			clothesMesh.transform.SetParent(clothesRoot.transform, false);
			clothesMesh.transform.localPosition = new Vector3(100f, 0f, 0f);

			SetPrivateField(player, "_avatars",
				new System.Collections.Generic.List<DhmgsRigDriver> { avatar });
			SetPrivateField(player, "_clothes",
				new System.Collections.Generic.List<DhmgsRigDriver> { clothes });
			SetPrivateField(player, "_defaultAvatar", 0);
			SetPrivateField(player, "_defaultClothes", 0);

			try
			{
				root.SetActive(true);
				SetPrivateField(player, "_playing", true);
				Assert.That(clothesRoot.activeInHierarchy, Is.True,
					"The clothes renderer must be active to prove it is excluded from avatar framing.");
				Assert.That(player.TryGetFramingBounds(out Bounds avatarBounds), Is.True);
				Assert.That(avatarBounds.center.x, Is.EqualTo(0f).Within(0.001f));
				Assert.That(avatarBounds.size.x, Is.EqualTo(1f).Within(0.001f));

				player.SetAvatarIndex(-1);
				Assert.That(player.TryGetFramingBounds(out Bounds clothesFallback), Is.True);
				Assert.That(clothesFallback.center.x, Is.EqualTo(100f).Within(0.001f),
					"Clothes must remain a framing fallback when no avatar is active.");
			}
			finally
			{
				Object.DestroyImmediate(root);
			}
		}

		private static void SetPrivateField(object target, string fieldName, object value)
		{
			System.Reflection.FieldInfo field = target.GetType().GetField(
				fieldName,
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null, "Missing private field " + fieldName + ".");
			field.SetValue(target, value);
		}

		private static T GetPrivateField<T>(object target, string fieldName)
		{
			System.Reflection.FieldInfo field = target.GetType().GetField(
				fieldName,
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null, "Missing private field " + fieldName + ".");
			return (T)field.GetValue(target);
		}
	}
}
