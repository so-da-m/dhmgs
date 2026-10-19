using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UIElements;

namespace Mnnh.Dhmgs.Samples
{
	[DefaultExecutionOrder(10000)]
	[RequireComponent(typeof(UIDocument))]
	public sealed class DhmgsFullDemoView : MonoBehaviour
	{
		[SerializeField] private UIDocument _document;
		[SerializeField] private DhmgsFullDemoController _controller;
		[SerializeField] private DhmgsFullDemoPlayback _playback;
		[SerializeField] private DhmgsFullDemoViewSettings _settings;
		[SerializeField] private DhmgsOrbitCamera _mainCamera;
		[SerializeField] private Camera _previewCamera;
		[SerializeField] private DhmgsAvatarPreviewCamera _humanoidCamera;
		[SerializeField] private DhmgsFullDemoSkeletonPreview _mainSkeleton;
		[SerializeField] private DhmgsFullDemoSkeletonPreview _humanoidSkeleton;

		private readonly List<Action> _unbind = new List<Action>();
		private VisualElement _documentRoot;
		private VisualElement _root;
		private IntegerField _attemptId;
		private Button _fetch;
		private Button _cancelFetch;
		private ProgressBar _progress;
		private Label _server;
		private Label _operationStatus;
		private Label _log;
		private Button _play;
		private Button _pause;
		private Button _stop;
		private FloatField _speed;
		private SliderInt _frame;
		private Label _frameValue;
		private Label _playbackStatus;
		private DropdownField _avatar;
		private DropdownField _clothes;
		private Slider _avatarOpacity;
		private Slider _clothesOpacity;
		private Slider _videoOpacity;
		private Toggle _mainBones;
		private Toggle _mainNames;
		private Toggle _humanoidBones;
		private Toggle _humanoidNames;
		private Preview _mainPreview;
		private Preview _humanoidPreview;
		private int _scrubPointer = -1;
		private VisualElement _scrubCapture;
		private bool _refreshingPlayback;
		private int _attemptIdValue;

		private void OnEnable()
		{
			if (_document == null)
			{
				_document = GetComponent<UIDocument>();
			}
			BindView();
		}

		private void BindView()
		{
			if (!_document.isActiveAndEnabled)
			{
				return;
			}
			_documentRoot = _document.rootVisualElement;
			_root = _documentRoot?.Q("demo");
			if (_root == null)
			{
				return;
			}

			_attemptId = _root.Q<IntegerField>("attempt-id");
			_attemptId.SetValueWithoutNotify(_attemptIdValue);
			_fetch = _root.Q<Button>("fetch");
			_cancelFetch = _root.Q<Button>("cancel-fetch");
			_progress = _root.Q<ProgressBar>("import-progress");
			_server = _root.Q<Label>("server");
			_operationStatus = _root.Q<Label>("operation-status");
			_log = _root.Q<Label>("log");
			_play = _root.Q<Button>("play");
			_pause = _root.Q<Button>("pause");
			_stop = _root.Q<Button>("stop");
			_speed = _root.Q<FloatField>("speed");
			_frame = _root.Q<SliderInt>("frame");
			_frameValue = _root.Q<Label>("frame-value");
			_playbackStatus = _root.Q<Label>("playback-status");
			_avatar = _root.Q<DropdownField>("avatar");
			_clothes = _root.Q<DropdownField>("clothes");
			_avatarOpacity = _root.Q<Slider>("avatar-opacity");
			_clothesOpacity = _root.Q<Slider>("clothes-opacity");
			_videoOpacity = _root.Q<Slider>("video-opacity");
			_mainBones = _root.Q<Toggle>("main-bones");
			_mainNames = _root.Q<Toggle>("main-names");
			_humanoidBones = _root.Q<Toggle>("humanoid-bones");
			_humanoidNames = _root.Q<Toggle>("humanoid-names");

			Bind(_fetch, Fetch);
			Bind(_cancelFetch, () => _controller.CancelImport());
			Bind(_attemptId, value =>
			{
				_attemptIdValue = value;
				RefreshImport();
			});
			Bind(_play, () => _playback.Play());
			Bind(_pause, () => _playback.Pause());
			Bind(_stop, Stop);
			Bind(_speed, value =>
			{
				_playback.SetSpeed(value);
				_speed.SetValueWithoutNotify(_playback.Speed);
			});
			Bind(_frame, value =>
			{
				if (!_refreshingPlayback)
				{
					_playback.SeekFrame(value - 1);
				}
			});
			Bind(_avatar, _ => _settings.SetAvatarIndex(_avatar.index));
			Bind(_clothes, _ => _settings.SetClothesIndex(_clothes.index));
			Bind(_avatarOpacity, value => _settings.SetAvatarOpacity(value));
			Bind(_clothesOpacity, value => _settings.SetClothesOpacity(value));
			Bind(_videoOpacity, value => _settings.SetVideoOpacity(value));
			Bind(_mainBones, value => _settings.SetMainBones(value));
			Bind(_mainNames, value => _settings.SetMainBoneNames(value));
			Bind(_humanoidBones, value => _settings.SetHumanoidBones(value));
			Bind(_humanoidNames, value => _settings.SetHumanoidBoneNames(value));
			Bind(_root.Q<Button>("reset-main"), () => _settings.ResetMainCamera());
			Bind(_root.Q<Button>("reset-humanoid"), () => _settings.ResetHumanoidPreviewCamera());

			_frame.RegisterCallback<PointerDownEvent>(OnScrubDown, TrickleDown.TrickleDown);
			_frame.RegisterCallback<PointerUpEvent>(OnScrubUp, TrickleDown.TrickleDown);
			_frame.RegisterCallback<PointerCancelEvent>(OnScrubCancel);
			_frame.RegisterCallback<PointerCaptureEvent>(OnScrubCapture);
			_frame.RegisterCallback<PointerCaptureOutEvent>(OnScrubCaptureOut);
			_root.RegisterCallback<DetachFromPanelEvent>(OnDetach);
			_root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
			_root.EnableInClassList("demo--narrow", _root.resolvedStyle.width < 900f);
			_mainPreview = new Preview(_root.Q("main-viewport"),
				new DhmgsPreviewInput(_mainCamera.Orbit, _mainCamera.Pan, _mainCamera.Zoom));
			_humanoidPreview = new Preview(_root.Q("humanoid-viewport"),
				new DhmgsPreviewInput(_humanoidCamera.Orbit, _humanoidCamera.Pan, _humanoidCamera.Zoom));
			_controller.Changed += RefreshImport;
			_settings.Changed += RefreshSettings;
			RefreshImport();
			RefreshSettings();
			RefreshPlayback();
		}

