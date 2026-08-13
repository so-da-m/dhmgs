using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;

namespace Mnnh.Dhmgs.Editor
{
	/// <summary>Keeps long Editor-only main-thread work responsive without moving Unity APIs to a worker thread.</summary>
	internal sealed class DhmgsEditorYield
	{
		private readonly double _budgetSeconds;
		private double _nextYieldAt;

		internal DhmgsEditorYield(double budgetSeconds = 0.01d)
		{
			_budgetSeconds = Math.Max(0.001d, budgetSeconds);
			_nextYieldAt = EditorApplication.timeSinceStartup + _budgetSeconds;
		}

		internal async Task CheckpointAsync(CancellationToken cancellationToken, bool force = false)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!force && EditorApplication.timeSinceStartup < _nextYieldAt)
			{
				return;
			}

			await Task.Yield();
			cancellationToken.ThrowIfCancellationRequested();
			_nextYieldAt = EditorApplication.timeSinceStartup + _budgetSeconds;
		}
	}
}
