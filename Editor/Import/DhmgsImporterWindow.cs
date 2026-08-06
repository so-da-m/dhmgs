using System;
using System.Threading;

using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	public sealed class DhmgsImporterWindow : EditorWindow
	{
		private const string BaseUrlPrefKey = "Dhmgs.Importer.BaseUrl";
		private const string OutputRootPrefKey = "Dhmgs.Importer.OutputRoot";
		private const string SaveVideoPrefKey = "Dhmgs.Importer.SavePreviewVideo";
		private const string BakeGenericPrefKey = "Dhmgs.Importer.BakeGeneric";
		private const string BakeHumanoidPrefKey = "Dhmgs.Importer.BakeHumanoid";
		private const string GenericRigPrefKey = "Dhmgs.Importer.GenericRigGuid";
		private const string HumanoidRigPrefKey = "Dhmgs.Importer.HumanoidRigGuid";

		private string _baseUrl;
		private string _outputRoot;
		private string _attemptIdText = string.Empty;
		private bool _savePreviewVideo;
		private bool _bakeGeneric;
		private bool _bakeHumanoid;
		private GameObject _genericRig;
		private GameObject _humanoidRig;

		private string _status = "Enter an attempt ID and select Import.";
		private MessageType _statusType = MessageType.Info;
		private string _lastSavedAssetPath;
		private float _progress;
		private bool _running;
		private CancellationTokenSource _cancellation;

		[MenuItem("DHMGS/Importer")]
		private static void Open()
		{
			DhmgsImporterWindow window = GetWindow<DhmgsImporterWindow>();
			window.titleContent = new GUIContent("DHMGS Importer");
			window.minSize = new Vector2(420f, 390f);
			window.Show();
		}

		private void OnEnable()
		{
			_baseUrl = EditorPrefs.GetString(BaseUrlPrefKey, DhmgsImportPipeline.DefaultBaseUrl);
			_outputRoot = EditorPrefs.GetString(OutputRootPrefKey, DhmgsAssetWriter.DefaultOutputRoot);
			_savePreviewVideo = EditorPrefs.GetBool(SaveVideoPrefKey, false);
			_bakeGeneric = EditorPrefs.GetBool(BakeGenericPrefKey, true);
			_bakeHumanoid = EditorPrefs.GetBool(BakeHumanoidPrefKey, true);
			_genericRig = LoadRigPref(GenericRigPrefKey);
			_humanoidRig = LoadRigPref(HumanoidRigPrefKey);
		}

		private void OnDisable()
		{
			_cancellation?.Cancel();
			_cancellation?.Dispose();
			_cancellation = null;
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
			StringPref(ref _baseUrl, "Base URL", BaseUrlPrefKey);
			StringPref(ref _outputRoot, "Output Root", OutputRootPrefKey);
			_attemptIdText = EditorGUILayout.TextField("Attempt ID", _attemptIdText);
			BoolPref(ref _savePreviewVideo, "Save preview video outside Assets", SaveVideoPrefKey);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Animation Clip Baking", EditorStyles.boldLabel);
			RigPref(ref _genericRig, "Generic SMPL-X Rig", GenericRigPrefKey);
			RigPref(ref _humanoidRig, "Humanoid Rig", HumanoidRigPrefKey);
			BoolPref(ref _bakeGeneric, "Bake Generic Clip", BakeGenericPrefKey);
			BoolPref(ref _bakeHumanoid, "Bake Humanoid Clips", BakeHumanoidPrefKey);

			EditorGUILayout.Space();
			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(_running))
				{
					if (GUILayout.Button("Import", GUILayout.Height(28f)))
					{
						BeginImport();
					}
				}

				using (new EditorGUI.DisabledScope(!_running))
				{
					if (GUILayout.Button("Cancel", GUILayout.Height(28f), GUILayout.Width(80f)))
					{
						_cancellation?.Cancel();
					}
				}
			}

			if (_running)
			{
				Rect rect = EditorGUILayout.GetControlRect(false, 18f);
				EditorGUI.ProgressBar(rect, _progress, $"{_progress:P0}");
			}

			EditorGUILayout.Space();
			EditorGUILayout.HelpBox(_status, _statusType);

			if (!string.IsNullOrEmpty(_lastSavedAssetPath) && GUILayout.Button("Select Imported Motion"))
			{
				UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_lastSavedAssetPath);
				Selection.activeObject = asset;
				EditorGUIUtility.PingObject(asset);
			}
		}

		private async void BeginImport()
		{
			if (_running)
			{
				return;
			}
			if (!int.TryParse(_attemptIdText, out int attemptId) || attemptId < 0)
			{
				SetStatus("Attempt ID must be a non-negative integer.", MessageType.Error);
				return;
			}

			_running = true;
			_progress = 0f;
			_lastSavedAssetPath = null;
			_cancellation = new CancellationTokenSource();
			IProgress<DhmgsImportProgress> progress = new Progress<DhmgsImportProgress>(OnProgress);

			try
			{
				DhmgsImportResult import = await DhmgsImportPipeline.ImportAsync(
					new DhmgsImportRequest(_baseUrl, attemptId),
					progress,
					_cancellation.Token);

				DhmgsAssetWriteResult written = DhmgsAssetWriter.Write(
					import,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = _outputRoot,
						GenericRig = _genericRig,
						HumanoidRig = _humanoidRig,
						BakeGenericClip = _bakeGeneric,
						BakeHumanoidClips = _bakeHumanoid,
					},
					progress);

				string videoNote = string.Empty;
				if (_savePreviewVideo && import.VideoBytes != null)
				{
					videoNote = $"\nPreview video: {DhmgsImportPipeline.WritePreviewVideo(import)}";
				}

				float leftDiff = DhmgsHandMeanReference.MaxDiff(import.Manifest.LeftHandOffset, DhmgsHandMeanReference.LeftHandMean);
				float rightDiff = DhmgsHandMeanReference.MaxDiff(import.Manifest.RightHandOffset, DhmgsHandMeanReference.RightHandMean);
				_lastSavedAssetPath = written.MotionAssetPath;
				SetStatus($"{written.Summary}{videoNote}\nHand offset max diff: L={leftDiff:F4}, R={rightDiff:F4}", MessageType.Info);

				Selection.activeObject = import.Motion;
				EditorGUIUtility.PingObject(import.Motion);
			}
			catch (OperationCanceledException)
			{
				SetStatus("Import cancelled.", MessageType.Warning);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
				SetStatus(exception.Message, MessageType.Error);
			}
			finally
			{
				_running = false;
				_progress = 0f;
				_cancellation?.Dispose();
				_cancellation = null;
				Repaint();
			}
		}

		private void OnProgress(DhmgsImportProgress progress)
		{
			_progress = progress.Stage switch
			{
				DhmgsImportStage.Downloading => progress.Value * 0.65f,
				DhmgsImportStage.Extracting => 0.65f + progress.Value * 0.1f,
				DhmgsImportStage.Decoding => 0.75f + progress.Value * 0.1f,
				DhmgsImportStage.WritingAssets => 0.85f + progress.Value * 0.15f,
				_ => progress.Value,
			};
			_status = progress.Stage.ToString();
			_statusType = MessageType.Info;
			Repaint();
		}

		private static GameObject LoadRigPref(string key)
		{
			string guid = EditorPrefs.GetString(key, string.Empty);
			string path = string.IsNullOrEmpty(guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(guid);
			return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
		}

		private static void SaveRigPref(string key, GameObject rig)
		{
			string path = rig != null ? AssetDatabase.GetAssetPath(rig) : string.Empty;
			EditorPrefs.SetString(key, string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
		}

		private static void StringPref(ref string value, string label, string key)
		{
			using EditorGUI.ChangeCheckScope check = new EditorGUI.ChangeCheckScope();
			value = EditorGUILayout.TextField(label, value);
			if (check.changed)
			{
				EditorPrefs.SetString(key, value);
			}
		}

		private static void BoolPref(ref bool value, string label, string key)
		{
			using EditorGUI.ChangeCheckScope check = new EditorGUI.ChangeCheckScope();
			value = EditorGUILayout.Toggle(label, value);
			if (check.changed)
			{
				EditorPrefs.SetBool(key, value);
			}
		}

		private static void RigPref(ref GameObject value, string label, string key)
		{
			using EditorGUI.ChangeCheckScope check = new EditorGUI.ChangeCheckScope();
			value = (GameObject)EditorGUILayout.ObjectField(label, value, typeof(GameObject), false);
			if (check.changed)
			{
				SaveRigPref(key, value);
			}
		}

		private void SetStatus(string message, MessageType type)
		{
			_status = message;
			_statusType = type;
			Repaint();
		}
	}
}
