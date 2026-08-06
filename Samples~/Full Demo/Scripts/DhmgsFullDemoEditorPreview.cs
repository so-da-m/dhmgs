#if UNITY_EDITOR
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace Mnnh.Dhmgs.Samples.Editor
{
	/// <summary>
	/// Play Mode-only host for editor preview coroutines and the freshly baked Generic clip.
	/// The class lives in the sample runtime assembly so Unity can attach it, but UNITY_EDITOR
	/// removes it completely from Player builds.
	/// </summary>
	public sealed class DhmgsFullDemoEditorPreview : MonoBehaviour, IDhmgsFramingTarget, IDhmgsFramingBounds
	{
		private GameObject _genericRoot;
		private GameObject _genericInstance;
		private Transform _genericPivot;
		private AnimationClip _genericClip;
		private IDhmgsPlaybackClock _clock;
		private float _fps = 30f;

		private Coroutine _clockMonitor;
		private Coroutine _clockActivation;
		private TaskCompletionSource<IDhmgsPlaybackClock> _clockCompletion;
		private Action _clockAbort;
		private CancellationToken _clockCancellationToken;

		public Transform Pivot => _clock != null && _genericInstance != null
			? (_genericPivot != null
				? _genericPivot
				: _genericInstance.transform)
			: null;

		public bool TryGetFramingBounds(out Bounds bounds)
		{
			if (_clock == null || _genericInstance == null)
			{
				bounds = default;
				return false;
			}
			return DhmgsFramingBoundsUtility.TryCalculate(_genericInstance.transform, out bounds);
		}

		public Task<IDhmgsPlaybackClock> PrepareClockAsync(
			IEnumerator activation,
			Func<bool> isCompleted,
			Func<IDhmgsPlaybackClock> getClock,
			Action abort,
			CancellationToken cancellationToken,
			float timeoutSeconds)
		{
			if (activation == null || isCompleted == null || getClock == null)
			{
				return Task.FromResult<IDhmgsPlaybackClock>(null);
			}

			CancelClockPreparation();
			_clockCompletion = new TaskCompletionSource<IDhmgsPlaybackClock>();
			_clockAbort = abort;
			_clockCancellationToken = cancellationToken;
			_clockMonitor = StartCoroutine(MonitorClock(
				activation,
				isCompleted,
				getClock,
				cancellationToken,
				Mathf.Max(0.1f, timeoutSeconds)));
			return _clockCompletion.Task;
		}

		public void CancelClockPreparation()
		{
			if (_clockMonitor == null && _clockActivation == null && _clockCompletion == null)
			{
				return;
			}

			if (_clockMonitor != null)
			{
				StopCoroutine(_clockMonitor);
			}
			StopClockActivation();

			TaskCompletionSource<IDhmgsPlaybackClock> completion = _clockCompletion;
			Action abort = _clockAbort;
			CancellationToken cancellationToken = _clockCancellationToken;
			ClearClockPreparationState();
			try
			{
				abort?.Invoke();
			}
			finally
			{
				if (completion != null)
				{
					if (cancellationToken.CanBeCanceled)
					{
						completion.TrySetCanceled(cancellationToken);
					}
					else
					{
						completion.TrySetCanceled();
					}
				}
			}
		}

		public void PlayGeneric(
			GameObject prefab,
			Material previewMaterial,
			AnimationClip clip,
			IDhmgsPlaybackClock clock,
			float fps,
			Vector3 isolationPosition)
		{
			ClearGeneric();
			if (prefab == null || clip == null || clock == null)
			{
				return;
			}

			_genericRoot = new GameObject("Generic Bake Preview Root")
			{
				hideFlags = HideFlags.DontSave,
			};
			_genericRoot.transform.SetParent(transform, false);
			_genericRoot.transform.position = isolationPosition;

			try
			{
				_genericInstance = UnityEngine.Object.Instantiate(prefab, _genericRoot.transform, false);
				if (_genericInstance == null)
				{
					ClearGeneric();
					return;
				}

				_genericInstance.name = $"{prefab.name} (Generic Bake Preview)";
				_genericInstance.transform.localPosition = Vector3.zero;
				_genericInstance.transform.localRotation = Quaternion.identity;
				_genericInstance.transform.localScale = Vector3.one;
				foreach (Transform candidate in _genericInstance.GetComponentsInChildren<Transform>(true))
				{
					if (candidate != null && candidate.name == "pelvis")
					{
						_genericPivot = candidate;
						break;
					}
				}
				foreach (SkinnedMeshRenderer renderer in _genericInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
				{
					if (renderer != null)
					{
						renderer.updateWhenOffscreen = true;
						ApplyPreviewMaterial(renderer, previewMaterial);
					}
				}

				clip.SampleAnimation(_genericInstance, 0f);
				_genericClip = clip;
				_clock = clock;
				_fps = fps > 0f ? fps : 30f;
			}
			catch
			{
				ClearGeneric();
				throw;
			}
		}

		public void ClearGeneric()
		{
			_clock = null;
			_genericClip = null;
			if (_genericRoot != null)
			{
				DestroyPreviewObject(_genericRoot);
			}
			else if (_genericInstance != null)
			{
				DestroyPreviewObject(_genericInstance);
			}
			_genericRoot = null;
			_genericInstance = null;
			_genericPivot = null;
		}

		private IEnumerator MonitorClock(
			IEnumerator activation,
			Func<bool> isCompleted,
			Func<IDhmgsPlaybackClock> getClock,
			CancellationToken cancellationToken,
			float timeoutSeconds)
		{
			_clockActivation = StartCoroutine(activation);
			float deadline = Time.realtimeSinceStartup + timeoutSeconds;
			while (!isCompleted() && Time.realtimeSinceStartup < deadline)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					CompleteClockCancellation(cancellationToken);
					yield break;
				}
				yield return null;
			}

			if (!isCompleted())
			{
				StopClockActivation();
				Action abort = _clockAbort;
				TaskCompletionSource<IDhmgsPlaybackClock> completion = _clockCompletion;
				ClearClockPreparationState();
				try
				{
					abort?.Invoke();
				}
				finally
				{
					completion?.TrySetResult(null);
				}
				yield break;
			}

			IDhmgsPlaybackClock clock = getClock();
			TaskCompletionSource<IDhmgsPlaybackClock> succeeded = _clockCompletion;
			ClearClockPreparationState();
			succeeded?.TrySetResult(clock);
		}

		private void CompleteClockCancellation(CancellationToken cancellationToken)
		{
			StopClockActivation();
			Action abort = _clockAbort;
			TaskCompletionSource<IDhmgsPlaybackClock> completion = _clockCompletion;
			ClearClockPreparationState();
			try
			{
				abort?.Invoke();
			}
			finally
			{
				completion?.TrySetCanceled(cancellationToken);
			}
		}

		private void StopClockActivation()
		{
			if (_clockActivation != null)
			{
				StopCoroutine(_clockActivation);
				_clockActivation = null;
			}
		}

		private void ClearClockPreparationState()
		{
			_clockMonitor = null;
			_clockActivation = null;
			_clockCompletion = null;
			_clockAbort = null;
			_clockCancellationToken = default;
		}

		private void Update()
		{
			if (_clock == null || _genericClip == null || _genericInstance == null)
			{
				return;
			}
			_genericClip.SampleAnimation(_genericInstance, _clock.CurrentFrame / _fps);
		}

		private void OnDestroy()
		{
			CancelClockPreparation();
			ClearGeneric();
		}

		private static void DestroyPreviewObject(UnityEngine.Object value)
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

		private static void ApplyPreviewMaterial(Renderer renderer, Material previewMaterial)
		{
			if (renderer == null || previewMaterial == null)
			{
				return;
			}

			Material[] materials = renderer.sharedMaterials;
			if (materials == null || materials.Length == 0)
			{
				renderer.sharedMaterial = previewMaterial;
				return;
			}
			for (int index = 0; index < materials.Length; index++)
			{
				materials[index] = previewMaterial;
			}
			renderer.sharedMaterials = materials;
		}
	}
}
#endif
