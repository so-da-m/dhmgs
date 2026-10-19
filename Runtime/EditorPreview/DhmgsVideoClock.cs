using System;

using UnityEngine;
using UnityEngine.Video;

namespace Mnnh.Dhmgs
{
	internal sealed class DhmgsVideoClock : IDhmgsSeekablePlaybackClock
	{
		private const double WaitTimeoutSeconds = 10d;
		private const long MaximumFrameDrift = 2;

		private readonly VideoPlayer _player;
		private readonly bool _loop;
		private readonly long _firstVideoFrame;
		private readonly DhmgsRealtimeClock _timeline = new DhmgsRealtimeClock(loop: false);
		private readonly bool _previousFrameReadyEvents;
		private readonly bool _previousLooping;
		private readonly VideoTimeReference _previousTimeReference;
		private readonly VideoTimeUpdateMode _previousTimeUpdateMode;
		private readonly float _previousPlaybackSpeed;
		private readonly bool _previousSkipOnDrop;
		private int _frameCount = 1;
		private float _fps = 30f;
		private double _videoFps;
		private long _lastVideoFrame;
		private long _lastSeekFrame;
		private double _publishedTime;
		private double _requestedTime;
		private long _inFlightFrame;
		private long _readyFrame = -1;
		private long _resolvedFrame = -1;
		private double _waitStartedAt;
		private int _sampledFrame = -1;
		private float _speed = 1f;
		private bool _begun;
		private bool _paused = true;
		private bool _waiting;
		private bool _seeking;
		private bool _seekCompleted;
		private bool _disposed;
		private string _error;

		public bool IsPaused => _paused;
		public bool IsSeeking => _waiting && _seeking && !_disposed;
		public bool IsWaiting => _waiting && !_disposed;
		public string Error => _error;
		public bool CanSeek => _begun && !_disposed && string.IsNullOrEmpty(_error) &&
			_player != null && _player.isPrepared && _player.canSetTime;
		public int CurrentFrame => Math.Min(_frameCount - 1, (int)(CurrentTime * _fps + 1e-7d));
		public double CurrentTime => _publishedTime;

		public DhmgsVideoClock(VideoPlayer player, bool loop = true, long firstFrame = -1)
		{
			_player = player;
			_loop = loop;
			_firstVideoFrame = Math.Max(0, firstFrame);
			if (_player == null)
			{
				return;
			}
			_previousFrameReadyEvents = _player.sendFrameReadyEvents;
			_previousLooping = _player.isLooping;
			_previousTimeReference = _player.timeReference;
			_previousTimeUpdateMode = _player.timeUpdateMode;
			_previousPlaybackSpeed = _player.playbackSpeed;
			_previousSkipOnDrop = _player.skipOnDrop;
			_player.seekCompleted += OnSeekCompleted;
			_player.frameReady += OnFrameReady;
			_player.errorReceived += OnVideoError;
		}

		public void Begin(int frameCount, float fps)
		{
			if (_disposed)
			{
				return;
			}
			_begun = true;
			_paused = false;
			_waiting = false;
			_seeking = false;
			_resolvedFrame = -1;
			_error = null;
			_publishedTime = 0d;
			_sampledFrame = -1;
			_frameCount = Mathf.Max(1, frameCount);
			_fps = fps > 0f && !float.IsInfinity(fps) ? fps : 30f;
			_timeline.Begin(_frameCount, _fps);
			_timeline.SetPaused(true);
			_timeline.SetSpeed(_speed);
			if (_player == null || !_player.isPrepared || !_player.canSetTime)
			{
				Fail("The preview video is not prepared or cannot seek.");
				return;
			}

			_videoFps = _player.frameRate;
			if (!IsFinite(_videoFps) || _videoFps <= 0d ||
				_player.frameCount == 0 || _player.frameCount > (ulong)long.MaxValue)
			{
				Fail("The preview video has invalid frame rate or frame count metadata.");
				return;
			}
			_lastVideoFrame = (long)_player.frameCount - 1;
			_lastSeekFrame = _lastVideoFrame;
			double videoDuration = _player.frameCount / _videoFps;
			if (videoDuration + 1d / _videoFps + 1e-6d < _timeline.Duration)
			{
				Fail($"The preview video is shorter than the motion ({videoDuration:F3}s versus {_timeline.Duration:F3}s).");
				return;
			}
			_lastVideoFrame = Math.Min(_lastVideoFrame,
				(long)Math.Max(0d, Math.Ceiling(_timeline.Duration * _videoFps - 1e-7d) - 1d));

			try
			{
				_player.Pause();
				if (_player.texture != null && _player.frame == _firstVideoFrame)
				{
					_resolvedFrame = _firstVideoFrame;
				}
				_player.isLooping = false;
				_player.sendFrameReadyEvents = true;
				_player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
				_player.timeReference = VideoTimeReference.Freerun;
				ApplyVideoSpeed();
				if (string.IsNullOrEmpty(_error))
				{
					RequestSeek(0d, true);
				}
			}
			catch (Exception exception)
			{
				Fail($"Could not start synchronized video playback: {exception.Message}");
			}
		}

