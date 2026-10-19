using System.Threading;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal static class DhmgsQuaternionCurves
	{
		internal static Keyframe[][] CreateKeys(
			Quaternion[] rotations,
			float fps,
			bool linearTangents,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Keyframe[][] keys = new Keyframe[4][];
			for (int componentIndex = 0; componentIndex < keys.Length; componentIndex++)
			{
				keys[componentIndex] = new Keyframe[rotations.Length];
			}

			float frameDuration = 1f / fps;
			Quaternion previous = Quaternion.identity;
			for (int frameIndex = 0; frameIndex < rotations.Length; frameIndex++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				Quaternion rotation = rotations[frameIndex];
				if (frameIndex > 0 && Quaternion.Dot(previous, rotation) < 0f)
				{
					rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
				}
				for (int componentIndex = 0; componentIndex < keys.Length; componentIndex++)
				{
					keys[componentIndex][frameIndex] = new Keyframe(
						frameIndex * frameDuration, rotation[componentIndex]);
				}
				previous = rotation;
			}

			if (linearTangents)
			{
				foreach (Keyframe[] componentKeys in keys)
				{
					for (int frameIndex = 0; frameIndex < componentKeys.Length; frameIndex++)
					{
						cancellationToken.ThrowIfCancellationRequested();
						Keyframe key = componentKeys[frameIndex];
						key.inTangent = frameIndex > 0
							? (key.value - componentKeys[frameIndex - 1].value) / frameDuration
							: frameIndex + 1 < componentKeys.Length
								? (componentKeys[frameIndex + 1].value - key.value) / frameDuration
								: 0f;
						key.outTangent = frameIndex + 1 < componentKeys.Length
							? (componentKeys[frameIndex + 1].value - key.value) / frameDuration
							: key.inTangent;
						componentKeys[frameIndex] = key;
					}
				}
			}
			return keys;
		}
	}
}
