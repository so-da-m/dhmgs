using System;
using System.Collections.Generic;
using System.Threading;

using Mnnh.Dhmgs;

using UnityEngine;
using UnityEngine.UI;

namespace Mnnh.Dhmgs.Samples
{
	/// <summary>
	/// Compact Canvas surface matching the original DHMGS workflow: Attempt ID Fetch,
	/// rig/clothes selection, opacity, speed, video opacity, and newest-first logging.
	/// Downloading and baking are supplied by an Editor-only gateway.
	/// </summary>
	public sealed class DhmgsFullDemoController : MonoBehaviour
	{
		private const int MaxLogLines = 12;

		[SerializeField] private DhmgsMotionPlayer _player;
		[SerializeField] private DhmgsMotionAsset _bundledMotion;
		[SerializeField] private bool _playOnStart = true;
		[SerializeField] private bool _loop = true;
		[SerializeField] private string _defaultBaseUrl = "http://192.168.1.128:8090";
		[SerializeField] private Camera _previewCamera;
		[SerializeField] private DhmgsFixedAvatarCamera _directPreviewCamera;
		[SerializeField] private DhmgsFixedAvatarCamera _humanoidPreviewCamera;

		[Header("Fetch")]
		[SerializeField] private InputField _attemptIdInput;
		[SerializeField] private Button _fetchButton;
		[SerializeField] private Slider _importProgress;
		[SerializeField] private Text _serverLabel;
		[SerializeField] private Text _operationStatusLabel;
		[SerializeField] private Text _logLabel;

		[Header("Playback Controls")]
		[SerializeField] private Dropdown _avatarDropdown;
		[SerializeField] private Dropdown _clothesDropdown;
		[SerializeField] private Slider _avatarOpacitySlider;
		[SerializeField] private Slider _clothesOpacitySlider;
		[SerializeField] private Slider _videoOpacitySlider;
		[SerializeField] private Slider _speedSlider;
		[SerializeField] private Text _avatarOpacityValue;
		[SerializeField] private Text _clothesOpacityValue;
		[SerializeField] private Text _videoOpacityValue;
		[SerializeField] private Text _speedValue;
		[SerializeField] private Text _playbackStatusLabel;

		private readonly List<string> _logLines = new List<string>();
		private DhmgsMotionAsset _activeMotion;
		private bool _importing;
		private CancellationTokenSource _importCancellation;
		private DhmgsSampleImportContext _importContext;
		private int _operationVersion;

		private void Awake()
		{
			_importContext = new DhmgsSampleImportContext(
				this,
				_player,
				_previewCamera,
				_humanoidPreviewCamera);
		}

		private void OnEnable()
		{
			_fetchButton?.onClick.AddListener(Fetch);
			_avatarDropdown?.onValueChanged.AddListener(OnAvatarChanged);
			_clothesDropdown?.onValueChanged.AddListener(OnClothesChanged);
			_avatarOpacitySlider?.onValueChanged.AddListener(OnAvatarOpacityChanged);
			_clothesOpacitySlider?.onValueChanged.AddListener(OnClothesOpacityChanged);
			_videoOpacitySlider?.onValueChanged.AddListener(OnVideoOpacityChanged);
			_speedSlider?.onValueChanged.AddListener(OnSpeedChanged);
		}

		private void Start()
		{
			_activeMotion = _bundledMotion;
			if (_serverLabel != null)
			{
				_serverLabel.text = $"Server  {_defaultBaseUrl}";
			}

			BindSelectors();
			RefreshControlValues();
			ConfigureEditorControls();
			if (_playOnStart && _activeMotion != null &&
				Play(_activeMotion, new DhmgsRealtimeClock(_loop)))
			{
				AppendLog($"Bundled motion {_activeMotion.attemptId} is playing.");
			}
		}

		private void Update()
		{
			if (_playbackStatusLabel == null)
			{
				return;
			}

			if (_player == null || _activeMotion == null)
			{
				_playbackStatusLabel.text = "No motion assigned";
				return;
			}

			string state = _player.IsPlaying ? "Playing" : "Stopped";
			_playbackStatusLabel.text =
				$"{state}  |  {_activeMotion.attemptId}  |  Frame {_player.CurrentFrame + 1}/{_activeMotion.frameCount}";
		}

