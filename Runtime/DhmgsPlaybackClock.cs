using UnityEngine;

namespace Mnnh.Dhmgs
{
	public interface IDhmgsPlaybackClock
	{
		void Begin(int frameCount, float fps);

		void SetSpeed(float speed);

		int CurrentFrame { get; }
	}

	public interface IDhmgsSeekablePlaybackClock : IDhmgsPlaybackClock
	{
		bool CanSeek { get; }

		void SeekFrame(int frame);

		void SetPaused(bool paused);

		bool IsPaused { get; }

		bool IsSeeking { get; }
	}

	public sealed class DhmgsRealtimeClock : IDhmgsSeekablePlaybackClock
	{
		private readonly bool _loop;
		private int _frameCount = 1;
		private float _fps = 30f;
		private double _anchorTime;
		private double _anchorFrame;
		private float _speed = 1f;
		private bool _paused;

		public bool IsPaused => _paused;
		public bool IsSeeking => false;
		public bool CanSeek => true;

		public DhmgsRealtimeClock(bool loop = true)
		{
			_loop = loop;
		}

		public void Begin(int frameCount, float fps)
		{
			_frameCount = Mathf.Max(1, frameCount);
			_fps = fps > 0f && !float.IsInfinity(fps) ? fps : 30f;
			_anchorTime = Time.timeAsDouble;
			_anchorFrame = 0d;
			_paused = false;
		}

		public void SetSpeed(float speed)
		{
			CapturePosition();
			_speed = float.IsNaN(speed) || float.IsInfinity(speed) ? 1f : Mathf.Max(0.01f, speed);
		}

		public void SeekFrame(int frame)
		{
			_anchorFrame = Mathf.Clamp(frame, 0, _frameCount - 1);
			_anchorTime = Time.timeAsDouble;
		}

		public void SetPaused(bool paused)
		{
			if (_paused == paused)
			{
				return;
			}
			CapturePosition();
			_paused = paused;
		}

		public int CurrentFrame => (int)ReadPosition();

		private void CapturePosition()
		{
			_anchorFrame = ReadPosition();
			_anchorTime = Time.timeAsDouble;
		}

		private double ReadPosition()
		{
			double position = _anchorFrame;
			if (!_paused)
			{
				position += (Time.timeAsDouble - _anchorTime) * _fps * _speed;
			}
			return _loop
				? ((position % _frameCount) + _frameCount) % _frameCount
				: System.Math.Max(0d, System.Math.Min(position, _frameCount - 1d));
		}
	}
}
