using UnityEngine;
using UnityEngine.UI;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 런타임 카메라 + RenderTexture + RawImage 출력을 한 묶음으로 만들고 정리하는 헬퍼(MonoBehaviour 아님).
	/// FixedAvatarCamera(orbit/pan)와 HumanoidPreview가 공유하던 렌더 보일러플레이트만 담는다.
	/// 카메라 배치/프레이밍은 소유자가 CameraTransform으로 직접 한다.
	/// </summary>
	public sealed class DhmgsPreviewSurface
	{
		private Camera _camera;
		private RenderTexture _renderTexture;
		private RawImage _output;

		public Camera Camera => _camera;
		public Transform CameraTransform => _camera != null ? _camera.transform : null;
		public bool IsValid => _camera != null;

		/// <summary>카메라/RT/출력 생성. 이미 만들어졌으면 무시.</summary>
		public void Create(string name, RawImage output, int width, int height, LayerMask cullingMask,
			float fieldOfView, float nearClip, float farClip, Color background)
		{
			if (_camera != null)
			{
				return;
			}
			_output = output;

			_renderTexture = new RenderTexture(Mathf.Max(1, width), Mathf.Max(1, height), 24);
			_renderTexture.name = $"{name}RT";
			_renderTexture.Create();

			GameObject go = new GameObject(name);
			_camera = go.AddComponent<Camera>();
			_camera.clearFlags = CameraClearFlags.SolidColor;
			_camera.backgroundColor = background;
			_camera.fieldOfView = fieldOfView;
			_camera.nearClipPlane = nearClip;
			_camera.farClipPlane = farClip;
			_camera.cullingMask = cullingMask;
			_camera.targetTexture = _renderTexture;

			if (_output != null)
			{
				_output.texture = _renderTexture;
			}
		}

		/// <summary>출력 해제 + 카메라/RT 파괴.</summary>
		public void Destroy()
		{
			if (_output != null && _renderTexture != null && _output.texture == _renderTexture)
			{
				_output.texture = null;
			}
			if (_camera != null)
			{
				_camera.targetTexture = null;
				Object.Destroy(_camera.gameObject);
				_camera = null;
			}
			if (_renderTexture != null)
			{
				_renderTexture.Release();
				Object.Destroy(_renderTexture);
				_renderTexture = null;
			}
		}
	}
}
