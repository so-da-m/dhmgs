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

	public sealed class DhmgsSampleImportContext
	{
		private static int _nextSessionId;

		public DhmgsSampleImportContext(
			DhmgsFullDemoController owner,
			DhmgsMotionPlayer player,
			Camera previewCamera,
			DhmgsFixedAvatarCamera humanoidPreviewCamera)
		{
			Owner = owner;
			Player = player;
			PreviewCamera = previewCamera;
			HumanoidPreviewCamera = humanoidPreviewCamera;
			SessionId = owner != null ? Interlocked.Increment(ref _nextSessionId) : 0;
		}

		public DhmgsFullDemoController Owner { get; }
		public DhmgsMotionPlayer Player { get; }
		public Camera PreviewCamera { get; }
		public DhmgsFixedAvatarCamera HumanoidPreviewCamera { get; }
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

	public static class DhmgsFullDemoImportGateway
	{
		public delegate Task<DhmgsSampleImportResult> ImportHandler(
			DhmgsSampleImportContext context,
			string baseUrl,
			int attemptId,
			Action<DhmgsSampleImportProgress> onProgress,
			CancellationToken cancellationToken);

		private static ImportHandler _importHandler;
		private static Action<DhmgsSampleImportContext, float> _previewOpacityHandler;
		private static Action<DhmgsSampleImportContext> _stopPreviewHandler;

		public static bool IsAvailable => _importHandler != null;
		public static event Action AvailabilityChanged;

		public static bool SupportsVideoPreview(DhmgsSampleImportContext context)
		{
			return _previewOpacityHandler != null && context != null && context.PreviewCamera != null;
		}

		public static void Register(
			ImportHandler importHandler,
			Action<DhmgsSampleImportContext, float> previewOpacityHandler,
			Action<DhmgsSampleImportContext> stopPreviewHandler)
		{
			bool wasAvailable = IsAvailable;
			_importHandler = importHandler;
			_previewOpacityHandler = previewOpacityHandler;
			_stopPreviewHandler = stopPreviewHandler;
			if (wasAvailable != IsAvailable)
			{
				AvailabilityChanged?.Invoke();
			}
		}

		public static void Unregister(ImportHandler importHandler)
		{
			if (_importHandler != importHandler)
			{
				return;
			}

			bool wasAvailable = IsAvailable;
			_importHandler = null;
			_previewOpacityHandler = null;
			_stopPreviewHandler = null;
			if (wasAvailable)
			{
				AvailabilityChanged?.Invoke();
			}
		}

		public static Task<DhmgsSampleImportResult> ImportAndSaveAsync(
			DhmgsSampleImportContext context,
			string baseUrl,
			int attemptId,
			Action<DhmgsSampleImportProgress> onProgress,
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

			return _importHandler(context, baseUrl, attemptId, onProgress, cancellationToken);
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