		private void OnDisable()
		{
			UnbindView();
		}

		private void UnbindView()
		{
			if (_root == null)
			{
				return;
			}
			CancelInput();
			_controller.Changed -= RefreshImport;
			_settings.Changed -= RefreshSettings;
			foreach (Action unbind in _unbind)
			{
				unbind();
			}
			_unbind.Clear();
			_frame.UnregisterCallback<PointerDownEvent>(OnScrubDown, TrickleDown.TrickleDown);
			_frame.UnregisterCallback<PointerUpEvent>(OnScrubUp, TrickleDown.TrickleDown);
			_frame.UnregisterCallback<PointerCancelEvent>(OnScrubCancel);
			_frame.UnregisterCallback<PointerCaptureEvent>(OnScrubCapture);
			_frame.UnregisterCallback<PointerCaptureOutEvent>(OnScrubCaptureOut);
			_root.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
			_root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
			_mainPreview.Dispose();
			_humanoidPreview.Dispose();
			_mainPreview = null;
			_humanoidPreview = null;
			_root = null;
			_documentRoot = null;
		}

		private void Bind(Button button, Action action)
		{
			button.clicked += action;
			_unbind.Add(() => button.clicked -= action);
		}

		private void Bind<T>(BaseField<T> field, Action<T> action)
		{
			EventCallback<ChangeEvent<T>> callback = evt => action(evt.newValue);
			field.RegisterValueChangedCallback(callback);
			_unbind.Add(() => field.UnregisterValueChangedCallback(callback));
		}

		private async void Fetch()
		{
			await _controller.FetchAsync(_attemptId.value);
		}

		private void Stop()
		{
			_playback.Stop();
			EndScrub();
		}

		private void RefreshImport()
		{
			_fetch.SetEnabled(_controller.CanFetch && _attemptId.value >= 0);
			_attemptId.SetEnabled(!_controller.IsImporting);
			_cancelFetch.SetEnabled(_controller.IsImporting);
			_server.text = $"Server: {_controller.BaseUrl}";
			_progress.value = _controller.Progress * 100f;
			_progress.title = $"{_controller.Progress:P0}";
			_operationStatus.text = _controller.Status;
			_operationStatus.style.display = string.IsNullOrEmpty(_controller.Status) ? DisplayStyle.None : DisplayStyle.Flex;
			_log.text = _controller.Log;
		}

