using System.Collections.Generic;

using UnityEngine;

namespace Mnnh.Dhmgs
{
	[DisallowMultipleComponent]
	public sealed class DhmgsRigDriver : MonoBehaviour
	{
		[Tooltip("SMPL-X 본 계층 루트. 비우면 이 GameObject를 루트로 사용.")]
		[SerializeField] private Transform _rigRoot;

		[Tooltip("Playback camera used to transform camera-space translation. If unassigned, translation is treated as world-space.")]
		[SerializeField] private Camera _camera;

		private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
		private Transform _root;
		private Transform _pelvis;
		private Vector3 _pelvisRestOffset;
		private bool _bound;

		public Transform Pelvis => _pelvis;

		public Transform Root => _root != null ? _root : transform;

		public void Bind(Transform rigRoot)
		{
			_rigRoot = rigRoot;
			BuildBones();
		}

		public void SetCamera(Camera camera)
		{
			_camera = camera;
		}

		public void ApplyFrame(DhmgsMotionAsset asset, int frameIndex)
		{
			if (asset == null || !asset.IsValid)
			{
				return;
			}
			EnsureBound();
			int frame = Mathf.Clamp(frameIndex, 0, asset.frameCount - 1);
			ApplyCameraPlacement(asset, frame);
			ApplyPoseFrame(asset, frame);
		}

		public void ApplyPose(DhmgsMotionAsset asset, int frameIndex)
		{
			if (asset == null || !asset.IsValid)
			{
				return;
			}
			EnsureBound();
			int frame = Mathf.Clamp(frameIndex, 0, asset.frameCount - 1);
			ApplyPoseFrame(asset, frame);
		}

		private void ApplyCameraPlacement(DhmgsMotionAsset asset, int frame)
		{
			if (_root != null)
			{
				Camera cam = _camera;
				Vector3 pelvisCameraLocal = asset.pelvisPositions[frame] - _pelvisRestOffset;
				if (cam != null)
				{
					_root.rotation = cam.transform.rotation;
					Vector3 pelvisTarget = cam.transform.TransformPoint(pelvisCameraLocal);
					_root.position = pelvisTarget - _root.rotation * _pelvisRestOffset;
				}
				else
				{
					_root.position = pelvisCameraLocal - _pelvisRestOffset;
				}
			}
		}

		private void ApplyPoseFrame(DhmgsMotionAsset asset, int frame)
		{
			if (_pelvis != null)
			{
				_pelvis.localRotation = asset.pelvisRotations[frame];
			}

			int baseIndex = frame * asset.boneCount;
			for (int b = 0; b < asset.boneCount; b++)
			{
				if (_bones.TryGetValue(asset.boneNames[b], out Transform bone))
				{
					bone.localRotation = asset.boneRotations[baseIndex + b];
				}
			}
		}

		private void EnsureBound()
		{
			if (!_bound)
			{
				BuildBones();
			}
		}

		private void BuildBones()
		{
			_bones.Clear();
			Transform root = _rigRoot != null ? _rigRoot : transform;
			_root = root;
			foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
			{
				if (!_bones.ContainsKey(t.name))
				{
					_bones[t.name] = t;
				}
			}
			_bones.TryGetValue("pelvis", out _pelvis);

			foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				smr.updateWhenOffscreen = true;
			}

			CanonicalizeSkeleton(root);

			if (_pelvis != null)
			{
				_pelvisRestOffset = root.InverseTransformPoint(_pelvis.position);
			}
			_bound = true;
		}

		private static void CanonicalizeSkeleton(Transform root)
		{
			DhmgsSmplx smplx = root.GetComponentInChildren<DhmgsSmplx>(true);
			if (smplx == null || smplx.betas == null)
			{
				return;
			}
			for (int i = 0; i < smplx.betas.Length; i++)
			{
				smplx.betas[i] = 0f;
			}
			if (smplx.HasBetaShapes())
			{
				smplx.SetBetaShapes();
			}
		}
	}
}
