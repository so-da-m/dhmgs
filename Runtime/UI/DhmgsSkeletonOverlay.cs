using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UIElements;

namespace Mnnh.Dhmgs
{
	public sealed class DhmgsSkeletonOverlay : VisualElement
	{
		private const float LineWidth = 1.5f;
		private const float JointRadius = 2.5f;
		private const float RootRadius = 5f;
		private const float RootAxisLength = 0.15f;
		private static readonly Color BoneColor = new Color(1f, 61f / 255f, 200f / 255f, 1f);
		private static readonly Color JointColor = new Color(1f, 228f / 255f, 92f / 255f, 1f);
		private static readonly Color RootColor = new Color(0f, 1f, 1f, 1f);
		private static readonly Color[] AxisColors =
		{
			new Color(1f, 0.25f, 0.25f, 1f),
			new Color(0.3f, 1f, 0.3f, 1f),
			new Color(0.3f, 0.6f, 1f, 1f),
		};

		private Camera _camera;
		private Transform _root;
		private Label _rootLabel;
		private Vector2 _rootPosition;
		private bool _rootVisible;
		private readonly Vector2[] _axisPositions = new Vector2[3];
		private readonly bool[] _axisVisible = new bool[3];
		private Transform[] _bones = Array.Empty<Transform>();
		private int[] _parents = Array.Empty<int>();
		private Vector2[] _positions = Array.Empty<Vector2>();
		private bool[] _visible = Array.Empty<bool>();
		private Label[] _labels = Array.Empty<Label>();
		private Rect _viewport;
		private bool _hasProjection;
		private bool _showNames;

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

		public DhmgsSkeletonOverlay()
		{
			pickingMode = PickingMode.Ignore;
			style.position = Position.Absolute;
			style.left = 0f;
			style.top = 0f;
			style.right = 0f;
			style.bottom = 0f;
			style.overflow = Overflow.Hidden;
			generateVisualContent += Draw;
			RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
		}

		private void OnGeometryChanged(GeometryChangedEvent evt)
		{
			if (evt.target == this)
			{
				Refresh();
			}
		}

		public void Bind(Camera camera, IReadOnlyList<Transform> bones, Transform root = null)
		{
			Clear();
			_camera = camera;
			_root = root;
			_rootVisible = false;
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
			if (_root == null && _bones.Length > 0)
			{
				Animator animator = _bones[0].GetComponentInParent<Animator>();
				_root = animator != null ? animator.transform : null;
			}
			_parents = new int[_bones.Length];
			_positions = new Vector2[_bones.Length];
			_visible = new bool[_bones.Length];
			_labels = new Label[_bones.Length];
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
				Label label = new Label(_bones[i].name) { pickingMode = PickingMode.Ignore };
				label.style.position = Position.Absolute;
				label.style.fontSize = 12f;
				label.style.display = DisplayStyle.None;
				_labels[i] = label;
				Add(label);
			}
			_rootLabel = new Label("Root") { pickingMode = PickingMode.Ignore };
			_rootLabel.style.position = Position.Absolute;
			_rootLabel.style.fontSize = 12f;
			_rootLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
			_rootLabel.style.color = RootColor;
			_rootLabel.style.backgroundColor = new Color(0f, 0f, 0f, 0.75f);
			_rootLabel.style.display = DisplayStyle.None;
			Add(_rootLabel);
			Refresh();
		}

		public void Refresh()
		{
			Rect rect = contentRect;
			_rootVisible = false;
			_hasProjection = _camera != null && _camera.isActiveAndEnabled && _bones.Length > 0 &&
				rect.width > 0f && rect.height > 0f;
			if (!_hasProjection)
			{
				UpdateLabels();
				MarkDirtyRepaint();
				return;
			}

			Rect cameraRect = _camera.rect;
			_viewport = Rect.MinMaxRect(
				rect.xMin + Mathf.Clamp01(cameraRect.xMin) * rect.width,
				rect.yMin + (1f - Mathf.Clamp01(cameraRect.yMax)) * rect.height,
				rect.xMin + Mathf.Clamp01(cameraRect.xMax) * rect.width,
				rect.yMin + (1f - Mathf.Clamp01(cameraRect.yMin)) * rect.height);
			for (int i = 0; i < _bones.Length; i++)
			{
				_visible[i] = false;
				if (_bones[i] == null || !_bones[i].gameObject.activeInHierarchy)
				{
					continue;
				}
				_visible[i] = TryProject(_bones[i].position, out _positions[i]);
			}
			if (_root != null && _root.gameObject.activeInHierarchy)
			{
				_rootVisible = TryProject(_root.position, out _rootPosition) && _viewport.Contains(_rootPosition);
				if (_rootVisible)
				{
					_axisVisible[0] = TryProject(_root.position + _root.right * RootAxisLength, out _axisPositions[0]);
					_axisVisible[1] = TryProject(_root.position + _root.up * RootAxisLength, out _axisPositions[1]);
					_axisVisible[2] = TryProject(_root.position + _root.forward * RootAxisLength, out _axisPositions[2]);
				}
			}
			_hasProjection = _viewport.width > 0f && _viewport.height > 0f;
			UpdateLabels();
			MarkDirtyRepaint();
		}

