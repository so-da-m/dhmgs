using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace Mnnh.Dhmgs.Samples
{
	public abstract class DhmgsFullDemoPreview : IDisposable
	{
		public static Func<Transform, Camera, DhmgsAvatarPreviewCamera, DhmgsFullDemoPreview> Factory { get; set; }

		public abstract Task<IDhmgsSeekablePlaybackClock> PrepareAsync(
			DhmgsSampleImportResult imported, bool loop, CancellationToken cancellationToken);

		public abstract void Play(IDhmgsPlaybackClock clock);
		public abstract void SetHumanoidRootMotion(bool enabled);
		public abstract void SetVideoOpacity(float opacity);
		public abstract void SetVideoVisible(bool visible);
		public abstract void Dispose();
	}
}
