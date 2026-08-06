using System.IO;
using UnityEditor;
using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	/// <summary>
	/// DHMGS 패키지를 임포트했을 때 필요한 프로젝트 환경을 자동으로 맞춘다.
	/// - TMP 필수 리소스(폰트/셰이더/설정) 임포트
	/// - HTTP 통신을 위한 Insecure HTTP 옵션 허용
	/// - 휴머노이드 프리뷰 전용 레이어 생성
	/// 도메인 리로드마다 실행되지만, 각 단계가 이미 처리된 경우에는 아무 작업도 하지 않는다.
	/// </summary>
	[InitializeOnLoad]
	internal static class DhmgsProjectBootstrap
	{
		private const string TmpEssentialsRelativePath = "Package Resources/TMP Essential Resources.unitypackage";
		private const string HumanoidLayerName = "DhmgsHumanoid";
		private const int FirstUserLayerIndex = 8;

		static DhmgsProjectBootstrap()
		{
			// 에셋 데이터베이스가 준비된 뒤에 실행되도록 한 프레임 미룬다.
			EditorApplication.delayCall += Run;
		}

		private static void Run()
		{
			EnsureInsecureHttpAllowed();
			EnsureTmpEssentialsImported();
			EnsureHumanoidLayerExists();
		}

		private static void EnsureInsecureHttpAllowed()
		{
			if (PlayerSettings.insecureHttpOption == InsecureHttpOption.AlwaysAllowed)
			{
				return;
			}

			PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
			AssetDatabase.SaveAssets();
			Debug.Log("[DHMGS] HTTP 통신을 허용하도록 Insecure HTTP 옵션을 켰습니다.");
		}

		private static void EnsureTmpEssentialsImported()
		{
			bool alreadyImported = AssetDatabase.FindAssets("t:TMP_Settings").Length > 0;
			if (alreadyImported)
			{
				return;
			}

			string essentialsPackagePath = FindTmpEssentialsPackagePath();
			if (essentialsPackagePath == null)
			{
				Debug.LogWarning("[DHMGS] TMP 필수 리소스 패키지를 찾지 못했습니다. Window > TextMeshPro > Import TMP Essential Resources 로 직접 임포트해 주세요.");
				return;
			}

			AssetDatabase.ImportPackage(essentialsPackagePath, false);
			Debug.Log("[DHMGS] TMP 필수 리소스를 임포트했습니다.");
		}

		private static void EnsureHumanoidLayerExists()
		{
			Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
			if (tagManagerAssets == null || tagManagerAssets.Length == 0)
			{
				return;
			}

			SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
			SerializedProperty layers = tagManager.FindProperty("layers");
			if (layers == null || !layers.isArray)
			{
				return;
			}

			for (int i = 0; i < layers.arraySize; i++)
			{
				if (layers.GetArrayElementAtIndex(i).stringValue == HumanoidLayerName)
				{
					return;
				}
			}

			for (int i = FirstUserLayerIndex; i < layers.arraySize; i++)
			{
				SerializedProperty slot = layers.GetArrayElementAtIndex(i);
				if (string.IsNullOrEmpty(slot.stringValue))
				{
					slot.stringValue = HumanoidLayerName;
					tagManager.ApplyModifiedProperties();
					Debug.Log($"[DHMGS] '{HumanoidLayerName}' 레이어를 {i}번 슬롯에 추가했습니다.");
					return;
				}
			}

			Debug.LogWarning($"[DHMGS] 사용자 레이어 슬롯이 가득 차 '{HumanoidLayerName}' 레이어를 추가하지 못했습니다. 수동으로 추가해 주세요.");
		}

		private static string FindTmpEssentialsPackagePath()
		{
			UnityEditor.PackageManager.PackageInfo tmpPackage =
				UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMPro.TMP_Settings).Assembly);
			if (tmpPackage == null)
			{
				return null;
			}

			string fullPath = Path.Combine(tmpPackage.resolvedPath, TmpEssentialsRelativePath);
			return File.Exists(fullPath) ? fullPath : null;
		}
	}
}
