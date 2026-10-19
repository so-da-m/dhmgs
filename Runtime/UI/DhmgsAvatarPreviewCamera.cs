using UnityEngine;

namespace Mnnh.Dhmgs
{
	public class DhmgsAvatarPreviewCamera : MonoBehaviour
	{
		[Header("대상")]
		[Tooltip("프레이밍 대상(IDhmgsFramingTarget 구현). 씬 격리를 위해 MotionPlayer 또는 Editor preview를 명시적으로 지정한다.")]
		[SerializeField] private MonoBehaviour _targetSource;

		[Header("출력")]
		[Tooltip("미리보기 RenderTexture 해상도.")]
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
		[Tooltip("스크롤 입력 1단위당 거리 변화(m). 방향을 뒤집으려면 음수.")]
		[SerializeField] private float _zoomSpeed = 0.02f;
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

		public Camera PreviewCamera => _surface.Camera;
		public RenderTexture Texture => _surface.Texture;
		public Transform TargetPivot => _targetSource != null && _target != null ? _target.Pivot : null;

		protected virtual void Awake()
		{
			CaptureInitialView();

			_surface.Create("DhmgsAvatarPreviewCamera", _textureWidth, _textureHeight,
				_cullingMask, _fieldOfView, _nearClip, _farClip, _background);
			_surface.SetEnabled(false);

			_target = _targetSource as IDhmgsFramingTarget;
			_boundsTarget = _targetSource as IDhmgsFramingBounds;
			if (_target == null && _targetSource != null)
			{
				Debug.LogWarning($"[DHMGS] Target Source가 IDhmgsFramingTarget 아님: {_targetSource.GetType().Name}");
			}
		}

		protected virtual void OnEnable()
		{
			_surface.SetEnabled(_pivotLocked);
		}

		protected virtual void OnDisable()
		{
			_surface.SetEnabled(false);
		}

		protected virtual void OnDestroy()
		{
			_surface.Destroy();
		}

		public void Relock()
		{
			_pivotLocked = false;
			_surface.SetEnabled(false);
			_surface.Clear();
		}

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

		public void Orbit(Vector2 delta)
		{
			_yaw += delta.x * _orbitSpeed;
			_pitch += delta.y * _orbitSpeed;
		}

		public void Pan(Vector2 delta)
		{
			_pan += DhmgsCameraNavigation.PanOffset(_surface.CameraTransform, delta, _distance, _panSpeed);
		}

		public void Zoom(float delta)
		{
			_distance -= delta * _zoomSpeed;
		}

		protected virtual void LateUpdate()
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

		private void UpdatePivot()
		{
			Transform pivot = TargetPivot;
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
					if (!DhmgsCameraNavigation.TryGetFramingOffset(bounds, pivot.position, out Vector3 boundsOffset))
					{
						return;
					}
					_pivot = bounds.center;
					_pivotOffset = boundsOffset;
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
			Vector3 orbitDirection = DhmgsCameraNavigation.OrbitDirection(_yaw, pitch);
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
			Vector3 dir = DhmgsCameraNavigation.OrbitDirection(_yaw, _pitch);
			Transform camT = _surface.CameraTransform;
			camT.position = target + dir * _distance;
			camT.LookAt(target, Vector3.up);
		}
	}
}
