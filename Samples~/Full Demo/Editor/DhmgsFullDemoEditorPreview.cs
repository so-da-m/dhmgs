using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;
using UnityEngine.Video;

namespace Mnnh.Dhmgs.Samples.Editor
{
	[InitializeOnLoad]
	public sealed class DhmgsFullDemoEditorPreview : DhmgsFullDemoPreview
	{
		private const string PreviewAvatarGuid = "121420fccd4c4bba93c676f768fdf0a5";
		private static int _backgroundPreparations;
		private static bool _previousRunInBackground;

		private readonly Transform _owner;
		private readonly Camera _referenceCamera;
		private readonly DhmgsAvatarPreviewCamera _humanoidCamera;
		private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
		private GameObject _host;
		private GameObject _avatarPrefab;
		private AnimationClip _clip;
		private DhmgsCameraOverlay _overlay;
		private DhmgsHumanoidPreview _humanoidPreview;
		private Matrix4x4 _previousProjection;
		private Rect _previousRect;
		private float _previousAspect;
		private float _videoOpacity = 1f;
		private bool _cameraConfigured;
		private bool _preparationStarted;
		private bool _hasHumanoidTarget;
		private bool _holdsBackgroundPreparation;
		private bool _disposed;

		static DhmgsFullDemoEditorPreview()
		{
			Factory = Create;
			AssemblyReloadEvents.beforeAssemblyReload += Unregister;
		}

		private DhmgsFullDemoEditorPreview(
			Transform owner,
			Camera referenceCamera,
			DhmgsAvatarPreviewCamera humanoidCamera)
		{
			_owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
			_referenceCamera = referenceCamera;
			_humanoidCamera = humanoidCamera;
		}

		private static DhmgsFullDemoPreview Create(
			Transform owner,
			Camera referenceCamera,
			DhmgsAvatarPreviewCamera humanoidCamera)
		{
			return new DhmgsFullDemoEditorPreview(owner, referenceCamera, humanoidCamera);
		}

		private static void Unregister()
		{
			if (Factory == Create)
			{
				Factory = null;
			}
			AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
			if (_backgroundPreparations > 0)
			{
				_backgroundPreparations = 0;
				Application.runInBackground = _previousRunInBackground;
			}
		}

		public override async Task<IDhmgsSeekablePlaybackClock> PrepareAsync(
			DhmgsSampleImportResult imported,
			bool loop,
			CancellationToken cancellationToken)
		{
			ThrowIfDisposed();
			cancellationToken.ThrowIfCancellationRequested();
			if (_preparationStarted)
			{
				throw new InvalidOperationException("The preview has already been prepared.");
			}
			if (imported == null || imported.Motion == null || !imported.Motion.IsValid)
			{
				throw new ArgumentException("A valid imported motion is required.", nameof(imported));
			}
			_preparationStarted = true;
			ConfigureCamera(imported.Motion);
			if (_humanoidCamera != null)
			{
				_clip = imported.HumanoidClip;
				if (_clip == null)
				{
					throw new InvalidOperationException("The imported motion has no Humanoid preview clip.");
				}
				string path = AssetDatabase.GUIDToAssetPath(PreviewAvatarGuid);
				_avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (_avatarPrefab == null)
				{
					throw new InvalidOperationException("The bundled Y Bot preview avatar is missing.");
				}
				EnsureHost();
				_humanoidPreview = _host.AddComponent<DhmgsHumanoidPreview>();
			}
			if (_referenceCamera == null || string.IsNullOrEmpty(imported.VideoPath))
			{
				return null;
			}

			EnsureHost();
			VideoPlayer player = _host.AddComponent<VideoPlayer>();
			player.audioOutputMode = VideoAudioOutputMode.None;
			_overlay = _host.AddComponent<DhmgsCameraOverlay>();
			_overlay.Configure(_referenceCamera, player, _videoOpacity, "Ignore Raycast");
			using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken, _lifetimeCancellation.Token);
			BeginBackgroundPreparation();
			try
			{
				return await _overlay.PrepareAsync(imported.Motion, imported.VideoPath, loop, cancellation.Token);
			}
			finally
			{
				EndBackgroundPreparation();
			}
		}

