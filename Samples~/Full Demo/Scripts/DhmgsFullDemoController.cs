using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.UI;

namespace Mnnh.Dhmgs.Samples
{
	public sealed class DhmgsFullDemoController : MonoBehaviour
	{
		private const int MaxLogLines = 12;

		[SerializeField] private DhmgsMotionPlayer _player;
		[SerializeField] private bool _loop = true;
		[SerializeField] private string _defaultBaseUrl = "http://192.168.1.128:8090";
		[SerializeField] private Camera _previewCamera;
		[SerializeField] private DhmgsFixedAvatarCamera _humanoidPreviewCamera;
		[SerializeField] private DhmgsFullDemoPlayback _playback;
		[SerializeField] private DhmgsFullDemoViewSettings _viewSettings;

		[Header("Fetch")]
		[SerializeField] private InputField _attemptIdInput;
		[SerializeField] private Button _fetchButton;
		[SerializeField] private Slider _importProgress;
		[SerializeField] private Text _serverLabel;
		[SerializeField] private Text _operationStatusLabel;
		[SerializeField] private Text _logLabel;

		private readonly List<string> _logLines = new List<string>();
		private CancellationTokenSource _importCancellation;
		private DhmgsSampleImportContext _importContext;

		private void Awake()
		{
			EnsureInitialized();
		}

		private void EnsureInitialized()
		{
			if (_playback == null)
			{
				_playback = GetComponent<DhmgsFullDemoPlayback>();
			}
			if (_viewSettings == null)
			{
				_viewSettings = GetComponent<DhmgsFullDemoViewSettings>();
			}
			if (_importContext == null)
			{
				_importContext = new DhmgsSampleImportContext(
					this,
					_player,
					_previewCamera,
					_humanoidPreviewCamera);
			}
		}

		private void OnEnable()
		{
			EnsureInitialized();
			_fetchButton?.onClick.AddListener(Fetch);
			DhmgsFullDemoImportGateway.AvailabilityChanged += OnImportAvailabilityChanged;
			_viewSettings?.SetImportContext(_importContext);
			if (_serverLabel != null)
			{
				_serverLabel.text = $"Server  {_defaultBaseUrl}";
			}
			ConfigureEditorControls();
		}

		private void OnDisable()
		{
			_fetchButton?.onClick.RemoveListener(Fetch);
			DhmgsFullDemoImportGateway.AvailabilityChanged -= OnImportAvailabilityChanged;
			CancellationTokenSource pendingImport = _importCancellation;
			_importCancellation = null;
			pendingImport?.Cancel();
			ReleasePlayback();
			_viewSettings?.SetImportContext(null);
			_viewSettings?.ResetMainCamera();
			SetFetchInteractable(false);
		}

		public async void Fetch()
		{
			await FetchAndPlayAsync();
		}

