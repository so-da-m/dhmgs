using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace Mnnh.Dhmgs.Samples
{
	public sealed class DhmgsFullDemoController : MonoBehaviour
	{
		private const int MaxLogLines = 12;

		[SerializeField] private string _defaultBaseUrl = "http://192.168.1.128:8090";
		[SerializeField] private DhmgsFullDemoPlayback _playback;

		private readonly List<string> _logLines = new List<string>();
		private CancellationTokenSource _importCancellation;

		public event Action Changed;

		public string BaseUrl => _defaultBaseUrl;
		public string Status { get; private set; } = string.Empty;
		public string Log { get; private set; } = string.Empty;
		public float Progress { get; private set; }
		public bool IsImporting => _importCancellation != null;
		public bool CanFetch => isActiveAndEnabled && !IsImporting &&
			DhmgsFullDemoImportGateway.IsAvailable && _playback != null && _playback.CanLoad;

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
		}

		private void OnEnable()
		{
			EnsureInitialized();
			DhmgsFullDemoImportGateway.AvailabilityChanged += OnImportAvailabilityChanged;
			RefreshAvailability();
		}

		private void OnDisable()
		{
			DhmgsFullDemoImportGateway.AvailabilityChanged -= OnImportAvailabilityChanged;
			CancellationTokenSource pendingImport = _importCancellation;
			_importCancellation = null;
			pendingImport?.Cancel();
			Changed?.Invoke();
		}

		public void CancelImport()
		{
			_importCancellation?.Cancel();
		}

		public async Task FetchAsync(int attemptId)
		{
			EnsureInitialized();
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
			if (attemptId < 0)
			{
				AppendLog("[Error] Attempt ID must be a non-negative integer.");
				return;
			}

			if (!isActiveAndEnabled || _playback == null || !_playback.CanLoad)
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
				_playback.Clear();
				SetProgress(0f, $"[{attemptId}] Downloading...");
				AppendLog($"[{attemptId}] Fetch started.");
				DhmgsSampleImportResult imported = await DhmgsFullDemoImportGateway.ImportAndSaveAsync(
					_defaultBaseUrl,
					attemptId,
					onProgress,
					cancellation.Token);
				cancellation.Token.ThrowIfCancellationRequested();
				if (!OwnsOperation(cancellation))
				{
					return;
				}

				SetProgress(0.85f, $"[{attemptId}] Preparing playback...");
				bool loaded = await _playback.LoadAndPlayAsync(imported, cancellation.Token);
				cancellation.Token.ThrowIfCancellationRequested();
				if (!OwnsOperation(cancellation))
				{
					return;
				}
				if (!loaded)
				{
					SetProgress(0f, $"[{attemptId}] Imported but could not play.");
					return;
				}
				SetProgress(1f, string.Empty);
				AppendSummary(imported.Summary);
				AppendLog(_playback.HasVideoPreview
					? $"[{attemptId}] Video preview synchronized."
					: $"[{attemptId}] No preview video; realtime clock in use.");
			}
			catch (OperationCanceledException)
			{
				if (OwnsOperation(cancellation, allowCancelled: true))
				{
					SetProgress(0f, $"[{attemptId}] Fetch cancelled.");
					AppendLog($"[{attemptId}] Fetch cancelled.");
				}
			}
			catch (Exception exception)
			{
				if (OwnsOperation(cancellation, allowCancelled: true))
				{
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
					Changed?.Invoke();
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

		private void OnImportAvailabilityChanged()
		{
			if (_importCancellation == null)
			{
				RefreshAvailability();
			}
		}

		private void RefreshAvailability()
		{
			bool available = DhmgsFullDemoImportGateway.IsAvailable;
			SetProgress(0f, available
				? string.Empty
				: "Fetch, baking, and video preview are Editor-only.");
		}

		private void OnImportProgress(DhmgsSampleImportProgress progress)
		{
			SetProgress(progress.Value * 0.85f, progress.Status);
		}

		private void SetProgress(float value, string status)
		{
			Progress = Mathf.Clamp01(value);
			Status = status ?? string.Empty;
			Changed?.Invoke();
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
			Log = string.Join("\n", _logLines);
			Changed?.Invoke();
		}
	}
}
