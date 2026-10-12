using System;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mnnh.Dhmgs.Samples
{
	// Place on a raycastable child covering the slider so pausing precedes the slider's first value change.
	public sealed class DhmgsFullDemoFrameScrubber : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
		IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		[SerializeField] private Slider _slider;

		private bool _scrubbing;
		private PointerEventData _pointer;

		public event Action ScrubStarted;
		public event Action ScrubEnded;

		public void OnPointerDown(PointerEventData eventData)
		{
			if (!BeginScrub(eventData))
			{
				return;
			}
			_slider.OnPointerDown(eventData);
		}

		public void OnPointerUp(PointerEventData eventData)
		{
			if (_scrubbing && eventData != null && eventData.button == PointerEventData.InputButton.Left &&
				eventData.pointerId == _pointer.pointerId)
			{
				CancelScrub();
			}
		}

		public void OnInitializePotentialDrag(PointerEventData eventData)
		{
			if (_slider != null)
			{
				_slider.OnInitializePotentialDrag(eventData);
			}
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			BeginScrub(eventData);
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (_scrubbing && eventData != null && eventData.button == PointerEventData.InputButton.Left &&
				eventData.pointerId == _pointer.pointerId && _slider != null)
			{
				_slider.OnDrag(eventData);
			}
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			OnPointerUp(eventData);
		}

		private bool BeginScrub(PointerEventData eventData)
		{
			if (!isActiveAndEnabled || eventData == null || eventData.button != PointerEventData.InputButton.Left ||
				_slider == null || !_slider.IsActive() || !_slider.IsInteractable())
			{
				return false;
			}
			if (_scrubbing)
			{
				return eventData.pointerId == _pointer.pointerId;
			}
			_pointer = eventData;
			_scrubbing = true;
			ScrubStarted?.Invoke();
			return true;
		}

		public void CancelScrub()
		{
			if (!_scrubbing)
			{
				return;
			}
			_scrubbing = false;
			if (_slider != null && _pointer != null)
			{
				PointerEventData.InputButton button = _pointer.button;
				_pointer.button = PointerEventData.InputButton.Left;
				try
				{
					_slider.OnPointerUp(_pointer);
				}
				finally
				{
					_pointer.button = button;
				}
			}
			_pointer = null;
			ScrubEnded?.Invoke();
		}

		private void OnDisable()
		{
			CancelScrub();
		}

		private void OnApplicationFocus(bool hasFocus)
		{
			if (!hasFocus)
			{
				CancelScrub();
			}
		}

		private void OnApplicationPause(bool paused)
		{
			if (paused)
			{
				CancelScrub();
			}
		}
	}
}