		public void SetSpeed(float speed)
		{
			if (_disposed || !string.IsNullOrEmpty(_error))
			{
				return;
			}
			Tick();
			if (!string.IsNullOrEmpty(_error))
			{
				return;
			}
			_speed = float.IsNaN(speed) || float.IsInfinity(speed) ? 1f : Mathf.Max(0.01f, speed);
			_timeline.SetSpeed(_speed);
			if (_begun && !_waiting)
			{
				try
				{
					ApplyVideoSpeed();
				}
				catch (Exception exception)
				{
					Fail($"Could not change video playback speed: {exception.Message}");
				}
			}
		}

		public void SetPaused(bool paused)
		{
			if (_disposed || !string.IsNullOrEmpty(_error))
			{
				return;
			}
			Tick();
			if (!string.IsNullOrEmpty(_error))
			{
				return;
			}
			_paused = paused;
			if (!_begun || _waiting)
			{
				return;
			}
			try
			{
				_timeline.SeekTime(_publishedTime);
				_timeline.SetPaused(paused);
				if (paused)
				{
					PauseAtPublishedTime();
				}
				else if (VideoFrameAt(_publishedTime) < _firstVideoFrame + _lastVideoFrame)
				{
					_player.Play();
				}
			}
			catch (Exception exception)
			{
				Fail($"Could not change video playback state: {exception.Message}");
			}
		}

		public void SeekFrame(int frame)
		{
			if (!CanSeek)
			{
				return;
			}
			if (!_waiting)
			{
				Tick();
			}
			if (!CanSeek)
			{
				return;
			}
			try
			{
				RequestSeek(Mathf.Clamp(frame, 0, _frameCount - 1) / (double)_fps, true);
			}
			catch (Exception exception)
			{
				Fail($"Could not seek the preview video: {exception.Message}");
			}
		}

		public void Tick()
		{
			if (_disposed || !_begun || !string.IsNullOrEmpty(_error) || _sampledFrame == Time.frameCount)
			{
				return;
			}
			_sampledFrame = Time.frameCount;
			try
			{
				if (_player == null || !_player.isPrepared)
				{
					Fail("The preview video became unavailable during playback.");
					return;
				}
				if (_waiting)
				{
					CompleteSeekIfReady();
					if (_waiting && Time.realtimeSinceStartupAsDouble - _waitStartedAt >= WaitTimeoutSeconds)
					{
						Fail($"Video synchronization timed out (requested frame {_inFlightFrame}, " +
							$"presented frame {_player.frame}, ready frame {_readyFrame}, seek completed {_seekCompleted}).");
					}
					return;
				}
				if (_paused)
				{
					return;
				}

				_timeline.Tick();
				double time = _timeline.CurrentTime;
				long expectedFrame = VideoFrameAt(time);
				long presentedFrame = _player.frame;
				if (_player.texture == null || presentedFrame < 0 ||
					Math.Abs(presentedFrame - expectedFrame) > MaximumFrameDrift)
				{
					RequestSeek(time, false);
					return;
				}

				_publishedTime = time;
				if (time >= _timeline.Duration)
				{
					if (_loop)
					{
						RequestSeek(0d, false);
					}
					else
					{
						_paused = true;
						PauseAtPublishedTime();
					}
				}
			}
			catch (Exception exception)
			{
				Fail($"Video synchronization failed: {exception.Message}");
			}
		}

		private void PauseAtPublishedTime()
		{
			_timeline.SetPaused(true);
			_player.Pause();
			if (!HasResolvedFrame(VideoFrameAt(_publishedTime)))
			{
				RequestSeek(_publishedTime, false);
			}
		}

		private void RequestSeek(double time, bool seeking)
		{
			_requestedTime = Math.Max(0d, Math.Min(time, _timeline.Duration));
			_seeking |= seeking;
			if (_waiting)
			{
				return;
			}
			_waiting = true;
			_timeline.SetPaused(true);
			_timeline.SeekTime(_publishedTime);
			_player.Pause();
			StartLatestSeek();
		}

