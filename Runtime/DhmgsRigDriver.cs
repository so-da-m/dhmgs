using System.Collections.Generic;

using SMPLXPlugin;

using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 베이크된 SO를 SMPL-X 리그에 적용하는 dumb 플레이어. 서버/JSON/좌표수학을 모른다.
	/// ApplyFrame은 포즈와 카메라 정합 배치를 함께 적용하고, ApplyPose는 카메라와 무관한 포즈만 적용한다.
	/// pose correctives는 리그의 SMPLX 컴포넌트(LateUpdate)가 알아서 처리한다.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class DhmgsRigDriver : MonoBehaviour
	{
		[Tooltip("SMPL-X 본 계층 루트. 비우면 이 GameObject를 루트로 사용.")]
		[SerializeField] private Transform _rigRoot;

		[Tooltip("정합용 카메라. 비우면 Camera.main. 아바타를 이 카메라 상대로 배치해 영상에 투영 정합(translation은 카메라공간 값).")]
		[SerializeField] private Camera _camera;

		private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
		private Transform _root;
		private Transform _pelvis;
		// pelvis 본의 rest 위치(루트 로컬). SMPLX가 pelvis를 루트보다 ~35cm 아래 두므로,
		// translation에 pelvis가 정확히 오도록 이 값을 빼서 배치한다(서버 translation = pelvis 위치 전제).
		private Vector3 _pelvisRestOffset;
		private bool _bound;

		/// <summary>현재 pelvis 본(바인딩 전이면 null).</summary>
		public Transform Pelvis => _pelvis;

		/// <summary>현재 리그 루트(rigRoot 지정 시 그것, 아니면 이 트랜스폼).</summary>
		public Transform Root => _root != null ? _root : transform;

		/// <summary>리그 루트를 지정하고 본 딕셔너리를 구성한다.</summary>
		public void Bind(Transform rigRoot)
		{
			_rigRoot = rigRoot;
			BuildBones();
		}

		/// <summary>정합용 카메라 주입(컨트롤러가 활성 리그에 설정). 비우면 Camera.main 사용.</summary>
		public void SetCamera(Camera camera)
		{
			_camera = camera;
		}

		/// <summary>한 프레임을 영상 정합 상태로 적용. 런타임 재생용.</summary>
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

		/// <summary>
		/// 한 프레임의 관절 포즈만 적용한다. 카메라 Transform과 카메라공간 translation은 사용하지 않는다.
		/// 범용 AnimationClip 베이크처럼 월드 배치와 독립적이어야 하는 경로에서 사용한다.
		/// </summary>
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
			// translation은 SMPL-X transl 파라미터다. 표준 SMPL-X에서 pelvis = transl + J[0](rest pelvis).
			// 리그의 restOffset이 곧 J[0](Unity 모델프레임)인데, 카메라 Y-flip 때문에 카메라공간에선 부호가 반대로 작용한다.
			// 따라서 pelvis 본의 카메라공간 목표 = (translation - restOffset). pelvis가 정확히 거기 오도록 루트를 역산한다.
			if (_root != null)
			{
				Camera cam = _camera != null ? _camera : Camera.main;
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

			// pelvis를 매 프레임 옮기므로 bind-time bounds로 컬링되지 않게 강제.
			foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				smr.updateWhenOffscreen = true;
			}

			CanonicalizeSkeleton(root);

			// SetBetaShapes로 pelvis가 template 위치에 놓인 뒤, 루트 로컬에서의 rest offset을 1회 실측.
			if (_pelvis != null)
			{
				_pelvisRestOffset = root.InverseTransformPoint(_pelvis.position);
			}
			_bound = true;
		}

		/// <summary>
		/// SMPLX 스켈레톤을 표준(betas=0 template) 상태로 1회 맞춘다. 포즈 회전은 이 위에 적용돼야 정확.
		/// SetBetaShapes는 관절 localPosition을, 플레이어는 루트 position을 다루므로 충돌하지 않는다.
		/// </summary>
		private static void CanonicalizeSkeleton(Transform root)
		{
			SMPLX smplx = root.GetComponentInChildren<SMPLX>(true);
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
