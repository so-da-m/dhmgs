using System;
using System.Collections.Generic;

using UnityEngine;

namespace Mnnh.Dhmgs.Samples
{
	[DefaultExecutionOrder(9990)]
	public sealed class DhmgsFullDemoSkeletonPreview : MonoBehaviour
	{
		[SerializeField] private DhmgsMotionPlayer _player;
		[SerializeField] private DhmgsAvatarPreviewCamera _previewCamera;
		[SerializeField] private Camera _camera;
		[SerializeField] private bool _useHumanoidBones;

		private Transform[] _bones = Array.Empty<Transform>();
		private Transform _boundRoot;
		private DhmgsMotionAsset _boundMotion;
		private Animator _boundAnimator;
		private Avatar _boundAvatar;

		public Camera ProjectionCamera => _previewCamera != null ? _previewCamera.PreviewCamera : _camera;
		public IReadOnlyList<Transform> Bones => _bones;
		public bool HasSource => isActiveAndEnabled && _bones.Length > 0 && ProjectionCamera != null &&
			ProjectionCamera.isActiveAndEnabled;

		private void LateUpdate()
		{
			if (_useHumanoidBones)
			{
				BindHumanoid();
			}
			else
			{
				BindMotion();
			}
		}

		private void BindMotion()
		{
			DhmgsRigDriver subject = _player != null ? _player.ActiveSubject : null;
			DhmgsMotionAsset motion = _player != null ? _player.Motion : null;
			Transform root = subject != null ? subject.Root : null;
			if (_player == null || !_player.IsPlaying || root == null ||
				!root.gameObject.activeInHierarchy || motion == null)
			{
				ClearSource();
				return;
			}
			if (root == _boundRoot && motion == _boundMotion && _boundAnimator == null)
			{
				return;
			}

			Dictionary<string, Transform> bonesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
			foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
			{
				if (!bonesByName.ContainsKey(bone.name))
				{
					bonesByName.Add(bone.name, bone);
				}
			}
			List<Transform> selected = new List<Transform>();
			if (bonesByName.TryGetValue("pelvis", out Transform pelvis))
			{
				selected.Add(pelvis);
			}
			foreach (string boneName in motion.boneNames)
			{
				if (bonesByName.TryGetValue(boneName, out Transform bone) && !selected.Contains(bone))
				{
					selected.Add(bone);
				}
			}
			_bones = selected.ToArray();
			_boundRoot = root;
			_boundMotion = motion;
			_boundAnimator = null;
			_boundAvatar = null;
		}

		private void BindHumanoid()
		{
			Transform pivot = _previewCamera != null ? _previewCamera.TargetPivot : null;
			Animator animator = pivot != null ? pivot.GetComponentInParent<Animator>() : null;
			Avatar avatar = animator != null ? animator.avatar : null;
			if (animator == null || !animator.isActiveAndEnabled || avatar == null ||
				!avatar.isValid || !avatar.isHuman || !animator.isHuman)
			{
				ClearSource();
				return;
			}
			if (pivot == _boundRoot && animator == _boundAnimator && avatar == _boundAvatar)
			{
				return;
			}

			List<Transform> selected = new List<Transform>();
			for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
			{
				Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
				if (bone != null)
				{
					selected.Add(bone);
				}
			}
			_bones = selected.ToArray();
			_boundRoot = pivot;
			_boundMotion = null;
			_boundAnimator = animator;
			_boundAvatar = avatar;
		}

		private void ClearSource()
		{
			_boundRoot = null;
			_boundMotion = null;
			_boundAnimator = null;
			_boundAvatar = null;
			_bones = Array.Empty<Transform>();
		}

		private void OnDisable()
		{
			ClearSource();
		}
	}
}
