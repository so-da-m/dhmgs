using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mnnh.Dhmgs
{
	[RequireComponent(typeof(RawImage))]
	public sealed class DhmgsFixedAvatarCamera : DhmgsAvatarPreviewCamera, IDragHandler, IScrollHandler
	{
		private RawImage _output;

		protected override void Awake()
		{
			base.Awake();
			_output = GetComponent<RawImage>();
			if (_output != null)
			{
				_output.texture = Texture;
			}
		}

		protected override void OnDestroy()
		{
			if (_output != null && _output.texture == Texture)
			{
				_output.texture = null;
			}
			base.OnDestroy();
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (eventData.button == PointerEventData.InputButton.Left)
			{
				Orbit(eventData.delta);
			}
			else
			{
				Pan(eventData.delta);
			}
		}

		public void OnScroll(PointerEventData eventData)
		{
			Zoom(eventData.scrollDelta.y);
		}
	}
}
