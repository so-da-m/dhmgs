using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.Video;

namespace Mnnh.Dhmgs
{
	public sealed class DhmgsCameraOverlay : MonoBehaviour
	{
		[SerializeField] private Camera _camera;
		[SerializeField] private VideoPlayer _videoPlayer;
		[SerializeField] private float _quadDepth;
		[Range(0f, 1f)]
		[SerializeField] private float _videoAlpha = 0.5f;
		[SerializeField] private string _surfaceLayer = string.Empty;

		private DhmgsVideoSurface _surface;
		private Action _cancelPreparation;
		private bool _destroyed;

		public void Configure(Camera camera, VideoPlayer videoPlayer, float videoAlpha, string surfaceLayer)
		{
			if (camera == null)
			{
				throw new ArgumentNullException(nameof(camera));
			}
			if (videoPlayer == null)
			{
				throw new ArgumentNullException(nameof(videoPlayer));
			}
			if (_cancelPreparation != null)
			{
				throw new InvalidOperationException("Video preparation is still active.");
			}
			DestroySurface();
			_camera = camera;
			_videoPlayer = videoPlayer;
			_surfaceLayer = surfaceLayer ?? string.Empty;
			SetVideoAlpha(videoAlpha);
		}

		public async Task<IDhmgsSeekablePlaybackClock> PrepareAsync(
			DhmgsMotionAsset asset,
			string videoPath,
			bool loop,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (_destroyed)
			{
				throw new ObjectDisposedException(nameof(DhmgsCameraOverlay));
			}
			if (_cancelPreparation != null)
			{
				throw new InvalidOperationException("Video preparation is already active.");
			}
			if (asset == null || !asset.IsValid || string.IsNullOrEmpty(videoPath))
			{
				throw new ArgumentException("A valid motion and video path are required.");
			}
			if (_camera == null)
			{
				_camera = GetComponent<Camera>();
			}
			if (_camera == null || _videoPlayer == null)
			{
				throw new InvalidOperationException("The video overlay requires a camera and video player.");
			}

			EnsureSurface(_camera);
			float depth = ResolveDepth(asset, _camera.nearClipPlane, _camera.farClipPlane);
			_surface.SetCorners(DhmgsProjection.FrustumCornersAtDepth(_camera.projectionMatrix, depth));
			_surface.BindVideo(_videoPlayer);
			_surface.SetAlpha(_videoAlpha);
			_surface.SetVisible(false);

			VideoPlayer player = _videoPlayer;
			bool previousFrameReadyEvents = player.sendFrameReadyEvents;
			bool previousSkipOnDrop = player.skipOnDrop;
			bool subscribed = false;
			bool prepared = false;
			bool succeeded = false;
			long firstFrame = -1;
			long readyFrame = -1;
			Exception error = null;
			using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			CancellationToken token = cancellation.Token;
			Action cancel = null;

			bool IsCurrent(VideoPlayer source)
			{
				return !_destroyed && !token.IsCancellationRequested && source == player &&
					ReferenceEquals(_cancelPreparation, cancel);
			}
			void OnPrepared(VideoPlayer source)
			{
				if (!IsCurrent(source))
				{
					return;
				}
				prepared = true;
				try
				{
					if (source.canSetSkipOnDrop)
					{
						source.skipOnDrop = false;
					}
					source.Play();
				}
				catch (Exception exception)
				{
					error = exception;
				}
			}
			void OnFrameReady(VideoPlayer source, long frame)
			{
				if (!IsCurrent(source) || frame < 0)
				{
					return;
				}
				if (firstFrame < 0)
				{
					firstFrame = frame;
				}
				readyFrame = frame;
				try
				{
					source.Pause();
				}
				catch (Exception exception)
				{
					error = exception;
				}
			}
			void OnError(VideoPlayer source, string message)
			{
				if (IsCurrent(source))
				{
					error = new InvalidOperationException($"Video preparation failed: {message}");
				}
			}
			void Unsubscribe()
			{
				if (!subscribed)
				{
					return;
				}
				subscribed = false;
				if (player != null)
				{
					player.prepareCompleted -= OnPrepared;
					player.frameReady -= OnFrameReady;
					player.errorReceived -= OnError;
					player.sendFrameReadyEvents = previousFrameReadyEvents;
					if (player.canSetSkipOnDrop)
					{
						player.skipOnDrop = previousSkipOnDrop;
					}
				}
			}

			cancel = () =>
			{
				cancellation.Cancel();
				Unsubscribe();
			};
			_cancelPreparation = cancel;
			try
			{
				player.Stop();
				player.source = VideoSource.Url;
				player.url = videoPath;
				player.isLooping = false;
				player.playOnAwake = false;
				player.waitForFirstFrame = true;
				player.prepareCompleted += OnPrepared;
				player.frameReady += OnFrameReady;
				player.errorReceived += OnError;
				subscribed = true;
				player.sendFrameReadyEvents = true;
				double deadline = Time.realtimeSinceStartupAsDouble + 15d;
				player.Prepare();
				while (true)
				{
					token.ThrowIfCancellationRequested();
					if (error != null)
					{
						throw error;
					}
					if (player == null)
					{
						throw new InvalidOperationException("The preview video player was destroyed during preparation.");
					}
					if (prepared && readyFrame >= 0 && player.isPrepared &&
						player.texture != null && player.frame == readyFrame)
					{
						succeeded = true;
						break;
					}
					if (Time.realtimeSinceStartupAsDouble >= deadline)
					{
						throw new TimeoutException("Video preparation timed out before the first frame was available.");
					}
					await Task.Yield();
				}
			}
			finally
			{
				Unsubscribe();
				if (ReferenceEquals(_cancelPreparation, cancel))
				{
					_cancelPreparation = null;
					if (!succeeded)
					{
						SetVideoVisible(false);
						if (player != null)
						{
							player.Stop();
						}
					}
				}
			}
			return new DhmgsVideoClock(player, loop, firstFrame);
		}

