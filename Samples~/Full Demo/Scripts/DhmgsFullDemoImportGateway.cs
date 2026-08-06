using System;
using System.Threading;
using System.Threading.Tasks;

using Mnnh.Dhmgs;

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
	/// Runtime-safe registration point. The sample Editor assembly installs the implementation;
	/// Player builds see no handler and keep only already imported motion playback.
	/// </summary>
	public static class DhmgsFullDemoImportGateway
	{
		public delegate Task<DhmgsMotionAsset> ImportHandler(
			string baseUrl,
			int attemptId,
			IProgress<DhmgsSampleImportProgress> progress,
			CancellationToken cancellationToken);

		private static ImportHandler _handler;

		public static bool IsAvailable => _handler != null;

		public static void Register(ImportHandler handler)
		{
			_handler = handler;
		}

		public static void Unregister(ImportHandler handler)
		{
			if (_handler == handler)
			{
				_handler = null;
			}
		}

		public static Task<DhmgsMotionAsset> ImportAndSaveAsync(
			string baseUrl,
			int attemptId,
			IProgress<DhmgsSampleImportProgress> progress,
			CancellationToken cancellationToken)
		{
			if (_handler == null)
			{
				throw new InvalidOperationException("Attempt ID import is available in the Unity Editor only.");
			}
			return _handler(baseUrl, attemptId, progress, cancellationToken);
		}
	}
}