		private void OnDisable()
		{
			_fetchButton?.onClick.RemoveListener(Fetch);
			_avatarDropdown?.onValueChanged.RemoveListener(OnAvatarChanged);
			_clothesDropdown?.onValueChanged.RemoveListener(OnClothesChanged);
			_avatarOpacitySlider?.onValueChanged.RemoveListener(OnAvatarOpacityChanged);
			_clothesOpacitySlider?.onValueChanged.RemoveListener(OnClothesOpacityChanged);
			_videoOpacitySlider?.onValueChanged.RemoveListener(OnVideoOpacityChanged);
			_speedSlider?.onValueChanged.RemoveListener(OnSpeedChanged);
			_operationVersion++;
			_importing = false;
			_importCancellation?.Cancel();
			_importCancellation = null;
			DhmgsFullDemoImportGateway.StopPreview(_importContext);
		}

		public void Fetch()
		{
			FetchAndPlay();
		}

		private async void FetchAndPlay()
		{
			if (_importing)
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

			int operation = ++_operationVersion;
			CancellationTokenSource cancellation = new CancellationTokenSource();
			_importCancellation = cancellation;
			_importing = true;
			SetFetchInteractable(false);
			SetProgress(0f, $"[{attemptId}] Downloading...");
			AppendLog($"[{attemptId}] Fetch started.");
			IProgress<DhmgsSampleImportProgress> progress =
				new Progress<DhmgsSampleImportProgress>(value =>
				{
					if (OwnsOperation(operation, cancellation))
					{
						OnImportProgress(value);
					}
				});

			try
			{
				DhmgsSampleImportResult imported = await DhmgsFullDemoImportGateway.ImportAndSaveAsync(
					_importContext,
					_defaultBaseUrl,
					attemptId,
					progress,
					cancellation.Token);
				if (!OwnsOperation(operation, cancellation))
				{
					return;
				}

				_activeMotion = imported.Motion;
				if (!Play(imported.Motion, imported.Clock ?? new DhmgsRealtimeClock(_loop)))
				{
					SetProgress(0f, $"[{attemptId}] Imported but could not play.");
					return;
				}
				SetProgress(1f, $"[{attemptId}] Imported, baked, and playing.");
				AppendSummary(imported.Summary);
				AppendLog(imported.HasVideoPreview
					? $"[{attemptId}] Video preview synchronized."
					: $"[{attemptId}] No preview video; realtime clock in use.");
			}
			catch (OperationCanceledException)
			{
				if (OwnsOperation(operation, cancellation, allowCancelled: true))
				{
					AppendLog($"[{attemptId}] Fetch cancelled.");
				}
			}
			catch (Exception exception)
			{
				if (OwnsOperation(operation, cancellation, allowCancelled: true))
				{
					Debug.LogException(exception, this);
					AppendLog($"[Error] [{attemptId}] {exception.Message}");
					SetProgress(0f, $"[{attemptId}] Failed.");
				}
			}
			finally
			{
				cancellation.Dispose();
				if (this != null && operation == _operationVersion &&
					ReferenceEquals(_importCancellation, cancellation))
				{
					_importing = false;
					_importCancellation = null;
					SetFetchInteractable(DhmgsFullDemoImportGateway.IsAvailable);
				}
			}
		}

		private bool OwnsOperation(
			int operation,
			CancellationTokenSource cancellation,
			bool allowCancelled = false)
		{
			return this != null && isActiveAndEnabled && operation == _operationVersion &&
				ReferenceEquals(_importCancellation, cancellation) &&
				(allowCancelled || !cancellation.IsCancellationRequested);
		}

		private bool Play(DhmgsMotionAsset motion, IDhmgsPlaybackClock clock)
		{
			if (_player == null || motion == null || !motion.IsValid)
			{
				AppendLog("[Error] A valid motion and player are required.");
				return false;
			}

			_player.Play(motion, clock);
			RelockPreviewCameras();
			OnSpeedChanged(_speedSlider != null ? _speedSlider.value : 1f);
			return _player.IsPlaying;
		}

