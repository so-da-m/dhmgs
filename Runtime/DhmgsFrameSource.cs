using UnityEngine;
using UnityEngine.Video;

namespace Mnnh.Dhmgs
{
	/// <summary>재생 타이밍 추상화. 플레이어는 프레임 인덱스가 영상에서 오는지 시계에서 오는지 모른다.</summary>
	public interface IDhmgsFrameSource
	{
		void Begin(int frameCount, float fps);

		/// <summary>재생 속도 배율(1=기본). 영상은 playbackSpeed, 시계는 시간 배율.</summary>
		void SetSpeed(float speed);

		int CurrentFrame { get; }
	}

	/// <summary>영상 없을 때: fps 기반 시계. 기본 루프.</summary>
	public sealed class DhmgsTimeClock : IDhmgsFrameSource
	{
		private readonly bool _loop;
		private int _frameCount = 1;
		private float _fps = 30f;
		private double _startTime;
		private float _speed = 1f;

		public DhmgsTimeClock(bool loop = true)
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
			// 현재 프레임 위치를 유지하며 속도 변경(연속성 보장 — 시작 시각을 재기준화).
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

	/// <summary>영상 있을 때: VideoPlayer.frame을 따른다. frame i = frames[i] 계약.</summary>
	public sealed class DhmgsVideoClock : IDhmgsFrameSource
	{
		private readonly VideoPlayer _player;
		private int _frameCount = 1;

		public DhmgsVideoClock(VideoPlayer player)
		{
			_player = player;
		}

		public void Begin(int frameCount, float fps)
		{
			_frameCount = Mathf.Max(1, frameCount);
		}

		public void SetSpeed(float speed)
		{
			if (_player != null)
			{
				_player.playbackSpeed = Mathf.Max(0f, speed);
			}
		}

		public int CurrentFrame => Mathf.Clamp((int)_player.frame, 0, _frameCount - 1);
	}
}
