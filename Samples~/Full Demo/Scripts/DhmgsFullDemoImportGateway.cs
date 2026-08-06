using System;
using System.Threading;
using System.Threading.Tasks;

using Mnnh.Dhmgs;

using UnityEngine;

namespace Mnnh.Dhmgs.Samples
{
	public readonly struct DhmgsSampleImportProgress
	{
		public DhmgsSampleImportProgress(float value, string status)
		{
			Value = value;
			Status = status;
		}

		public float Value { get; }
		public string Status { get; }
	}

	/// <summary>
	/// Explicit sample-owned references used by Editor preview code. Passing this context
	/// keeps additive scenes and unrelated tools from being discovered or modified globally.
	/// </summary>
	public sealed class DhmgsSampleImportContext
	{
		private static int _nextSessionId;

		public DhmgsSampleImportContext(
			DhmgsFullDemoController owner,
			DhmgsMotionPlayer player,
			Camera previewCamera,
			DhmgsFixedAvatarCamera bakedPreviewCamera)
		{
			Owner = owner;
			Player = player;
			PreviewCamera = previewCamera;
			BakedPreviewCamera = bakedPreviewCamera;
			SessionId = owner != null ? Interlocked.Increment(ref _nextSessionId) : 0;
		}

		public DhmgsFullDemoController Owner { get; }
		public DhmgsMotionPlayer Player { get; }
		public Camera PreviewCamera { get; }
		public DhmgsFixedAvatarCamera BakedPreviewCamera { get; }
		public int SessionId { get; }
		public bool IsActive => Owner != null && Owner.isActiveAndEnabled;
	}

	public sealed class DhmgsSampleImportResult
	{
		public DhmgsSampleImportResult(
			DhmgsMotionAsset motion,
			IDhmgsPlaybackClock clock,
			string summary,
			bool hasVideoPreview)
		{
			Motion = motion;
			Clock = clock;
			Summary = summary;
			HasVideoPreview = hasVideoPreview;
		}

		public DhmgsMotionAsset Motion { get; }
		public IDhmgsPlaybackClock Clock { get; }
		public string Summary { get; }
		public bool HasVideoPreview { get; }
	}

	/// <summary>
	/// Runtime-safe registration point. The sample Editor assembly installs downloading,
	/// baking, and video-preview handlers. Player builds retain only serialized playback.
	/// </summary>
	public static class DhmgsFullDemoImportGateway
	{
		public delegate Task<DhmgsSampleImportResult> ImportHandler(
			DhmgsSampleImportContext context,
			string baseUrl,
			int attemptId,
			IProgress<DhmgsSampleImportProgress> progress,
			CancellationToken cancellationToken);

		private static ImportHandler _importHandler;
		private static Action<DhmgsSampleImportContext, float> _previewOpacityHandler;
		private static Action<DhmgsSampleImportContext> _stopPreviewHandler;

		public static bool IsAvailable => _importHandler != null;

		public static bool SupportsVideoPreview(DhmgsSampleImportContext context)
		{
			return _previewOpacityHandler != null && context != null && context.PreviewCamera != null;
		}

		public static void Register(
			ImportHandler importHandler,
			Action<DhmgsSampleImportContext, float> previewOpacityHandler,
			Action<DhmgsSampleImportContext> stopPreviewHandler)
		{
			_importHandler = importHandler;
			_previewOpacityHandler = previewOpacityHandler;
			_stopPreviewHandler = stopPreviewHandler;
		}

		public static void Unregister(ImportHandler importHandler)
		{
			if (_importHandler != importHandler)
			{
				return;
			}

			_importHandler = null;
			_previewOpacityHandler = null;
			_stopPreviewHandler = null;
		}

		public static Task<DhmgsSampleImportResult> ImportAndSaveAsync(
			DhmgsSampleImportContext context,
			string baseUrl,
			int attemptId,
			IProgress<DhmgsSampleImportProgress> progress,
			CancellationToken cancellationToken)
		{
			if (_importHandler == null)
			{
				throw new InvalidOperationException("Attempt ID import is available in the Unity Editor only.");
			}
			if (context == null)
			{
				throw new ArgumentNullException(nameof(context));
			}

			return _importHandler(context, baseUrl, attemptId, progress, cancellationToken);
		}

		public static void SetVideoOpacity(DhmgsSampleImportContext context, float opacity)
		{
			if (context != null)
			{
				_previewOpacityHandler?.Invoke(context, opacity);
			}
		}

		public static void StopPreview(DhmgsSampleImportContext context)
		{
			if (context != null)
			{
				_stopPreviewHandler?.Invoke(context);
			}
		}
	}
}
