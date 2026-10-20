using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

using UnityEditor;

namespace Mnnh.Dhmgs
{
	public sealed class DhmgsHumanoidPreview : MonoBehaviour, IDhmgsFramingTarget, IDhmgsFramingBounds
	{
		private GameObject _instance;
		private Animator _animator;
		private Transform _pivot;
		private PlayableGraph _graph;
		private AnimationClipPlayable _clipPlayable;
		private bool _graphValid;
		private IDhmgsPlaybackClock _source;
		private double _clipDuration;
		private Vector3 _rootPosition;
		private Quaternion _rootRotation;

		public Transform Pivot => _source != null && _instance != null
			? (_pivot != null ? _pivot : _instance.transform)
			: null;

		public bool TryGetFramingBounds(out Bounds bounds)
		{
			if (_source == null || _instance == null)
			{
				bounds = default;
				return false;
			}
			return DhmgsFramingBoundsUtility.TryCalculate(_instance.transform, out bounds);
		}

		public bool Play(
			GameObject avatarPrefab,
			AnimationClip clip,
			IDhmgsPlaybackClock source,
			Vector3 isolationPosition,
			bool applyRootMotion = false)
		{
			Clear();
			if (avatarPrefab == null || clip == null || source == null)
			{
				return false;
			}

			try
			{
				_instance = Instantiate(avatarPrefab, transform, false);
				_instance.name = $"{avatarPrefab.name} (DHMGS Humanoid Preview)";
				_instance.hideFlags = HideFlags.DontSave;
				_instance.transform.position = isolationPosition;
				_instance.transform.rotation = Quaternion.identity;
				_instance.transform.localScale = Vector3.one;

				_animator = _instance.GetComponentInChildren<Animator>(true);
				if (_animator == null)
				{
					_animator = _instance.AddComponent<Animator>();
				}
				Avatar avatar = ResolveHumanoidAvatar(avatarPrefab, _animator);
				if (avatar == null)
				{
					Debug.LogError($"[DHMGS] '{avatarPrefab.name}' is not a valid Humanoid avatar.", avatarPrefab);
					Clear();
					return false;
				}

				_animator.avatar = avatar;
				_animator.runtimeAnimatorController = null;
				_animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
				_rootPosition = _animator.transform.position;
				_rootRotation = _animator.transform.rotation;
				_pivot = _animator.GetBoneTransform(HumanBodyBones.Hips);
				foreach (SkinnedMeshRenderer renderer in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
				{
					renderer.updateWhenOffscreen = true;
				}

				_source = source;
				SetClip(clip, applyRootMotion);
				return true;
			}
			catch
			{
				Clear();
				throw;
			}
		}

		public void Stop()
		{
			Clear();
		}

		public void SetClip(AnimationClip clip, bool applyRootMotion)
		{
			if (_animator == null || _source == null || clip == null)
			{
				return;
			}
			DestroyGraph();
			_animator.applyRootMotion = applyRootMotion;
			_animator.transform.SetPositionAndRotation(_rootPosition, _rootRotation);
			_clipDuration = clip.length;
			BuildGraph(clip);
			EvaluateCurrentTime();
		}

		private void Update()
		{
			EvaluateCurrentTime();
		}

		private void EvaluateCurrentTime()
		{
			if (_source == null || !_graphValid)
			{
				return;
			}
			if (_animator.applyRootMotion)
			{
				_clipPlayable.SetTime(0d);
				_graph.Evaluate(0f);
				_animator.transform.SetPositionAndRotation(_rootPosition, _rootRotation);
			}
			_clipPlayable.SetTime(System.Math.Max(0d, System.Math.Min(_source.CurrentTime, _clipDuration)));
			_graph.Evaluate(0f);
		}

		private void BuildGraph(AnimationClip clip)
		{
			DestroyGraph();
			_graph = PlayableGraph.Create("DHMGS Humanoid Preview");
			_graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
			AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Animation", _animator);
			_clipPlayable = AnimationClipPlayable.Create(_graph, clip);
			_clipPlayable.SetApplyFootIK(false);
			output.SetSourcePlayable(_clipPlayable);
			_graph.Play();
			_graphValid = true;
		}

		private static Avatar ResolveHumanoidAvatar(GameObject avatarAsset, Animator animator)
		{
			Avatar avatar = animator != null ? animator.avatar : null;
			if (avatar != null && avatar.isValid && avatar.isHuman)
			{
				return avatar;
			}

			string path = AssetDatabase.GetAssetPath(avatarAsset);
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			foreach (Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
			{
				if (candidate is Avatar imported && imported.isValid && imported.isHuman)
				{
					return imported;
				}
			}
			return null;
		}

		private void Clear()
		{
			_source = null;
			_pivot = null;
			_animator = null;
			DestroyGraph();
			if (_instance != null)
			{
				DestroyPreviewObject(_instance);
				_instance = null;
			}
		}

		private void OnDestroy()
		{
			Clear();
		}

		private void DestroyGraph()
		{
			if (_graph.IsValid())
			{
				_graph.Destroy();
			}
			_graphValid = false;
		}

		private static void DestroyPreviewObject(Object value)
		{
			if (value == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				Destroy(value);
			}
			else
			{
				DestroyImmediate(value);
			}
		}
	}
}
