using System;
using System.Collections.Generic;
using System.Globalization;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	[Flags]
	public enum DhmgsBakeOutputs
	{
		None = 0,
		Generic = 1 << 0,
		Humanoid = 1 << 1,
	}

	public enum DhmgsVideoPolicy
	{
		Ignore,
		CacheIfPresent,
		Require,
	}

	public enum DhmgsImportJobStage
	{
		Validating,
		Downloading,
		Extracting,
		Decoding,
		ValidatingBakeSource,
		BakingGeneric,
		BakingHumanoid,
		PreparingAssets,
		PreparingCache,
		Publishing,
		Completed,
	}

	public enum DhmgsImportSeverity
	{
		Info,
		Warning,
		Error,
	}

	public readonly struct DhmgsImportDiagnostic
	{
		public DhmgsImportDiagnostic(string code, DhmgsImportSeverity severity, string message)
		{
			Code = code ?? string.Empty;
			Severity = severity;
			Message = message ?? string.Empty;
		}

		public string Code { get; }
		public DhmgsImportSeverity Severity { get; }
		public string Message { get; }
	}

	public readonly struct DhmgsImportJobProgress
	{
		public DhmgsImportJobProgress(
			Guid jobId,
			long revision,
			DhmgsImportJobStage stage,
			float stageProgress,
			float overallProgress,
			string message,
			bool canCancel)
		{
			JobId = jobId;
			Revision = revision;
			Stage = stage;
			StageProgress = Mathf.Clamp01(stageProgress);
			OverallProgress = Mathf.Clamp01(overallProgress);
			Message = message ?? stage.ToString();
			CanCancel = canCancel;
		}

		public Guid JobId { get; }
		public long Revision { get; }
		public DhmgsImportJobStage Stage { get; }
		public float StageProgress { get; }
		public float OverallProgress { get; }
		public string Message { get; }
		public bool CanCancel { get; }
	}

	public sealed class DhmgsImportJobRequest
	{
		public DhmgsImportJobRequest(
			string baseUrl,
			int attemptId,
			string outputRoot,
			DhmgsBakeOutputs bakeOutputs,
			DhmgsVideoPolicy videoPolicy = DhmgsVideoPolicy.CacheIfPresent,
			int timeoutSeconds = DhmgsImportService.DefaultTimeoutSeconds)
		{
			IsArchiveSource = false;
			BaseUrl = baseUrl;
			AttemptId = attemptId;
			SourceId = attemptId.ToString(CultureInfo.InvariantCulture);
			OutputRoot = outputRoot;
			BakeOutputs = bakeOutputs;
			VideoPolicy = videoPolicy;
			TimeoutSeconds = timeoutSeconds;
		}

		public DhmgsImportJobRequest(
			string sourceId,
			byte[] archiveBytes,
			string outputRoot,
			DhmgsBakeOutputs bakeOutputs,
			DhmgsVideoPolicy videoPolicy = DhmgsVideoPolicy.CacheIfPresent)
		{
			if (archiveBytes != null && archiveBytes.LongLength > DhmgsArchiveLimits.MaxArchiveBytes)
			{
				throw new ArgumentOutOfRangeException(
					nameof(archiveBytes),
					$"The archive exceeds the {DhmgsArchiveLimits.MaxArchiveBytes} byte limit.");
			}
			IsArchiveSource = true;
			SourceId = sourceId;
			ArchiveBytes = archiveBytes == null ? null : (byte[])archiveBytes.Clone();
			BaseUrl = string.Empty;
			AttemptId = -1;
			OutputRoot = outputRoot;
			BakeOutputs = bakeOutputs;
			VideoPolicy = videoPolicy;
			TimeoutSeconds = DhmgsImportService.DefaultTimeoutSeconds;
		}

		public string BaseUrl { get; }
		public int AttemptId { get; }
		public string SourceId { get; }
		public string OutputRoot { get; }
		public DhmgsBakeOutputs BakeOutputs { get; }
		public DhmgsVideoPolicy VideoPolicy { get; }
		public int TimeoutSeconds { get; }
		internal byte[] ArchiveBytes { get; }
		internal bool IsArchiveSource { get; }
	}

	public sealed class DhmgsImportValidation
	{
		internal DhmgsImportValidation(IReadOnlyList<DhmgsImportDiagnostic> diagnostics)
		{
			Diagnostics = diagnostics == null
				? Array.Empty<DhmgsImportDiagnostic>()
				: new List<DhmgsImportDiagnostic>(diagnostics).AsReadOnly();
			bool canRun = true;
			foreach (DhmgsImportDiagnostic diagnostic in Diagnostics)
			{
				if (diagnostic.Severity == DhmgsImportSeverity.Error)
				{
					canRun = false;
					break;
				}
			}
			CanRun = canRun;
		}

		public bool CanRun { get; }
		public IReadOnlyList<DhmgsImportDiagnostic> Diagnostics { get; }
	}

	public sealed class DhmgsImportJobResult
	{
		internal DhmgsImportJobResult(
			Guid jobId,
			DhmgsMotionAsset motion,
			string motionAssetPath,
			AnimationClip genericClip,
			AnimationClip humanoidInPlaceClip,
			AnimationClip humanoidRootMotionClip,
			string sourceManifestPath,
			string sourceSequencePath,
			string previewVideoPath,
			float leftHandOffsetMaxDiff,
			float rightHandOffsetMaxDiff,
			IReadOnlyList<DhmgsImportDiagnostic> diagnostics)
		{
			JobId = jobId;
			Motion = motion;
			MotionAssetPath = motionAssetPath;
			GenericClip = genericClip;
			HumanoidInPlaceClip = humanoidInPlaceClip;
			HumanoidRootMotionClip = humanoidRootMotionClip;
			SourceManifestPath = sourceManifestPath;
			SourceSequencePath = sourceSequencePath;
			PreviewVideoPath = previewVideoPath;
			LeftHandOffsetMaxDiff = leftHandOffsetMaxDiff;
			RightHandOffsetMaxDiff = rightHandOffsetMaxDiff;
			Diagnostics = diagnostics == null
				? Array.Empty<DhmgsImportDiagnostic>()
				: new List<DhmgsImportDiagnostic>(diagnostics).AsReadOnly();
		}

		public Guid JobId { get; }
		public DhmgsMotionAsset Motion { get; }
		public string MotionAssetPath { get; }
		public AnimationClip GenericClip { get; }
		public AnimationClip HumanoidInPlaceClip { get; }
		public AnimationClip HumanoidRootMotionClip { get; }
		public string SourceManifestPath { get; }
		public string SourceSequencePath { get; }
		public string PreviewVideoPath { get; }
		public float LeftHandOffsetMaxDiff { get; }
		public float RightHandOffsetMaxDiff { get; }
		public IReadOnlyList<DhmgsImportDiagnostic> Diagnostics { get; }
	}
}