		public override void Play(IDhmgsPlaybackClock clock)
		{
			ThrowIfDisposed();
			if (clock == null)
			{
				throw new ArgumentNullException(nameof(clock));
			}
			if (_humanoidPreview == null)
			{
				return;
			}
			if (!_humanoidPreview.Play(_avatarPrefab, _clip, clock, ResolveIsolationPosition()))
			{
				throw new InvalidOperationException("Could not play the baked Humanoid clip on the preview avatar.");
			}
			if (_humanoidCamera != null)
			{
				_humanoidCamera.SetTarget(_humanoidPreview);
				_hasHumanoidTarget = true;
			}
		}

		public override void SetVideoOpacity(float opacity)
		{
			_videoOpacity = Mathf.Clamp01(opacity);
			if (_overlay != null)
			{
				_overlay.SetVideoAlpha(_videoOpacity);
			}
		}

		public override void SetVideoVisible(bool visible)
		{
			if (_overlay != null)
			{
				_overlay.SetVideoVisible(visible);
			}
		}

		private void ConfigureCamera(DhmgsMotionAsset motion)
		{
			if (_referenceCamera == null || !motion.HasProjection)
			{
				return;
			}
			_previousProjection = _referenceCamera.projectionMatrix;
			_previousRect = _referenceCamera.rect;
			_previousAspect = _referenceCamera.aspect;
			_cameraConfigured = true;
			_referenceCamera.rect = new Rect(0f, 0f, 1f, 1f);
			_referenceCamera.aspect = (float)motion.imageWidth / motion.imageHeight;
			_referenceCamera.projectionMatrix = DhmgsProjection.BuildProjectionMatrix(
				motion.focalLength.x, motion.focalLength.y,
				motion.principalPoint.x, motion.principalPoint.y,
				motion.imageWidth, motion.imageHeight,
				_referenceCamera.nearClipPlane, _referenceCamera.farClipPlane);
		}

		private void EnsureHost()
		{
			if (_host != null)
			{
				return;
			}
			if (_owner == null)
			{
				throw new InvalidOperationException("The preview owner is no longer available.");
			}
			_host = new GameObject($"DHMGS Editor Preview - {_owner.name}")
			{
				hideFlags = HideFlags.DontSave,
			};
			_host.transform.SetParent(_owner, false);
		}

		private Vector3 ResolveIsolationPosition()
		{
			if (_referenceCamera != null)
			{
				float distance = Mathf.Max(1000f, _referenceCamera.farClipPlane * 2f + 100f);
				return _referenceCamera.transform.position + _referenceCamera.transform.right * distance;
			}
			return _owner.position + Vector3.right * 10000f;
		}

		private void BeginBackgroundPreparation()
		{
			if (_backgroundPreparations++ == 0)
			{
				_previousRunInBackground = Application.runInBackground;
				Application.runInBackground = true;
			}
			_holdsBackgroundPreparation = true;
		}

		private void EndBackgroundPreparation()
		{
			if (!_holdsBackgroundPreparation)
			{
				return;
			}
			_holdsBackgroundPreparation = false;
			if (_backgroundPreparations > 0 && --_backgroundPreparations == 0)
			{
				Application.runInBackground = _previousRunInBackground;
			}
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
			{
				throw new ObjectDisposedException(nameof(DhmgsFullDemoEditorPreview));
			}
		}

		public override void Dispose()
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			_lifetimeCancellation.Cancel();
			if (_overlay != null)
			{
				_overlay.Deactivate();
				_overlay = null;
			}
			EndBackgroundPreparation();
			if (_humanoidPreview != null)
			{
				if (_hasHumanoidTarget && _humanoidCamera != null &&
					_humanoidCamera.TargetPivot == _humanoidPreview.Pivot)
				{
					_humanoidCamera.SetTarget(null);
				}
				_humanoidPreview.Stop();
				_humanoidPreview = null;
			}
			_hasHumanoidTarget = false;
			if (_cameraConfigured && _referenceCamera != null)
			{
				_referenceCamera.rect = _previousRect;
				_referenceCamera.aspect = _previousAspect;
				_referenceCamera.projectionMatrix = _previousProjection;
			}
			_cameraConfigured = false;
			if (_host != null)
			{
				if (Application.isPlaying)
				{
					UnityEngine.Object.Destroy(_host);
				}
				else
				{
					UnityEngine.Object.DestroyImmediate(_host);
				}
				_host = null;
			}
			_avatarPrefab = null;
			_clip = null;
			_lifetimeCancellation.Dispose();
		}
	}
}