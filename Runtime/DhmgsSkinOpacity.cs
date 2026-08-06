using System.Collections.Generic;

using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// Controls the package shaders' per-renderer _Opacity value without cloning or mutating
	/// shared materials. Attach this component to the root that owns the skinned renderers.
	/// </summary>
	public sealed class DhmgsSkinOpacity : MonoBehaviour
	{
		private readonly struct MaterialSlot
		{
			public MaterialSlot(Renderer renderer, int index)
			{
				Renderer = renderer;
				Index = index;
			}

			public Renderer Renderer { get; }
			public int Index { get; }
		}

		private static readonly int OpacityProperty = Shader.PropertyToID("_Opacity");

		private readonly List<MaterialSlot> _slots = new List<MaterialSlot>();
		private readonly List<string> _inspectedMaterials = new List<string>();
		private MaterialPropertyBlock _propertyBlock;
		private bool _built;

		/// <summary>0 is transparent and 1 is opaque.</summary>
		public void SetOpacity(float opacity)
		{
			EnsureBuilt();
			float alpha = Mathf.Clamp01(opacity);
			foreach (MaterialSlot slot in _slots)
			{
				Renderer renderer = slot.Renderer;
				if (renderer == null)
				{
					continue;
				}

				_propertyBlock.Clear();
				renderer.GetPropertyBlock(_propertyBlock, slot.Index);
				_propertyBlock.SetFloat(OpacityProperty, alpha);
				renderer.SetPropertyBlock(_propertyBlock, slot.Index);
			}
		}

		private void EnsureBuilt()
		{
			if (_built)
			{
				return;
			}
			_built = true;
			_propertyBlock = new MaterialPropertyBlock();

			foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				Material[] materials = renderer.sharedMaterials;
				for (int index = 0; index < materials.Length; index++)
				{
					Material material = materials[index];
					if (material == null)
					{
						_inspectedMaterials.Add($"{renderer.name}[{index}]=null");
						continue;
					}

					string shaderName = material.shader != null ? material.shader.name : "Missing Shader";
					_inspectedMaterials.Add($"{renderer.name}[{index}]={material.name} ({shaderName})");
					if (material.HasProperty(OpacityProperty))
					{
						_slots.Add(new MaterialSlot(renderer, index));
					}
				}
			}

			if (_slots.Count == 0)
			{
				string inspected = _inspectedMaterials.Count > 0
					? string.Join(", ", _inspectedMaterials)
					: "no SkinnedMeshRenderer materials";
				Debug.LogWarning(
					$"[DHMGS] DhmgsSkinOpacity '{name}' found no material with _Opacity. Inspected: {inspected}",
					this);
			}
		}
	}
}
