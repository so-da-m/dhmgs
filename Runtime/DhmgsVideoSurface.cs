using UnityEngine;
using UnityEngine.Video;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 카메라 프러스텀을 채우는 영상 쿼드. 코너 4개를 받아 메시를 만들고 VideoPlayer를 머티리얼에 직접 렌더한다.
	/// 카메라의 자식(로컬 origin/identity)으로 둔다. 수학/네트워크를 모른다 — 코너와 VideoPlayer만 받는다.
	/// </summary>
	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public sealed class DhmgsVideoSurface : MonoBehaviour
	{
		private MeshFilter _filter;
		private MeshRenderer _renderer;
		private Mesh _mesh;
		private Material _material;
		private Color _baseColor = new Color(1f, 1f, 1f, 0.5f);

		private void Awake()
		{
			EnsureComponents();
		}

		/// <summary>카메라 로컬좌표 코너 4개(좌하, 우하, 좌상, 우상)로 쿼드 메시 구성.</summary>
		public void SetCorners(Vector3[] corners)
		{
			EnsureComponents();
			_mesh.Clear();
			_mesh.vertices = corners;
			_mesh.uv = new[]
			{
				new Vector2(0f, 0f),
				new Vector2(1f, 0f),
				new Vector2(0f, 1f),
				new Vector2(1f, 1f),
			};
			_mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
			_mesh.RecalculateBounds();
		}

		/// <summary>VideoPlayer를 이 쿼드 머티리얼에 직접 렌더(RenderTexture 불필요).</summary>
		public void BindVideo(VideoPlayer videoPlayer)
		{
			EnsureComponents();
			videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
			videoPlayer.targetMaterialRenderer = _renderer;
			videoPlayer.targetMaterialProperty = "_BaseMap";
		}

		public void SetVisible(bool visible)
		{
			gameObject.SetActive(visible);
		}

		/// <summary>영상 불투명도(0=투명, 1=불투명).</summary>
		public void SetAlpha(float alpha)
		{
			EnsureComponents();
			_baseColor.a = Mathf.Clamp01(alpha);
			_material.SetColor("_BaseColor", _baseColor);
		}

		private void EnsureComponents()
		{
			if (_filter == null)
			{
				_filter = GetComponent<MeshFilter>();
			}
			if (_renderer == null)
			{
				_renderer = GetComponent<MeshRenderer>();
			}
			if (_material == null)
			{
				Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
				if (shader == null)
				{
					shader = Shader.Find("Unlit/Transparent");
				}
				_material = new Material(shader);
				// 반투명 합성: 영상 위로 아바타가 비쳐 보이게(ZWrite off + alpha blend). _BaseColor.a로 투명도 조절.
				_material.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
				_material.SetFloat("_Blend", 0f);   // 0=Alpha
				_material.SetFloat("_ZWrite", 0f);
				_material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
				_material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
				_material.SetFloat("_Cull", 0f); // 양면
				_material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
				_material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
				_material.SetColor("_BaseColor", _baseColor);
				_renderer.sharedMaterial = _material;
				_renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				_renderer.receiveShadows = false;
			}
			if (_mesh == null)
			{
				_mesh = new Mesh { name = "DhmgsVideoSurface" };
				_filter.sharedMesh = _mesh;
			}
		}

		private void OnDestroy()
		{
			if (_mesh != null)
			{
				Destroy(_mesh);
			}
			if (_material != null)
			{
				Destroy(_material);
			}
		}
	}
}
