using UnityEngine;
using UnityEngine.Video;

namespace Mnnh.Dhmgs
{
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
				_material.SetFloat("_Surface", 1f);
				_material.SetFloat("_Blend", 0f);
				_material.SetFloat("_ZWrite", 0f);
				_material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
				_material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
				_material.SetFloat("_Cull", 0f);
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
				DestroyResource(_mesh);
			}
			if (_material != null)
			{
				DestroyResource(_material);
			}
		}

		private static void DestroyResource(Object value)
		{
			if (value == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				Destroy(value);
			}
			else
			{
				DestroyImmediate(value);
			}
		}
	}
}
