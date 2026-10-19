using System;

using UnityEngine;
using UnityEngine.UIElements;

namespace Mnnh.Dhmgs.Samples
{
	internal sealed class DhmgsPreviewInput : PointerManipulator
	{
		private readonly Action<Vector2> _orbit;
		private readonly Action<Vector2> _pan;
		private readonly Action<float> _zoom;
		private int _pointerId = -1;
		private int _button;

		public DhmgsPreviewInput(Action<Vector2> orbit, Action<Vector2> pan, Action<float> zoom)
		{
			_orbit = orbit;
			_pan = pan;
			_zoom = zoom;
		}

		protected override void RegisterCallbacksOnTarget()
		{
			target.RegisterCallback<PointerDownEvent>(OnPointerDown);
			target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
			target.RegisterCallback<PointerUpEvent>(OnPointerUp);
			target.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
			target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
			target.RegisterCallback<WheelEvent>(OnWheel);
			target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
		}

		protected override void UnregisterCallbacksFromTarget()
		{
			Cancel();
			target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
			target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
			target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
			target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
			target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
			target.UnregisterCallback<WheelEvent>(OnWheel);
			target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
		}

		private void OnPointerDown(PointerDownEvent evt)
		{
			if (_pointerId >= 0 || evt.button < 0 || evt.button > 2)
			{
				return;
			}
			_pointerId = evt.pointerId;
			_button = evt.button;
			target.CapturePointer(_pointerId);
			evt.StopPropagation();
		}

		private void OnPointerMove(PointerMoveEvent evt)
		{
			if (evt.pointerId != _pointerId || !target.HasPointerCapture(_pointerId))
			{
				return;
			}
			if ((evt.pressedButtons & (1 << _button)) == 0)
			{
				Cancel();
				return;
			}
			Vector2 delta = new Vector2(evt.deltaPosition.x, -evt.deltaPosition.y);
			if (_button == 0)
			{
				_orbit(delta);
			}
			else
			{
				_pan(delta);
			}
			evt.StopPropagation();
		}

		private void OnPointerUp(PointerUpEvent evt)
		{
			if (evt.pointerId == _pointerId && (evt.pressedButtons & (1 << _button)) == 0)
			{
				Cancel();
				evt.StopPropagation();
			}
		}

		private void OnPointerCancel(PointerCancelEvent evt)
		{
			if (evt.pointerId == _pointerId)
			{
				Cancel();
			}
		}

		private void OnCaptureOut(PointerCaptureOutEvent evt)
		{
			if (evt.pointerId == _pointerId)
			{
				_pointerId = -1;
			}
		}

		private void OnWheel(WheelEvent evt)
		{
			_zoom(-evt.delta.y);
			evt.StopPropagation();
		}

		private void OnDetach(DetachFromPanelEvent evt)
		{
			Cancel();
		}

		public void Cancel()
		{
			int pointerId = _pointerId;
			_pointerId = -1;
			if (pointerId >= 0 && target.HasPointerCapture(pointerId))
			{
				target.ReleasePointer(pointerId);
			}
		}
	}
}
