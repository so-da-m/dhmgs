using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	public static class DhmgsImportService
	{
		public const string DefaultBaseUrl = DhmgsImportPipeline.DefaultBaseUrl;
		public const string DefaultOutputRoot = DhmgsAssetWriter.DefaultOutputRoot;
		public const int DefaultTimeoutSeconds = DhmgsImportPipeline.DefaultTimeoutSeconds;

		private static int _activeJob;
		private static long _nextRevision;

		public static bool IsBusy => Volatile.Read(ref _activeJob) != 0;

		public static GameObject LoadBundledSmplxBakeSource()
		{
			return DhmgsPackageDefaults.LoadNeutralSmplx();
		}

		public static DhmgsImportValidation Validate(DhmgsImportJobRequest request)
		{
			List<DhmgsImportDiagnostic> diagnostics = new List<DhmgsImportDiagnostic>();
			if (request == null)
			{
				diagnostics.Add(Error("request.missing", "An import request is required."));
				return new DhmgsImportValidation(diagnostics);
			}

			if (request.IsArchiveSource)
			{
				if (string.IsNullOrWhiteSpace(request.SourceId)
					|| request.SourceId == "."
					|| request.SourceId == "..")
				{
					diagnostics.Add(Error(
						"request.source-id.invalid",
						"An archive source ID other than '.' or '..' is required."));
				}
				if (request.ArchiveBytes == null || request.ArchiveBytes.Length == 0 ||
					request.ArchiveBytes.LongLength > DhmgsArchiveLimits.MaxArchiveBytes)
				{
					diagnostics.Add(Error(
						"request.archive.invalid",
						$"Archive bytes must contain 1 to {DhmgsArchiveLimits.MaxArchiveBytes} bytes."));
				}
			}
			else
			{
				if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out Uri baseUri) ||
					(baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
					!string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Fragment) ||
					!string.IsNullOrEmpty(baseUri.Query))
				{
					diagnostics.Add(Error("request.url.invalid",
						"Base URL must be an absolute HTTP or HTTPS URL without credentials, a query, or a fragment."));
				}
				if (request.AttemptId < 0)
				{
					diagnostics.Add(Error("request.attempt.invalid", "Attempt ID must be a non-negative integer."));
				}
				if (request.TimeoutSeconds <= 0)
				{
					diagnostics.Add(Error("request.timeout.invalid", "Timeout must be greater than zero."));
				}
			}
			if ((request.BakeOutputs & ~(DhmgsBakeOutputs.Generic | DhmgsBakeOutputs.Humanoid)) != 0)
			{
				diagnostics.Add(Error("request.outputs.invalid", "The requested bake output flags are invalid."));
			}
			if (!Enum.IsDefined(typeof(DhmgsVideoPolicy), request.VideoPolicy))
			{
				diagnostics.Add(Error("request.video-policy.invalid", "The preview video policy is invalid."));
			}

			try
			{
				DhmgsAssetWriter.ValidateOutputRoot(request.OutputRoot);
			}
			catch (Exception exception)
			{
				diagnostics.Add(Error("request.output.invalid", exception.Message));
			}

			DhmgsBakeSourceValidator.ValidatePreflight(
				request.SmplxBakeSource,
				request.BakeOutputs,
				diagnostics);
			return new DhmgsImportValidation(diagnostics);
		}

		public static async Task<DhmgsImportJobResult> ImportAsync(
			DhmgsImportJobRequest request,
			Action<DhmgsImportJobProgress> onProgress = null,
			CancellationToken cancellationToken = default)
		{
			DhmgsImportValidation validation = Validate(request);
			if (!validation.CanRun)
			{
				throw new ArgumentException(JoinErrors(validation.Diagnostics), nameof(request));
			}
			if (Interlocked.CompareExchange(ref _activeJob, 1, 0) != 0)
			{
				throw new InvalidOperationException("Another DHMGS import is already running.");
			}

			Guid jobId = Guid.NewGuid();
			long revision = Interlocked.Increment(ref _nextRevision);
			List<DhmgsImportDiagnostic> diagnostics = new List<DhmgsImportDiagnostic>();
			DhmgsImportResult imported = null;
			DhmgsImportCacheTransaction cacheTransaction = null;
			DhmgsPreparedAssetWrite preparedAssets = null;
			try
			{
				Report(onProgress, jobId, revision, DhmgsImportJobStage.Validating, 1f, 0.02f,
					"Validated import request", true);
				cancellationToken.ThrowIfCancellationRequested();

				InlinePipelineProgress pipelineProgress = new InlinePipelineProgress(
					value => ReportPipeline(onProgress, jobId, revision, value, request.IsArchiveSource));
				imported = request.IsArchiveSource
					? await DhmgsImportPipeline.ImportArchiveAsync(
						request.ArchiveBytes,
						request.SourceId,
						pipelineProgress,
						cancellationToken)
					: await DhmgsImportPipeline.ImportAsync(
						new DhmgsImportRequest(request.BaseUrl, request.AttemptId, request.TimeoutSeconds),
						pipelineProgress,
						cancellationToken);

				if (request.BakeOutputs != DhmgsBakeOutputs.None)
				{
					Report(onProgress, jobId, revision, DhmgsImportJobStage.ValidatingBakeSource, 0f, 0.57f,
						"Validating SMPL-X bake source", true);
					DhmgsBakeSourceValidator.ValidateForMotion(
						request.SmplxBakeSource,
						imported.Motion,
						diagnostics);
					if (HasErrors(diagnostics))
					{
						throw new InvalidOperationException(JoinErrors(diagnostics));
					}
					Report(onProgress, jobId, revision, DhmgsImportJobStage.ValidatingBakeSource, 1f, 0.6f,
						"SMPL-X bake source is compatible", true);
				}

				string videoPath = null;
				bool hasVideo = imported.VideoBytes != null && imported.VideoBytes.Length > 0;
				if (request.VideoPolicy == DhmgsVideoPolicy.Require && !hasVideo)
				{
					throw new InvalidOperationException("The downloaded archive has no preview video.");
				}
				if (request.VideoPolicy != DhmgsVideoPolicy.Ignore && !hasVideo)
				{
					diagnostics.Add(new DhmgsImportDiagnostic(
						"video.not-present",
						DhmgsImportSeverity.Info,
						"The downloaded archive has no preview video."));
				}

				preparedAssets = await DhmgsAssetWriter.PrepareAsync(
					imported,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = request.OutputRoot,
						GenericRig = request.SmplxBakeSource,
						HumanoidRig = request.SmplxBakeSource,
						BakeGenericClip = (request.BakeOutputs & DhmgsBakeOutputs.Generic) != 0,
						BakeHumanoidClips = (request.BakeOutputs & DhmgsBakeOutputs.Humanoid) != 0,
						CancellationToken = cancellationToken,
					},
					(stage, stageProgress, message, canCancel) =>
						ReportWriter(onProgress, jobId, revision, stage, stageProgress, message, canCancel),
					cancellationToken);

				Report(onProgress, jobId, revision, DhmgsImportJobStage.PreparingCache, 0f, 0.9f,
					"Preparing project-local import cache", true);
				DhmgsImportCacheArtifacts cacheArtifacts = DhmgsImportCacheArtifacts.SourceJson;
				if (request.VideoPolicy != DhmgsVideoPolicy.Ignore)
				{
					cacheArtifacts |= DhmgsImportCacheArtifacts.PreviewVideo;
				}
				try
				{
					cacheTransaction = await DhmgsImportCacheTransaction.PrepareAsync(
						imported,
						cacheArtifacts,
						cancellationToken);
					videoPath = cacheTransaction?.PreviewVideoPath;
				}
				catch (Exception exception) when (
					request.VideoPolicy != DhmgsVideoPolicy.Require &&
					exception is not OperationCanceledException)
				{
					diagnostics.Add(new DhmgsImportDiagnostic(
						"cache.prepare-failed",
						DhmgsImportSeverity.Warning,
						$"Core assets can still be imported, but the project-local source cache could not be prepared: {exception.Message}"));
				}
				Report(onProgress, jobId, revision, DhmgsImportJobStage.PreparingCache, 1f, 0.95f,
					cacheTransaction != null
						? "Project-local import cache prepared"
						: "Project-local cache skipped; build-safe assets can still be published",
					true);

				cancellationToken.ThrowIfCancellationRequested();
				Report(onProgress, jobId, revision, DhmgsImportJobStage.Publishing, 0f, 0.95f,
					"Publishing cache and assets (cancellation disabled)", false);
				DhmgsAssetWriteResult written;
				bool reloadLocked = false;
				try
				{
					EditorApplication.LockReloadAssemblies();
					reloadLocked = true;
					cacheTransaction?.Publish(CancellationToken.None);
					written = preparedAssets.Publish();
					if ((request.BakeOutputs & DhmgsBakeOutputs.Generic) != 0 && written.GenericClip == null)
					{
						throw new InvalidOperationException("The requested Generic clip was not produced.");
					}
					if ((request.BakeOutputs & DhmgsBakeOutputs.Humanoid) != 0 &&
						(written.HumanoidInPlaceClip == null || written.HumanoidRootMotionClip == null))
					{
						throw new InvalidOperationException("The requested Humanoid clips were not produced.");
					}
					foreach (string warning in written.CleanupWarnings)
					{
						diagnostics.Add(new DhmgsImportDiagnostic(
							"assets.cleanup-incomplete",
							DhmgsImportSeverity.Warning,
							warning));
					}
					cacheTransaction?.Complete();
				}
				finally
				{
					if (reloadLocked)
					{
						EditorApplication.UnlockReloadAssemblies();
					}
				}
				Report(onProgress, jobId, revision, DhmgsImportJobStage.Publishing, 1f, 1f,
					"Published project-local cache and build-safe assets", false);

				float leftDiff = DhmgsHandMeanReference.MaxDiff(
					imported.Manifest.LeftHandOffset,
					DhmgsHandMeanReference.LeftHandMean);
				float rightDiff = DhmgsHandMeanReference.MaxDiff(
					imported.Manifest.RightHandOffset,
					DhmgsHandMeanReference.RightHandMean);
				Report(onProgress, jobId, revision, DhmgsImportJobStage.Completed, 1f, 1f,
					"Import completed", false);

				return new DhmgsImportJobResult(
					jobId,
					written.Motion,
					written.MotionAssetPath,
					written.GenericClip,
					written.HumanoidInPlaceClip,
					written.HumanoidRootMotionClip,
					cacheTransaction?.ManifestPath,
					cacheTransaction?.SequencePath,
					videoPath,
					leftDiff,
					rightDiff,
					diagnostics);
			}
			catch (Exception importError)
			{
				Exception rollbackError = null;
				try
				{
					cacheTransaction?.Rollback();
				}
				catch (Exception exception)
				{
					rollbackError = exception;
				}
				if (imported?.Motion != null && !AssetDatabase.Contains(imported.Motion))
				{
					UnityEngine.Object.DestroyImmediate(imported.Motion);
					imported.Motion = null;
				}
				if (rollbackError != null)
				{
					throw new AggregateException(
						"DHMGS import failed and the project-local cache rollback was incomplete.",
						importError,
						rollbackError);
				}
				throw;
			}
			finally
			{
				preparedAssets?.Dispose();
				try
				{
					cacheTransaction?.Dispose();
				}
				catch (Exception exception)
				{
					Debug.LogException(exception);
				}
				Volatile.Write(ref _activeJob, 0);
			}
		}

		private static void ReportPipeline(
			Action<DhmgsImportJobProgress> callback,
			Guid jobId,
			long revision,
			DhmgsImportProgress value,
			bool archiveSource)
		{
			(DhmgsImportJobStage stage, float start, float span) = archiveSource
				? value.Stage switch
				{
					DhmgsImportStage.Extracting => (DhmgsImportJobStage.Extracting, 0.02f, 0.08f),
					DhmgsImportStage.Decoding => (DhmgsImportJobStage.Decoding, 0.1f, 0.47f),
					_ => (DhmgsImportJobStage.Decoding, 0.1f, 0.47f),
				}
				: value.Stage switch
			{
				DhmgsImportStage.Downloading => (DhmgsImportJobStage.Downloading, 0.02f, 0.43f),
				DhmgsImportStage.Extracting => (DhmgsImportJobStage.Extracting, 0.45f, 0.05f),
				DhmgsImportStage.Decoding => (DhmgsImportJobStage.Decoding, 0.5f, 0.07f),
				_ => (DhmgsImportJobStage.Decoding, 0.5f, 0.07f),
			};
			Report(callback, jobId, revision, stage, value.Value, start + value.Value * span,
				stage.ToString(), true);
		}

		private static void ReportWriter(
			Action<DhmgsImportJobProgress> callback,
			Guid jobId,
			long revision,
			DhmgsImportJobStage stage,
			float stageProgress,
			string message,
			bool canCancel)
		{
			(float start, float span) = stage switch
			{
				DhmgsImportJobStage.BakingGeneric => (0.61f, 0.11f),
				DhmgsImportJobStage.BakingHumanoid => (0.72f, 0.16f),
				DhmgsImportJobStage.PreparingAssets => (0.88f, 0.02f),
				DhmgsImportJobStage.Publishing => (0.95f, 0.05f),
				_ => (0.61f, 0.29f),
			};
			Report(callback, jobId, revision, stage, stageProgress, start + stageProgress * span,
				message, canCancel);
		}

		private static void Report(
			Action<DhmgsImportJobProgress> callback,
			Guid jobId,
			long revision,
			DhmgsImportJobStage stage,
			float stageProgress,
			float overallProgress,
			string message,
			bool canCancel)
		{
			if (callback == null)
			{
				return;
			}
			try
			{
				callback(new DhmgsImportJobProgress(
					jobId,
					revision,
					stage,
					stageProgress,
					overallProgress,
					message,
					canCancel));
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private static bool HasErrors(IEnumerable<DhmgsImportDiagnostic> diagnostics)
		{
			foreach (DhmgsImportDiagnostic diagnostic in diagnostics)
			{
				if (diagnostic.Severity == DhmgsImportSeverity.Error)
				{
					return true;
				}
			}
			return false;
		}

		private static string JoinErrors(IEnumerable<DhmgsImportDiagnostic> diagnostics)
		{
			List<string> messages = new List<string>();
			foreach (DhmgsImportDiagnostic diagnostic in diagnostics)
			{
				if (diagnostic.Severity == DhmgsImportSeverity.Error)
				{
					messages.Add(diagnostic.Message);
				}
			}
			return string.Join("\n", messages);
		}

		private static DhmgsImportDiagnostic Error(string code, string message)
		{
			return new DhmgsImportDiagnostic(code, DhmgsImportSeverity.Error, message);
		}

		private sealed class InlinePipelineProgress : IProgress<DhmgsImportProgress>
		{
			private readonly Action<DhmgsImportProgress> _callback;

			public InlinePipelineProgress(Action<DhmgsImportProgress> callback)
			{
				_callback = callback;
			}

			public void Report(DhmgsImportProgress value)
			{
				_callback?.Invoke(value);
			}
		}
	}
}
