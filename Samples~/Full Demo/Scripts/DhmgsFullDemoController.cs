using System;
using System.Threading;

using Mnnh.Dhmgs;

using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Mnnh.Dhmgs.Samples
{
	/// <summary>
	/// Canvas UI for runtime playback plus an Editor-registered Attempt ID import gateway.
	/// The import gateway is absent from Player builds; already imported assets remain playable.
	/// </summary>
	public sealed class DhmgsFullDemoController : MonoBehaviour
	{
		[SerializeField] private DhmgsMotionPlayer _player;
		[FormerlySerializedAs("_motion")]
		[SerializeField] private DhmgsMotionAsset _bundledMotion;
		[SerializeField] private bool _playOnStart = true;
		[SerializeField] private bool _loop = true;
		[SerializeField] private string _defaultBaseUrl = "http://192.168.1.128:8090";

		[Header("Editor Import")]
		[SerializeField] private InputField _baseUrlInput;
		[SerializeField] private InputField _attemptIdInput;
		[SerializeField] private Button _importAndPlayButton;
		[SerializeField] private Slider _importProgress;
		[SerializeField] private Text _importStatusLabel;

		[Header("Playback")]
		[SerializeField] private Button _bundledMotionButton;
		[SerializeField] private Button _playButton;
		[SerializeField] private Button _stopButton;
		[SerializeField] private Button _previousAvatarButton;
		[SerializeField] private Button _nextAvatarButton;
		[SerializeField] private Text _motionLabel;
		[SerializeField] private Text _avatarLabel;
		[SerializeField] private Slider _speedSlider;
		[SerializeField] private Text _speedLabel;
		[SerializeField] private Text _playbackStatusLabel;

		private DhmgsMotionAsset _activeMotion;
		private int _avatarIndex;
		private float _speed = 1f;
		private bool _importing;
		private CancellationTokenSource _importCancellation;

		private void OnEnable()
		{
			_importAndPlayButton?.onClick.AddListener(ImportAndPlay);
			_bundledMotionButton?.onClick.AddListener(UseBundledMotion);
			_playButton?.onClick.AddListener(Play);
			_stopButton?.onClick.AddListener(Stop);
			_previousAvatarButton?.onClick.AddListener(SelectPreviousAvatar);
			_nextAvatarButton?.onClick.AddListener(SelectNextAvatar);
			_speedSlider?.onValueChanged.AddListener(ApplySpeed);
		}

		private void Start()
		{
			_activeMotion = _bundledMotion;
			if (_baseUrlInput != null && string.IsNullOrWhiteSpace(_baseUrlInput.text))
			{
				_baseUrlInput.SetTextWithoutNotify(_defaultBaseUrl);
			}
			if (_speedSlider != null)
			{
				_speedSlider.SetValueWithoutNotify(_speed);
			}

			InitializeAvatar();
			RefreshMotionLabel();
			ApplySpeed(_speed);
			ConfigureImportAvailability();
			if (_playOnStart)
			{
				Play();
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
				$"{state}  |  Frame {_player.CurrentFrame + 1}/{_activeMotion.frameCount}  |  {_activeMotion.fps:F2} fps";
		}

		private void OnDisable()
		{
			_importAndPlayButton?.onClick.RemoveListener(ImportAndPlay);
			_bundledMotionButton?.onClick.RemoveListener(UseBundledMotion);
			_playButton?.onClick.RemoveListener(Play);
			_stopButton?.onClick.RemoveListener(Stop);
			_previousAvatarButton?.onClick.RemoveListener(SelectPreviousAvatar);
			_nextAvatarButton?.onClick.RemoveListener(SelectNextAvatar);
			_speedSlider?.onValueChanged.RemoveListener(ApplySpeed);
			_importCancellation?.Cancel();
			_importCancellation?.Dispose();
			_importCancellation = null;
		}

		public void Play()
		{
			if (_player == null || _activeMotion == null || !_activeMotion.IsValid)
			{
				SetImportStatus("Assign or import a valid motion asset.");
				return;
			}

			_player.Play(_activeMotion, new DhmgsRealtimeClock(_loop));
			ApplySpeed(_speedSlider != null ? _speedSlider.value : _speed);
		}

		public void Stop()
		{
			_player?.Stop();
		}

		private async void ImportAndPlay()
		{
			if (_importing)
			{
				return;
			}
			if (!DhmgsFullDemoImportGateway.IsAvailable)
			{
				SetImportStatus("Attempt ID import is available in the Unity Editor only.");
				return;
			}
			if (_attemptIdInput == null || !int.TryParse(_attemptIdInput.text, out int attemptId) || attemptId < 0)
			{
				SetImportStatus("Attempt ID must be a non-negative integer.");
				return;
			}

			string baseUrl = _baseUrlInput != null ? _baseUrlInput.text.Trim() : _defaultBaseUrl;
			if (string.IsNullOrWhiteSpace(baseUrl))
			{
				SetImportStatus("Base URL is required.");
				return;
			}

			_importing = true;
			SetImportInteractable(false);
			SetImportProgress(0f, $"Downloading attempt {attemptId}...");
			_importCancellation = new CancellationTokenSource();
			IProgress<DhmgsSampleImportProgress> progress =
				new Progress<DhmgsSampleImportProgress>(OnImportProgress);

			try
			{
				DhmgsMotionAsset imported = await DhmgsFullDemoImportGateway.ImportAndSaveAsync(
					baseUrl,
					attemptId,
					progress,
					_importCancellation.Token);
				if (this == null)
				{
					return;
				}

				_activeMotion = imported;
				RefreshMotionLabel();
				SetImportProgress(1f, $"Attempt {attemptId} imported and saved. Playback started.");
				Play();
			}
			catch (OperationCanceledException)
			{
				SetImportStatus("Import cancelled.");
			}
			catch (Exception exception)
			{
				Debug.LogException(exception, this);
				SetImportStatus(exception.Message);
			}
			finally
			{
				_importing = false;
				SetImportInteractable(DhmgsFullDemoImportGateway.IsAvailable);
				_importCancellation?.Dispose();
				_importCancellation = null;
			}
		}

		private void UseBundledMotion()
		{
			_activeMotion = _bundledMotion;
			RefreshMotionLabel();
			SetImportStatus("Using the bundled sample motion.");
			Play();
		}

		private void ConfigureImportAvailability()
		{
			bool available = DhmgsFullDemoImportGateway.IsAvailable;
			SetImportInteractable(available);
			SetImportProgress(0f, available
				? "Enter an Attempt ID, then select Import & Play."
				: "Attempt ID import is Editor-only. Bundled or serialized motions still play in builds.");
		}

		private void OnImportProgress(DhmgsSampleImportProgress progress)
		{
			SetImportProgress(progress.Value, progress.Status);
		}

		private void SetImportInteractable(bool interactable)
		{
			if (_importAndPlayButton != null)
			{
				_importAndPlayButton.interactable = interactable;
			}
			if (_baseUrlInput != null)
			{
				_baseUrlInput.interactable = interactable;
			}
			if (_attemptIdInput != null)
			{
				_attemptIdInput.interactable = interactable;
			}
		}

		private void SetImportProgress(float value, string status)
		{
			if (_importProgress != null)
			{
				_importProgress.SetValueWithoutNotify(Mathf.Clamp01(value));
			}
			SetImportStatus(status);
		}

		private void SetImportStatus(string status)
		{
			if (_importStatusLabel != null)
			{
				_importStatusLabel.text = status;
			}
		}

		private void RefreshMotionLabel()
		{
			if (_motionLabel != null)
			{
				_motionLabel.text = _activeMotion != null
					? $"Motion  {_activeMotion.attemptId}"
					: "Motion  None";
			}
		}

		private void InitializeAvatar()
		{
			if (_player == null || _player.AvatarCount == 0)
			{
				return;
			}

			_avatarIndex = Mathf.Clamp(_player.ActiveAvatar, 0, _player.AvatarCount - 1);
			SelectAvatar(_avatarIndex);
		}

		private void SelectPreviousAvatar()
		{
			SelectAvatar(_avatarIndex - 1);
		}

		private void SelectNextAvatar()
		{
			SelectAvatar(_avatarIndex + 1);
		}

		private void SelectAvatar(int index)
		{
			if (_player == null || _player.AvatarCount == 0)
			{
				return;
			}

			_avatarIndex = (index + _player.AvatarCount) % _player.AvatarCount;
			_player.SetAvatarIndex(_avatarIndex);
			if (_avatarLabel != null)
			{
				_avatarLabel.text = $"Avatar  {_player.AvatarName(_avatarIndex)}";
			}
		}

		private void ApplySpeed(float speed)
		{
			_speed = Mathf.Clamp(speed, 0.25f, 2f);
			_player?.SetPlaybackSpeed(_speed);
			if (_speedLabel != null)
			{
				_speedLabel.text = $"Speed  {_speed:F2}x";
			}
		}
	}
}
