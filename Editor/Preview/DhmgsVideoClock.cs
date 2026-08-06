using UnityEngine;
using UnityEngine.Video;

namespace Mnnh.Dhmgs
{
	/// <summary>Editor preview clock synchronized to a VideoPlayer frame.</summary>
	internal sealed class DhmgsVideoClock : IDhmgsPlaybackClock
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

		public int CurrentFrame => _player == null
			? 0
			: Mathf.Clamp((int)_player.frame, 0, _frameCount - 1);
	}
}
