using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal static class DhmgsProjectValidator
	{
		private const string HumanoidLayerName = "DhmgsHumanoid";
		private const int FirstUserLayerIndex = 8;

		[MenuItem("DHMGS/Project/Validate")]
		private static void Validate()
		{
			bool valid = true;
			if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
			{
				valid = false;
				Debug.LogWarning("[DHMGS] The current HTTP endpoint requires Insecure HTTP to be allowed. Use DHMGS/Project/Allow Insecure HTTP when needed.");
			}

			if (AssetDatabase.FindAssets("t:TMP_Settings").Length == 0)
			{
				valid = false;
				Debug.LogWarning("[DHMGS] TMP Essential Resources are not imported. Import them only if the optional playback UI is used.");
			}

			if (!HasLayer(HumanoidLayerName))
			{
				valid = false;
				Debug.LogWarning("[DHMGS] The optional humanoid preview layer is missing. Use DHMGS/Project/Add Humanoid Preview Layer when needed.");
			}

			if (valid)
			{
				Debug.Log("[DHMGS] Project validation passed.");
			}
		}

		[MenuItem("DHMGS/Project/Allow Insecure HTTP")]
		private static void AllowInsecureHttp()
		{
			PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
			AssetDatabase.SaveAssets();
			Debug.Log("[DHMGS] Insecure HTTP is now allowed for this project.");
		}

		[MenuItem("DHMGS/Project/Add Humanoid Preview Layer")]
		private static void AddHumanoidLayer()
		{
			if (HasLayer(HumanoidLayerName))
			{
				Debug.Log($"[DHMGS] Layer '{HumanoidLayerName}' already exists.");
				return;
			}

			SerializedProperty layers = GetLayers();
			if (layers == null)
			{
				Debug.LogError("[DHMGS] ProjectSettings/TagManager.asset could not be read.");
				return;
			}

			for (int index = FirstUserLayerIndex; index < layers.arraySize; index++)
			{
				SerializedProperty slot = layers.GetArrayElementAtIndex(index);
				if (!string.IsNullOrEmpty(slot.stringValue))
				{
					continue;
				}

				slot.stringValue = HumanoidLayerName;
				layers.serializedObject.ApplyModifiedProperties();
				Debug.Log($"[DHMGS] Added layer '{HumanoidLayerName}' at index {index}.");
				return;
			}

			Debug.LogError($"[DHMGS] No empty user layer slot is available for '{HumanoidLayerName}'.");
		}

		private static bool HasLayer(string layerName)
		{
			SerializedProperty layers = GetLayers();
			if (layers == null)
			{
				return false;
			}

			for (int index = 0; index < layers.arraySize; index++)
			{
				if (layers.GetArrayElementAtIndex(index).stringValue == layerName)
				{
					return true;
				}
			}
			return false;
		}

		private static SerializedProperty GetLayers()
		{
			Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
			if (assets == null || assets.Length == 0)
			{
				return null;
			}

			SerializedObject tagManager = new SerializedObject(assets[0]);
			return tagManager.FindProperty("layers");
		}
	}
}
