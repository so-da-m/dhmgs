using System;
using System.Collections;

using UnityEngine;
using UnityEngine.Video;

namespace Mnnh.Dhmgs
{
	public sealed class DhmgsCameraOverlay : MonoBehaviour
	{
		private sealed class VideoPreparation
		{
			public VideoPlayer Player;
			public VideoPlayer.EventHandler PreparedHandler;
			public VideoPlayer.ErrorEventHandler ErrorHandler;
			public bool IsFinished;
			public bool Succeeded;
		}

		[SerializeField] private Camera _camera;
		[SerializeField] private VideoPlayer _videoPlayer;
		[Tooltip("쿼드 depth(카메라 앞 거리). 0이면 아바타 뒤로 자동(배경처럼 깔림).")]
		[SerializeField] private float _quadDepth = 0f;
		[SerializeField] private bool _loop = true;
		[Tooltip("영상 불투명도. 0=투명, 1=불투명. 0.5면 아바타와 영상이 겹쳐 보임(정합 확인용).")]
		[Range(0f, 1f)]
		[SerializeField] private float _videoAlpha = 0.5f;
		[Tooltip("영상 쿼드를 둘 레이어 이름(선택). 지정하면 그 레이어로 생성 → 특정 PiP 카메라 Culling Mask에서 빼 숨길 수 있다. 비우면 Default.")]
		[SerializeField] private string _surfaceLayer = "";

		private DhmgsVideoSurface _surface;
		private VideoPreparation _preparation;
		private DhmgsVideoClock _clock;
		private Rect _backupRect;
		private bool _backedUp;

		/// <summary>
		/// Configure before Activate. Cancel the activation routine and Deactivate before reconfiguring.
		/// </summary>
		public void Configure(Camera camera, VideoPlayer videoPlayer, bool loop, float videoAlpha, string surfaceLayer)
		{
			if (camera == null)
			{
				throw new ArgumentNullException(nameof(camera));
			}
			if (videoPlayer == null)
			{
				throw new ArgumentNullException(nameof(videoPlayer));
			}
			if (_preparation != null || _clock != null || _backedUp)
			{
				throw new InvalidOperationException("Deactivate the video overlay before reconfiguring it.");
			}

			DestroySurface();
			_camera = camera;
			_videoPlayer = videoPlayer;
			_loop = loop;
			_surfaceLayer = surfaceLayer ?? string.Empty;
			SetVideoAlpha(videoAlpha);
		}

		public IEnumerator Activate(DhmgsMotionAsset asset, string videoPath, Action<IDhmgsPlaybackClock> onReady)
		{
			ReleaseClock();
			CancelPreparation();
			Camera cam = ResolveCamera();
			if (cam == null || asset == null || !asset.HasProjection || string.IsNullOrEmpty(videoPath) || _videoPlayer == null)
			{
				onReady?.Invoke(null);
				yield break;
			}

			BackupCamera(cam);

			float near = cam.nearClipPlane;
			float far = cam.farClipPlane;
			Matrix4x4 proj = DhmgsProjection.BuildProjectionMatrix(
				asset.focalLength.x, asset.focalLength.y,
				asset.principalPoint.x, asset.principalPoint.y,
				asset.imageWidth, asset.imageHeight, near, far);

			cam.projectionMatrix = proj;
			ApplyAspectFitRect(cam, asset.imageWidth, asset.imageHeight);

			float depth = ResolveDepth(asset, near, far);
			EnsureSurface(cam);
			_surface.SetCorners(DhmgsProjection.FrustumCornersAtDepth(proj, depth));
			_surface.BindVideo(_videoPlayer);
			_surface.SetAlpha(_videoAlpha);
			_surface.SetVisible(true);

			VideoPreparation preparation = new VideoPreparation();
			yield return PrepareVideo(videoPath, preparation);
			if (!preparation.Succeeded)
			{
				_surface.SetVisible(false);
				RestoreCamera(cam);
				onReady?.Invoke(null);
				yield break;
			}

			_videoPlayer.Play();
			_clock = new DhmgsVideoClock(_videoPlayer);
			onReady?.Invoke(_clock);
		}

		public void Deactivate()
		{
			ReleaseClock();
			CancelPreparation();
			if (_surface != null)
			{
				_surface.SetVisible(false);
			}
			if (_videoPlayer != null)
			{
				_videoPlayer.Stop();
			}
			Camera cam = ResolveCamera();
			if (cam != null && _backedUp)
			{
				RestoreCamera(cam);
			}
		}

		private void ReleaseClock()
		{
			_clock?.Dispose();
			_clock = null;
		}

		private void Update()
		{
			_clock?.Tick();
			if (_surface != null)
			{
				_surface.SetAlpha(_videoAlpha);
			}
		}

		public void SetVideoAlpha(float alpha)
		{
			_videoAlpha = Mathf.Clamp01(alpha);
			if (_surface != null)
			{
				_surface.SetAlpha(_videoAlpha);
			}
		}

