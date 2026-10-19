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
		private static readonly Color BoneColor = new Color(1f, 61f / 255f, 200f / 255f, 1f);
		private static readonly Color JointColor = new Color(1f, 228f / 255f, 92f / 255f, 1f);

		private Camera _camera;
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

		public void Bind(Camera camera, IReadOnlyList<Transform> bones)
		{
			Clear();
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
			Refresh();
		}

		public void Refresh()
		{
			Rect rect = contentRect;
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
				Vector3 projected = _camera.WorldToViewportPoint(_bones[i].position);
				if (projected.z < _camera.nearClipPlane || projected.z > _camera.farClipPlane ||
					float.IsNaN(projected.x) || float.IsInfinity(projected.x) ||
					float.IsNaN(projected.y) || float.IsInfinity(projected.y))
				{
					continue;
				}
				_positions[i] = new Vector2(
					rect.xMin + (cameraRect.x + projected.x * cameraRect.width) * rect.width,
					rect.yMin + (1f - cameraRect.y - projected.y * cameraRect.height) * rect.height);
				_visible[i] = true;
			}
			_hasProjection = _viewport.width > 0f && _viewport.height > 0f;
			UpdateLabels();
			MarkDirtyRepaint();
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
		}

		private void UpdateLabels()
		{
			for (int i = 0; i < _labels.Length; i++)
			{
				Label label = _labels[i];
				bool visible = _showNames && _hasProjection && _visible[i] && _viewport.Contains(_positions[i]);
				label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
				if (!visible)
				{
					continue;
				}
				Vector2 size = label.MeasureTextSize(label.text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined);
				Vector2 point = _positions[i];
				label.style.left = Mathf.Clamp(point.x + 6f, _viewport.xMin, Mathf.Max(_viewport.xMin, _viewport.xMax - size.x));
				label.style.top = Mathf.Clamp(point.y - size.y - 4f, _viewport.yMin, Mathf.Max(_viewport.yMin, _viewport.yMax - size.y));
			}
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
