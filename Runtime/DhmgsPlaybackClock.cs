using UnityEngine;

namespace Mnnh.Dhmgs
{
	public interface IDhmgsPlaybackClock : System.IDisposable
	{
		void Begin(int frameCount, float fps);

		void Tick();

		void SetSpeed(float speed);

		int CurrentFrame { get; }

		double CurrentTime { get; }
	}

	public interface IDhmgsSeekablePlaybackClock : IDhmgsPlaybackClock
	{
		bool CanSeek { get; }

		void SeekFrame(int frame);

		void SetPaused(bool paused);

		bool IsPaused { get; }

		bool IsSeeking { get; }

		bool IsWaiting { get; }

		string Error { get; }
	}

	public sealed class DhmgsRealtimeClock : IDhmgsSeekablePlaybackClock
	{
		private readonly bool _loop;
		private int _frameCount = 1;
		private float _fps = 30f;
		private double _anchorTime;
		private double _anchorPosition;
		private double _position;
		private int _sampledFrame = -1;
		private float _speed = 1f;
		private bool _paused = true;
		private bool _disposed;

		public bool IsPaused => _paused;
		public bool IsSeeking => false;
		public bool IsWaiting => false;
		public string Error => null;
		public bool CanSeek => !_disposed;
		public double Duration => _frameCount / (double)_fps;

		public DhmgsRealtimeClock(bool loop = true)
		{
			_loop = loop;
		}

		public void Begin(int frameCount, float fps)
		{
			if (_disposed)
			{
				return;
			}
			_frameCount = Mathf.Max(1, frameCount);
			_fps = fps > 0f && !float.IsInfinity(fps) ? fps : 30f;
			SetPosition(0d);
			_paused = false;
		}

		public void SetSpeed(float speed)
		{
			if (_disposed)
			{
				return;
			}
			CapturePosition();
			_speed = float.IsNaN(speed) || float.IsInfinity(speed) ? 1f : Mathf.Max(0.01f, speed);
		}

		public void SeekFrame(int frame)
		{
			SeekTime(Mathf.Clamp(frame, 0, _frameCount - 1) / (double)_fps);
		}

		public void SeekTime(double time)
		{
			if (_disposed || double.IsNaN(time) || double.IsInfinity(time))
			{
				return;
			}
			SetPosition(System.Math.Max(0d, System.Math.Min(time, Duration)));
		}

		public void SetPaused(bool paused)
		{
			if (_disposed || _paused == paused)
			{
				return;
			}
			CapturePosition();
			_paused = paused;
		}

		public int CurrentFrame => System.Math.Min(_frameCount - 1, (int)(CurrentTime * _fps + 1e-7d));

		public double CurrentTime => _position;

		public void Tick()
		{
			if (_disposed || _sampledFrame == Time.frameCount)
			{
				return;
			}
			_sampledFrame = Time.frameCount;
			_position = ReadPosition();
		}

		private void CapturePosition()
		{
			Tick();
			SetPosition(_position);
		}

		private void SetPosition(double position)
		{
			_position = position;
			_anchorPosition = position;
			_anchorTime = Time.unscaledTimeAsDouble;
			_sampledFrame = Time.frameCount;
		}

		private double ReadPosition()
		{
			if (_paused)
			{
				return _anchorPosition;
			}
			double position = _anchorPosition + (Time.unscaledTimeAsDouble - _anchorTime) * _speed;
			if (_loop)
			{
				return position % Duration;
			}
			if (position >= Duration)
			{
				_paused = true;
				_anchorPosition = Duration;
			}
			return System.Math.Max(0d, System.Math.Min(position, Duration));
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}
			SetPaused(true);
			_disposed = true;
		}
	}
}
