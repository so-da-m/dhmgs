using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Unity.Pipeline.Commands;

using UnityEditor;

namespace Mnnh.Dhmgs.Editor.Cli
{
	public sealed class DhmgsImportCliResult
	{
		public string SourceId;
		public string MotionAssetPath;
		public string HumanoidInPlaceClipPath;
		public string HumanoidRootMotionClipPath;
		public string PreviewVideoPath;
		public List<string> Diagnostics = new List<string>();
	}

	public sealed class DhmgsImportCliStatus
	{
		public bool IsBusy;
		public string Stage;
		public float Progress;
		public string Message;
		public DhmgsImportCliResult LastResult;
		public string LastError;
	}

	public static class DhmgsImportCliCommands
	{
		private static readonly object StatusLock = new object();
		private static readonly DhmgsImportCliStatus Status = new DhmgsImportCliStatus();

		[CliCommand(
			"dhmgs_import",
			"Download an ExAvatar attempt from the DHMGS server, decode it, bake Humanoid In-Place/Root Motion clips, cache the preview video, and publish the assets. Waits for completion; raise --timeout for long motions.",
			Tags = new[] { "dhmgs" })]
		public static async Task<DhmgsImportCliResult> Import(
			[CliArg("attempt_id", "Attempt ID on the DHMGS server.", Required = true)] int attemptId,
			[CliArg("base_url", "DHMGS server base URL. Defaults to the package default URL.")] string baseUrl = null,
			[CliArg("output_root", "Asset folder that receives the generated assets. Defaults to Assets/Mnnh.Dhmgs/Content/Generated.")] string outputRoot = null)
		{
			DhmgsImportJobRequest request = new DhmgsImportJobRequest(
				string.IsNullOrWhiteSpace(baseUrl) ? DhmgsImportService.DefaultBaseUrl : baseUrl,
				attemptId,
				string.IsNullOrWhiteSpace(outputRoot) ? DhmgsImportService.DefaultOutputRoot : outputRoot,
				true,
				DhmgsVideoPolicy.CacheIfPresent);

			lock (StatusLock)
			{
				Status.IsBusy = true;
				Status.Stage = DhmgsImportJobStage.Validating.ToString();
				Status.Progress = 0f;
				Status.Message = $"Starting import of attempt {attemptId}";
				Status.LastError = null;
			}

			try
			{
				DhmgsImportJobResult job = await DhmgsImportService.ImportAsync(
					request,
					ReportProgress,
					CancellationToken.None);
				DhmgsImportCliResult result = ToResult(job);
				lock (StatusLock)
				{
					Status.LastResult = result;
				}
				return result;
			}
			catch (Exception exception)
			{
				lock (StatusLock)
				{
					Status.LastError = exception.GetBaseException().Message;
				}
				throw;
			}
			finally
			{
				lock (StatusLock)
				{
					Status.IsBusy = false;
				}
			}
		}

		[CliCommand(
			"dhmgs_import_status",
			"Report whether a DHMGS import is running, its latest stage and progress, and the last result or error.",
			MainThreadRequired = false,
			Tags = new[] { "dhmgs" })]
		public static DhmgsImportCliStatus ImportStatus()
		{
			lock (StatusLock)
			{
				return new DhmgsImportCliStatus
				{
					IsBusy = Status.IsBusy || DhmgsImportService.IsBusy,
					Stage = Status.Stage,
					Progress = Status.Progress,
					Message = Status.Message,
					LastResult = Status.LastResult,
					LastError = Status.LastError,
				};
			}
		}

		private static void ReportProgress(DhmgsImportJobProgress progress)
		{
			lock (StatusLock)
			{
				Status.Stage = progress.Stage.ToString();
				Status.Progress = progress.OverallProgress;
				Status.Message = progress.Message;
			}
		}

		private static DhmgsImportCliResult ToResult(DhmgsImportJobResult job)
		{
			DhmgsImportCliResult result = new DhmgsImportCliResult
			{
				SourceId = job.Motion != null ? job.Motion.attemptId : null,
				MotionAssetPath = job.MotionAssetPath,
				HumanoidInPlaceClipPath = AssetDatabase.GetAssetPath(job.HumanoidInPlaceClip),
				HumanoidRootMotionClipPath = AssetDatabase.GetAssetPath(job.HumanoidRootMotionClip),
				PreviewVideoPath = job.PreviewVideoPath,
			};
			foreach (DhmgsImportDiagnostic diagnostic in job.Diagnostics)
			{
				result.Diagnostics.Add($"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}");
			}
			return result;
		}
	}
}
