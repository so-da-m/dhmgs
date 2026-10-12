using UnityEngine;
using UnityEngine.UI;

namespace Mnnh.Dhmgs
{
	public sealed class DhmgsPreviewSurface
	{
		private Camera _camera;
		private RenderTexture _renderTexture;
		private RawImage _output;
		private Color _background;

		public Camera Camera => _camera;
		public Transform CameraTransform => _camera != null ? _camera.transform : null;
		public bool IsValid => _camera != null;

		public void SetEnabled(bool enabled)
		{
			if (_camera != null)
			{
				_camera.enabled = enabled;
			}
		}

		public void Create(string name, RawImage output, int width, int height, LayerMask cullingMask,
			float fieldOfView, float nearClip, float farClip, Color background)
		{
			if (_camera != null)
			{
				return;
			}
			_output = output;
			_background = background;

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
			Clear();
		}

		public void Clear()
		{
			if (_renderTexture == null)
			{
				return;
			}

			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = _renderTexture;
			GL.Clear(true, true, _background);
			RenderTexture.active = previous;
		}

		public void Destroy()
		{
			RawImage output = _output;
			Camera camera = _camera;
			RenderTexture renderTexture = _renderTexture;

			_output = null;
			_camera = null;
			_renderTexture = null;
			_background = default;

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