		private Camera ResolveCamera()
		{
			if (_camera == null)
			{
				_camera = GetComponent<Camera>();
			}
			return _camera;
		}

		private void BackupCamera(Camera cam)
		{
			_backupRect = cam.rect;
			_backedUp = true;
		}

		private void RestoreCamera(Camera cam)
		{
			cam.ResetProjectionMatrix();
			cam.ResetAspect();
			cam.rect = _backupRect;
			_backedUp = false;
		}

		private static void ApplyAspectFitRect(Camera cam, int videoWidth, int videoHeight)
		{
			if (videoWidth <= 0 || videoHeight <= 0)
			{
				return;
			}
			float videoAspect = (float)videoWidth / videoHeight;
			float screenAspect = (float)Screen.width / Screen.height;
			if (screenAspect > videoAspect)
			{
				float w = videoAspect / screenAspect;
				cam.rect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
			}
			else
			{
				float h = screenAspect / videoAspect;
				cam.rect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
			}
		}

		private float ResolveDepth(DhmgsMotionAsset asset, float near, float far)
		{
			if (_quadDepth > 0f)
			{
				return Mathf.Clamp(_quadDepth, near * 1.01f, far * 0.99f);
			}

			float maxPelvisZ = near;
			for (int i = 0; i < asset.pelvisPositions.Length; i++)
			{
				float z = asset.pelvisPositions[i].z;
				if (z > maxPelvisZ)
				{
					maxPelvisZ = z;
				}
			}
			return Mathf.Clamp(maxPelvisZ + 1f, near * 1.01f, far * 0.99f);
		}

		private void EnsureSurface(Camera cam)
		{
			if (_surface != null)
			{
				return;
			}
			GameObject go = new GameObject("DhmgsVideoSurface");
			go.transform.SetParent(cam.transform, false);
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;
			if (!string.IsNullOrEmpty(_surfaceLayer))
			{
				int layer = LayerMask.NameToLayer(_surfaceLayer);
				if (layer >= 0)
				{
					go.layer = layer;
				}
			}
			_surface = go.AddComponent<DhmgsVideoSurface>();
		}

		public void CancelPreparation()
		{
			VideoPreparation preparation = _preparation;
			if (preparation == null)
			{
				return;
			}

			_preparation = null;
			preparation.Succeeded = false;
			preparation.IsFinished = true;
			UnsubscribePreparation(preparation);
			if (preparation.Player != null)
			{
				preparation.Player.Stop();
			}
		}

		private static void UnsubscribePreparation(VideoPreparation preparation)
		{
			VideoPlayer player = preparation.Player;
			if (player != null)
			{
				if (preparation.PreparedHandler != null)
				{
					player.prepareCompleted -= preparation.PreparedHandler;
				}
				if (preparation.ErrorHandler != null)
				{
					player.errorReceived -= preparation.ErrorHandler;
				}
			}

			preparation.PreparedHandler = null;
			preparation.ErrorHandler = null;
		}

		private void OnDestroy()
		{
			Deactivate();
			DestroySurface();
		}

		private void DestroySurface()
		{
			if (_surface == null)
			{
				return;
			}

			GameObject surfaceObject = _surface.gameObject;
			_surface = null;
			if (surfaceObject == null)
			{
				return;
			}

			if (Application.isPlaying)
			{
				Destroy(surfaceObject);
			}
			else
			{
				DestroyImmediate(surfaceObject);
			}
		}

		private IEnumerator PrepareVideo(string filePath, VideoPreparation preparation)
		{
			CancelPreparation();
			preparation.Player = _videoPlayer;
			_preparation = preparation;
			_videoPlayer.source = VideoSource.Url;
			_videoPlayer.url = filePath;
			_videoPlayer.isLooping = _loop;
			_videoPlayer.playOnAwake = false;

			void OnPrepared(VideoPlayer vp)
			{
				preparation.Succeeded = true;
				preparation.IsFinished = true;
				UnsubscribePreparation(preparation);
			}
			void OnError(VideoPlayer vp, string message)
			{
				preparation.Succeeded = false;
				preparation.IsFinished = true;
				UnsubscribePreparation(preparation);
				Debug.LogError($"[DHMGS] 영상 prepare 실패: {message}");
			}

			preparation.PreparedHandler = OnPrepared;
			preparation.ErrorHandler = OnError;
			_videoPlayer.prepareCompleted += preparation.PreparedHandler;
			_videoPlayer.errorReceived += preparation.ErrorHandler;
			try
			{
				_videoPlayer.Prepare();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception, this);
				preparation.Succeeded = false;
				preparation.IsFinished = true;
				UnsubscribePreparation(preparation);
			}
			while (!preparation.IsFinished)
			{
				yield return null;
			}
			UnsubscribePreparation(preparation);
			if (ReferenceEquals(_preparation, preparation))
			{
				_preparation = null;
			}
		}
	}
}
