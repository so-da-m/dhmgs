using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mnnh.Dhmgs
{
	[DefaultExecutionOrder(10000)]
	public sealed class DhmgsSkeletonOverlay : MaskableGraphic
	{
		[SerializeField] private Color _boneColor = new Color(0.15f, 0.9f, 1f, 0.95f);
		[SerializeField] private Color _jointColor = new Color(1f, 0.85f, 0.2f, 1f);
		[SerializeField, Min(0.5f)] private float _lineWidth = 1.5f;
		[SerializeField, Min(1f)] private float _jointRadius = 2.5f;
		[SerializeField] private bool _showNames;
		[SerializeField] private Font _labelFont;
		[SerializeField, Min(8)] private int _fontSize = 12;
		[SerializeField] private Color _labelColor = Color.white;

		private Camera _camera;
		private Transform[] _bones = new Transform[0];
		private int[] _parents = new int[0];
		private Vector2[] _positions = new Vector2[0];
		private bool[] _visible = new bool[0];
		private Text[] _labels = new Text[0];
		private RectTransform _labelsRoot;
		private Rect _viewport;
		private bool _hasProjection;

		public bool ShowNames
		{
			get => _showNames;
			set
			{
				if (_showNames == value)
				{
					return;
				}
				_showNames = value;
				UpdateLabels();
			}
		}

		public Font LabelFont
		{
			get => _labelFont;
			set
			{
				_labelFont = value;
				UpdateLabelStyle();
			}
		}

		public void Bind(Camera camera, IReadOnlyList<Transform> bones)
		{
			DestroyLabels();
			_camera = camera;
			_hasProjection = false;
			Dictionary<Transform, int> indices = new Dictionary<Transform, int>();
			List<Transform> selected = new List<Transform>();
			if (bones != null)
			{
				for (int i = 0; i < bones.Count; i++)
				{
					Transform bone = bones[i];
					if (bone != null && !indices.ContainsKey(bone))
					{
						indices.Add(bone, selected.Count);
						selected.Add(bone);
					}
				}
			}
			_bones = selected.ToArray();
			_parents = new int[_bones.Length];
			_positions = new Vector2[_bones.Length];
			_visible = new bool[_bones.Length];
			_labels = new Text[_bones.Length];
			for (int i = 0; i < _bones.Length; i++)
			{
				_parents[i] = -1;
				Transform ancestor = _bones[i].parent;
				while (ancestor != null)
				{
					if (indices.TryGetValue(ancestor, out int parent))
					{
						_parents[i] = parent;
						break;
					}
					ancestor = ancestor.parent;
				}
			}
			CreateLabels();
			SetVerticesDirty();
		}

		protected override void Awake()
		{
			base.Awake();
			raycastTarget = false;
		}

		protected override void OnEnable()
		{
			base.OnEnable();
			raycastTarget = false;
			_hasProjection = false;
			UpdateLabels();
		}

		protected override void OnDisable()
		{
			_hasProjection = false;
			UpdateLabels();
			base.OnDisable();
		}

		protected override void OnDestroy()
		{
			DestroyLabels();
			base.OnDestroy();
		}

		private void LateUpdate()
		{
			if (_camera == null || !_camera.isActiveAndEnabled || _bones.Length == 0)
			{
				if (_hasProjection)
				{
					_hasProjection = false;
					SetVerticesDirty();
				}
				UpdateLabels();
				return;
			}

			Rect rect = rectTransform.rect;
			Rect cameraRect = _camera.rect;
			_viewport = Rect.MinMaxRect(
				rect.xMin + Mathf.Clamp01(cameraRect.xMin) * rect.width,
				rect.yMin + Mathf.Clamp01(cameraRect.yMin) * rect.height,
				rect.xMin + Mathf.Clamp01(cameraRect.xMax) * rect.width,
				rect.yMin + Mathf.Clamp01(cameraRect.yMax) * rect.height);
			for (int i = 0; i < _bones.Length; i++)
			{
				_visible[i] = false;
				if (_bones[i] == null || !_bones[i].gameObject.activeInHierarchy)
				{
					continue;
				}
				Vector3 projected = _camera.WorldToViewportPoint(_bones[i].position);
				if (projected.z < _camera.nearClipPlane || projected.z > _camera.farClipPlane ||
					float.IsNaN(projected.x) || float.IsInfinity(projected.x) ||
					float.IsNaN(projected.y) || float.IsInfinity(projected.y))
				{
					continue;
				}
				_positions[i] = new Vector2(
					rect.xMin + (cameraRect.x + projected.x * cameraRect.width) * rect.width,
					rect.yMin + (cameraRect.y + projected.y * cameraRect.height) * rect.height);
				_visible[i] = true;
			}
			_hasProjection = _viewport.width > 0f && _viewport.height > 0f;
			UpdateLabels();
			SetVerticesDirty();
		}

		protected override void OnPopulateMesh(VertexHelper vertices)
		{
			vertices.Clear();
			if (!_hasProjection)
			{
				return;
			}
			float halfWidth = Mathf.Max(0.5f, _lineWidth) * 0.5f;
			Rect lineRect = Rect.MinMaxRect(_viewport.xMin + halfWidth, _viewport.yMin + halfWidth,
				_viewport.xMax - halfWidth, _viewport.yMax - halfWidth);
			Color32 boneColor = _boneColor * color;
			Color32 jointColor = _jointColor * color;
			for (int i = 0; i < _bones.Length; i++)
			{
				int parent = _parents[i];
				if (!_visible[i] || parent < 0 || !_visible[parent] || lineRect.width <= 0f || lineRect.height <= 0f)
				{
					continue;
				}
				Vector2 start = _positions[parent];
				Vector2 end = _positions[i];
				if (!ClipLine(ref start, ref end, lineRect))
				{
					continue;
				}
				Vector2 delta = end - start;
				if (delta.sqrMagnitude < 0.0001f)
				{
					continue;
				}
				Vector2 normal = new Vector2(-delta.y, delta.x).normalized * halfWidth;
				AddQuad(vertices, start - normal, start + normal, end + normal, end - normal, boneColor);
			}
			float radius = Mathf.Max(1f, _jointRadius);
			for (int i = 0; i < _bones.Length; i++)
			{
				if (!_visible[i])
				{
					continue;
				}
				Vector2 point = _positions[i];
				float left = Mathf.Max(_viewport.xMin, point.x - radius);
				float right = Mathf.Min(_viewport.xMax, point.x + radius);
				float bottom = Mathf.Max(_viewport.yMin, point.y - radius);
				float top = Mathf.Min(_viewport.yMax, point.y + radius);
				if (right > left && top > bottom)
				{
					AddQuad(vertices, new Vector2(left, bottom), new Vector2(left, top),
						new Vector2(right, top), new Vector2(right, bottom), jointColor);
				}
			}
		}

		private void CreateLabels()
		{
			if (_bones.Length == 0)
			{
				return;
			}
			GameObject root = new GameObject("SkeletonLabels", typeof(RectTransform), typeof(RectMask2D));
			root.layer = gameObject.layer;
			root.SetActive(false);
			_labelsRoot = (RectTransform)root.transform;
			_labelsRoot.SetParent(rectTransform, false);
			_labelsRoot.anchorMin = Vector2.zero;
			_labelsRoot.anchorMax = Vector2.one;
			_labelsRoot.offsetMin = Vector2.zero;
			_labelsRoot.offsetMax = Vector2.zero;
			for (int i = 0; i < _bones.Length; i++)
			{
				GameObject labelObject = new GameObject("BoneName", typeof(RectTransform), typeof(Text));
				labelObject.layer = gameObject.layer;
				labelObject.transform.SetParent(_labelsRoot, false);
				Text label = labelObject.GetComponent<Text>();
				label.raycastTarget = false;
				label.supportRichText = false;
				label.alignment = TextAnchor.MiddleLeft;
				label.horizontalOverflow = HorizontalWrapMode.Overflow;
				label.verticalOverflow = VerticalWrapMode.Overflow;
				label.text = _bones[i].name;
				label.rectTransform.anchorMin = Vector2.zero;
				label.rectTransform.anchorMax = Vector2.zero;
				label.rectTransform.pivot = Vector2.zero;
				_labels[i] = label;
			}
			UpdateLabelStyle();
		}

		private void UpdateLabelStyle()
		{
			if (_labelFont == null && _labels.Length > 0)
			{
				_labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			}
			for (int i = 0; i < _labels.Length; i++)
			{
				Text label = _labels[i];
				if (label == null)
				{
					continue;
				}
				label.font = _labelFont;
				label.fontSize = Mathf.Max(8, _fontSize);
				label.color = _labelColor;
				label.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(label.preferredWidth) + 2f, label.fontSize + 4f);
			}
		}

		private void UpdateLabels()
		{
			if (_labelsRoot == null)
			{
				return;
			}
			bool active = isActiveAndEnabled && _hasProjection && _showNames;
			if (_labelsRoot.gameObject.activeSelf != active)
			{
				_labelsRoot.gameObject.SetActive(active);
			}
			if (!active)
			{
				return;
			}
			Vector2 origin = rectTransform.rect.min;
			for (int i = 0; i < _labels.Length; i++)
			{
				Text label = _labels[i];
				Vector2 size = label.rectTransform.sizeDelta;
				bool visible = _visible[i] && _viewport.Contains(_positions[i]) &&
					size.x <= _viewport.width && size.y <= _viewport.height;
				label.enabled = visible;
				if (!visible)
				{
					continue;
				}
				Vector2 point = _positions[i];
				point.x = Mathf.Clamp(point.x + 6f, _viewport.xMin, Mathf.Max(_viewport.xMin, _viewport.xMax - size.x));
				point.y = Mathf.Clamp(point.y + 4f, _viewport.yMin, Mathf.Max(_viewport.yMin, _viewport.yMax - size.y));
				label.rectTransform.anchoredPosition = point - origin;
			}
		}

		private void DestroyLabels()
		{
			if (_labelsRoot == null)
			{
				return;
			}
			GameObject root = _labelsRoot.gameObject;
			_labelsRoot = null;
			root.SetActive(false);
			if (Application.isPlaying)
			{
				Destroy(root);
			}
			else
			{
				DestroyImmediate(root);
			}
		}

		private static void AddQuad(VertexHelper vertices, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
		{
			int first = vertices.currentVertCount;
			vertices.AddVert(a, tint, Vector2.zero);
			vertices.AddVert(b, tint, Vector2.zero);
			vertices.AddVert(c, tint, Vector2.zero);
			vertices.AddVert(d, tint, Vector2.zero);
			vertices.AddTriangle(first, first + 1, first + 2);
			vertices.AddTriangle(first + 2, first + 3, first);
		}

		private static bool ClipLine(ref Vector2 start, ref Vector2 end, Rect rect)
		{
			Vector2 delta = end - start;
			float first = 0f;
			float last = 1f;
			if (!Clip(-delta.x, start.x - rect.xMin, ref first, ref last) ||
				!Clip(delta.x, rect.xMax - start.x, ref first, ref last) ||
				!Clip(-delta.y, start.y - rect.yMin, ref first, ref last) ||
				!Clip(delta.y, rect.yMax - start.y, ref first, ref last))
			{
				return false;
			}
			end = start + last * delta;
			start += first * delta;
			return true;
		}

		private static bool Clip(float direction, float distance, ref float first, ref float last)
		{
			if (Mathf.Abs(direction) < 0.00001f)
			{
				return distance >= 0f;
			}
			float ratio = distance / direction;
			if (direction < 0f)
			{
				if (ratio > last) return false;
				first = Mathf.Max(first, ratio);
			}
			else
			{
				if (ratio < first) return false;
				last = Mathf.Min(last, ratio);
			}
			return true;
		}
	}
}