		private void RefreshSettings()
		{
			RefreshChoices(_avatar, _settings.AvatarNames, _settings.AvatarIndex);
			RefreshChoices(_clothes, _settings.ClothesNames, _settings.ClothesIndex);
			_avatarOpacity.SetValueWithoutNotify(_settings.AvatarOpacity);
			_clothesOpacity.SetValueWithoutNotify(_settings.ClothesOpacity);
			_videoOpacity.SetValueWithoutNotify(_settings.VideoOpacity);
			_avatarOpacity.SetEnabled(_settings.AvatarIndex > 0);
			_clothesOpacity.SetEnabled(_settings.ClothesIndex > 0);
			_videoOpacity.SetEnabled(_settings.CanSetVideoOpacity);
			_mainBones.SetValueWithoutNotify(_settings.ShowMainBones);
			_mainNames.SetValueWithoutNotify(_settings.ShowMainBoneNames);
			_mainNames.SetEnabled(_settings.ShowMainBones);
			_humanoidBones.SetValueWithoutNotify(_settings.ShowHumanoidBones);
			_humanoidNames.SetValueWithoutNotify(_settings.ShowHumanoidBoneNames);
			_humanoidNames.SetEnabled(_settings.ShowHumanoidBones);
		}

		private static void RefreshChoices(DropdownField field, IReadOnlyList<string> names, int index)
		{
			bool changed = field.choices.Count != names.Count;
			for (int i = 0; !changed && i < names.Count; i++)
			{
				changed = field.choices[i] != names[i];
			}
			if (changed)
			{
				field.choices = new List<string>(names);
			}
			field.SetValueWithoutNotify(names.Count > 0 ? names[Mathf.Clamp(index, 0, names.Count - 1)] : string.Empty);
			field.SetEnabled(names.Count > 1);
		}

		private void LateUpdate()
		{
			if (!_document.isActiveAndEnabled)
			{
				UnbindView();
				return;
			}
			if (_root == null || _documentRoot != _document.rootVisualElement)
			{
				UnbindView();
				BindView();
			}
			if (_root == null)
			{
				return;
			}
			if (!_playback.CanSeek)
			{
				EndScrub();
			}
			RefreshPlayback();
			_mainPreview.Refresh(_previewCamera.targetTexture, _mainSkeleton,
				_settings.ShowMainBones, _settings.ShowMainBoneNames);
			_humanoidPreview.Refresh(_humanoidCamera.Texture, _humanoidSkeleton,
				_settings.ShowHumanoidBones, _settings.ShowHumanoidBoneNames);
		}

		private void RefreshPlayback()
		{
			_refreshingPlayback = true;
			_play.SetEnabled(_playback.CanPlay);
			_pause.SetEnabled(_playback.CanPause);
			_stop.SetEnabled(_playback.CanStop);
			_speed.SetEnabled(_playback.isActiveAndEnabled);
			if (!_speed.Contains(_speed.panel?.focusController.focusedElement as VisualElement))
			{
				_speed.SetValueWithoutNotify(_playback.Speed);
			}
			int total = _playback.FrameCount;
			int current = total > 0 ? _playback.CurrentFrame + 1 : 0;
			_frame.highValue = Mathf.Max(1, total);
			_frame.SetEnabled(_playback.CanSeek);
			if (!_playback.IsScrubbing && !_playback.IsSeeking)
			{
				_frame.SetValueWithoutNotify(Mathf.Max(1, current));
			}
			_frameValue.text = $"{current} / {total}";
			_playbackStatus.text = total > 0
				? $"{PlaybackStatus()} | {_playback.Motion.attemptId}"
				: PlaybackStatus();
			_refreshingPlayback = false;
		}

		private string PlaybackStatus()
		{
			if (!string.IsNullOrEmpty(_playback.PlaybackError))
			{
				return $"Playback error: {_playback.PlaybackError}";
			}
			if (_playback.IsScrubbing)
			{
				return "Scrubbing";
			}
			if (_playback.IsSeeking)
			{
				return "Seeking";
			}
			if (_playback.IsWaiting)
			{
				return "Waiting for video";
			}
			return _playback.State switch
			{
				DhmgsFullDemoPlaybackState.Loading => "Loading...",
				DhmgsFullDemoPlaybackState.Playing => "Playing",
				DhmgsFullDemoPlaybackState.Paused => "Paused",
				DhmgsFullDemoPlaybackState.Stopped => "Stopped",
				_ => "No motion loaded",
			};
		}