		public void SetVideoAlpha(float alpha)
		{
			_videoAlpha = Mathf.Clamp01(alpha);
			if (_surface != null)
			{
				_surface.SetAlpha(_videoAlpha);
			}
		}

		public void SetVideoVisible(bool visible)
		{
			if (_surface != null)
			{
				_surface.SetVisible(visible);
			}
		}

		public void Deactivate()
		{
			Action cancel = _cancelPreparation;
			_cancelPreparation = null;
			cancel?.Invoke();
			SetVideoVisible(false);
			if (_videoPlayer != null)
			{
				_videoPlayer.Stop();
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
				maxPelvisZ = Mathf.Max(maxPelvisZ, asset.pelvisPositions[i].z);
			}
			return Mathf.Clamp(maxPelvisZ + 1f, near * 1.01f, far * 0.99f);
		}

		private void EnsureSurface(Camera camera)
		{
			if (_surface != null)
			{
				return;
			}
			GameObject surface = new GameObject("DhmgsVideoSurface");
			surface.transform.SetParent(camera.transform, false);
			if (!string.IsNullOrEmpty(_surfaceLayer))
			{
				int layer = LayerMask.NameToLayer(_surfaceLayer);
				if (layer >= 0)
				{
					surface.layer = layer;
				}
			}
			_surface = surface.AddComponent<DhmgsVideoSurface>();
		}

		private void OnDestroy()
		{
			_destroyed = true;
			Deactivate();
			DestroySurface();
		}

		private void DestroySurface()
		{
			if (_surface == null)
			{
				return;
			}
			GameObject surface = _surface.gameObject;
			_surface = null;
			if (Application.isPlaying)
			{
				Destroy(surface);
			}
			else
			{
				DestroyImmediate(surface);
			}
		}
	}
}