using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	/// <summary>
	/// attempt_id로 ExAvatar 결과를 받아 Assets/Mnnh.Dhmgs/Content/Generated/{id}/ 에 SO + 클립2 + json을 굽는 에디터 창.
	/// 베이크 본체는 씬과 공유하는 DhmgsAssetBaker가 담당하고, 여기선 UI/프리팹 지정/검증만.
	/// (씬 단일 표면으로도 동일 베이크가 가능하지만, 플레이 모드 없이 굽고 싶을 때 쓰는 창.)
	/// </summary>
	public sealed class DhmgsWindow : EditorWindow
	{
		private const string BaseUrlPrefKey = "Dhmgs.BaseUrl";
		private const string DownloadVideoPrefKey = "Dhmgs.DownloadVideo";
		private const string BakeGenericPrefKey = "Dhmgs.BakeGeneric";
		private const string BakeHumanoidPrefKey = "Dhmgs.BakeHumanoid";
		private const string GenericModelPrefKey = "Dhmgs.GenericModelGuid";
		private const string HumanoidModelPrefKey = "Dhmgs.HumanoidModelGuid";

		private string _baseUrl = DhmgsBakeService.DefaultBaseUrl;
		private string _attemptIdText = string.Empty;
		private bool _downloadVideo = true;
		private bool _bakeGeneric = true;
		private bool _bakeHumanoid = true;
		private GameObject _genericModel;
		private GameObject _humanoidModel;

		private string _status = "attempt_id를 입력하고 Download & Bake를 누르세요.";
		private MessageType _statusType = MessageType.Info;
		private string _lastSavedAssetPath;

		private DhmgsEditorCoroutine _routine;
		private bool _running;

		[MenuItem("DHMGS/Importer")]
		private static void Open()
		{
			DhmgsWindow window = GetWindow<DhmgsWindow>();
			window.titleContent = new GUIContent("DHMGS Importer");
			window.minSize = new Vector2(380f, 320f);
			window.Show();
		}

		private void OnEnable()
		{
			_baseUrl = EditorPrefs.GetString(BaseUrlPrefKey, DhmgsBakeService.DefaultBaseUrl);
			_downloadVideo = EditorPrefs.GetBool(DownloadVideoPrefKey, true);
			_bakeGeneric = EditorPrefs.GetBool(BakeGenericPrefKey, true);
			_bakeHumanoid = EditorPrefs.GetBool(BakeHumanoidPrefKey, true);
			_genericModel = LoadModelPref(GenericModelPrefKey);
			_humanoidModel = LoadModelPref(HumanoidModelPrefKey);
		}

		private void OnDisable()
		{
			if (_routine != null)
			{
				_routine.Stop();
				_routine = null;
			}
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
			using (EditorGUI.ChangeCheckScope check = new EditorGUI.ChangeCheckScope())
			{
				_baseUrl = EditorGUILayout.TextField("Base URL", _baseUrl);
				if (check.changed)
				{
					EditorPrefs.SetString(BaseUrlPrefKey, _baseUrl);
				}
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Template (클립 베이크용)", EditorStyles.boldLabel);
			using (EditorGUI.ChangeCheckScope check = new EditorGUI.ChangeCheckScope())
			{
				_genericModel = (GameObject)EditorGUILayout.ObjectField("Generic 리그 (SMPL-X)", _genericModel, typeof(GameObject), false);
				if (check.changed)
				{
					SaveModelPref(GenericModelPrefKey, _genericModel);
				}
			}
			using (EditorGUI.ChangeCheckScope check = new EditorGUI.ChangeCheckScope())
			{
				_humanoidModel = (GameObject)EditorGUILayout.ObjectField("Humanoid 리그 (Rig=Humanoid)", _humanoidModel, typeof(GameObject), false);
				if (check.changed)
				{
					SaveModelPref(HumanoidModelPrefKey, _humanoidModel);
				}
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Attempt", EditorStyles.boldLabel);
			_attemptIdText = EditorGUILayout.TextField("attempt_id", _attemptIdText);
			BoolPref(ref _downloadVideo, "영상(mp4)도 받기", DownloadVideoPrefKey);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);
			BoolPref(ref _bakeGeneric, "Generic 클립", BakeGenericPrefKey);
			BoolPref(ref _bakeHumanoid, "Humanoid 클립 (In-Place + Root Motion)", BakeHumanoidPrefKey);

			EditorGUILayout.Space();
			using (new EditorGUI.DisabledScope(_running))
			{
				if (GUILayout.Button(_running ? "처리 중..." : "Download & Bake", GUILayout.Height(28f)))
				{
					BeginDownload();
				}
			}

			EditorGUILayout.Space();
			EditorGUILayout.HelpBox(_status, _statusType);

			if (!string.IsNullOrEmpty(_lastSavedAssetPath))
			{
				if (GUILayout.Button("저장된 SO 선택"))
				{
					Object asset = AssetDatabase.LoadAssetAtPath<Object>(_lastSavedAssetPath);
					if (asset != null)
					{
						Selection.activeObject = asset;
						EditorGUIUtility.PingObject(asset);
					}
				}
			}
		}

		private void BoolPref(ref bool value, string label, string key)
		{
			using (EditorGUI.ChangeCheckScope check = new EditorGUI.ChangeCheckScope())
			{
				value = EditorGUILayout.Toggle(label, value);
				if (check.changed)
				{
					EditorPrefs.SetBool(key, value);
				}
			}
		}

		private void BeginDownload()
		{
			if (_running)
			{
				return;
			}
			if (!int.TryParse(_attemptIdText, out int attemptId))
			{
				SetStatus("attempt_id를 정수로 입력하세요.", MessageType.Error);
				return;
			}
			if (string.IsNullOrWhiteSpace(_baseUrl))
			{
				SetStatus("Base URL이 비어 있습니다.", MessageType.Error);
				return;
			}

			_lastSavedAssetPath = null;
			SetStatus($"[{attemptId}] 다운로드 중...", MessageType.Info);
			_running = true;

			DhmgsBakeResult result = new DhmgsBakeResult();
			_routine = new DhmgsEditorCoroutine(
				DhmgsBakeService.DownloadAndBuild(_baseUrl, attemptId, DhmgsBakeService.DefaultTimeoutSec, result),
				() => OnBuilt(attemptId, result));
			_routine.Start();
		}

		private void OnBuilt(int attemptId, DhmgsBakeResult result)
		{
			_routine = null;
			_running = false;

			if (!result.ok)
			{
				SetStatus($"[{attemptId}] 실패: {result.error}", MessageType.Error);
				return;
			}

			// 영상(옵션) → persistentDataPath.
			string videoNote = string.Empty;
			if (_downloadVideo)
			{
				if (result.videoMp4 != null)
				{
					string videoPath = DhmgsBakeService.WriteVideo(attemptId, result.videoMp4);
					videoNote = $"\n영상: {videoPath}";
				}
				else
				{
					videoNote = "\n영상: 서버에 없음";
				}
			}

			// SO + 클립 + json (씬과 공유하는 베이커).
			_lastSavedAssetPath = DhmgsAssetBaker.Persist(result, attemptId, _genericModel, _humanoidModel, _bakeGeneric, _bakeHumanoid, out _, out string summary);

			// 손 offset 회귀 가드.
			float diffLeft = DhmgsHandMeanReference.MaxDiff(result.manifest.leftHandOffset, DhmgsHandMeanReference.LeftHandMean);
			float diffRight = DhmgsHandMeanReference.MaxDiff(result.manifest.rightHandOffset, DhmgsHandMeanReference.RightHandMean);
			string handNote = $"\n손 offset 대조(옛 상수와 maxDiff): L={diffLeft:F4}, R={diffRight:F4}";

			SetStatus(summary + videoNote + handNote, MessageType.Info);

			Selection.activeObject = result.asset;
			EditorGUIUtility.PingObject(result.asset);
		}

		private static GameObject LoadModelPref(string key)
		{
			string guid = EditorPrefs.GetString(key, string.Empty);
			if (string.IsNullOrEmpty(guid))
			{
				return null;
			}
			string path = AssetDatabase.GUIDToAssetPath(guid);
			return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
		}

		private static void SaveModelPref(string key, GameObject model)
		{
			string guid = string.Empty;
			if (model != null)
			{
				guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(model));
			}
			EditorPrefs.SetString(key, guid);
		}

		private void SetStatus(string message, MessageType type)
		{
			_status = message;
			_statusType = type;
			Repaint();
		}
	}
}
