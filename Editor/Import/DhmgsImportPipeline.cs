using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	public enum DhmgsImportStage
	{
		Downloading,
		Extracting,
		Decoding,
		WritingAssets,
	}

	public readonly struct DhmgsImportProgress
	{
		public DhmgsImportProgress(DhmgsImportStage stage, float value)
		{
			Stage = stage;
			Value = Mathf.Clamp01(value);
		}

		public DhmgsImportStage Stage { get; }
		public float Value { get; }
	}

	public sealed class DhmgsImportRequest
	{
		public DhmgsImportRequest(string baseUrl, int attemptId, int timeoutSeconds = DhmgsImportPipeline.DefaultTimeoutSeconds)
		{
			BaseUrl = baseUrl;
			AttemptId = attemptId;
			TimeoutSeconds = timeoutSeconds;
		}

		public string BaseUrl { get; }
		public int AttemptId { get; }
		public int TimeoutSeconds { get; }
	}

	public sealed class DhmgsImportResult
	{
		internal DhmgsManifestDto Manifest { get; set; }

		public string SourceId { get; internal set; }
		public DhmgsMotionAsset Motion { get; internal set; }
		public byte[] VideoBytes { get; internal set; }
		public string ManifestJson { get; internal set; }
		public string SequenceJson { get; internal set; }
	}

	public sealed class DhmgsImportException : Exception
	{
		public DhmgsImportException(DhmgsImportStage stage, string message)
			: base(message)
		{
			Stage = stage;
		}

		public DhmgsImportException(DhmgsImportStage stage, string message, Exception innerException)
			: base(message, innerException)
		{
			Stage = stage;
		}

		public DhmgsImportStage Stage { get; }
	}

	/// <summary>
	/// Editor-only authoring pipeline. Downloads or accepts a DHMGS archive and produces
	/// a converted in-memory DhmgsMotionAsset. Persisting Unity assets is a separate step.
	/// </summary>
	public static class DhmgsImportPipeline
	{
		public const string DefaultBaseUrl = "http://192.168.1.128:8090";
		public const int DefaultTimeoutSeconds = 15;

		public static async Task<DhmgsImportResult> ImportAsync(
			DhmgsImportRequest request,
			IProgress<DhmgsImportProgress> progress = null,
			CancellationToken cancellationToken = default)
		{
			Validate(request);
			string url = DhmgsEndpoint.Attempt(request.BaseUrl, request.AttemptId);
			byte[] archiveBytes = await DhmgsHttpClient.DownloadAsync(
				url,
				request.TimeoutSeconds,
				progress,
				cancellationToken);

			cancellationToken.ThrowIfCancellationRequested();
			return ImportArchive(archiveBytes, request.AttemptId.ToString(), progress);
		}

		public static DhmgsImportResult ImportArchive(
			byte[] archiveBytes,
			string sourceId,
			IProgress<DhmgsImportProgress> progress = null)
		{
			if (string.IsNullOrWhiteSpace(sourceId))
			{
				throw new ArgumentException("A source ID is required.", nameof(sourceId));
			}

			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Extracting, 0f));
			DhmgsArchiveData archive = DhmgsArchiveReader.Read(archiveBytes);
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Extracting, 1f));

			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Decoding, 0f));
			DhmgsManifestDto manifest = DhmgsManifestDto.Parse(archive.ManifestJson);
			DhmgsSequenceDto sequence = DhmgsSequenceDto.Parse(archive.SequenceJson, manifest.FrameCount);
			DhmgsMotionAsset motion = DhmgsMotionDecoder.Decode(sourceId, manifest, sequence);
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Decoding, 1f));

			return new DhmgsImportResult
			{
				SourceId = sourceId,
				Motion = motion,
				Manifest = manifest,
				VideoBytes = archive.VideoBytes,
				ManifestJson = archive.ManifestJson,
				SequenceJson = archive.SequenceJson,
			};
		}

		public static string WritePreviewVideo(DhmgsImportResult result)
		{
			if (result?.VideoBytes == null)
			{
				return null;
			}

			string directory = Path.Combine(Application.persistentDataPath, "Dhmgs", result.SourceId);
			Directory.CreateDirectory(directory);
			string path = Path.Combine(directory, "video.mp4");
			File.WriteAllBytes(path, result.VideoBytes);
			return path;
		}

		private static void Validate(DhmgsImportRequest request)
		{
			if (request == null)
			{
				throw new ArgumentNullException(nameof(request));
			}
			if (string.IsNullOrWhiteSpace(request.BaseUrl))
			{
				throw new ArgumentException("The base URL is required.", nameof(request));
			}
			if (request.AttemptId < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(request), "The attempt ID must be non-negative.");
			}
			if (request.TimeoutSeconds <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(request), "The timeout must be positive.");
			}
		}
	}
}
