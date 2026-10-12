using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace Mnnh.Dhmgs.Samples
{
	/// <summary>View selection, visibility and camera controls, independent of transport UI.</summary>
	public sealed class DhmgsFullDemoViewSettings : MonoBehaviour
	{
		[SerializeField] private DhmgsMotionPlayer _player;
		[SerializeField] private DhmgsOrbitCamera _mainViewCamera;
		[SerializeField] private DhmgsFixedAvatarCamera _humanoidPreviewCamera;
		[SerializeField] private Button _mainResetCameraButton;
		[SerializeField] private Button _humanoidResetCameraButton;
		[SerializeField] private Dropdown _avatarDropdown;
		[SerializeField] private Dropdown _clothesDropdown;
		[SerializeField] private Slider _avatarOpacitySlider;
		[SerializeField] private Slider _clothesOpacitySlider;
		[SerializeField] private Slider _videoOpacitySlider;
		[SerializeField] private Text _avatarOpacityValue;
		[SerializeField] private Text _clothesOpacityValue;
		[SerializeField] private Text _videoOpacityValue;

		private DhmgsSampleImportContext _importContext;

		public void SetImportContext(DhmgsSampleImportContext context)
		{
			_importContext = context;
			if (_videoOpacitySlider != null)
			{
				_videoOpacitySlider.interactable = DhmgsFullDemoImportGateway.IsAvailable &&
					DhmgsFullDemoImportGateway.SupportsVideoPreview(context);
			}
		}

		private void OnEnable()
		{
			_mainResetCameraButton?.onClick.AddListener(ResetMainCamera);
			_humanoidResetCameraButton?.onClick.AddListener(ResetHumanoidPreviewCamera);
			_avatarDropdown?.onValueChanged.AddListener(OnAvatarChanged);
			_clothesDropdown?.onValueChanged.AddListener(OnClothesChanged);
			_avatarOpacitySlider?.onValueChanged.AddListener(OnAvatarOpacityChanged);
			_clothesOpacitySlider?.onValueChanged.AddListener(OnClothesOpacityChanged);
			_videoOpacitySlider?.onValueChanged.AddListener(OnVideoOpacityChanged);
		}

		private void Start()
		{
			BindSelectors();
			RefreshControlValues();
		}

		private void OnDisable()
		{
			_mainResetCameraButton?.onClick.RemoveListener(ResetMainCamera);
			_humanoidResetCameraButton?.onClick.RemoveListener(ResetHumanoidPreviewCamera);
			_avatarDropdown?.onValueChanged.RemoveListener(OnAvatarChanged);
			_clothesDropdown?.onValueChanged.RemoveListener(OnClothesChanged);
			_avatarOpacitySlider?.onValueChanged.RemoveListener(OnAvatarOpacityChanged);
			_clothesOpacitySlider?.onValueChanged.RemoveListener(OnClothesOpacityChanged);
			_videoOpacitySlider?.onValueChanged.RemoveListener(OnVideoOpacityChanged);
		}

		public void OnMotionLoaded()
		{
			ResetMainCamera();
			_humanoidPreviewCamera?.Relock();
			RefreshControlValues();
		}

		private void BindSelectors()
		{
			if (_player == null)
			{
				return;
			}

			BindDropdown(_avatarDropdown, _player.AvatarCount, _player.AvatarName, _player.ActiveAvatar);
			BindDropdown(_clothesDropdown, _player.ClothesCount, _player.ClothesName, _player.ActiveClothes);
			if (_clothesDropdown != null)
			{
				_clothesDropdown.interactable = _player.ClothesCount > 0;
			}
			if (_clothesOpacitySlider != null)
			{
				_clothesOpacitySlider.interactable = _player.ClothesCount > 0;
			}
		}

		private static void BindDropdown(
			Dropdown dropdown,
			int count,
			Func<int, string> nameAt,
			int activeIndex)
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
			dropdown.SetValueWithoutNotify(Mathf.Clamp(activeIndex + 1, 0, options.Count - 1));
			dropdown.RefreshShownValue();
		}

		private void OnAvatarChanged(int value)
		{
			_player?.SetAvatarIndex(value - 1);
			OnAvatarOpacityChanged(_avatarOpacitySlider != null ? _avatarOpacitySlider.value : 1f);
		}

		public void ResetMainCamera()
		{
			_mainViewCamera?.ResetView();
		}

		private void ResetHumanoidPreviewCamera()
		{
			_humanoidPreviewCamera?.ResetView();
		}

		private void OnClothesChanged(int value)
		{
			_player?.SetClothesIndex(value - 1);
			OnClothesOpacityChanged(_clothesOpacitySlider != null ? _clothesOpacitySlider.value : 1f);
		}

		private void OnAvatarOpacityChanged(float value)
		{
			ApplyOpacity(_player?.ActiveAvatarDriver, value);
			SetValue(_avatarOpacityValue, value);
		}

		private void OnClothesOpacityChanged(float value)
		{
			ApplyOpacity(_player?.ActiveClothesDriver, value);
			SetValue(_clothesOpacityValue, value);
		}

		private void OnVideoOpacityChanged(float value)
		{
			DhmgsFullDemoImportGateway.SetVideoOpacity(_importContext, value);
			SetValue(_videoOpacityValue, value);
		}

		private static void ApplyOpacity(DhmgsRigDriver driver, float value)
		{
			DhmgsSkinOpacity opacity = driver != null ? driver.GetComponent<DhmgsSkinOpacity>() : null;
			opacity?.SetOpacity(value);
		}

		private static void SetValue(Text label, float value)
		{
			if (label != null)
			{
				label.text = value.ToString("F2");
			}
		}

		private void RefreshControlValues()
		{
			OnAvatarOpacityChanged(_avatarOpacitySlider != null ? _avatarOpacitySlider.value : 1f);
			OnClothesOpacityChanged(_clothesOpacitySlider != null ? _clothesOpacitySlider.value : 1f);
			OnVideoOpacityChanged(_videoOpacitySlider != null ? _videoOpacitySlider.value : 1f);
		}
	}
}
