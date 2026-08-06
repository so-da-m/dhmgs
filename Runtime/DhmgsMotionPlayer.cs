using System.Collections.Generic;

using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// Plays an already imported DhmgsMotionAsset on the selected avatar and clothes rigs.
	/// It has no server, importer, or UI responsibilities.
	/// </summary>
	public sealed class DhmgsMotionPlayer : MonoBehaviour, IDhmgsFramingTarget, IDhmgsFramingBounds
	{
		[SerializeField] private List<DhmgsRigDriver> _avatars = new List<DhmgsRigDriver>();
		[SerializeField] private List<DhmgsRigDriver> _clothes = new List<DhmgsRigDriver>();
		[SerializeField] private int _defaultAvatar = 0;
		[SerializeField] private int _defaultClothes = -1;
		[SerializeField] private Camera _camera;

		private DhmgsMotionAsset _motion;
		private IDhmgsPlaybackClock _clock;
		private bool _playing;
		private int _activeAvatar = -1;
		private int _activeClothes = -1;

		public int AvatarCount => _avatars.Count;
		public int ClothesCount => _clothes.Count;
		public int ActiveAvatar => _activeAvatar;
		public int ActiveClothes => _activeClothes;
		public bool IsPlaying => _playing;
		public DhmgsMotionAsset Motion => _motion;
		public int CurrentFrame => _clock?.CurrentFrame ?? 0;

		public DhmgsRigDriver ActiveAvatarDriver => DriverAt(_avatars, _activeAvatar);
		public DhmgsRigDriver ActiveClothesDriver => DriverAt(_clothes, _activeClothes);
		public DhmgsRigDriver ActiveSubject => ActiveAvatarDriver != null ? ActiveAvatarDriver : ActiveClothesDriver;

		public Transform Pivot
		{
			get
			{
				DhmgsRigDriver subject = _playing ? ActiveSubject : null;
				if (subject == null)
				{
					return null;
				}
				return subject.Pelvis != null ? subject.Pelvis : subject.transform;
			}
		}

		public bool TryGetFramingBounds(out Bounds bounds)
		{
			bounds = default;
			bool found = false;
			DhmgsRigDriver avatar = ActiveAvatarDriver;
			DhmgsRigDriver clothes = ActiveClothesDriver;
			DhmgsFramingBoundsUtility.Encapsulate(
				avatar != null ? avatar.transform : null,
				ref bounds,
				ref found);
			DhmgsFramingBoundsUtility.Encapsulate(
				clothes != null ? clothes.transform : null,
				ref bounds,
				ref found);
			return _playing && found;
		}

		private void Awake()
		{
			HideAll(_avatars);
			HideAll(_clothes);
			SetAvatarIndex(_defaultAvatar);
			SetClothesIndex(_defaultClothes);
		}

		private void Update()
		{
			if (!_playing || _motion == null || _clock == null)
			{
				return;
			}
			ApplyFrame(_clock.CurrentFrame);
		}

		public void Play(DhmgsMotionAsset motion, IDhmgsPlaybackClock clock)
		{
			_motion = motion;
			_clock = clock;
			if (motion == null || !motion.IsValid || clock == null)
			{
				_playing = false;
				return;
			}

			clock.Begin(motion.frameCount, motion.fps);
			_playing = true;
			ApplyFrame(0);
		}

		public void Stop()
		{
			_playing = false;
		}

		public void SetPlaybackSpeed(float speed)
		{
			_clock?.SetSpeed(speed);
		}

		public string AvatarName(int index)
		{
			return DriverName(_avatars, index);
		}

		public string ClothesName(int index)
		{
			return DriverName(_clothes, index);
		}

		public void SetAvatarIndex(int index)
		{
			_activeAvatar = Activate(_avatars, _activeAvatar, index);
			BindCamera(_avatars, _activeAvatar);
			ReapplyCurrentFrame();
		}

		public void SetClothesIndex(int index)
		{
			_activeClothes = Activate(_clothes, _activeClothes, index);
			BindCamera(_clothes, _activeClothes);
			ReapplyCurrentFrame();
		}

		private void ApplyFrame(int frameIndex)
		{
			ApplyTo(_avatars, _activeAvatar, frameIndex);
			ApplyTo(_clothes, _activeClothes, frameIndex);
		}

		private void ApplyTo(List<DhmgsRigDriver> drivers, int index, int frameIndex)
		{
			DhmgsRigDriver driver = DriverAt(drivers, index);
			if (_motion != null && driver != null)
			{
				driver.ApplyFrame(_motion, frameIndex);
			}
		}

		private void ReapplyCurrentFrame()
		{
			if (_playing && _motion != null && _clock != null)
			{
				ApplyFrame(_clock.CurrentFrame);
			}
		}

		private void BindCamera(List<DhmgsRigDriver> drivers, int index)
		{
			DhmgsRigDriver driver = DriverAt(drivers, index);
			if (driver != null && _camera != null)
			{
				driver.SetCamera(_camera);
			}
		}

		private static int Activate(List<DhmgsRigDriver> drivers, int current, int next)
		{
			DhmgsRigDriver currentDriver = DriverAt(drivers, current);
			if (currentDriver != null)
			{
				currentDriver.gameObject.SetActive(false);
			}

			DhmgsRigDriver nextDriver = DriverAt(drivers, next);
			if (nextDriver == null)
			{
				return -1;
			}

			nextDriver.gameObject.SetActive(true);
			return next;
		}

		private static void HideAll(List<DhmgsRigDriver> drivers)
		{
			foreach (DhmgsRigDriver driver in drivers)
			{
				if (driver != null)
				{
					driver.gameObject.SetActive(false);
				}
			}
		}

		private static string DriverName(List<DhmgsRigDriver> drivers, int index)
		{
			DhmgsRigDriver driver = DriverAt(drivers, index);
			return driver != null ? driver.gameObject.name : "None";
		}

		private static DhmgsRigDriver DriverAt(List<DhmgsRigDriver> drivers, int index)
		{
			return index >= 0 && index < drivers.Count ? drivers[index] : null;
		}
	}
}
