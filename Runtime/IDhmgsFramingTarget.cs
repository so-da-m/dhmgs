using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>Provides the stable transform used as an orbit target.</summary>
	public interface IDhmgsFramingTarget
	{
		Transform Pivot { get; }
	}

	/// <summary>
	/// Optional companion to <see cref="IDhmgsFramingTarget"/>. Preview cameras use these
	/// renderer bounds to fit the complete subject instead of relying on a rig-specific distance.
	/// </summary>
	public interface IDhmgsFramingBounds
	{
		bool TryGetFramingBounds(out Bounds bounds);
	}

	public static class DhmgsFramingBoundsUtility
	{
		public static bool TryCalculate(Transform root, out Bounds bounds)
		{
			bounds = default;
			bool found = false;
			Encapsulate(root, ref bounds, ref found);
			return found;
		}

		public static void Encapsulate(Transform root, ref Bounds bounds, ref bool found)
		{
			if (root == null)
			{
				return;
			}

			foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
			{
				if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
				{
					continue;
				}

				Bounds candidate = renderer.bounds;
				if (!IsFinite(candidate.center) || !IsFinite(candidate.extents))
				{
					continue;
				}

				if (found)
				{
					bounds.Encapsulate(candidate);
				}
				else
				{
					bounds = candidate;
					found = true;
				}
			}
		}

		private static bool IsFinite(Vector3 value)
		{
			return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
				&& !float.IsNaN(value.y) && !float.IsInfinity(value.y)
				&& !float.IsNaN(value.z) && !float.IsInfinity(value.z);
		}
	}
}
