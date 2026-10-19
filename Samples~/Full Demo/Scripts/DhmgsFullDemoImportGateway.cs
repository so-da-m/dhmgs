using System;
using System.Threading;
using System.Threading.Tasks;

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

	public sealed class DhmgsSampleImportResult
	{
		public DhmgsSampleImportResult(
			DhmgsMotionAsset motion,
			AnimationClip humanoidClip,
			string videoPath,
			string summary)
		{
			Motion = motion;
			HumanoidClip = humanoidClip;
			VideoPath = videoPath;
			Summary = summary;
		}

		public DhmgsMotionAsset Motion { get; }
		public AnimationClip HumanoidClip { get; }
		public string VideoPath { get; }
		public string Summary { get; }
	}

	public static class DhmgsFullDemoImportGateway
	{
		public delegate Task<DhmgsSampleImportResult> ImportHandler(
			string baseUrl,
			int attemptId,
			Action<DhmgsSampleImportProgress> onProgress,
			CancellationToken cancellationToken);

		private static ImportHandler _importHandler;

		public static bool IsAvailable => _importHandler != null;
		public static event Action AvailabilityChanged;

		public static void Register(ImportHandler importHandler)
		{
			bool wasAvailable = IsAvailable;
			_importHandler = importHandler;
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
			if (wasAvailable)
			{
				AvailabilityChanged?.Invoke();
			}
		}

		public static Task<DhmgsSampleImportResult> ImportAndSaveAsync(
			string baseUrl,
			int attemptId,
			Action<DhmgsSampleImportProgress> onProgress,
			CancellationToken cancellationToken)
		{
			if (_importHandler == null)
			{
				throw new InvalidOperationException("Attempt ID import is available in the Unity Editor only.");
			}

			return _importHandler(baseUrl, attemptId, onProgress, cancellationToken);
		}
	}
}
