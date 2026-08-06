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
