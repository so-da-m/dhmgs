using System.Collections.Generic;

using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 리그(아바타/의상) 피부 머티리얼의 투명도(_Opacity)를 조절. 머티리얼은 이미 적용돼 있다고 가정한다.
	/// _Opacity 프로퍼티를 가진 셰이더면 무엇이든 동작하므로, 아바타와 의상이 서로 다른 셰이더여도
	/// 프로퍼티 이름만 _Opacity로 같으면 동일하게 쓸 수 있다.
	/// 이 컴포넌트가 붙은 GameObject 하위에서 SkinnedMeshRenderer를 찾으니 리그 프리팹 루트에 붙인다.
	/// 머티리얼 인스턴스를 써서 공유 .mat 에셋의 영구 변경을 막고 리그별 개별 투명도를 지원한다.
	/// </summary>
	public sealed class DhmgsSkinOpacity : MonoBehaviour
	{
		private const string OpacityProperty = "_Opacity";

		private readonly List<Material> _materials = new List<Material>();
		private bool _built;

		/// <summary>0=완전 투명, 1=불투명.</summary>
		public void SetOpacity(float opacity)
		{
			EnsureBuilt();
			float alpha = Mathf.Clamp01(opacity);
			foreach (Material material in _materials)
			{
				if (material != null && material.HasProperty(OpacityProperty))
				{
					material.SetFloat(OpacityProperty, alpha);
				}
			}
		}

		// 하위 SkinnedMeshRenderer의 머티리얼 인스턴스를 1회 확보(렌더러가 인스턴스를 쓰게 됨 → 공유 에셋 보호).
		private void EnsureBuilt()
		{
			if (_built)
			{
				return;
			}
			_built = true;

			int withOpacity = 0;
			foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				foreach (Material material in smr.materials)
				{
					if (material != null)
					{
						_materials.Add(material);
						if (material.HasProperty(OpacityProperty))
						{
							withOpacity++;
						}
					}
				}
			}

			if (withOpacity == 0)
			{
				Debug.LogWarning($"[DHMGS] DhmgsSkinOpacity '{name}': _Opacity 프로퍼티를 가진 머티리얼을 찾지 못함. 메쉬가 있는 리그 루트에 붙였는지, 셰이더에 _Opacity가 있는지 확인.");
			}
		}

		private void OnDestroy()
		{
			foreach (Material material in _materials)
			{
				if (material != null)
				{
					Destroy(material);
				}
			}
		}
	}
}
