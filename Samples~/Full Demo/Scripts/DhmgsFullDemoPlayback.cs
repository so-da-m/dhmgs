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

	[DisallowMultipleComponent]
	public sealed class DhmgsFullDemoPlayback : MonoBehaviour
	{
		[SerializeField] private DhmgsMotionPlayer _player;

		private DhmgsMotionAsset _motion;
		// Retain the owned shared clock so cleanup can pause it even after an external player.Stop().
		private IDhmgsSeekablePlaybackClock _clockForCleanup;
		private DhmgsFullDemoPlaybackState _state;
		private float _speed = 1f;
		private bool _scrubbing;

		public DhmgsMotionAsset Motion => _motion;
		public DhmgsFullDemoPlaybackState State => _state;
		public float Speed => _speed;
		public bool HasMotion => _motion != null && _player != null &&
			_player.Motion == _motion && _player.IsPlaying &&
			_state != DhmgsFullDemoPlaybackState.Empty && _state != DhmgsFullDemoPlaybackState.Loading;
		public int FrameCount => HasMotion ? _motion.frameCount : 0;
		public int CurrentFrame => HasMotion ? Mathf.Clamp(_player.CurrentFrame, 0, _motion.frameCount - 1) : 0;
		public bool IsSeeking => HasMotion && _player.IsSeeking;
		public bool IsScrubbing => _scrubbing;
		public bool CanPlay => CanSeek &&
			(_state == DhmgsFullDemoPlaybackState.Paused || _state == DhmgsFullDemoPlaybackState.Stopped);
		public bool CanPause => CanSeek &&
			_state == DhmgsFullDemoPlaybackState.Playing;
		public bool CanSeek => isActiveAndEnabled && HasMotion && _player.CanSeek;
		public bool CanStop => CanSeek && (_state != DhmgsFullDemoPlaybackState.Stopped ||
			CurrentFrame != 0 || IsSeeking || _scrubbing);

		public void BeginLoad()
		{
			Clear();
			_state = DhmgsFullDemoPlaybackState.Loading;
		}

		public void CancelLoad()
		{
			if (_state == DhmgsFullDemoPlaybackState.Loading)
			{
				Clear();
			}
		}

		public bool LoadAndPlay(DhmgsMotionAsset motion, IDhmgsPlaybackClock clock)
		{
			Clear();
			if (!isActiveAndEnabled || _player == null || motion == null || !motion.IsValid || clock == null)
			{
				return false;
			}

			_motion = motion;
			_clockForCleanup = clock as IDhmgsSeekablePlaybackClock;
			try
			{
				_player.Play(motion, clock);
				if (!_player.IsPlaying)
				{
					Clear();
					return false;
				}
				_state = DhmgsFullDemoPlaybackState.Playing;
				_player.SetPlaybackSpeed(_speed);
				ApplyPauseState();
				return true;
			}
			catch
			{
				Clear();
				throw;
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
			// Keep the player sampling decoded poses while the paused video seeks, including its avatar and bones.
			ApplyPauseState();
			_player.SeekFrame(0);
		}

		public void SetSpeed(float speed)
		{
			if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f)
			{
				return;
			}
			_speed = Mathf.Clamp(speed, 0.25f, 2f);
			if (HasMotion)
			{
				_player.SetPlaybackSpeed(_speed);
			}
		}

		public bool SeekFrame(int frame)
		{
			return CanSeek && _player.SeekFrame(frame);
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

		private void ApplyPauseState()
		{
			if (HasMotion)
			{
				_player.SetPaused(_scrubbing || _state != DhmgsFullDemoPlaybackState.Playing);
			}
		}

		public void Clear()
		{
			_scrubbing = false;
			_clockForCleanup?.SetPaused(true);
			_player?.Stop();
			_clockForCleanup = null;
			_motion = null;
			_state = DhmgsFullDemoPlaybackState.Empty;
		}

		private void OnDisable()
		{
			Clear();
		}

		private void Update()
		{
			if (HasMotion && !_scrubbing && !IsSeeking &&
				_state == DhmgsFullDemoPlaybackState.Playing && _player.IsPaused)
			{
				_state = DhmgsFullDemoPlaybackState.Paused;
			}
		}
	}
}
