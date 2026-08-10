using System.IO;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

using SMPLXPlugin;

namespace Mnnh.Dhmgs.Editor.Tests
{
	public sealed class DhmgsRuntimePresentationTests
	{
		private static readonly string PackageRoot = ResolvePackageRoot();
		private static readonly string RigRoot = PackageRoot + "/SmplxRig";
		private static readonly string ClothesPrefabPath = RigRoot + "/SmplxChatGarmentClothes.prefab";
		private static readonly string YBotSourcePath = PackageRoot + "/Samples~/Full Demo/Rig/YBot.fbx";
		private static readonly string FullDemoScenePath =
			PackageRoot + "/Samples~/Full Demo/Scenes/DhmgsFullDemo.unity";
		private static readonly string FullDemoControllerPath =
			PackageRoot + "/Samples~/Full Demo/Scripts/DhmgsFullDemoController.cs";

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
		public void FullDemoCameras_StartOnTheirExpectedSides()
		{
			string scene = File.ReadAllText(Path.GetFullPath(FullDemoScenePath))
				.Replace("\r\n", "\n");
			const string humanoidCamera =
				"_targetSource: {fileID: 0}\n" +
				"  _textureWidth: 500\n" +
				"  _textureHeight: 500\n" +
				"  _yaw: 0\n";
			Assert.That(scene, Does.Contain(humanoidCamera),
				"The lower Humanoid preview camera must face the front of a standard Unity avatar.");

			const string directCamera =
				"_targetSource: {fileID: 1147522662}\n" +
				"  _textureWidth: 500\n" +
				"  _textureHeight: 500\n" +
				"  _yaw: 181.39365\n";
			Assert.That(scene, Does.Contain(directCamera),
				"The upper Direct preview camera must keep the established SMPL-X orientation.");
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

		private static string GetYamlDocument(string yaml, long fileId)
		{
			string id = "&" + fileId + "\n";
			int idOffset = yaml.IndexOf(id, System.StringComparison.Ordinal);
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
	}
}
