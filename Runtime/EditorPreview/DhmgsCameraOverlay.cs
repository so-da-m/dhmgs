using System;
using System.Collections;

using UnityEngine;
using UnityEngine.Video;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 얇은 코디네이터: SO의 intrinsics로 카메라 projection을 맞추고, 영상을 프러스텀 쿼드에 깔아
	/// 아바타를 그 위에 정합 렌더한다. 수학은 DhmgsProjection, 쿼드 렌더는 DhmgsVideoSurface에 위임.
	/// 카메라는 원점/단위회전 전제(sequence에 카메라 extrinsic 없음).
	/// ON/OFF는 projection + rect + 쿼드 + clock 상태를 한 곳에서 함께 바꾼다.
	/// </summary>
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
		private Rect _backupRect;
		private bool _backedUp;

		/// <summary>
		/// 오버레이 활성화. 영상 준비 성공 시 onReady(영상 동기 클럭). 오버레이 불가/영상 실패 시 onReady(null)
		/// (호출측이 DhmgsRealtimeClock으로 폴백).
		/// </summary>
		public IEnumerator Activate(DhmgsMotionAsset asset, string videoPath, Action<IDhmgsPlaybackClock> onReady)
		{
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
				// 영상 준비 실패 → 오버레이 접고 폴백(아바타는 RuntimeUI가 fps 시계로 재생).
				_surface.SetVisible(false);
				RestoreCamera(cam);
				onReady?.Invoke(null);
				yield break;
			}

			_videoPlayer.Play();
			onReady?.Invoke(new DhmgsVideoClock(_videoPlayer));
		}

		/// <summary>오버레이 해제: 쿼드 숨김 + 영상 정지 + 카메라 projection/aspect/rect 복원.</summary>
		public void Deactivate()
		{
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

		private void Update()
		{
			// 인스펙터에서 투명도를 플레이 중 조절할 수 있게 매 프레임 반영.
			if (_surface != null)
			{
				_surface.SetAlpha(_videoAlpha);
			}
		}

		/// <summary>런타임에서 영상 투명도 조절(UI 슬라이더 등).</summary>
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

		// 일반(auto) 씬 카메라 전제: projection/aspect를 auto로 되돌리고 rect 복원.
		private void RestoreCamera(Camera cam)
		{
			cam.ResetProjectionMatrix();
			cam.ResetAspect();
			cam.rect = _backupRect;
		}

		// 창 비율이 영상 비율과 다르면 letterbox/pillarbox로 왜곡 방지(정합은 무관하게 유지).
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

		// 쿼드를 아바타보다 뒤(더 먼 깊이)에 둔다. 아바타가 불투명 depth를 기록하므로
		// depth test로 아바타가 항상 앞에 그려진다(쿼드가 불투명이어도 안 가림).
		// 쿼드는 프러스텀을 채우므로 깊이가 멀어도 화면 전체를 덮는다.
		private float ResolveDepth(DhmgsMotionAsset asset, float near, float far)
		{
			if (_quadDepth > 0f)
			{
				return Mathf.Clamp(_quadDepth, near * 1.01f, far * 0.99f);
			}

			// 가장 먼 pelvis 깊이 + 여유(몸 두께/사지). 모든 프레임에서 아바타 뒤가 되도록.
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

		/// <summary>
		/// Cancels an in-flight VideoPlayer preparation and releases all event subscriptions.
		/// The activation coroutine observes the cancellation as a failed preparation.
		/// </summary>
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
