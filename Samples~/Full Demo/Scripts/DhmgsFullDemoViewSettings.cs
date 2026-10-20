using System;
using System.Collections.Generic;

using UnityEngine;

namespace Mnnh.Dhmgs.Samples
{
	public sealed class DhmgsFullDemoViewSettings : MonoBehaviour
	{
		[SerializeField] private DhmgsFullDemoPlayback _playback;
		[SerializeField] private DhmgsOrbitCamera _mainViewCamera;
		[SerializeField] private DhmgsAvatarPreviewCamera _humanoidPreviewCamera;

		private readonly List<string> _avatarNames = new List<string> { "None" };
		private readonly List<string> _clothesNames = new List<string> { "None" };
		private DhmgsMotionPlayer Player => _playback != null ? _playback.Player : null;

		public event Action Changed;

		public IReadOnlyList<string> AvatarNames => _avatarNames;
		public IReadOnlyList<string> ClothesNames => _clothesNames;
		public int AvatarIndex => Player != null ? Player.ActiveAvatar + 1 : 0;
		public int ClothesIndex => Player != null ? Player.ActiveClothes + 1 : 0;
		public float AvatarOpacity { get; private set; } = 1f;
		public float ClothesOpacity { get; private set; } = 1f;
		public float VideoOpacity { get; private set; } = 1f;
		public bool ShowMainBones { get; private set; } = true;
		public bool ShowMainBoneNames { get; private set; }
		public bool ShowMainGround { get; private set; } = true;
		public float MainGroundHeight { get; private set; } = -0.85f;
		public bool ShowHumanoidBones { get; private set; } = true;
		public bool ShowHumanoidBoneNames { get; private set; }
		public bool ShowHumanoidGround { get; private set; } = true;
		public float HumanoidGroundHeight { get; private set; } = -1.35f;
		public bool CanSetVideoOpacity => _playback != null && _playback.CanSetVideoOpacity;

		private void OnEnable()
		{
			if (_playback != null)
			{
				_playback.MotionLoaded += OnMotionLoaded;
			}
		}

		private void OnDisable()
		{
			if (_playback != null)
			{
				_playback.MotionLoaded -= OnMotionLoaded;
			}
		}

		private void Start()
		{
			RefreshSelections();
			ApplyOpacities();
			Changed?.Invoke();
		}

		public void OnMotionLoaded()
		{
			ResetMainCamera();
			_humanoidPreviewCamera?.Relock();
			RefreshSelections();
			ApplyOpacities();
			Changed?.Invoke();
		}

		private void RefreshSelections()
		{
			_avatarNames.Clear();
			_avatarNames.Add("None");
			_clothesNames.Clear();
			_clothesNames.Add("None");
			if (Player == null)
			{
				return;
			}

			for (int index = 0; index < Player.AvatarCount; index++)
			{
				_avatarNames.Add(Player.AvatarName(index));
			}
			for (int index = 0; index < Player.ClothesCount; index++)
			{
				_clothesNames.Add(Player.ClothesName(index));
			}
		}

		public void SetAvatarIndex(int value)
		{
			Player?.SetAvatarIndex(Mathf.Clamp(value, 0, Player.AvatarCount) - 1);
			ApplyOpacity(Player?.ActiveAvatarDriver, AvatarOpacity);
			Changed?.Invoke();
		}

		public void ResetMainCamera()
		{
			_mainViewCamera?.ResetView();
		}

		public void ResetHumanoidPreviewCamera()
		{
			_humanoidPreviewCamera?.ResetView();
		}

		public void SetClothesIndex(int value)
		{
			Player?.SetClothesIndex(Mathf.Clamp(value, 0, Player.ClothesCount) - 1);
			ApplyOpacity(Player?.ActiveClothesDriver, ClothesOpacity);
			Changed?.Invoke();
		}

		public void SetAvatarOpacity(float value)
		{
			AvatarOpacity = Mathf.Clamp01(value);
			ApplyOpacity(Player?.ActiveAvatarDriver, AvatarOpacity);
			Changed?.Invoke();
		}

		public void SetClothesOpacity(float value)
		{
			ClothesOpacity = Mathf.Clamp01(value);
			ApplyOpacity(Player?.ActiveClothesDriver, ClothesOpacity);
			Changed?.Invoke();
		}

		public void SetVideoOpacity(float value)
		{
			VideoOpacity = Mathf.Clamp01(value);
			_playback?.SetVideoOpacity(VideoOpacity);
			Changed?.Invoke();
		}

		public void SetMainBones(bool value)
		{
			ShowMainBones = value;
			Changed?.Invoke();
		}

		public void SetMainBoneNames(bool value)
		{
			ShowMainBoneNames = value;
			Changed?.Invoke();
		}

		public void SetHumanoidBones(bool value)
		{
			ShowHumanoidBones = value;
			Changed?.Invoke();
		}

		public void SetHumanoidBoneNames(bool value)
		{
			ShowHumanoidBoneNames = value;
			Changed?.Invoke();
		}

		public void SetMainGround(bool value)
		{
			ShowMainGround = value;
			Changed?.Invoke();
		}

		public void SetMainGroundHeight(float value)
		{
			if (float.IsNaN(value) || float.IsInfinity(value))
			{
				return;
			}
			MainGroundHeight = value;
			Changed?.Invoke();
		}

		public void SetHumanoidGround(bool value)
		{
			ShowHumanoidGround = value;
			Changed?.Invoke();
		}

		public void SetHumanoidGroundHeight(float value)
		{
			if (float.IsNaN(value) || float.IsInfinity(value))
			{
				return;
			}
			HumanoidGroundHeight = value;
			Changed?.Invoke();
		}

		private static void ApplyOpacity(DhmgsRigDriver driver, float value)
		{
			DhmgsSkinOpacity opacity = driver != null ? driver.GetComponent<DhmgsSkinOpacity>() : null;
			opacity?.SetOpacity(value);
		}

		private void ApplyOpacities()
		{
			ApplyOpacity(Player?.ActiveAvatarDriver, AvatarOpacity);
			ApplyOpacity(Player?.ActiveClothesDriver, ClothesOpacity);
			_playback?.SetVideoOpacity(VideoOpacity);
		}
	}
}
