using UnityEngine;
using UnityEngine.Video;

namespace Mnnh.Dhmgs
{
	internal sealed class DhmgsVideoClock : IDhmgsSeekablePlaybackClock, System.IDisposable
	{
		private readonly VideoPlayer _player;
		private int _frameCount = 1;
		private int _requestedFrame;
		private int _inFlightFrame;
		private bool _paused;
		private bool _seeking;
		private bool _seekCompleted;
		private bool _frameReady;
		private bool _resumeAfterSeek;
		private bool _previousFrameReadyEvents;
		private bool _disposed;
		private double _seekStartedAt;

		public bool IsPaused => _paused;
		public bool IsSeeking => _seeking && !_disposed && _player != null;
		public bool CanSeek => !_disposed && _player != null && _player.isPrepared && _player.canSetTime;

		public DhmgsVideoClock(VideoPlayer player)
		{
			_player = player;
		}

		public void Begin(int frameCount, float fps)
		{
			if (_disposed)
			{
				return;
			}
			CancelSeek();
			_frameCount = Mathf.Max(1, frameCount);
			_paused = _player != null && _player.isPaused;
		}

		public void SetSpeed(float speed)
		{
			if (!_disposed && _player != null && _player.canSetPlaybackSpeed)
			{
				_player.playbackSpeed = float.IsNaN(speed) || float.IsInfinity(speed) ? 1f : Mathf.Max(0.01f, speed);
			}
		}

		public void SetPaused(bool paused)
		{
			if (_disposed || _player == null)
			{
				return;
			}
			_paused = paused;
			if (_seeking)
			{
				_resumeAfterSeek = !paused;
				return;
			}
			if (paused)
			{
				_player.Pause();
			}
			else
			{
				_player.Play();
			}
		}

		public void SeekFrame(int frame)
		{
			if (!CanSeek)
			{
				return;
			}
			int maximumFrame = _frameCount - 1;
			if (_player.frameCount > 0 && _player.frameCount <= (ulong)maximumFrame)
			{
				maximumFrame = (int)_player.frameCount - 1;
			}
			_requestedFrame = Mathf.Clamp(frame, 0, maximumFrame);
			if (_seeking || _player.frame == _requestedFrame)
			{
				return;
			}

			_resumeAfterSeek = !_paused && _player.isPlaying;
			_player.Pause();
			_seeking = true;
			_previousFrameReadyEvents = _player.sendFrameReadyEvents;
			_player.seekCompleted += OnSeekCompleted;
			_player.frameReady += OnFrameReady;
			_player.errorReceived += OnSeekError;
			_player.sendFrameReadyEvents = true;
			StartLatestSeek();
		}

		// VideoPlayer.frame describes the frame actually available in its texture.
		// Never publish the requested slider frame before the decoder presents it.
		public int CurrentFrame => _disposed || _player == null
			? 0
			: (int)System.Math.Max(0L, System.Math.Min(_player.frame, _frameCount - 1L));

		private void StartLatestSeek()
		{
			_inFlightFrame = _requestedFrame;
			_seekCompleted = false;
			_frameReady = false;
			_seekStartedAt = Time.realtimeSinceStartupAsDouble;
			try
			{
				_player.frame = _inFlightFrame;
			}
			catch
			{
				CancelSeek();
				_paused = true;
				throw;
			}
		}

		private void OnSeekCompleted(VideoPlayer source)
		{
			if (!_seeking || source != _player)
			{
				return;
			}
			_seekCompleted = true;
			CompleteSeekIfReady();
		}

		private void OnFrameReady(VideoPlayer source, long frame)
		{
			if (!_seeking || source != _player || frame != _inFlightFrame)
			{
				return;
			}
			_frameReady = true;
			CompleteSeekIfReady();
		}

		private void CompleteSeekIfReady()
		{
			// Some decoders expose the presented frame before emitting frameReady.
			// The frame property refers to the texture's available frame, not our request.
			if (_seekCompleted && _player != null && _player.texture != null && _player.frame == _inFlightFrame)
			{
				_frameReady = true;
			}
			if (!_seekCompleted || !_frameReady)
			{
				return;
			}
			if (_requestedFrame != _inFlightFrame)
			{
				StartLatestSeek();
				return;
			}

			bool resume = _resumeAfterSeek && !_paused;
			CancelSeek();
			if (resume && _player != null && _player.isPrepared)
			{
				_player.Play();
			}
		}

		private void OnSeekError(VideoPlayer source, string message)
		{
			if (!_seeking || source != _player)
			{
				return;
			}
			CancelSeek();
			_paused = true;
			Debug.LogError($"[DHMGS] Video seek failed: {message}", source);
		}

		internal void Tick()
		{
			if (!_seeking)
			{
				return;
			}
			if (_player == null || !_player.isPrepared)
			{
				CancelSeek();
				_paused = true;
				return;
			}
			CompleteSeekIfReady();
			if (_seeking && Time.realtimeSinceStartupAsDouble - _seekStartedAt >= 10d)
			{
				bool resume = _resumeAfterSeek && !_paused;
				CancelSeek();
				if (resume)
				{
					_player.Play();
				}
				Debug.LogWarning("[DHMGS] Video seek timed out; keeping the available frame.", _player);
			}
		}

		private void CancelSeek()
		{
			if (_seeking && _player != null)
			{
				_player.seekCompleted -= OnSeekCompleted;
				_player.frameReady -= OnFrameReady;
				_player.errorReceived -= OnSeekError;
				_player.sendFrameReadyEvents = _previousFrameReadyEvents;
			}
			_seeking = false;
			_resumeAfterSeek = false;
		}

		public void Dispose()
		{
			CancelSeek();
			_disposed = true;
		}
	}
}
