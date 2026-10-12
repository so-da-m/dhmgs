using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

namespace Mnnh.Dhmgs.Samples
{
	[DefaultExecutionOrder(9990)]
	public sealed class DhmgsFullDemoSkeletonPreview : MonoBehaviour
	{
		[SerializeField] private DhmgsMotionPlayer _player;
		[SerializeField] private DhmgsFixedAvatarCamera _previewCamera;
		[SerializeField] private Camera _camera;
		[SerializeField] private bool _useHumanoidBones;
		[SerializeField] private DhmgsSkeletonOverlay _overlay;
		[SerializeField] private Toggle _bonesToggle;
		[SerializeField] private Toggle _namesToggle;

		private readonly List<Transform> _bones = new List<Transform>();
		private readonly Dictionary<string, Transform> _bonesByName =
			new Dictionary<string, Transform>(System.StringComparer.Ordinal);
		private Transform _boundRoot;
		private DhmgsMotionAsset _boundMotion;
		private Camera _boundCamera;
		private Animator _boundAnimator;
		private Avatar _boundAvatar;

		private void LateUpdate()
		{
			bool showBones = _bonesToggle != null && _bonesToggle.isOn;
			if (_namesToggle != null)
			{
				_namesToggle.interactable = showBones;
			}
			if (_overlay == null)
			{
				return;
			}

			Camera camera = _previewCamera != null ? _previewCamera.PreviewCamera : _camera;
			if (!showBones || camera == null || !camera.isActiveAndEnabled)
			{
				_overlay.enabled = false;
				return;
			}

			bool hasBones = _useHumanoidBones ? BindHumanoid(camera) : BindMotion(camera);
			if (!hasBones)
			{
				_overlay.enabled = false;
				return;
			}
			_overlay.ShowNames = _namesToggle != null && _namesToggle.isOn;
			_overlay.enabled = true;
		}

		private bool BindMotion(Camera camera)
		{
			DhmgsRigDriver subject = _player != null ? _player.ActiveSubject : null;
			DhmgsMotionAsset motion = _player != null ? _player.Motion : null;
			Transform root = subject != null ? subject.Root : null;
			if (_player == null || !_player.IsPlaying || root == null ||
				!root.gameObject.activeInHierarchy || motion == null)
			{
				ClearSource();
				return false;
			}
			if (root == _boundRoot && motion == _boundMotion && camera == _boundCamera &&
				_boundAnimator == null)
			{
				return _bones.Count > 0;
			}

			_bones.Clear();
			_bonesByName.Clear();
			foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
			{
				if (!_bonesByName.ContainsKey(bone.name))
				{
					_bonesByName.Add(bone.name, bone);
				}
			}
			if (_bonesByName.TryGetValue("pelvis", out Transform pelvis))
			{
				_bones.Add(pelvis);
			}
			foreach (string boneName in motion.boneNames)
			{
				if (_bonesByName.TryGetValue(boneName, out Transform bone))
				{
					_bones.Add(bone);
				}
			}
			_overlay.Bind(camera, _bones);
			_boundRoot = root;
			_boundMotion = motion;
			_boundCamera = camera;
			_boundAnimator = null;
			_boundAvatar = null;
			return _bones.Count > 0;
		}

		private bool BindHumanoid(Camera camera)
		{
			Transform pivot = _previewCamera != null ? _previewCamera.TargetPivot : null;
			Animator animator = pivot != null ? pivot.GetComponentInParent<Animator>() : null;
			Avatar avatar = animator != null ? animator.avatar : null;
			if (animator == null || !animator.isActiveAndEnabled || avatar == null ||
				!avatar.isValid || !avatar.isHuman || !animator.isHuman)
			{
				ClearSource();
				return false;
			}
			if (pivot == _boundRoot && animator == _boundAnimator && avatar == _boundAvatar &&
				camera == _boundCamera)
			{
				return _bones.Count > 0;
			}

			_bones.Clear();
			_bonesByName.Clear();
			for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
			{
				Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
				if (bone != null)
				{
					_bones.Add(bone);
				}
			}
			_overlay.Bind(camera, _bones);
			_boundRoot = pivot;
			_boundMotion = null;
			_boundCamera = camera;
			_boundAnimator = animator;
			_boundAvatar = avatar;
			return _bones.Count > 0;
		}

		private void ClearSource()
		{
			_boundRoot = null;
			_boundMotion = null;
			_boundCamera = null;
			_boundAnimator = null;
			_boundAvatar = null;
			_bones.Clear();
			_bonesByName.Clear();
		}

		private void OnDisable()
		{
			if (_overlay != null)
			{
				_overlay.enabled = false;
			}
		}
	}
}
