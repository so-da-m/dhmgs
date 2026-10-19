using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal sealed class DhmgsImporterWindow : EditorWindow
	{
		private string _baseUrl;
		private string _outputRoot;
		private string _attemptIdText;
		private bool _bakeGeneric;
		private bool _bakeHumanoid;
		private bool _cachePreviewVideo;

		private string _status = "Enter an attempt ID and select Import.";
		private MessageType _statusType = MessageType.Info;
		private float _progress;
		private bool _running;
		private bool _canCancel;
		private bool _cancelRequested;
		private bool _terminal;
		private Guid _activeJobId;
		private long _activeRevision;
		private CancellationTokenSource _cancellation;
		private DhmgsImportJobResult _lastResult;

		[MenuItem("DHMGS/Importer")]
		private static void Open()
		{
			DhmgsImporterWindow window = GetWindow<DhmgsImporterWindow>();
			window.titleContent = new GUIContent("DHMGS Importer");
			window.minSize = new Vector2(440f, 430f);
			window.Show();
		}

		private void OnEnable()
		{
			DhmgsImporterSettings settings = DhmgsImporterSettings.instance;
			_baseUrl = settings.BaseUrl;
			_outputRoot = settings.OutputRoot;
			_attemptIdText = settings.AttemptIdText;
			_bakeGeneric = settings.BakeGeneric;
			_bakeHumanoid = settings.BakeHumanoid;
			_cachePreviewVideo = settings.CachePreviewVideo;
		}

		private void OnDisable()
		{
			_cancellation?.Cancel();
		}

		private void OnGUI()
		{
			DhmgsImportValidation validation;
			DhmgsImportJobRequest request;

			using (new EditorGUI.DisabledScope(_running))
			{
				using EditorGUI.ChangeCheckScope change = new EditorGUI.ChangeCheckScope();
				EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
				_baseUrl = EditorGUILayout.TextField("Base URL", _baseUrl);
				_outputRoot = EditorGUILayout.TextField("Output Root", _outputRoot);
				_attemptIdText = EditorGUILayout.TextField("Attempt ID", _attemptIdText);

				EditorGUILayout.Space();
				EditorGUILayout.LabelField("Animation Clip Baking", EditorStyles.boldLabel);
				_bakeGeneric = EditorGUILayout.Toggle("Bake Generic Clip", _bakeGeneric);
				_bakeHumanoid = EditorGUILayout.Toggle("Bake Humanoid Clips", _bakeHumanoid);
				_cachePreviewVideo = EditorGUILayout.Toggle(
					"Cache Preview Video If Present",
					_cachePreviewVideo);

				if (change.changed)
				{
					DhmgsImporterSettings.instance.Store(
						_baseUrl,
						_outputRoot,
						_attemptIdText,
						_bakeGeneric,
						_bakeHumanoid,
						_cachePreviewVideo);
				}
			}

			request = BuildRequest();
			validation = DhmgsImportService.Validate(request);
			DrawValidation(validation);
			if (!_running && DhmgsImportService.IsBusy)
			{
				EditorGUILayout.HelpBox(
					"Another DHMGS import is already running.",
					MessageType.Warning);
			}

			EditorGUILayout.Space();
			using (new EditorGUILayout.HorizontalScope())
			{
				bool canImport = !_running && !DhmgsImportService.IsBusy && validation.CanRun;
				using (new EditorGUI.DisabledScope(!canImport))
				{
					if (GUILayout.Button("Import", GUILayout.Height(28f)))
					{
						BeginImport(request);
					}
				}

				using (new EditorGUI.DisabledScope(!_running || !_canCancel || _cancelRequested))
				{
					if (GUILayout.Button("Cancel", GUILayout.Height(28f), GUILayout.Width(80f)))
					{
						RequestCancellation();
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
			DrawResultButtons();
		}

		private DhmgsImportJobRequest BuildRequest()
		{
			int attemptId = int.TryParse(_attemptIdText, out int parsedAttemptId)
				? parsedAttemptId
				: -1;
			DhmgsBakeOutputs outputs = DhmgsBakeOutputs.None;
			if (_bakeGeneric)
			{
				outputs |= DhmgsBakeOutputs.Generic;
			}
			if (_bakeHumanoid)
			{
				outputs |= DhmgsBakeOutputs.Humanoid;
			}

			return new DhmgsImportJobRequest(
				_baseUrl,
				attemptId,
				_outputRoot,
				outputs,
				_cachePreviewVideo ? DhmgsVideoPolicy.CacheIfPresent : DhmgsVideoPolicy.Ignore);
		}

		private async void BeginImport(DhmgsImportJobRequest request)
		{
			if (_running)
			{
				return;
			}

			CancellationTokenSource cancellation = new CancellationTokenSource();
			_cancellation = cancellation;
			_running = true;
			_canCancel = true;
			_cancelRequested = false;
			_terminal = false;
			_activeJobId = Guid.Empty;
			_activeRevision = 0;
			_progress = 0f;
			_lastResult = null;
			SetStatus("Starting import...", MessageType.Info);

			try
			{
				DhmgsImportJobResult result = await DhmgsImportService.ImportAsync(
					request,
					OnProgress,
					cancellation.Token);
				if (!OwnsJob(result.JobId))
				{
					return;
				}

				_terminal = true;
				_canCancel = false;
				_progress = 1f;
				_lastResult = result;
				SetStatus(BuildResultSummary(result), ResultMessageType(result.Diagnostics));
				SelectAsset(result.Motion);
			}
			catch (OperationCanceledException)
			{
				_terminal = true;
				_canCancel = false;
				if (this != null)
				{
					SetStatus("Import cancelled.", MessageType.Warning);
				}
			}
			catch (Exception exception)
			{
				_terminal = true;
				_canCancel = false;
				Debug.LogException(exception);
				if (this != null)
				{
					SetStatus(exception.Message, MessageType.Error);
				}
			}
			finally
			{
				cancellation.Dispose();
				if (ReferenceEquals(_cancellation, cancellation))
				{
					_cancellation = null;
					_running = false;
					_canCancel = false;
				}
				if (this != null)
				{
					Repaint();
				}
			}
		}

		private void RequestCancellation()
		{
			if (!_running || !_canCancel || _cancelRequested)
			{
				return;
			}

			_cancelRequested = true;
			_canCancel = false;
			SetStatus("Cancelling import...", MessageType.Warning);
			_cancellation?.Cancel();
		}

		private void OnProgress(DhmgsImportJobProgress progress)
		{
			if (!_running || _terminal || _cancelRequested)
			{
				return;
			}

			if (_activeJobId == Guid.Empty)
			{
				_activeJobId = progress.JobId;
				_activeRevision = progress.Revision;
			}
			if (progress.JobId != _activeJobId || progress.Revision != _activeRevision)
			{
				return;
			}

			_progress = progress.OverallProgress;
			_canCancel = progress.CanCancel;
			SetStatus(progress.Message, MessageType.Info);
		}

		private bool OwnsJob(Guid jobId)
		{
			return this != null && _running && _activeJobId != Guid.Empty && jobId == _activeJobId;
		}

		private void DrawValidation(DhmgsImportValidation validation)
		{
			if (_running || validation == null || validation.Diagnostics.Count == 0)
			{
				return;
			}

			StringBuilder message = new StringBuilder();
			MessageType type = MessageType.Info;
			foreach (DhmgsImportDiagnostic diagnostic in validation.Diagnostics)
			{
				if (message.Length > 0)
				{
					message.AppendLine();
				}
				message.Append(diagnostic.Message);
				type = Max(type, ToMessageType(diagnostic.Severity));
			}
			EditorGUILayout.HelpBox(message.ToString(), type);
		}

		private void DrawResultButtons()
		{
			if (_lastResult == null)
			{
				return;
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Imported Assets", EditorStyles.boldLabel);
			DrawAssetButton("Select Motion", _lastResult.Motion);
			DrawAssetButton("Select Generic Clip", _lastResult.GenericClip);
			DrawAssetButton("Select Humanoid In-Place Clip", _lastResult.HumanoidInPlaceClip);
			DrawAssetButton("Select Humanoid Root Motion Clip", _lastResult.HumanoidRootMotionClip);

			string folderPath = ImportFolderPath(_lastResult.MotionAssetPath);
			DefaultAsset folder = string.IsNullOrEmpty(folderPath)
				? null
				: AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
			DrawAssetButton("Select Import Folder", folder);
		}

		private static void DrawAssetButton(string label, UnityEngine.Object asset)
		{
			using (new EditorGUI.DisabledScope(asset == null))
			{
				if (GUILayout.Button(label))
				{
					SelectAsset(asset);
				}
			}
		}

		private static void SelectAsset(UnityEngine.Object asset)
		{
			if (asset == null)
			{
				return;
			}
			Selection.activeObject = asset;
			EditorGUIUtility.PingObject(asset);
		}

		private static string ImportFolderPath(string motionAssetPath)
		{
			return string.IsNullOrEmpty(motionAssetPath)
				? string.Empty
				: Path.GetDirectoryName(motionAssetPath)?.Replace('\\', '/');
		}

		private static string BuildResultSummary(DhmgsImportJobResult result)
		{
			StringBuilder summary = new StringBuilder();
			summary.Append('[').Append(result.Motion.attemptId).Append("] Saved ")
				.Append(result.Motion.frameCount).Append(" frames @ ")
				.Append(result.Motion.fps.ToString("F2")).AppendLine(" fps");
			summary.AppendLine(result.MotionAssetPath);
			AppendAssetPath(summary, "Generic", result.GenericClip);
			AppendAssetPath(summary, "Humanoid In-Place", result.HumanoidInPlaceClip);
			AppendAssetPath(summary, "Humanoid Root Motion", result.HumanoidRootMotionClip);
			if (!string.IsNullOrEmpty(result.SourceManifestPath))
			{
				summary.Append("Source cache: ").AppendLine(Path.GetDirectoryName(result.SourceManifestPath));
			}
			if (!string.IsNullOrEmpty(result.PreviewVideoPath))
			{
				summary.Append("Preview video: ").AppendLine(result.PreviewVideoPath);
			}
			summary.Append("Hand offset max diff: L=")
				.Append(result.LeftHandOffsetMaxDiff.ToString("F4"))
				.Append(", R=")
				.Append(result.RightHandOffsetMaxDiff.ToString("F4"));

			foreach (DhmgsImportDiagnostic diagnostic in result.Diagnostics)
			{
				summary.AppendLine().Append('[').Append(diagnostic.Severity).Append("] ")
					.Append(diagnostic.Message);
			}
			return summary.ToString();
		}

		private static void AppendAssetPath(StringBuilder summary, string label, UnityEngine.Object asset)
		{
			if (asset != null)
			{
				summary.Append(label).Append(": ").AppendLine(AssetDatabase.GetAssetPath(asset));
			}
		}

		private static MessageType ResultMessageType(IReadOnlyList<DhmgsImportDiagnostic> diagnostics)
		{
			MessageType type = MessageType.Info;
			foreach (DhmgsImportDiagnostic diagnostic in diagnostics)
			{
				type = Max(type, ToMessageType(diagnostic.Severity));
			}
			return type;
		}

		private static MessageType ToMessageType(DhmgsImportSeverity severity)
		{
			return severity switch
			{
				DhmgsImportSeverity.Warning => MessageType.Warning,
				DhmgsImportSeverity.Error => MessageType.Error,
				_ => MessageType.Info,
			};
		}

		private static MessageType Max(MessageType left, MessageType right)
		{
			return (MessageType)Mathf.Max((int)left, (int)right);
		}

		private void SetStatus(string message, MessageType type)
		{
			_status = string.IsNullOrEmpty(message) ? " " : message;
			_statusType = type;
			if (this != null)
			{
				Repaint();
			}
		}
	}
}