		private void OnScrubDown(PointerDownEvent evt)
		{
			VisualElement target = evt.target as VisualElement;
			if (target == _frame || target == _frame.labelElement || _frame.labelElement.Contains(target))
			{
				return;
			}
			if (evt.button == 0 && _scrubPointer < 0 && _playback.CanSeek)
			{
				_scrubPointer = evt.pointerId;
				_playback.BeginScrub();
			}
		}

		private void OnScrubUp(PointerUpEvent evt)
		{
			if (evt.button == 0 && evt.pointerId == _scrubPointer)
			{
				EndScrub(false);
			}
		}

		private void OnScrubCancel(PointerCancelEvent evt)
		{
			if (evt.pointerId == _scrubPointer)
			{
				EndScrub();
			}
		}

		private void OnScrubCapture(PointerCaptureEvent evt)
		{
			if (evt.pointerId == _scrubPointer)
			{
				_scrubCapture = evt.target as VisualElement;
			}
		}

		private void OnScrubCaptureOut(PointerCaptureOutEvent evt)
		{
			if (evt.pointerId == _scrubPointer)
			{
				EndScrub(false);
			}
		}

		private void EndScrub(bool releaseCapture = true)
		{
			int pointerId = _scrubPointer;
			VisualElement capture = _scrubCapture;
			_scrubPointer = -1;
			_scrubCapture = null;
			if (pointerId < 0)
			{
				return;
			}
			if (releaseCapture && capture != null && capture.HasPointerCapture(pointerId))
			{
				capture.ReleasePointer(pointerId);
			}
			_playback.EndScrub();
		}

		private void OnGeometryChanged(GeometryChangedEvent evt)
		{
			_root.EnableInClassList("demo--narrow", evt.newRect.width < 900f);
		}

		private void OnDetach(DetachFromPanelEvent evt)
		{
			CancelInput();
		}

		private void OnApplicationFocus(bool hasFocus)
		{
			if (!hasFocus)
			{
				CancelInput();
			}
		}

		private void OnApplicationPause(bool paused)
		{
			if (paused)
			{
				CancelInput();
			}
		}

		private void CancelInput()
		{
			EndScrub();
			_mainPreview?.Input.Cancel();
			_humanoidPreview?.Input.Cancel();
		}

		private sealed class Preview : IDisposable
		{
			private readonly VisualElement _viewport;
			private readonly Image _image;
			private readonly DhmgsSkeletonOverlay _overlay;
			private Camera _camera;
			private IReadOnlyList<Transform> _bones;
			private float _aspect;

			public DhmgsPreviewInput Input { get; }

			public Preview(VisualElement viewport, DhmgsPreviewInput input)
			{
				_viewport = viewport;
				Input = input;
				_image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
				_image.StretchToParentSize();
				_overlay = new DhmgsSkeletonOverlay { pickingMode = PickingMode.Ignore };
				_overlay.style.position = Position.Absolute;
				_overlay.style.right = StyleKeyword.Auto;
				_overlay.style.bottom = StyleKeyword.Auto;
				_viewport.Add(_image);
				_viewport.Add(_overlay);
				_viewport.AddManipulator(Input);
			}

			public void Refresh(Texture texture, DhmgsFullDemoSkeletonPreview source, bool showBones, bool showNames)
			{
				_image.image = texture;
				float aspect = texture != null && texture.height > 0 ? (float)texture.width / texture.height : 1f;
				if (!Mathf.Approximately(_aspect, aspect))
				{
					_aspect = aspect;
					_viewport.parent.style.flexGrow = aspect;
				}
				bool visible = texture != null && showBones && source != null && source.HasSource;
				_overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
				if (!visible)
				{
					return;
				}
				if (_camera != source.ProjectionCamera || !ReferenceEquals(_bones, source.Bones))
				{
					_camera = source.ProjectionCamera;
					_bones = source.Bones;
					_overlay.Bind(_camera, _bones);
				}
				Rect bounds = _viewport.contentRect;
				float scale = Mathf.Min(bounds.width / texture.width, bounds.height / texture.height);
				float width = texture.width * scale;
				float height = texture.height * scale;
				_overlay.style.left = (bounds.width - width) * 0.5f;
				_overlay.style.top = (bounds.height - height) * 0.5f;
				_overlay.style.width = width;
				_overlay.style.height = height;
				_overlay.ShowNames = showNames;
				_overlay.Refresh();
			}

			public void Dispose()
			{
				_viewport.RemoveManipulator(Input);
				_overlay.RemoveFromHierarchy();
				_image.RemoveFromHierarchy();
			}
		}
	}
}
