using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 베이크된 휴머노이드 muscle 클립을 지정한 Humanoid 아바타 프리팹에 재생한다(소환 + 재생만 담당).
	/// SMPL-X와 같은 IDhmgsFrameSource 클럭으로 time = CurrentFrame/fps를 매 프레임 직접 세팅 → 완전 동기.
	/// 인스턴스를 전용 레이어에 둬, 그 레이어만 비추는 FixedAvatarCamera 패널에만 보이게 한다(렌더/프레이밍은 그쪽 담당).
	/// 클립은 에디터 베이크 산출물이라 호출측(DhmgsRuntimeUI)이 에디터 가드 안에서 넘긴다. 재생 자체는 런타임 Playables.
	/// </summary>
	public sealed class DhmgsHumanoidPreview : MonoBehaviour, IDhmgsFramingTarget
	{
		[Header("아바타")]
		[Tooltip("재생할 Humanoid 아바타 프리팹(Animator + Humanoid Avatar 필수). Fetch 시 1회 인스턴스화.")]
		[SerializeField] private GameObject _avatarPrefab;

		[Tooltip("인스턴스를 둘 레이어 이름. 이 레이어만 비추는 FixedAvatarCamera로 찍고 메인 카메라 Culling Mask에선 뺀다.")]
		[SerializeField] private string _layer = "DhmgsHumanoid";

		[Tooltip("인스턴스 배치 위치(월드). 메인 씬과 겹치지 않게 멀찍이 두면 안전.")]
		[SerializeField] private Vector3 _spawnPosition = new Vector3(1000f, 0f, 0f);

		private GameObject _instance;
		private Animator _animator;
		private Transform _pivot;
		private PlayableGraph _graph;
		private AnimationClipPlayable _clipPlayable;
		private bool _graphValid;
		private IDhmgsFrameSource _source;
		private float _fps = 30f;

		/// <summary>FixedAvatarCamera가 비출 기준 Transform(hips). 인스턴스화+재생 전이면 null.</summary>
		public Transform Pivot => (_instance != null && _source != null) ? _pivot : null;

		/// <summary>휴머노이드 클립을 같은 클럭으로 재생 시작. clip은 에디터 베이크 산출물(호출측이 가드).</summary>
		public void Play(AnimationClip clip, IDhmgsFrameSource source, float fps)
		{
			if (_avatarPrefab == null || clip == null || source == null)
			{
				return;
			}
			EnsureInstance();
			if (_animator == null)
			{
				return;
			}
			_source = source;
			_fps = fps > 0f ? fps : 30f;
			BuildGraph(clip);
		}

		public void Stop()
		{
			_source = null;
		}

		private void Update()
		{
			if (_source == null || !_graphValid)
			{
				return;
			}
			_clipPlayable.SetTime(_source.CurrentFrame / _fps);
			_graph.Evaluate();
		}

		private void EnsureInstance()
		{
			if (_instance != null)
			{
				return;
			}
			_instance = Instantiate(_avatarPrefab, _spawnPosition, Quaternion.identity);
			_instance.name = $"{_avatarPrefab.name} (DhmgsHumanoidPreview)";
			SetLayerRecursive(_instance, ResolveLayer());

			_animator = _instance.GetComponentInChildren<Animator>();
			if (_animator != null)
			{
				// 컨트롤러가 있으면 Playable과 충돌하므로 끄고, 프리뷰는 제자리 재생(root motion 제거).
				_animator.runtimeAnimatorController = null;
				_animator.applyRootMotion = false;
				_animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
				_pivot = _animator.GetBoneTransform(HumanBodyBones.Hips);
			}
			if (_pivot == null)
			{
				_pivot = _instance.transform;
			}
			foreach (SkinnedMeshRenderer smr in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				smr.updateWhenOffscreen = true;
			}
		}

		private void BuildGraph(AnimationClip clip)
		{
			DestroyGraph();
			_graph = PlayableGraph.Create("DhmgsHumanoidPreview");
			_graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
			AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Animation", _animator);
			_clipPlayable = AnimationClipPlayable.Create(_graph, clip);
			_clipPlayable.SetApplyFootIK(false);
			output.SetSourcePlayable(_clipPlayable);
			_graphValid = true;
		}

		private int ResolveLayer()
		{
			int layer = LayerMask.NameToLayer(_layer);
			if (layer < 0)
			{
				Debug.LogWarning($"[DHMGS] 레이어 '{_layer}' 없음. Default(0)로 → 메인 카메라에도 보일 수 있음.");
				return 0;
			}
			return layer;
		}

		private static void SetLayerRecursive(GameObject go, int layer)
		{
			go.layer = layer;
			foreach (Transform child in go.transform)
			{
				SetLayerRecursive(child.gameObject, layer);
			}
		}

		private void OnDestroy()
		{
			DestroyGraph();
			if (_instance != null)
			{
				Destroy(_instance);
				_instance = null;
			}
		}

		private void DestroyGraph()
		{
			if (_graphValid && _graph.IsValid())
			{
				_graph.Destroy();
			}
			_graphValid = false;
		}
	}
}