		private void BindSelectors()
		{
			if (_player == null)
			{
				return;
			}

			BindDropdown(_avatarDropdown, _player.AvatarCount, _player.AvatarName, _player.ActiveAvatar);
			BindDropdown(_clothesDropdown, _player.ClothesCount, _player.ClothesName, _player.ActiveClothes);
			if (_clothesDropdown != null)
			{
				_clothesDropdown.interactable = _player.ClothesCount > 0;
			}
			if (_clothesOpacitySlider != null)
			{
				_clothesOpacitySlider.interactable = _player.ClothesCount > 0;
			}
		}

		private static void BindDropdown(
			Dropdown dropdown,
			int count,
			Func<int, string> nameAt,
			int activeIndex)
		{
			if (dropdown == null)
			{
				return;
			}

			List<string> options = new List<string> { "None" };
			for (int index = 0; index < count; index++)
			{
				options.Add(nameAt(index));
			}
			dropdown.ClearOptions();
			dropdown.AddOptions(options);
			dropdown.SetValueWithoutNotify(Mathf.Clamp(activeIndex + 1, 0, options.Count - 1));
			dropdown.RefreshShownValue();
		}

		private void OnAvatarChanged(int value)
		{
			_player?.SetAvatarIndex(value - 1);
			RelockPreviewCameras();
			OnAvatarOpacityChanged(_avatarOpacitySlider != null ? _avatarOpacitySlider.value : 1f);
		}

		private void RelockPreviewCameras()
		{
			_directPreviewCamera?.Relock();
			_humanoidPreviewCamera?.Relock();
		}

		private void OnClothesChanged(int value)
		{
			_player?.SetClothesIndex(value - 1);
			_directPreviewCamera?.Relock();
			OnClothesOpacityChanged(_clothesOpacitySlider != null ? _clothesOpacitySlider.value : 1f);
		}

		private void OnAvatarOpacityChanged(float value)
		{
			ApplyOpacity(_player?.ActiveAvatarDriver, value);
			SetValue(_avatarOpacityValue, value);
		}

		private void OnClothesOpacityChanged(float value)
		{
			ApplyOpacity(_player?.ActiveClothesDriver, value);
			SetValue(_clothesOpacityValue, value);
		}

		private void OnVideoOpacityChanged(float value)
		{
			DhmgsFullDemoImportGateway.SetVideoOpacity(_importContext, value);
			SetValue(_videoOpacityValue, value);
		}

		private void OnSpeedChanged(float value)
		{
			float speed = Mathf.Clamp(value, 0.25f, 2f);
			_player?.SetPlaybackSpeed(speed);
			if (_speedValue != null)
			{
				_speedValue.text = $"{speed:F2}x";
			}
		}

		private static void ApplyOpacity(DhmgsRigDriver driver, float value)
		{
			DhmgsSkinOpacity opacity = driver != null ? driver.GetComponent<DhmgsSkinOpacity>() : null;
			opacity?.SetOpacity(value);
		}

		private static void SetValue(Text label, float value)
		{
			if (label != null)
			{
				label.text = value.ToString("F2");
			}
		}

		private void RefreshControlValues()
		{
			OnAvatarOpacityChanged(_avatarOpacitySlider != null ? _avatarOpacitySlider.value : 1f);
			OnClothesOpacityChanged(_clothesOpacitySlider != null ? _clothesOpacitySlider.value : 1f);
			OnVideoOpacityChanged(_videoOpacitySlider != null ? _videoOpacitySlider.value : 1f);
			OnSpeedChanged(_speedSlider != null ? _speedSlider.value : 1f);
		}

		private void ConfigureEditorControls()
		{
			bool available = DhmgsFullDemoImportGateway.IsAvailable;
			SetFetchInteractable(available);
			if (_videoOpacitySlider != null)
			{
				_videoOpacitySlider.interactable =
					available && DhmgsFullDemoImportGateway.SupportsVideoPreview(_importContext);
			}
			SetProgress(0f, available
				? "Ready. Enter an Attempt ID and Fetch."
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