		private bool TryProject(Vector3 worldPosition, out Vector2 point)
		{
			point = default;
			Vector3 projected = _camera.WorldToViewportPoint(worldPosition);
			if (projected.z < _camera.nearClipPlane || projected.z > _camera.farClipPlane ||
				float.IsNaN(projected.x) || float.IsInfinity(projected.x) ||
				float.IsNaN(projected.y) || float.IsInfinity(projected.y))
			{
				return false;
			}
			Rect rect = contentRect;
			Rect cameraRect = _camera.rect;
			point = new Vector2(
				rect.xMin + (cameraRect.x + projected.x * cameraRect.width) * rect.width,
				rect.yMin + (1f - cameraRect.y - projected.y * cameraRect.height) * rect.height);
			return true;
		}

		private void Draw(MeshGenerationContext context)
		{
			if (!_hasProjection)
			{
				return;
			}
			Painter2D painter = context.painter2D;
			painter.lineWidth = LineWidth;
			painter.strokeColor = BoneColor;
			float halfWidth = LineWidth * 0.5f;
			Rect lineRect = Rect.MinMaxRect(_viewport.xMin + halfWidth, _viewport.yMin + halfWidth,
				_viewport.xMax - halfWidth, _viewport.yMax - halfWidth);
			for (int i = 0; i < _bones.Length; i++)
			{
				int parent = _parents[i];
				if (!_visible[i] || parent < 0 || !_visible[parent] || lineRect.width <= 0f || lineRect.height <= 0f)
				{
					continue;
				}
				Vector2 start = _positions[parent];
				Vector2 end = _positions[i];
				if (!ClipLine(ref start, ref end, lineRect) || (end - start).sqrMagnitude < 0.0001f)
				{
					continue;
				}
				painter.BeginPath();
				painter.MoveTo(start);
				painter.LineTo(end);
				painter.Stroke();
			}
			painter.fillColor = JointColor;
			for (int i = 0; i < _bones.Length; i++)
			{
				if (!_visible[i])
				{
					continue;
				}
				Vector2 point = _positions[i];
				float left = Mathf.Max(_viewport.xMin, point.x - JointRadius);
				float right = Mathf.Min(_viewport.xMax, point.x + JointRadius);
				float top = Mathf.Max(_viewport.yMin, point.y - JointRadius);
				float bottom = Mathf.Min(_viewport.yMax, point.y + JointRadius);
				if (right <= left || bottom <= top)
				{
					continue;
				}
				painter.BeginPath();
				painter.MoveTo(new Vector2(left, top));
				painter.LineTo(new Vector2(right, top));
				painter.LineTo(new Vector2(right, bottom));
				painter.LineTo(new Vector2(left, bottom));
				painter.ClosePath();
				painter.Fill();
			}
			DrawRoot(painter);
		}

		private void DrawRoot(Painter2D painter)
		{
			if (!_rootVisible)
			{
				return;
			}
			painter.lineWidth = 2f;
			Rect rect = Rect.MinMaxRect(_viewport.xMin + 1f, _viewport.yMin + 1f,
				_viewport.xMax - 1f, _viewport.yMax - 1f);
			if (rect.width <= 0f || rect.height <= 0f)
			{
				return;
			}
			for (int i = 0; i < _axisPositions.Length; i++)
			{
				Vector2 start = _rootPosition;
				Vector2 end = _axisPositions[i];
				if (!_axisVisible[i] || !ClipLine(ref start, ref end, rect) || (end - start).sqrMagnitude < 0.0001f)
				{
					continue;
				}
				painter.strokeColor = AxisColors[i];
				painter.BeginPath();
				painter.MoveTo(start);
				painter.LineTo(end);
				painter.Stroke();
			}
			if (_rootPosition.x - RootRadius < rect.xMin || _rootPosition.x + RootRadius > rect.xMax ||
				_rootPosition.y - RootRadius < rect.yMin || _rootPosition.y + RootRadius > rect.yMax)
			{
				return;
			}
			painter.strokeColor = RootColor;
			painter.fillColor = Color.black;
			painter.BeginPath();
			painter.MoveTo(_rootPosition + new Vector2(0f, -RootRadius));
			painter.LineTo(_rootPosition + new Vector2(RootRadius, 0f));
			painter.LineTo(_rootPosition + new Vector2(0f, RootRadius));
			painter.LineTo(_rootPosition + new Vector2(-RootRadius, 0f));
			painter.ClosePath();
			painter.Fill();
			painter.Stroke();
		}

		private void UpdateLabels()
		{
			if (_rootLabel != null)
			{
				_rootLabel.style.display = _hasProjection && _rootVisible ? DisplayStyle.Flex : DisplayStyle.None;
				if (_hasProjection && _rootVisible)
				{
					PositionLabel(_rootLabel, _rootPosition);
				}
			}
			for (int i = 0; i < _labels.Length; i++)
			{
				Label label = _labels[i];
				bool visible = _showNames && _hasProjection && _visible[i] && _viewport.Contains(_positions[i]);
				label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
				if (!visible)
				{
					continue;
				}
				PositionLabel(label, _positions[i]);
			}
		}

		private void PositionLabel(Label label, Vector2 point)
		{
			Vector2 size = label.MeasureTextSize(label.text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined);
			label.style.left = Mathf.Clamp(point.x + 6f, _viewport.xMin, Mathf.Max(_viewport.xMin, _viewport.xMax - size.x));
			label.style.top = Mathf.Clamp(point.y - size.y - 4f, _viewport.yMin, Mathf.Max(_viewport.yMin, _viewport.yMax - size.y));
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
