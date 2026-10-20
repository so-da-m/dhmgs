using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace Mnnh.Dhmgs.Samples
{
	public enum DhmgsFullDemoPlaybackState
	{
		Empty,
		Loading,
		Playing,
		Paused,
		Stopped,
	}

	[DefaultExecutionOrder(-1000)]
	[DisallowMultipleComponent]
	public sealed class DhmgsFullDemoPlayback : MonoBehaviour
	{
		[SerializeField] private DhmgsMotionPlayer _player;
		[SerializeField] private bool _loop = true;
		[SerializeField] private Camera _previewCamera;
		[SerializeField] private DhmgsAvatarPreviewCamera _humanoidPreviewCamera;

		private DhmgsMotionAsset _motion;
		private IDhmgsSeekablePlaybackClock _clock;
		private DhmgsFullDemoPreview _preview;
		private CancellationTokenSource _preparationCancellation;
		private DhmgsFullDemoPlaybackState _state;
		private float _speed = 1f;
		private float _videoOpacity = 1f;
		private bool _humanoidRootMotion = true;
		private bool _scrubbing;

		public event Action MotionLoaded;

		public DhmgsMotionPlayer Player => _player;
		public DhmgsMotionAsset Motion => _motion;
		public DhmgsFullDemoPlaybackState State => _state;
		public float Speed => _speed;
		public bool HumanoidRootMotion => _humanoidRootMotion;
		public bool CanSetHumanoidRootMotion => HasMotion && _preview != null && _humanoidPreviewCamera != null;
		public bool CanLoad => isActiveAndEnabled && _player != null && _player.isActiveAndEnabled;
		public bool HasVideoPreview { get; private set; }
		public bool HasMotion => _clock != null && _motion != null && OwnsPlayer;
		public int FrameCount => HasMotion ? _motion.frameCount : 0;
		public int CurrentFrame => HasMotion ? Mathf.Clamp(_clock.CurrentFrame, 0, _motion.frameCount - 1) : 0;
		public double CurrentTime => HasMotion ? _clock.CurrentTime : 0d;
		public bool IsSeeking => HasMotion && _clock.IsSeeking;
		public bool IsWaiting => HasMotion && _clock.IsWaiting;
		public string PlaybackError => HasMotion ? _clock.Error : null;
		public bool IsScrubbing => _scrubbing;
		public bool CanPlay => CanSeek &&
			(_state == DhmgsFullDemoPlaybackState.Paused || _state == DhmgsFullDemoPlaybackState.Stopped);
		public bool CanPause => CanSeek && _state == DhmgsFullDemoPlaybackState.Playing;
		public bool CanSeek => CanLoad && HasMotion && _clock.CanSeek && string.IsNullOrEmpty(_clock.Error);
		public bool CanStop => CanSeek && (_state != DhmgsFullDemoPlaybackState.Stopped ||
			CurrentFrame != 0 || IsSeeking || _scrubbing);
		public bool CanSetVideoOpacity => isActiveAndEnabled && _previewCamera != null &&
			DhmgsFullDemoPreview.Factory != null;

		private bool OwnsPlayer => _player != null && _player.IsPlaying &&
			_player.Motion == _motion && ReferenceEquals(_player.Clock, _clock);

		public async Task<bool> LoadAndPlayAsync(DhmgsSampleImportResult imported, CancellationToken cancellationToken)
		{
			Clear();
			if (!CanLoad || imported?.Motion == null || !imported.Motion.IsValid)
			{
				return false;
			}

			using CancellationTokenSource preparation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			_preparationCancellation = preparation;
			_state = DhmgsFullDemoPlaybackState.Loading;
			IDhmgsSeekablePlaybackClock preparedClock = null;
			try
			{
				preparation.Token.ThrowIfCancellationRequested();
				_preview = DhmgsFullDemoPreview.Factory?.Invoke(transform, _previewCamera, _humanoidPreviewCamera);
				if (_preview != null)
				{
					_preview.SetVideoOpacity(_videoOpacity);
					_preview.SetHumanoidRootMotion(_humanoidRootMotion);
					preparedClock = await _preview.PrepareAsync(imported, _loop, preparation.Token);
				}
				preparation.Token.ThrowIfCancellationRequested();
				if (!ReferenceEquals(_preparationCancellation, preparation) || !CanLoad)
				{
					throw new OperationCanceledException(preparation.Token);
				}

				HasVideoPreview = preparedClock != null;
				_clock = preparedClock ?? new DhmgsRealtimeClock(_loop);
				preparedClock = null;
				_motion = imported.Motion;
				_clock.Begin(_motion.frameCount, _motion.fps);
				_clock.SetSpeed(_speed);
				if (!string.IsNullOrEmpty(_clock.Error))
				{
					throw new InvalidOperationException(_clock.Error);
				}
				_player.Play(_motion, _clock);
				_preview?.Play(_clock);
				_state = DhmgsFullDemoPlaybackState.Playing;
				ApplyPauseState();
				MotionLoaded?.Invoke();
				return true;
			}
			catch
			{
				if (ReferenceEquals(_preparationCancellation, preparation))
				{
					Clear();
				}
				throw;
			}
			finally
			{
				preparedClock?.Dispose();
				if (ReferenceEquals(_preparationCancellation, preparation))
				{
					_preparationCancellation = null;
				}
			}
		}

		public void Play()
		{
			if (!CanPlay)
			{
				return;
			}
			_state = DhmgsFullDemoPlaybackState.Playing;
			ApplyPauseState();
		}

		public void Pause()
		{
			if (!CanPause)
			{
				return;
			}
			_state = DhmgsFullDemoPlaybackState.Paused;
			ApplyPauseState();
		}

		public void Stop()
		{
			if (!CanSeek)
			{
				return;
			}
			_scrubbing = false;
			_state = DhmgsFullDemoPlaybackState.Stopped;
			ApplyPauseState();
			_clock.SeekFrame(0);
		}

		public void SetSpeed(float speed)
		{
			if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f)
			{
				return;
			}
			_speed = Mathf.Clamp(speed, 0.25f, 2f);
			_clock?.SetSpeed(_speed);
		}

		public bool SeekFrame(int frame)
		{
			if (!CanSeek)
			{
				return false;
			}
			_clock.SeekFrame(Mathf.Clamp(frame, 0, _motion.frameCount - 1));
			return true;
		}

		public void BeginScrub()
		{
			if (_scrubbing || !CanSeek)
			{
				return;
			}
			_scrubbing = true;
			ApplyPauseState();
		}

		public void EndScrub()
		{
			if (!_scrubbing)
			{
				return;
			}
			_scrubbing = false;
			ApplyPauseState();
		}

		public void SetVideoOpacity(float opacity)
		{
			_videoOpacity = Mathf.Clamp01(opacity);
			_preview?.SetVideoOpacity(_videoOpacity);
		}

		public void SetHumanoidRootMotion(bool enabled)
		{
			if (_humanoidRootMotion == enabled)
			{
				return;
			}
			_humanoidRootMotion = enabled;
			_preview?.SetHumanoidRootMotion(enabled);
		}

		private void ApplyPauseState()
		{
			_clock?.SetPaused(_scrubbing || _state != DhmgsFullDemoPlaybackState.Playing);
		}

		public void Clear()
		{
			CancellationTokenSource preparation = _preparationCancellation;
			_preparationCancellation = null;
			preparation?.Cancel();
			if (_clock != null && OwnsPlayer)
			{
				_player.Stop();
			}
			IDhmgsSeekablePlaybackClock clock = _clock;
			DhmgsFullDemoPreview preview = _preview;
			_clock = null;
			_preview = null;
			_motion = null;
			_scrubbing = false;
			HasVideoPreview = false;
			_state = DhmgsFullDemoPlaybackState.Empty;
			try
			{
				clock?.Dispose();
			}
			finally
			{
				preview?.Dispose();
			}
		}

		private void OnDisable()
		{
			Clear();
		}

		private void Update()
		{
			if (_clock == null)
			{
				return;
			}
			if (!CanLoad || !OwnsPlayer)
			{
				Clear();
				return;
			}
			_clock.Tick();
			_preview?.SetVideoVisible(!_clock.IsWaiting && string.IsNullOrEmpty(_clock.Error));
			if (!_scrubbing && !_clock.IsSeeking &&
				_state == DhmgsFullDemoPlaybackState.Playing && _clock.IsPaused)
			{
				_state = DhmgsFullDemoPlaybackState.Paused;
			}
		}
	}
}
