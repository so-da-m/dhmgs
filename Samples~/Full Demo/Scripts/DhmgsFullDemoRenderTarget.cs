using UnityEngine;

namespace Mnnh.Dhmgs.Samples
{
	[DefaultExecutionOrder(9010)]
	public sealed class DhmgsFullDemoRenderTarget : MonoBehaviour
	{
		[SerializeField] private Camera _camera;
		[SerializeField] private DhmgsMotionPlayer _player;
		[SerializeField, Min(128)] private int _maximumResolution = 2048;

		private RenderTexture _texture;
		private RenderTexture _previousTarget;
		private Rect _previousRect;
		private float _previousAspect;

		private void OnEnable()
		{
			if (_camera == null)
			{
				return;
			}
			_previousTarget = _camera.targetTexture;
			_previousRect = _camera.rect;
			_previousAspect = _camera.aspect;
			UpdateTarget();
		}

		private void LateUpdate()
		{
			UpdateTarget();
		}

		private void UpdateTarget()
		{
			if (_camera == null)
			{
				return;
			}
			DhmgsMotionAsset motion = _player != null ? _player.Motion : null;
			int width = motion != null && motion.HasProjection ? motion.imageWidth : 1280;
			int height = motion != null && motion.HasProjection ? motion.imageHeight : 720;
			float scale = Mathf.Min(1f, Mathf.Max(128, _maximumResolution) / (float)Mathf.Max(width, height));
			width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
			height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
			if (_texture == null || _texture.width != width || _texture.height != height)
			{
				ReleaseTexture();
				_texture = new RenderTexture(width, height, 24)
				{
					name = "DhmgsMainPreview",
				};
				_texture.Create();
			}
			_camera.targetTexture = _texture;
			_camera.rect = new Rect(0f, 0f, 1f, 1f);
			_camera.aspect = (float)width / height;
		}

		private void OnDisable()
		{
			if (_camera != null && _camera.targetTexture == _texture)
			{
				_camera.targetTexture = _previousTarget;
				_camera.rect = _previousRect;
				_camera.aspect = _previousAspect;
			}
			ReleaseTexture();
		}

		private void ReleaseTexture()
		{
			if (_texture == null)
			{
				return;
			}
			if (_camera != null && _camera.targetTexture == _texture)
			{
				_camera.targetTexture = null;
			}
			_texture.Release();
			Destroy(_texture);
			_texture = null;
		}
	}
}
