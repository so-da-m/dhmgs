#if UNITY_EDITOR
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace Mnnh.Dhmgs.Samples.Editor
{
	/// <summary>
	/// Play Mode-only host for asynchronous Editor preview preparation.
	/// The actual retargeted avatar playback is owned by DhmgsHumanoidPreview.
	/// </summary>
	public sealed class DhmgsFullDemoEditorPreview : MonoBehaviour
	{
		private Coroutine _clockMonitor;
		private Coroutine _clockActivation;
		private TaskCompletionSource<IDhmgsPlaybackClock> _clockCompletion;
		private Action _clockAbort;
		private CancellationToken _clockCancellationToken;

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
			TaskCompletionSource<IDhmgsPlaybackClock> completion =
				new TaskCompletionSource<IDhmgsPlaybackClock>(
					TaskCreationOptions.RunContinuationsAsynchronously);
			_clockCompletion = completion;
			_clockAbort = abort;
			_clockCancellationToken = cancellationToken;
			Coroutine monitor = StartCoroutine(MonitorClock(
				activation,
				isCompleted,
				getClock,
				cancellationToken,
				Mathf.Max(0.1f, timeoutSeconds)));
			// StartCoroutine may run the iterator through completion before returning.
			// Do not resurrect a monitor that already cleared this operation's state.
			if (ReferenceEquals(_clockCompletion, completion))
			{
				_clockMonitor = monitor;
			}
			return completion.Task;
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

		private void OnDestroy()
		{
			CancelClockPreparation();
		}
	}
}
#endif
