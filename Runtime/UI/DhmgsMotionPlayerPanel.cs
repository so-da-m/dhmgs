using System.Collections.Generic;

using TMPro;

using UnityEngine;
using UnityEngine.UI;

namespace Mnnh.Dhmgs
{
	[DefaultExecutionOrder(-1000)]
	public sealed class DhmgsMotionPlayerPanel : MonoBehaviour
	{
		[SerializeField] private DhmgsMotionAsset _motion;
		[SerializeField] private DhmgsMotionPlayer _player;
		[SerializeField] private bool _loop = true;

		[Header("Controls")]
		[SerializeField] private Button _playButton;
		[SerializeField] private Button _stopButton;
		[SerializeField] private TMP_Dropdown _avatarDropdown;
		[SerializeField] private TMP_Dropdown _clothesDropdown;
		[SerializeField] private Slider _avatarOpacity;
		[SerializeField] private Slider _clothesOpacity;
		[SerializeField] private Slider _speed;

		private DhmgsRealtimeClock _clock;

		public DhmgsMotionAsset Motion
		{
			get => _motion;
			set => _motion = value;
		}

		private void OnEnable()
		{
			BindDropdowns();
			if (_playButton != null)
			{
				_playButton.onClick.AddListener(Play);
			}
			if (_stopButton != null)
			{
				_stopButton.onClick.AddListener(Stop);
			}
			if (_avatarDropdown != null)
			{
				_avatarDropdown.onValueChanged.AddListener(OnAvatarChanged);
			}
			if (_clothesDropdown != null)
			{
				_clothesDropdown.onValueChanged.AddListener(OnClothesChanged);
			}
			if (_avatarOpacity != null)
			{
				_avatarOpacity.onValueChanged.AddListener(OnAvatarOpacityChanged);
			}
			if (_clothesOpacity != null)
			{
				_clothesOpacity.onValueChanged.AddListener(OnClothesOpacityChanged);
			}
			if (_speed != null)
			{
				_speed.onValueChanged.AddListener(OnSpeedChanged);
			}
		}

		private void OnDisable()
		{
			Stop();
			if (_playButton != null)
			{
				_playButton.onClick.RemoveListener(Play);
			}
			if (_stopButton != null)
			{
				_stopButton.onClick.RemoveListener(Stop);
			}
			if (_avatarDropdown != null)
			{
				_avatarDropdown.onValueChanged.RemoveListener(OnAvatarChanged);
			}
			if (_clothesDropdown != null)
			{
				_clothesDropdown.onValueChanged.RemoveListener(OnClothesChanged);
			}
			if (_avatarOpacity != null)
			{
				_avatarOpacity.onValueChanged.RemoveListener(OnAvatarOpacityChanged);
			}
			if (_clothesOpacity != null)
			{
				_clothesOpacity.onValueChanged.RemoveListener(OnClothesOpacityChanged);
			}
			if (_speed != null)
			{
				_speed.onValueChanged.RemoveListener(OnSpeedChanged);
			}
		}

		public void Play()
		{
			if (_player == null || _motion == null || !_motion.IsValid)
			{
				Debug.LogError("[DHMGS] Assign a valid imported motion and motion player.", this);
				return;
			}

			Stop();
			_clock = new DhmgsRealtimeClock(_loop);
			_clock.Begin(_motion.frameCount, _motion.fps);
			_player.Play(_motion, _clock);
			if (_speed != null)
			{
				_clock.SetSpeed(_speed.value);
			}
		}

		public void Stop()
		{
			if (_clock != null && _player != null && ReferenceEquals(_player.Clock, _clock))
			{
				_player.Stop();
			}
			_clock?.Dispose();
			_clock = null;
		}

		private void Update()
		{
			if (_clock == null)
			{
				return;
			}
			if (_player == null || !ReferenceEquals(_player.Clock, _clock))
			{
				Stop();
				return;
			}
			_clock.Tick();
		}

		private void BindDropdowns()
		{
			if (_player == null)
			{
				return;
			}

			BindDropdown(_avatarDropdown, _player.AvatarCount, _player.AvatarName, _player.ActiveAvatar);
			BindDropdown(_clothesDropdown, _player.ClothesCount, _player.ClothesName, _player.ActiveClothes);
		}

		private static void BindDropdown(TMP_Dropdown dropdown, int count, System.Func<int, string> nameAt, int active)
		{
			if (dropdown == null)
			{
				return;
			}

			List<string> options = new List<string> { "None" };
			for (int index = 0; index < count; index++)
			{
				options.Add(nameAt(index));
			}
			dropdown.ClearOptions();
			dropdown.AddOptions(options);
			dropdown.SetValueWithoutNotify(active + 1);
			dropdown.RefreshShownValue();
		}

		private void OnAvatarChanged(int value)
		{
			_player?.SetAvatarIndex(value - 1);
			ApplyOpacity(_player?.ActiveAvatarDriver, _avatarOpacity);
		}

		private void OnClothesChanged(int value)
		{
			_player?.SetClothesIndex(value - 1);
			ApplyOpacity(_player?.ActiveClothesDriver, _clothesOpacity);
		}

		private void OnAvatarOpacityChanged(float value)
		{
			ApplyOpacity(_player?.ActiveAvatarDriver, _avatarOpacity);
		}

		private void OnClothesOpacityChanged(float value)
		{
			ApplyOpacity(_player?.ActiveClothesDriver, _clothesOpacity);
		}

		private void OnSpeedChanged(float value)
		{
			_clock?.SetSpeed(value);
		}

		private static void ApplyOpacity(DhmgsRigDriver driver, Slider slider)
		{
			if (driver == null || slider == null)
			{
				return;
			}

			DhmgsSkinOpacity opacity = driver.GetComponent<DhmgsSkinOpacity>();
			if (opacity != null)
			{
				opacity.SetOpacity(slider.value);
			}
		}
	}
}
