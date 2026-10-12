using UnityEngine;

namespace Mnnh.Dhmgs
{
	// Shared geometry only; each camera owns its framing, limits and reset policy.
	internal static class DhmgsCameraNavigation
	{
		private const float MinimumBoundsCenterTolerance = 1f;
		private const float BoundsRadiusToleranceMultiplier = 2f;

		internal static Vector3 OrbitDirection(float yaw, float pitch)
		{
			return Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
		}

		internal static Vector3 PanOffset(Transform camera, Vector2 delta, float distance, float speed)
		{
			return camera == null ? Vector3.zero :
				(-camera.right * delta.x - camera.up * delta.y) * (speed * distance);
		}

		internal static bool TryGetFramingOffset(Bounds bounds, Vector3 pivot, out Vector3 offset)
		{
			offset = bounds.center - pivot;
			float maximumOffset = Mathf.Max(MinimumBoundsCenterTolerance,
				bounds.extents.magnitude * BoundsRadiusToleranceMultiplier);
			return offset.sqrMagnitude <= maximumOffset * maximumOffset;
		}
	}
}
