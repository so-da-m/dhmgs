using UnityEngine;

namespace Mnnh.Dhmgs
{
	[DisallowMultipleComponent]
	[DefaultExecutionOrder(9000)]
	public sealed class DhmgsOrbitCamera : MonoBehaviour
	{
		[Header("Cameras")]
		[SerializeField] private Camera _camera;
		[Tooltip("Calibration and playback camera. This component never modifies it.")]
		[SerializeField] private Camera _referenceCamera;
		[Tooltip("A component implementing IDhmgsFramingTarget, such as DhmgsMotionPlayer.")]
		[SerializeField] private MonoBehaviour _targetSource;

		[Header("Navigation")]
		[SerializeField] private float _orbitSpeed = 0.3f;
		[SerializeField] private float _panSpeed = 0.002f;
		[Tooltip("Distance change per UI scroll unit (m). Negative values reverse zoom direction.")]
		[SerializeField] private float _zoomSpeed = 0.02f;
		[SerializeField] private float _minPitch = -80f;
		[SerializeField] private float _maxPitch = 80f;
		[SerializeField, Min(0.01f)] private float _minDistance = 0.3f;
		[SerializeField, Min(0.01f)] private float _maxDistance = 30f;

		private Transform _trackedPivot;
		private Vector3 _pivotOffset;
		private Vector3 _pan;
		private Quaternion _viewRotationOffset;
		private float _yaw;
		private float _pitch;
		private float _distance;
		private bool _isOrbiting;

		public bool IsOrbiting => _isOrbiting;

		private bool HasCameras => _camera != null && _referenceCamera != null && _camera != _referenceCamera;
		private Transform TargetPivot => _targetSource != null && _targetSource is IDhmgsFramingTarget target
			? target.Pivot : null;

		private void OnEnable()
		{
			ResetView();
		}

		private void OnDisable()
		{
			// The display camera belongs to the scene, so leave it showing the aligned view.
			ResetView();
		}

		public void ResetView()
		{
			_isOrbiting = false;
			_trackedPivot = null;
			_pivotOffset = Vector3.zero;
			_pan = Vector3.zero;
			ApplyReferenceView();
		}

		public void Orbit(Vector2 delta)
		{
			if (!isActiveAndEnabled || delta.sqrMagnitude == 0f || !TryBeginOrbit())
			{
				return;
			}
			_yaw += delta.x * _orbitSpeed;
			float minimumPitch = Mathf.Clamp(_minPitch, -89f, 89f);
			_pitch = Mathf.Clamp(_pitch + delta.y * _orbitSpeed,
				minimumPitch, Mathf.Clamp(_maxPitch, minimumPitch, 89f));
			ApplyOrbitView();
		}

		public void Pan(Vector2 delta)
		{
			if (!isActiveAndEnabled || delta.sqrMagnitude == 0f || !TryBeginOrbit())
			{
				return;
			}
			_pan += DhmgsCameraNavigation.PanOffset(_camera.transform, delta, _distance, _panSpeed);
			ApplyOrbitView();
		}

		public void Zoom(float delta)
		{
			if (!isActiveAndEnabled || delta == 0f || !TryBeginOrbit())
			{
				return;
			}
			float minimum = Mathf.Max(0.01f, _minDistance);
			_distance = Mathf.Clamp(_distance - delta * _zoomSpeed,
				minimum, Mathf.Max(minimum, _maxDistance));
			ApplyOrbitView();
		}

		private bool TryBeginOrbit()
		{
			if (!HasCameras || !_referenceCamera.gameObject.activeInHierarchy)
			{
				return false;
			}
			Transform pivot = TargetPivot;
			if (pivot == null)
			{
				return false;
			}
			if (_isOrbiting && pivot == _trackedPivot)
			{
				return true;
			}

			ApplyReferenceView();
			Vector3 center = pivot.position;
			if (_targetSource is IDhmgsFramingBounds boundsTarget && boundsTarget.TryGetFramingBounds(out Bounds bounds) &&
				DhmgsCameraNavigation.TryGetFramingOffset(bounds, pivot.position, out _))
			{
				center = bounds.center;
			}

			Vector3 direction = _camera.transform.position - center;
			_distance = direction.magnitude;
			if (_distance < 0.001f)
			{
				return false;
			}
			direction /= _distance;
			_yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
			_pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
			_viewRotationOffset = Quaternion.Inverse(OrbitRotation(direction)) * _camera.transform.rotation;
			_trackedPivot = pivot;
			_pivotOffset = center - pivot.position;
			_pan = Vector3.zero;
			_isOrbiting = true;
			return true;
		}

		private void LateUpdate()
		{
			if (_isOrbiting)
			{
				Transform pivot = TargetPivot;
				if (pivot == null || pivot != _trackedPivot)
				{
					ResetView();
					return;
				}
				ApplyOrbitView();
			}
			else
			{
				ApplyReferenceView();
			}
		}

		private void ApplyReferenceView()
		{
			if (!HasCameras)
			{
				return;
			}
			CopyReferenceSettings();
			_camera.transform.SetPositionAndRotation(_referenceCamera.transform.position, _referenceCamera.transform.rotation);
			ResetViewMatrices();
		}

		private void ApplyOrbitView()
		{
			if (!HasCameras || _trackedPivot == null)
			{
				return;
			}
			CopyReferenceSettings();
			Vector3 direction = DhmgsCameraNavigation.OrbitDirection(_yaw, _pitch);
			Vector3 center = _trackedPivot.position + _pivotOffset + _pan;
			_camera.transform.SetPositionAndRotation(center + direction * _distance,
				OrbitRotation(direction) * _viewRotationOffset);
			ResetViewMatrices();
		}

		private void CopyReferenceSettings()
		{
			RenderTexture output = _camera.targetTexture;
			Rect viewport = _camera.rect;
			float aspect = _camera.aspect;
			_camera.CopyFrom(_referenceCamera);
			_camera.targetTexture = output;
			_camera.rect = viewport;
			_camera.aspect = aspect;
			_camera.projectionMatrix = _referenceCamera.projectionMatrix;
			_camera.enabled = _referenceCamera.gameObject.activeInHierarchy;
		}

		private void ResetViewMatrices()
		{
			// Culling and the view matrix must use the output pose, never the reference pose.
			_camera.ResetWorldToCameraMatrix();
			_camera.ResetCullingMatrix();
		}

		private static Quaternion OrbitRotation(Vector3 direction)
		{
			Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.9999f ? Vector3.forward : Vector3.up;
			return Quaternion.LookRotation(-direction, up);
		}
	}
}
