using UnityEngine;

namespace Mnnh.Dhmgs
{
	public interface IDhmgsPlaybackClock
	{
		void Begin(int frameCount, float fps);

		void SetSpeed(float speed);

		int CurrentFrame { get; }
	}

	public sealed class DhmgsRealtimeClock : IDhmgsPlaybackClock
	{
		private readonly bool _loop;
		private int _frameCount = 1;
		private float _fps = 30f;
		private double _startTime;
		private float _speed = 1f;

		public DhmgsRealtimeClock(bool loop = true)
		{
			_loop = loop;
		}

		public void Begin(int frameCount, float fps)
		{
			_frameCount = Mathf.Max(1, frameCount);
			_fps = fps > 0f ? fps : 30f;
			_startTime = Time.timeAsDouble;
		}

		public void SetSpeed(float speed)
		{
			speed = Mathf.Max(0.01f, speed);
			double frames = (Time.timeAsDouble - _startTime) * _fps * _speed;
			_speed = speed;
			_startTime = Time.timeAsDouble - frames / (_fps * _speed);
		}

		public int CurrentFrame
		{
			get
			{
				int frame = (int)((Time.timeAsDouble - _startTime) * _fps * _speed);
				if (_loop)
				{
					return ((frame % _frameCount) + _frameCount) % _frameCount;
				}
				return Mathf.Clamp(frame, 0, _frameCount - 1);
			}
		}
	}
}
