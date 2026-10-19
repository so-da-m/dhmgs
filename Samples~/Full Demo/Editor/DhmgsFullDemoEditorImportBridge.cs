using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Mnnh.Dhmgs.Editor;

using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Samples.Editor
{
	[InitializeOnLoad]
	internal static class DhmgsFullDemoEditorImportBridge
	{
		static DhmgsFullDemoEditorImportBridge()
		{
			DhmgsFullDemoImportGateway.Register(ImportAndSaveAsync);
			AssemblyReloadEvents.beforeAssemblyReload += Unregister;
		}

		private static void Unregister()
		{
			DhmgsFullDemoImportGateway.Unregister(ImportAndSaveAsync);
			AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
		}

		private static async Task<DhmgsSampleImportResult> ImportAndSaveAsync(
			string baseUrl,
			int attemptId,
			Action<DhmgsSampleImportProgress> onProgress,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			DhmgsImportJobResult job = await DhmgsImportService.ImportAsync(
				new DhmgsImportJobRequest(
					baseUrl,
					attemptId,
					DhmgsImportService.DefaultOutputRoot,
					DhmgsBakeOutputs.Humanoid,
					DhmgsVideoPolicy.CacheIfPresent),
				value => Report(onProgress, value),
				cancellationToken);

			cancellationToken.ThrowIfCancellationRequested();
			if (job == null || job.Motion == null || !job.Motion.IsValid)
			{
				throw new InvalidOperationException("The import did not produce valid motion data.");
			}
			if (job.HumanoidInPlaceClip == null)
			{
				throw new InvalidOperationException("Humanoid bake did not produce an in-place clip.");
			}

			bool videoNotPresent = false;
			foreach (DhmgsImportDiagnostic diagnostic in job.Diagnostics)
			{
				videoNotPresent |= diagnostic.Code == "video.not-present";
			}
			if (string.IsNullOrEmpty(job.PreviewVideoPath) && !videoNotPresent)
			{
				throw new InvalidOperationException("The preview video could not be cached.");
			}

			return new DhmgsSampleImportResult(
				job.Motion,
				job.HumanoidInPlaceClip,
				job.PreviewVideoPath,
				BuildSummary(job));
		}

		private static string BuildSummary(DhmgsImportJobResult job)
		{
			List<string> lines = new List<string>
			{
				$"[{job.Motion.attemptId}] Saved {job.Motion.frameCount} frames @ {job.Motion.fps:F2} fps",
				job.MotionAssetPath,
				$"Humanoid In-Place: {AssetDatabase.GetAssetPath(job.HumanoidInPlaceClip)}",
				$"Humanoid Root Motion: {AssetDatabase.GetAssetPath(job.HumanoidRootMotionClip)}",
				string.IsNullOrEmpty(job.PreviewVideoPath)
					? "Preview video: not present"
					: $"Preview video: {job.PreviewVideoPath}",
				$"Hand offset max diff: L={job.LeftHandOffsetMaxDiff:F4}, R={job.RightHandOffsetMaxDiff:F4}",
			};

			foreach (DhmgsImportDiagnostic diagnostic in job.Diagnostics)
			{
				if (diagnostic.Severity != DhmgsImportSeverity.Info)
				{
					lines.Add($"[{diagnostic.Severity}] {diagnostic.Message}");
				}
			}
			return string.Join("\n", lines);
		}

		private static void Report(
			Action<DhmgsSampleImportProgress> onProgress,
			DhmgsImportJobProgress value)
		{
			if (onProgress == null)
			{
				return;
			}

			string status = string.IsNullOrWhiteSpace(value.Message)
				? value.Stage.ToString()
				: value.Message;
			onProgress(new DhmgsSampleImportProgress(Mathf.Clamp01(value.OverallProgress), status));
		}
	}
}
