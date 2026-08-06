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

		/// <summary>소유자의 활성 상태에 맞춰 생성된 카메라만 켜고 끈다.</summary>
		public void SetEnabled(bool enabled)
		{
			if (_camera != null)
			{
				_camera.enabled = enabled;
			}
		}

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
			RawImage output = _output;
			Camera camera = _camera;
			RenderTexture renderTexture = _renderTexture;

			_output = null;
			_camera = null;
			_renderTexture = null;

			if (output != null && renderTexture != null && output.texture == renderTexture)
			{
				output.texture = null;
			}
			if (camera != null)
			{
				camera.enabled = false;
				camera.targetTexture = null;
				DestroyObject(camera.gameObject);
			}
			if (renderTexture != null)
			{
				if (renderTexture.IsCreated())
				{
					renderTexture.Release();
				}
				DestroyObject(renderTexture);
			}
		}

		private static void DestroyObject(Object value)
		{
			if (value == null)
			{
				return;
			}

			if (Application.isPlaying)
			{
				Object.Destroy(value);
			}
			else
			{
				Object.DestroyImmediate(value);
			}
		}
	}
}
