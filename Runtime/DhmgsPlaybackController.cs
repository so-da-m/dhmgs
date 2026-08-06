using System.Collections.Generic;

using TMPro;

using UnityEngine;
using UnityEngine.UI;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 선택된 아바타 1개 + 의상 1개에 같은 모션을 동시에 적용한다(둘 다 SMPL-X 리그라 그냥 겹쳐 재생).
	/// 각 리스트에서 None(-1)이면 적용/표시 안 함. 클럭은 컨트롤러가 소유하고,
	/// 활성 리그의 DhmgsAvatarPlayer.ApplyFrame을 매 프레임 호출한다(리그별 바인딩/정합은 그쪽이 담당).
	/// </summary>
	public sealed class DhmgsPlaybackController : MonoBehaviour, IDhmgsFramingTarget
	{
		[Tooltip("아바타 리그 목록(각 리그의 DhmgsAvatarPlayer). 드롭다운 None=미선택.")]
		[SerializeField] private List<DhmgsAvatarPlayer> _avatars = new List<DhmgsAvatarPlayer>();

		[Tooltip("의상 리그 목록(SMPL-X, 각 리그의 DhmgsAvatarPlayer). 드롭다운 None=미선택.")]
		[SerializeField] private List<DhmgsAvatarPlayer> _clothes = new List<DhmgsAvatarPlayer>();

		[Tooltip("시작 선택 인덱스(-1=None). 드롭다운이 있으면 드롭다운 값이 표시에 반영된다.")]
		[SerializeField] private int _defaultAvatar = 0;
		[SerializeField] private int _defaultClothes = -1;

		[Tooltip("정합용 카메라. 활성 리그에 주입한다. 비우면 각 리그가 Camera.main 사용.")]
		[SerializeField] private Camera _camera;

		[Header("UI (선택)")]
		[Tooltip("아바타 선택 드롭다운. 컨트롤러가 None+리그이름으로 채우고 연결한다.")]
		[SerializeField] private TMP_Dropdown _avatarDropdown;
		[Tooltip("의상 선택 드롭다운.")]
		[SerializeField] private TMP_Dropdown _clothesDropdown;

		[Tooltip("아바타 피부 투명도 슬라이더(선택). 활성 아바타의 DhmgsSkinOpacity에 적용(0=투명,1=원본).")]
		[SerializeField] private Slider _opacitySlider;

		[Tooltip("의상 투명도 슬라이더(선택). 활성 의상의 DhmgsSkinOpacity에 적용(0=투명,1=원본).")]
		[SerializeField] private Slider _clothesOpacitySlider;

		[Tooltip("재생 속도 슬라이더(선택). 값=배율(1=기본). 영상은 playbackSpeed, 영상 없으면 시간 배율.")]
		[SerializeField] private Slider _speedSlider;

		private DhmgsMotionAsset _asset;
		private IDhmgsFrameSource _source;
		private bool _playing;
		private int _activeAvatar = -1;
		private int _activeClothes = -1;

		public int AvatarCount => _avatars.Count;
		public int ClothesCount => _clothes.Count;
		public int ActiveAvatar => _activeAvatar;
		public int ActiveClothes => _activeClothes;
		public bool IsPlaying => _playing;

		/// <summary>현재 활성 아바타 플레이어(None/범위밖이면 null).</summary>
		public DhmgsAvatarPlayer ActiveAvatarPlayer => PlayerAt(_avatars, _activeAvatar);

		/// <summary>현재 활성 의상 플레이어(None/범위밖이면 null).</summary>
		public DhmgsAvatarPlayer ActiveClothesPlayer => PlayerAt(_clothes, _activeClothes);

		/// <summary>프레이밍 대상: 활성 아바타 우선, 없으면 활성 의상.</summary>
		public DhmgsAvatarPlayer ActiveSubject => ActiveAvatarPlayer != null ? ActiveAvatarPlayer : ActiveClothesPlayer;

		/// <summary>FixedAvatarCamera가 비출 기준 Transform(활성 아바타 pelvis). 재생 중 + 활성 대상 있을 때만, 아니면 null.</summary>
		public Transform Pivot
		{
			get
			{
				if (!_playing)
				{
					return null;
				}
				DhmgsAvatarPlayer subject = ActiveSubject;
				if (subject == null)
				{
					return null;
				}
				return subject.Pelvis != null ? subject.Pelvis : subject.transform;
			}
		}

		public string AvatarName(int index)
		{
			return RigName(_avatars, index);
		}

		public string ClothesName(int index)
		{
			return RigName(_clothes, index);
		}

		private void Awake()
		{
			HideAll(_avatars);
			HideAll(_clothes);
			SetAvatarIndex(_defaultAvatar);
			SetClothesIndex(_defaultClothes);
		}

		private void OnEnable()
		{
			BindDropdown(_avatarDropdown, _avatars, _activeAvatar, OnAvatarDropdownChanged);
			BindDropdown(_clothesDropdown, _clothes, _activeClothes, OnClothesDropdownChanged);

			if (_opacitySlider != null)
			{
				_opacitySlider.onValueChanged.RemoveListener(OnAvatarOpacityChanged);
				_opacitySlider.onValueChanged.AddListener(OnAvatarOpacityChanged);
			}
			if (_clothesOpacitySlider != null)
			{
				_clothesOpacitySlider.onValueChanged.RemoveListener(OnClothesOpacityChanged);
				_clothesOpacitySlider.onValueChanged.AddListener(OnClothesOpacityChanged);
			}
			ApplyOpacity(_avatars, _activeAvatar, _opacitySlider);
			ApplyOpacity(_clothes, _activeClothes, _clothesOpacitySlider);
			if (_speedSlider != null)
			{
				_speedSlider.onValueChanged.RemoveListener(OnSpeedChanged);
				_speedSlider.onValueChanged.AddListener(OnSpeedChanged);
			}
		}

		private void OnDisable()
		{
			if (_avatarDropdown != null)
			{
				_avatarDropdown.onValueChanged.RemoveListener(OnAvatarDropdownChanged);
			}
			if (_clothesDropdown != null)
			{
				_clothesDropdown.onValueChanged.RemoveListener(OnClothesDropdownChanged);
			}
			if (_opacitySlider != null)
			{
				_opacitySlider.onValueChanged.RemoveListener(OnAvatarOpacityChanged);
			}
			if (_clothesOpacitySlider != null)
			{
				_clothesOpacitySlider.onValueChanged.RemoveListener(OnClothesOpacityChanged);
			}
			if (_speedSlider != null)
			{
				_speedSlider.onValueChanged.RemoveListener(OnSpeedChanged);
			}
		}

		// 드롭다운을 None+리그이름으로 채우고 현재 선택을 반영한 뒤 onValueChanged 연결.
		private static void BindDropdown(TMP_Dropdown dropdown, List<DhmgsAvatarPlayer> list, int active, UnityEngine.Events.UnityAction<int> handler)
		{
			if (dropdown == null)
			{
				return;
			}
			List<string> options = new List<string> { "None" };
			foreach (DhmgsAvatarPlayer player in list)
			{
				options.Add(player != null ? player.gameObject.name : "(missing)");
			}
			dropdown.ClearOptions();
			dropdown.AddOptions(options);
			dropdown.SetValueWithoutNotify(active + 1); // 0 = None
			dropdown.RefreshShownValue();

			dropdown.onValueChanged.RemoveListener(handler);
			dropdown.onValueChanged.AddListener(handler);
		}

		private void OnAvatarDropdownChanged(int value)
		{
			SetAvatarIndex(value - 1); // 0 = None → -1
		}

		private void OnClothesDropdownChanged(int value)
		{
			SetClothesIndex(value - 1);
		}

		/// <summary>선택된 아바타/의상에 SO를 frame source 타이밍으로 재생.</summary>
		public void Play(DhmgsMotionAsset asset, IDhmgsFrameSource source)
		{
			_asset = asset;
			_source = source;
			if (asset == null || source == null)
			{
				_playing = false;
				return;
			}
			source.Begin(asset.frameCount, asset.fps);
			ApplySpeed();
			_playing = true;
			ApplyFrame(0);
		}

		public void Stop()
		{
			_playing = false;
		}

		private void Update()
		{
			if (!_playing || _asset == null || _source == null)
			{
				return;
			}
			ApplyFrame(_source.CurrentFrame);
		}

		/// <summary>아바타 선택(-1=None). 이전 선택은 숨기고 새 선택을 표시한 뒤 현재 프레임 재적용.</summary>
		public void SetAvatarIndex(int index)
		{
			_activeAvatar = Activate(_avatars, _activeAvatar, index);
			BindCamera(_avatars, _activeAvatar);
			ApplyOpacity(_avatars, _activeAvatar, _opacitySlider);
			ReapplyCurrent();
		}

		/// <summary>의상 선택(-1=None).</summary>
		public void SetClothesIndex(int index)
		{
			_activeClothes = Activate(_clothes, _activeClothes, index);
			BindCamera(_clothes, _activeClothes);
			ApplyOpacity(_clothes, _activeClothes, _clothesOpacitySlider);
			ReapplyCurrent();
		}

		private void BindCamera(List<DhmgsAvatarPlayer> list, int index)
		{
			if (_camera != null && index >= 0 && index < list.Count && list[index] != null)
			{
				list[index].SetCamera(_camera);
			}
		}

		private void OnSpeedChanged(float value)
		{
			if (_source != null)
			{
				_source.SetSpeed(value);
			}
		}

		// 현재 시계에 슬라이더 속도 적용(재생 시작/속도 변경 시).
		private void ApplySpeed()
		{
			if (_source != null && _speedSlider != null)
			{
				_source.SetSpeed(_speedSlider.value);
			}
		}

		private void OnAvatarOpacityChanged(float value)
		{
			ApplyOpacity(_avatars, _activeAvatar, _opacitySlider);
		}

		private void OnClothesOpacityChanged(float value)
		{
			ApplyOpacity(_clothes, _activeClothes, _clothesOpacitySlider);
		}

		// 활성 리그의 DhmgsSkinOpacity에 슬라이더 값 적용(슬라이더/컴포넌트 없으면 무시).
		private void ApplyOpacity(List<DhmgsAvatarPlayer> list, int index, Slider slider)
		{
			if (slider == null || index < 0 || index >= list.Count || list[index] == null)
			{
				return;
			}
			DhmgsSkinOpacity skin = list[index].GetComponent<DhmgsSkinOpacity>();
			if (skin != null)
			{
				skin.SetOpacity(slider.value);
			}
		}

		private void ReapplyCurrent()
		{
			if (_playing && _asset != null && _source != null)
			{
				ApplyFrame(_source.CurrentFrame);
			}
		}

		private void ApplyFrame(int frame)
		{
			ApplyTo(_avatars, _activeAvatar, frame);
			ApplyTo(_clothes, _activeClothes, frame);
		}

		private void ApplyTo(List<DhmgsAvatarPlayer> list, int index, int frame)
		{
			if (_asset == null || index < 0 || index >= list.Count || list[index] == null)
			{
				return;
			}
			list[index].ApplyFrame(_asset, frame);
		}

		// 이전 활성 숨김 → 새 인덱스 표시. 범위 밖/null/-1이면 None(-1) 반환.
		private static int Activate(List<DhmgsAvatarPlayer> list, int current, int index)
		{
			if (current >= 0 && current < list.Count && list[current] != null)
			{
				list[current].gameObject.SetActive(false);
			}
			if (index < 0 || index >= list.Count || list[index] == null)
			{
				return -1;
			}
			list[index].gameObject.SetActive(true);
			return index;
		}

		private static void HideAll(List<DhmgsAvatarPlayer> list)
		{
			foreach (DhmgsAvatarPlayer player in list)
			{
				if (player != null)
				{
					player.gameObject.SetActive(false);
				}
			}
		}

		private static string RigName(List<DhmgsAvatarPlayer> list, int index)
		{
			if (index >= 0 && index < list.Count && list[index] != null)
			{
				return list[index].gameObject.name;
			}
			return "None";
		}

		private static DhmgsAvatarPlayer PlayerAt(List<DhmgsAvatarPlayer> list, int index)
		{
			if (index >= 0 && index < list.Count)
			{
				return list[index];
			}
			return null;
		}
	}
}
