using System.Collections;
using System.Collections.Generic;

using TMPro;

using UnityEngine;
using UnityEngine.UI;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 씬 단일 표면: attempt_id 입력 → 다운로드(비동기) → SO + 클립2 + json 디스크 베이크 → 재생.
	/// 베이크는 에디터 전용이라 #if UNITY_EDITOR로 감싼다(이 도구는 에디터 플레이 모드 전용).
	/// 영상이 있으면 오버레이 동기, 없으면 fps 시계. 어느 리그(아바타/의상)에 재생할지는 컨트롤러가 정한다.
	/// 진행/오류는 콘솔과 UI 로그 필드에 함께 출력한다(디자이너가 씬에서 바로 확인).
	/// </summary>
	public sealed class DhmgsRuntimeUI : MonoBehaviour
	{
		[Header("Server")]
		[SerializeField] private string _baseUrl = DhmgsBakeService.DefaultBaseUrl;
		[SerializeField] private int _timeoutSec = DhmgsBakeService.DefaultTimeoutSec;

		[Header("UI")]
		[SerializeField] private TMP_InputField _idInput;
		[SerializeField] private Button _loadButton;
		[Tooltip("상태/오류 표시용 읽기전용 InputField(선택). 최신 줄이 맨 위에 쌓인다. 비우면 콘솔만.")]
		[SerializeField] private TMP_InputField _logField;

		[Header("Playback")]
		[Tooltip("아바타/의상 선택을 들고 선택된 리그에 모션을 적용하는 컨트롤러.")]
		[SerializeField] private DhmgsPlaybackController _player;
		[Tooltip("영상 위 정합 오버레이. 없으면 영상 없이 fps 시계로 재생.")]
		[SerializeField] private DhmgsCameraOverlay _overlay;
		[Tooltip("영상 불투명도 슬라이더(선택). 0=투명, 1=불투명. 오버레이 SetVideoAlpha에 연결.")]
		[SerializeField] private Slider _videoOpacitySlider;
		[Tooltip("휴머노이드 프리뷰(선택). 베이크된 humanoid 클립을 지정 프리팹에 재생해 별도 RawImage에 띄움. 에디터 전용.")]
		[SerializeField] private DhmgsHumanoidPreview _humanoidPreview;
		[SerializeField] private bool _loop = true;

		[Header("Bake (클립용 리그)")]
		[SerializeField] private GameObject _genericModel;
		[SerializeField] private GameObject _humanoidModel;
		[SerializeField] private bool _bakeGeneric = true;
		[SerializeField] private bool _bakeHumanoid = true;

		private const int MaxLogLines = 12;

		private readonly List<string> _logLines = new List<string>();
		private Coroutine _running;

		public bool IsBusy => _running != null;

		private void OnEnable()
		{
			if (_logField != null)
			{
				_logField.readOnly = true;
			}
			if (_loadButton != null)
			{
				_loadButton.onClick.AddListener(Run);
			}
			if (_videoOpacitySlider != null && _overlay != null)
			{
				_videoOpacitySlider.onValueChanged.RemoveListener(_overlay.SetVideoAlpha);
				_videoOpacitySlider.onValueChanged.AddListener(_overlay.SetVideoAlpha);
				_overlay.SetVideoAlpha(_videoOpacitySlider.value);
			}
		}

		private void OnDisable()
		{
			if (_loadButton != null)
			{
				_loadButton.onClick.RemoveListener(Run);
			}
			if (_videoOpacitySlider != null && _overlay != null)
			{
				_videoOpacitySlider.onValueChanged.RemoveListener(_overlay.SetVideoAlpha);
			}
		}

		/// <summary>입력 필드의 id로 실행 (버튼 onClick에 자동 연결).</summary>
		public void Run()
		{
			if (_idInput == null || !int.TryParse(_idInput.text, out int attemptId))
			{
				LogError("attempt_id를 정수로 입력하세요.");
				return;
			}
			LoadAndPlay(attemptId);
		}

		/// <summary>코드/고정 id용.</summary>
		public void LoadAndPlay(int attemptId)
		{
			if (_running != null)
			{
				LogError("이미 처리 중입니다.");
				return;
			}
			_running = StartCoroutine(Flow(attemptId));
		}

		private IEnumerator Flow(int attemptId)
		{
			Log($"[{attemptId}] 다운로드 중...");

			DhmgsBakeResult result = new DhmgsBakeResult();
			yield return DhmgsBakeService.DownloadAndBuild(_baseUrl, attemptId, _timeoutSec, result);

			if (!result.ok)
			{
				LogError($"[{attemptId}] 실패: {result.error}");
				_running = null;
				yield break;
			}
			if (_player == null)
			{
				LogError("재생 컨트롤러(DhmgsPlaybackController)가 지정되지 않음");
				_running = null;
				yield break;
			}

			// 영상은 자산이 아니라 persistentDataPath에 떨군다(오버레이가 경로로 재생).
			string videoPath = result.videoMp4 != null ? DhmgsBakeService.WriteVideo(attemptId, result.videoMp4) : null;

#if UNITY_EDITOR
			// 디스크 베이크: SO + 클립2 + json. result.asset이 영구 자산이 된다.
			DhmgsAssetBaker.Persist(result, attemptId, _genericModel, _humanoidModel, _bakeGeneric, _bakeHumanoid, out AnimationClip humanoidClip, out string summary);
			Log(summary);
#endif

			IDhmgsFrameSource source = null;
			if (_overlay != null && videoPath != null)
			{
				yield return _overlay.Activate(result.asset, videoPath, clock => source = clock);
			}
			if (source == null)
			{
				source = new DhmgsTimeClock(_loop);
			}

			// 리그 바인딩은 플레이어가 스스로 한다(Play → EnsureBound).
			_player.Play(result.asset, source);
#if UNITY_EDITOR
			// 휴머노이드 프리뷰: 베이크된 muscle 클립을 같은 클럭으로 동기 재생(에디터 전용 산출물).
			if (_humanoidPreview != null)
			{
				_humanoidPreview.Play(humanoidClip, source, result.asset.fps);
			}
#endif
			Log($"[{attemptId}] 재생 시작 ({result.asset.frameCount}프레임 @ {result.asset.fps:F2}fps)");
			_running = null;
		}

		private void Log(string message)
		{
			Debug.Log($"[DHMGS] {message}");
			AppendLog(message);
		}

		private void LogError(string message)
		{
			Debug.LogError($"[DHMGS] {message}");
			AppendLog($"[오류] {message}");
		}

		// 최신 줄을 맨 위에 쌓고 MaxLogLines로 제한한다. 필드 미지정이면 콘솔만.
		private void AppendLog(string line)
		{
			if (_logField == null)
			{
				return;
			}
			_logLines.Insert(0, line);
			if (_logLines.Count > MaxLogLines)
			{
				_logLines.RemoveRange(MaxLogLines, _logLines.Count - MaxLogLines);
			}
			_logField.text = string.Join("\n", _logLines);
		}
	}
}
