using System;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Mnnh.Dhmgs.Samples
{
	internal sealed class DhmgsFullDemoGround : IDisposable
	{
		private readonly Material _material;
		private GameObject _instance;
		private Mesh _mesh;
		private Vector3 _anchor;
		private bool _hasAnchor;

		public DhmgsFullDemoGround(Material material)
		{
			_material = material;
		}

		public void Refresh(Transform origin, bool visible, float height)
		{
			if (origin == null || !origin.gameObject.activeInHierarchy)
			{
				_hasAnchor = false;
				_instance?.SetActive(false);
				return;
			}
			if (!_hasAnchor)
			{
				_anchor = origin.position;
				_hasAnchor = true;
			}
			if (!visible || _material == null)
			{
				_instance?.SetActive(false);
				return;
			}
			if (_instance == null)
			{
				Create(origin.gameObject.scene);
			}
			_instance.transform.position = new Vector3(_anchor.x, height, _anchor.z);
			_instance.SetActive(true);
		}

		private void Create(Scene scene)
		{
			_mesh = new Mesh
			{
				name = "DHMGS Demo Ground",
				hideFlags = HideFlags.HideAndDontSave,
				vertices = new[]
				{
					new Vector3(-20f, 0f, -20f),
					new Vector3(-20f, 0f, 20f),
					new Vector3(20f, 0f, 20f),
					new Vector3(20f, 0f, -20f),
				},
				triangles = new[] { 0, 1, 2, 0, 2, 3 },
			};
			_mesh.RecalculateBounds();
			_mesh.UploadMeshData(true);
			_instance = new GameObject("DHMGS Demo Ground") { hideFlags = HideFlags.DontSave };
			SceneManager.MoveGameObjectToScene(_instance, scene);
			_instance.AddComponent<MeshFilter>().sharedMesh = _mesh;
			MeshRenderer renderer = _instance.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = _material;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = true;
		}

		public void Dispose()
		{
			_instance?.SetActive(false);
			Destroy(_instance);
			Destroy(_mesh);
			_instance = null;
			_mesh = null;
			_hasAnchor = false;
		}

		private static void Destroy(UnityEngine.Object value)
		{
			if (value == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(value);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(value);
			}
		}
	}
}
