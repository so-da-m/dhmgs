using System.Globalization;

using UnityEngine;
using UnityEngine.UI;

namespace Mnnh.Dhmgs.Samples
{
	[DisallowMultipleComponent]
	public sealed class DhmgsFullDemoPlaybackPanel : MonoBehaviour
	{
		[SerializeField] private DhmgsFullDemoPlayback _playback;
		[SerializeField] private Button _playButton;
		[SerializeField] private Button _pauseButton;
		[SerializeField] private Button _stopButton;
		[SerializeField] private InputField _speedInput;
		[SerializeField] private Slider _frameSlider;
		[SerializeField] private Text _frameValue;
		[SerializeField] private DhmgsFullDemoFrameScrubber _frameScrubber;
		[SerializeField] private Text _playbackStatusLabel;

		private bool _updatingFrameControl;
		private float _displayedSpeed = float.NaN;
		private string _speedText;
		private int _displayedFrame = -1;
		private int _displayedFrameCount = -1;
		private string _displayedState;
		private string _displayedAttemptId;

		private void OnEnable()
		{
			_displayedSpeed = float.NaN;
			_displayedFrame = -1;
			_displayedFrameCount = -1;
			_displayedState = null;
			_displayedAttemptId = null;
			_playButton?.onClick.AddListener(OnPlayClicked);
			_pauseButton?.onClick.AddListener(OnPauseClicked);
			_stopButton?.onClick.AddListener(OnStopClicked);
			_speedInput?.onEndEdit.AddListener(OnSpeedEdited);
			_frameSlider?.onValueChanged.AddListener(OnFrameChanged);
			if (_frameScrubber != null)
			{
				_frameScrubber.ScrubStarted += BeginScrub;
				_frameScrubber.ScrubEnded += EndScrub;
			}
			Refresh();
		}

		private void OnDisable()
		{
			CancelScrub();
			_playButton?.onClick.RemoveListener(OnPlayClicked);
			_pauseButton?.onClick.RemoveListener(OnPauseClicked);
			_stopButton?.onClick.RemoveListener(OnStopClicked);
			_speedInput?.onEndEdit.RemoveListener(OnSpeedEdited);
			_frameSlider?.onValueChanged.RemoveListener(OnFrameChanged);
			if (_frameScrubber != null)
			{
				_frameScrubber.ScrubStarted -= BeginScrub;
				_frameScrubber.ScrubEnded -= EndScrub;
			}
			SetTransportInteractable(false, false, false);
			SetInteractable(_frameSlider, false);
			SetInteractable(_speedInput, false);
		}

		private void Update()
		{
			if (_playback == null || !_playback.HasMotion)
			{
				CancelScrub();
			}
			Refresh();
		}

		private void Refresh()
		{
			SetTransportInteractable(_playback != null && _playback.CanPlay,
				_playback != null && _playback.CanPause, _playback != null && _playback.CanStop);
			if (_speedInput != null)
			{
				SetInteractable(_speedInput, _playback != null && _playback.isActiveAndEnabled);
				if (!_speedInput.isFocused)
				{
					RefreshSpeedInput();
				}
			}
			RefreshPlaybackDisplay();
		}

		private void SetTransportInteractable(bool canPlay, bool canPause, bool canStop)
		{
			SetInteractable(_playButton, canPlay);
			SetInteractable(_pauseButton, canPause);
			SetInteractable(_stopButton, canStop);
		}

		private static void SetInteractable(Selectable control, bool interactable)
		{
			if (control != null && control.interactable != interactable)
			{
				control.interactable = interactable;
			}
		}

		private void OnPlayClicked()
		{
			_playback?.Play();
			Refresh();
		}

		private void OnPauseClicked()
		{
			_playback?.Pause();
			Refresh();
		}

		private void OnStopClicked()
		{
			_playback?.Stop();
			CancelScrub();
			Refresh();
		}

		private void OnSpeedEdited(string value)
		{
			bool parsed = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float speed) ||
				float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out speed);
			if (parsed)
			{
				_playback?.SetSpeed(speed);
			}
			RefreshSpeedInput();
		}

		private void RefreshSpeedInput()
		{
			if (_speedInput != null)
			{
				float speed = _playback != null ? _playback.Speed : 1f;
				if (speed != _displayedSpeed)
				{
					_displayedSpeed = speed;
					_speedText = speed.ToString("0.0#", CultureInfo.InvariantCulture);
				}
				if (_speedInput.text != _speedText)
				{
					_speedInput.SetTextWithoutNotify(_speedText);
				}
			}
		}

		private void RefreshPlaybackDisplay()
		{
			int total = _playback != null ? _playback.FrameCount : 0;
			int current = total > 0 ? _playback.CurrentFrame + 1 : 0;
			bool frameChanged = current != _displayedFrame || total != _displayedFrameCount;
			if (_frameValue != null && frameChanged)
			{
				_frameValue.text = $"{current} / {total}";
			}
			string state = GetStateLabel();
			string attemptId = total > 0 ? _playback.Motion.attemptId : null;
			if (_playbackStatusLabel != null &&
				(frameChanged || state != _displayedState || attemptId != _displayedAttemptId))
			{
				_playbackStatusLabel.text = total > 0
					? $"{state}  |  {attemptId}  |  Frame {current}/{total}" : state;
			}
			_displayedFrame = current;
			_displayedFrameCount = total;
			_displayedState = state;
			_displayedAttemptId = attemptId;
			if (_frameSlider == null)
			{
				return;
			}

			_updatingFrameControl = true;
			if (!_frameSlider.wholeNumbers)
			{
				_frameSlider.wholeNumbers = true;
			}
			if (_frameSlider.minValue != 1f)
			{
				_frameSlider.minValue = 1f;
			}
			int maximum = Mathf.Max(1, total);
			if (_frameSlider.maxValue != maximum)
			{
				_frameSlider.maxValue = maximum;
			}
			SetInteractable(_frameSlider, _playback != null && _playback.CanSeek);
			if (_playback == null || total == 0 || (!_playback.IsScrubbing && !_playback.IsSeeking))
			{
				int value = Mathf.Max(1, current);
				if (_frameSlider.value != value)
				{
					_frameSlider.SetValueWithoutNotify(value);
				}
			}
			_updatingFrameControl = false;
		}

		private string GetStateLabel()
		{
			if (_playback == null || !_playback.HasMotion)
			{
				return _playback != null && _playback.State == DhmgsFullDemoPlaybackState.Loading
					? "Loading..." : "No motion loaded";
			}
			if (_playback.IsScrubbing)
			{
				return "Scrubbing";
			}
			if (_playback.IsSeeking)
			{
				return "Seeking";
			}
			return _playback.State switch
			{
				DhmgsFullDemoPlaybackState.Playing => "Playing",
				DhmgsFullDemoPlaybackState.Paused => "Paused",
				DhmgsFullDemoPlaybackState.Stopped => "Stopped",
				_ => "No motion loaded",
			};
		}

		private void OnFrameChanged(float value)
		{
			if (_updatingFrameControl || _playback == null || !_playback.CanSeek)
			{
				return;
			}
			_playback.SeekFrame(Mathf.RoundToInt(value) - 1);
			RefreshPlaybackDisplay();
		}

		private void BeginScrub()
		{
			_playback?.BeginScrub();
		}

		private void EndScrub()
		{
			_playback?.EndScrub();
			RefreshPlaybackDisplay();
		}

		public void CancelScrub()
		{
			_frameScrubber?.CancelScrub();
			_playback?.EndScrub();
		}
	}
}