		private async Task FetchAndPlayAsync()
		{
			if (_importCancellation != null)
			{
				AppendLog("A Fetch operation is already running.");
				return;
			}
			if (!DhmgsFullDemoImportGateway.IsAvailable)
			{
				AppendLog("Fetch is available in the Unity Editor only.");
				return;
			}
			if (_attemptIdInput == null || !int.TryParse(_attemptIdInput.text, out int attemptId) || attemptId < 0)
			{
				AppendLog("[Error] Attempt ID must be a non-negative integer.");
				return;
			}

			if (!isActiveAndEnabled || _playback == null || !_playback.isActiveAndEnabled || _player == null)
			{
				AppendLog("[Error] An active playback session and player are required.");
				return;
			}

			using CancellationTokenSource cancellation = new CancellationTokenSource();
			_importCancellation = cancellation;
			Action<DhmgsSampleImportProgress> onProgress = value =>
			{
				if (OwnsOperation(cancellation))
				{
					OnImportProgress(value);
				}
			};

			try
			{
				_viewSettings?.ResetMainCamera();
				_playback.BeginLoad();
				SetFetchInteractable(false);
				SetProgress(0f, $"[{attemptId}] Downloading...");
				AppendLog($"[{attemptId}] Fetch started.");
				DhmgsSampleImportResult imported = await DhmgsFullDemoImportGateway.ImportAndSaveAsync(
					_importContext,
					_defaultBaseUrl,
					attemptId,
					onProgress,
					cancellation.Token);
				if (!OwnsOperation(cancellation))
				{
					return;
				}

				if (!_playback.LoadAndPlay(imported.Motion, imported.Clock ?? new DhmgsRealtimeClock(_loop)))
				{
					DhmgsFullDemoImportGateway.StopPreview(_importContext);
					SetProgress(0f, $"[{attemptId}] Imported but could not play.");
					return;
				}
				_viewSettings?.OnMotionLoaded();
				SetProgress(1f, string.Empty);
				AppendSummary(imported.Summary);
				AppendLog(imported.HasVideoPreview
					? $"[{attemptId}] Video preview synchronized."
					: $"[{attemptId}] No preview video; realtime clock in use.");
			}
			catch (OperationCanceledException)
			{
				if (OwnsOperation(cancellation, allowCancelled: true))
				{
					ReleasePlayback();
					SetProgress(0f, $"[{attemptId}] Fetch cancelled.");
					AppendLog($"[{attemptId}] Fetch cancelled.");
				}
			}
			catch (Exception exception)
			{
				if (OwnsOperation(cancellation, allowCancelled: true))
				{
					ReleasePlayback();
					Debug.LogException(exception, this);
					AppendLog($"[Error] [{attemptId}] {exception.Message}");
					SetProgress(0f, $"[{attemptId}] Failed.");
				}
			}
			finally
			{
				if (this != null && ReferenceEquals(_importCancellation, cancellation))
				{
					_importCancellation = null;
					SetFetchInteractable(isActiveAndEnabled && DhmgsFullDemoImportGateway.IsAvailable);
					_playback?.CancelLoad();
				}
			}
		}

		private bool OwnsOperation(
			CancellationTokenSource cancellation,
			bool allowCancelled = false)
		{
			return this != null && isActiveAndEnabled &&
				ReferenceEquals(_importCancellation, cancellation) &&
				(allowCancelled || !cancellation.IsCancellationRequested);
		}

		private void ReleasePlayback()
		{
			_playback?.Clear();
			DhmgsFullDemoImportGateway.StopPreview(_importContext);
		}

		private void OnImportAvailabilityChanged()
		{
			_viewSettings?.SetImportContext(_importContext);
			if (_importCancellation == null)
			{
				ConfigureEditorControls();
			}
		}

		private void ConfigureEditorControls()
		{
			bool available = DhmgsFullDemoImportGateway.IsAvailable;
			SetFetchInteractable(available);
			SetProgress(0f, available
				? string.Empty
				: "Fetch, baking, and video preview are Editor-only.");
		}

		private void SetFetchInteractable(bool interactable)
		{
			if (_fetchButton != null)
			{
				_fetchButton.interactable = interactable;
			}
			if (_attemptIdInput != null)
			{
				_attemptIdInput.interactable = interactable;
			}
		}

		private void OnImportProgress(DhmgsSampleImportProgress progress)
		{
			SetProgress(progress.Value, progress.Status);
		}

		private void SetProgress(float value, string status)
		{
			if (_importProgress != null)
			{
				_importProgress.SetValueWithoutNotify(Mathf.Clamp01(value));
			}
			if (_operationStatusLabel != null)
			{
				_operationStatusLabel.text = status;
			}
		}

		private void AppendSummary(string summary)
		{
			if (string.IsNullOrWhiteSpace(summary))
			{
				return;
			}

			string[] lines = summary.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string line in lines)
			{
				AppendLog(line);
			}
		}

		private void AppendLog(string message)
		{
			if (string.IsNullOrWhiteSpace(message))
			{
				return;
			}

			_logLines.Insert(0, message.Trim());
			if (_logLines.Count > MaxLogLines)
			{
				_logLines.RemoveRange(MaxLogLines, _logLines.Count - MaxLogLines);
			}
			if (_logLabel != null)
			{
				_logLabel.text = string.Join("\n", _logLines);
			}
		}
	}
}