		private void StartLatestSeek()
		{
			_waitStartedAt = Time.realtimeSinceStartupAsDouble;
			_inFlightFrame = VideoFrameAt(_requestedTime);
			_seekCompleted = false;
			_readyFrame = -1;
			if (HasResolvedFrame(_inFlightFrame))
			{
				_seekCompleted = true;
				_readyFrame = _resolvedFrame;
				return;
			}
			if (_player.canSetPlaybackSpeed)
			{
				_player.playbackSpeed = 1f;
			}
			if (_player.canSetSkipOnDrop)
			{
				_player.skipOnDrop = false;
			}
			long seekFrame = Math.Min(_inFlightFrame, _lastSeekFrame);
			if (_player.frame == seekFrame)
			{
				_seekCompleted = true;
				if (seekFrame == _inFlightFrame && _player.texture != null)
				{
					_readyFrame = seekFrame;
				}
				else
				{
					_player.Play();
				}
			}
			else
			{
				_player.frame = seekFrame;
			}
		}

		private void OnSeekCompleted(VideoPlayer source)
		{
			if (_waiting && !_disposed && source == _player)
			{
				_seekCompleted = true;
				if (_readyFrame >= 0 && _readyFrame < _inFlightFrame)
				{
					source.Play();
				}
			}
		}

		private void OnFrameReady(VideoPlayer source, long frame)
		{
			if (_disposed || source != _player || frame < 0)
			{
				return;
			}
			if (!_waiting)
			{
				_resolvedFrame = frame;
				return;
			}
			_readyFrame = frame;
			if (frame == _inFlightFrame)
			{
				source.Pause();
			}
			else if (_seekCompleted && frame < _inFlightFrame)
			{
				source.Play();
			}
		}

		private void CompleteSeekIfReady()
		{
			if (!_seekCompleted || _readyFrame != _inFlightFrame ||
				_player.texture == null || _player.frame != _readyFrame)
			{
				return;
			}
			_resolvedFrame = _readyFrame;
			if (VideoFrameAt(_requestedTime) != _inFlightFrame)
			{
				StartLatestSeek();
				return;
			}

			_publishedTime = _requestedTime;
			_waiting = false;
			_seeking = false;
			_timeline.SeekTime(_publishedTime);
			_timeline.SetPaused(_paused);
			if (_player.canSetSkipOnDrop)
			{
				_player.skipOnDrop = _previousSkipOnDrop;
			}
			ApplyVideoSpeed();
			if (!_paused && VideoFrameAt(_publishedTime) < _firstVideoFrame + _lastVideoFrame)
			{
				_player.Play();
			}
		}

		private bool HasResolvedFrame(long targetFrame)
		{
			return _resolvedFrame == targetFrame && _resolvedFrame >= 0 &&
				_player.texture != null && _player.frame == _resolvedFrame;
		}

		private long VideoFrameAt(double time)
		{
			return _firstVideoFrame + (long)Math.Max(0d,
				Math.Min(Math.Floor(time * _videoFps + 1e-7d), _lastVideoFrame));
		}

		private void ApplyVideoSpeed()
		{
			if (!_player.canSetPlaybackSpeed)
			{
				if (!Mathf.Approximately(_speed, 1f))
				{
					Fail("The preview video does not support changing playback speed.");
				}
				return;
			}
			_player.playbackSpeed = _speed;
		}

		private void OnVideoError(VideoPlayer source, string message)
		{
			if (!_disposed && source == _player)
			{
				Fail($"Preview video failed: {message}");
			}
		}

		private void Fail(string message)
		{
			if (_disposed || !string.IsNullOrEmpty(_error))
			{
				return;
			}
			_error = message;
			_paused = true;
			_waiting = false;
			_seeking = false;
			_timeline.SetPaused(true);
			_timeline.SeekTime(_publishedTime);
			if (_player != null && _player.isPrepared)
			{
				_player.Pause();
			}
			Debug.LogError($"[DHMGS] {message}", _player);
		}

		private static bool IsFinite(double value)
		{
			return !double.IsNaN(value) && !double.IsInfinity(value);
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			_paused = true;
			_waiting = false;
			_seeking = false;
			_timeline.Dispose();
			if (_player != null)
			{
				_player.seekCompleted -= OnSeekCompleted;
				_player.frameReady -= OnFrameReady;
				_player.errorReceived -= OnVideoError;
				if (_player.isPrepared)
				{
					_player.Pause();
				}
				_player.sendFrameReadyEvents = _previousFrameReadyEvents;
				_player.isLooping = _previousLooping;
				_player.timeReference = _previousTimeReference;
				_player.timeUpdateMode = _previousTimeUpdateMode;
				if (_player.canSetPlaybackSpeed)
				{
					_player.playbackSpeed = _previousPlaybackSpeed;
				}
				if (_player.canSetSkipOnDrop)
				{
					_player.skipOnDrop = _previousSkipOnDrop;
				}
			}
		}
	}
}
