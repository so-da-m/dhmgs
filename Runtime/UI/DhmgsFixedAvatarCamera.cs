using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 런타임에 카메라를 생성해 활성 아바타를 RenderTexture로 찍어 RawImage에 띄우는 인스펙션 뷰.
	/// pivot(아바타 pelvis)은 재생 시작 시 한 번만 잡아 고정하므로 pelvis bob에 흔들리지 않는다.
	/// 그 고정 pivot 주위로 마우스 드래그=orbit, 휠=zoom(dolly)만 사용자 입력으로 움직인다.
	/// 입력은 UI 이벤트(IDragHandler/IScrollHandler)라 RawImage 위에 있을 때만 반응한다 → 이 컴포넌트는 RawImage에 붙인다.
	/// 드롭다운으로 활성 아바타가 바뀌면 pivot을 자동으로 다시 잡는다(orbit 각도/거리는 유지).
	/// </summary>
	[RequireComponent(typeof(RawImage))]
	public sealed class DhmgsFixedAvatarCamera : MonoBehaviour, IDragHandler, IScrollHandler
	{
		[Header("대상")]
		[Tooltip("프레이밍 대상(IDhmgsFramingTarget 구현). 씬 격리를 위해 MotionPlayer 또는 Editor preview를 명시적으로 지정한다.")]
		[SerializeField] private MonoBehaviour _targetSource;

		[Header("출력")]
		[Tooltip("RenderTexture 해상도. RawImage 비율과 맞추면 왜곡이 없다. 이 컴포넌트가 붙은 RawImage에 그려진다(입력 대상과 항상 일치).")]
		[SerializeField] private int _textureWidth = 512;
		[SerializeField] private int _textureHeight = 512;

		[Header("Orbit / Zoom 시작값")]
		[Tooltip("방위각(도). 0=아바타 정면 쪽, 180=등 뒤.")]
		[SerializeField] private float _yaw = 0f;
		[Tooltip("고도각(도). +면 위에서 내려봄.")]
		[SerializeField] private float _pitch = 20f;
		[Tooltip("pivot까지 거리(m).")]
		[SerializeField] private float _distance = 3f;

		[Header("Automatic framing")]
		[Tooltip("Fits the complete active renderer bounds whenever the target changes or Relock is called.")]
		[SerializeField] private bool _autoFrame = true;
		[Tooltip("Extra room around the fitted subject. 1 is tight; larger values produce a wider view.")]
		[SerializeField, Min(1f)] private float _framePadding = 1.25f;

		[Header("Orbit / Zoom 한계·감도")]
		[SerializeField] private float _minPitch = -80f;
		[SerializeField] private float _maxPitch = 80f;
		[SerializeField] private float _minDistance = 0.8f;
		[SerializeField] private float _maxDistance = 8f;
		[Tooltip("드래그 1px당 회전(도). 방향을 뒤집으려면 음수.")]
		[SerializeField] private float _orbitSpeed = 0.3f;
		[Tooltip("휠 1노치당 거리 변화(m). 방향을 뒤집으려면 음수.")]
		[SerializeField] private float _zoomSpeed = 0.5f;
		[Tooltip("우클릭 드래그 1px당 패닝(거리에 비례). 방향을 뒤집으려면 음수.")]
		[SerializeField] private float _panSpeed = 0.002f;

		[Header("카메라")]
		[SerializeField] private float _fieldOfView = 60f;
		[SerializeField] private float _nearClip = 0.05f;
		[SerializeField] private float _farClip = 100f;

		[Tooltip("이 카메라가 렌더할 레이어. 영상 쿼드 레이어를 빼면 PiP에 판때기가 안 나온다.")]
		[SerializeField] private LayerMask _cullingMask = ~0;

		[Tooltip("배경색(클리어). 알파 0이면 투명 배경(렌더 파이프라인 설정에 따라 다름).")]
		[SerializeField] private Color _background = new Color(0.1f, 0.1f, 0.1f, 1f);

		private readonly DhmgsPreviewSurface _surface = new DhmgsPreviewSurface();
		private IDhmgsFramingTarget _target;
		private IDhmgsFramingBounds _boundsTarget;
		private Transform _lastPivot;
		private Vector3 _pivot;
		private Vector3 _pivotOffset;
		private Vector3 _pan;
		private bool _pivotLocked;
		private float _initialYaw;
		private float _initialPitch;
		private float _initialDistance;
		private bool _initialViewCaptured;

		private void Awake()
		{
			CaptureInitialView();

			// 입력(IDragHandler/IScrollHandler)이 이 RawImage에서 오므로 보이는 이미지=입력 대상으로 항상 일치시킨다.
			RawImage output = GetComponent<RawImage>();
			_surface.Create("DhmgsFixedAvatarCamera", output, _textureWidth, _textureHeight,
				_cullingMask, _fieldOfView, _nearClip, _farClip, _background);
			_surface.SetEnabled(false);

			// 대상은 항상 명시해 additive scene과 다른 도구의 플레이어를 건드리지 않는다.
			_target = _targetSource as IDhmgsFramingTarget;
			_boundsTarget = _targetSource as IDhmgsFramingBounds;
			if (_target == null && _targetSource != null)
			{
				Debug.LogWarning($"[DHMGS] Target Source가 IDhmgsFramingTarget 아님: {_targetSource.GetType().Name}");
			}
		}

		private void OnEnable()
		{
			_surface.SetEnabled(_pivotLocked);
		}

		private void OnDisable()
		{
			_surface.SetEnabled(false);
		}

		private void OnDestroy()
		{
			_surface.Destroy();
		}

		/// <summary>다음 프레임에 pivot을 다시 잡도록 강제.</summary>
		public void Relock()
		{
			_pivotLocked = false;
			_surface.SetEnabled(false);
			_surface.Clear();
		}

		/// <summary>Restores the authored orbit, zoom, and pan, then frames the current target again.</summary>
		public void ResetView()
		{
			CaptureInitialView();
			_yaw = _initialYaw;
			_pitch = _initialPitch;
			_distance = _initialDistance;
			_pan = Vector3.zero;
			_pivotOffset = Vector3.zero;
			Relock();
		}

		private void CaptureInitialView()
		{
			if (_initialViewCaptured)
			{
				return;
			}
			_initialYaw = _yaw;
			_initialPitch = _pitch;
			_initialDistance = _distance;
			_initialViewCaptured = true;
		}

		/// <summary>
		/// Changes the framing target at runtime. This is used by Editor previews that are
		/// created after Play Mode has already started.
		/// </summary>
		public void SetTarget(MonoBehaviour targetSource)
		{
			_targetSource = targetSource;
			_target = targetSource as IDhmgsFramingTarget;
			_boundsTarget = targetSource as IDhmgsFramingBounds;
			_lastPivot = null;
			_pivotLocked = false;
			_pivotOffset = Vector3.zero;
			_pan = Vector3.zero;
			_surface.SetEnabled(false);
			_surface.Clear();
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (eventData.button == PointerEventData.InputButton.Left)
			{
				Orbit(eventData.delta);
			}
			else
			{
				Pan(eventData.delta);
			}
		}

		// 좌클릭 드래그: 고정 pivot 주위 회전.
		private void Orbit(Vector2 delta)
		{
			_yaw += delta.x * _orbitSpeed;
			_pitch += delta.y * _orbitSpeed;
		}

		// 우(중)클릭 드래그: 카메라 시야평면(right/up)으로 pivot 평행이동. 거리에 비례시켜 줌과 무관하게 일관된 속도.
		private void Pan(Vector2 delta)
		{
			Transform camT = _surface.CameraTransform;
			if (camT == null)
			{
				return;
			}
			float scale = _panSpeed * _distance;
			_pan += (-camT.right * delta.x - camT.up * delta.y) * scale;
		}

		public void OnScroll(PointerEventData eventData)
		{
			_distance -= eventData.scrollDelta.y * _zoomSpeed;
		}

		private void LateUpdate()
		{
			if (!_surface.IsValid)
			{
				return;
			}

			UpdatePivot();
			if (!_pivotLocked)
			{
				_surface.SetEnabled(false);
				return;
			}
			_surface.SetEnabled(isActiveAndEnabled);
			ApplyOrbit();
		}

		// 대상의 pivot Transform을 추적하며 위치를 한 번 잡는다. 대상(또는 활성 아바타)이 바뀌면 다시 잡는다(각도/거리는 유지).
		private void UpdatePivot()
		{
			Transform pivot = _target != null ? _target.Pivot : null;
			if (pivot == null)
			{
				if (_lastPivot != null || _pivotLocked)
				{
					_lastPivot = null;
					_pivotLocked = false;
					_pivotOffset = Vector3.zero;
					_surface.SetEnabled(false);
					_surface.Clear();
				}
				return;
			}
			if (pivot != _lastPivot)
			{
				_lastPivot = pivot;
				_pivotLocked = false;
			}
			if (!_pivotLocked)
			{
				if (_autoFrame && _boundsTarget != null)
				{
					if (!_boundsTarget.TryGetFramingBounds(out Bounds bounds))
					{
						return;
					}
					Vector3 boundsOffset = bounds.center - pivot.position;
					float maximumExpectedOffset = Mathf.Max(1f, bounds.extents.magnitude * 2f);
					if (boundsOffset.sqrMagnitude > maximumExpectedOffset * maximumExpectedOffset)
					{
						// A moved skinned mesh can report its previous-frame bounds once. Waiting one
						// render avoids permanently locking the camera to that stale world position.
						return;
					}
					_pivot = bounds.center;
					_pivotOffset = _pivot - pivot.position;
					_distance = CalculateFitDistance(bounds);
				}
				else
				{
					_pivot = pivot.position;
					_pivotOffset = Vector3.zero;
				}
				_pan = Vector3.zero;
				_pivotLocked = true;
			}
			if (_pivotLocked)
			{
				// Follow root translation while preserving the initial full-body framing offset.
				// This keeps travelling motion in frame without refitting on every animated pose.
				_pivot = pivot.position + _pivotOffset;
			}
		}

		private float CalculateFitDistance(Bounds bounds)
		{
			float aspect = Mathf.Max(0.01f, (float)_textureWidth / Mathf.Max(1, _textureHeight));
			float verticalHalfAngle = Mathf.Clamp(_fieldOfView * 0.5f, 1f, 89f) * Mathf.Deg2Rad;
			float horizontalHalfAngle = Mathf.Atan(Mathf.Tan(verticalHalfAngle) * aspect);
			float tanVertical = Mathf.Max(0.01f, Mathf.Tan(verticalHalfAngle));
			float tanHorizontal = Mathf.Max(0.01f, Mathf.Tan(horizontalHalfAngle));

			float pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
			Vector3 orbitDirection = Quaternion.Euler(-pitch, _yaw, 0f) * Vector3.forward;
			Quaternion cameraRotation = Quaternion.LookRotation(-orbitDirection, Vector3.up);
			Vector3 right = cameraRotation * Vector3.right;
			Vector3 up = cameraRotation * Vector3.up;
			Vector3 forward = cameraRotation * Vector3.forward;

			float fitted = _minDistance;
			Vector3 extents = bounds.extents;
			for (int x = -1; x <= 1; x += 2)
			{
				for (int y = -1; y <= 1; y += 2)
				{
					for (int z = -1; z <= 1; z += 2)
					{
						Vector3 offset = Vector3.Scale(extents, new Vector3(x, y, z));
						float depthOffset = Vector3.Dot(offset, forward);
						float horizontal = Mathf.Abs(Vector3.Dot(offset, right)) / tanHorizontal - depthOffset;
						float vertical = Mathf.Abs(Vector3.Dot(offset, up)) / tanVertical - depthOffset;
						float nearPlane = _nearClip - depthOffset;
						fitted = Mathf.Max(fitted, horizontal, vertical, nearPlane);
					}
				}
			}

			return Mathf.Clamp(fitted * Mathf.Max(1f, _framePadding), _minDistance, _maxDistance);
		}

		private void ApplyOrbit()
		{
			_pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
			_distance = Mathf.Clamp(_distance, _minDistance, _maxDistance);

			Vector3 target = _pivot + _pan;
			Vector3 dir = Quaternion.Euler(-_pitch, _yaw, 0f) * Vector3.forward;
			Transform camT = _surface.CameraTransform;
			camT.position = target + dir * _distance;
			camT.LookAt(target, Vector3.up);
		}
	}
}
