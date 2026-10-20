using System.Threading;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal static class DhmgsCurveTangents
	{
		internal static void ApplyLinear(
			Keyframe[] keys,
			float frameDuration,
			CancellationToken cancellationToken)
		{
			for (int frameIndex = 0; frameIndex < keys.Length; frameIndex++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				Keyframe key = keys[frameIndex];
				key.inTangent = frameIndex > 0
					? (key.value - keys[frameIndex - 1].value) / frameDuration
					: frameIndex + 1 < keys.Length
						? (keys[frameIndex + 1].value - key.value) / frameDuration
						: 0f;
				key.outTangent = frameIndex + 1 < keys.Length
					? (keys[frameIndex + 1].value - key.value) / frameDuration
					: key.inTangent;
				keys[frameIndex] = key;
			}
		}
	}
}
