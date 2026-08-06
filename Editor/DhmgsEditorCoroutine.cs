using System;
using System.Collections;
using System.Collections.Generic;

using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	/// <summary>
	/// 에디터에서 IEnumerator를 구동한다. 중첩 IEnumerator와 AsyncOperation(UnityWebRequest 포함)을
	/// 처리하므로, 런타임 StartCoroutine과 같은 코루틴(DhmgsBakeService.DownloadAndBuild 등)을 그대로 쓴다.
	/// </summary>
	public sealed class DhmgsEditorCoroutine
	{
		private readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();
		private readonly Action _onDone;
		private AsyncOperation _pendingOp;
		private CustomYieldInstruction _pendingYield;
		private bool _running;

		public DhmgsEditorCoroutine(IEnumerator routine, Action onDone)
		{
			_stack.Push(routine);
			_onDone = onDone;
		}

		public void Start()
		{
			if (_running)
			{
				return;
			}
			_running = true;
			EditorApplication.update += Tick;
		}

		public void Stop()
		{
			if (!_running)
			{
				return;
			}
			_running = false;
			EditorApplication.update -= Tick;
		}

		private void Tick()
		{
			if (_pendingOp != null)
			{
				if (!_pendingOp.isDone)
				{
					return;
				}
				_pendingOp = null;
			}
			if (_pendingYield != null)
			{
				if (_pendingYield.keepWaiting)
				{
					return;
				}
				_pendingYield = null;
			}

			if (_stack.Count == 0)
			{
				Finish();
				return;
			}

			IEnumerator top = _stack.Peek();
			bool moved;
			try
			{
				moved = top.MoveNext();
			}
			catch (Exception e)
			{
				Debug.LogError($"[DHMGS] 코루틴 오류: {e}");
				Finish();
				return;
			}

			if (!moved)
			{
				_stack.Pop();
				return;
			}

			object current = top.Current;
			if (current is IEnumerator nested)
			{
				_stack.Push(nested);
			}
			else if (current is AsyncOperation op)
			{
				_pendingOp = op;
			}
			else if (current is CustomYieldInstruction customYield)
			{
				_pendingYield = customYield;
			}
			// null 등 그 외는 다음 tick에 계속.
		}

		private void Finish()
		{
			Stop();
			_onDone?.Invoke();
		}
	}
}
