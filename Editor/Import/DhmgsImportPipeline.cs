using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal enum DhmgsImportStage
	{
		Downloading,
		Extracting,
		Decoding,
		WritingAssets,
	}

	internal readonly struct DhmgsImportProgress
	{
		public DhmgsImportProgress(DhmgsImportStage stage, float value)
		{
			Stage = stage;
			Value = Mathf.Clamp01(value);
		}

		public DhmgsImportStage Stage { get; }
		public float Value { get; }
	}

	internal sealed class DhmgsImportRequest
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

	internal sealed class DhmgsImportResult
	{
		internal DhmgsManifestDto Manifest { get; set; }

		public string SourceId { get; internal set; }
		public string SourceKey { get; internal set; }
		public DhmgsMotionAsset Motion { get; internal set; }
		public byte[] VideoBytes { get; internal set; }
		public string ManifestJson { get; internal set; }
		public string SequenceJson { get; internal set; }
	}

	internal sealed class DhmgsImportException : Exception
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

	internal static class DhmgsImportPipeline
	{
		private sealed class ParsedArchive
		{
			public DhmgsManifestDto Manifest;
			public DhmgsSequenceDto Sequence;
		}

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
			string sourceId = request.AttemptId.ToString(CultureInfo.InvariantCulture);
			string sourceKey = CreateSourceKey(sourceId);

			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Extracting, 0f));
			cancellationToken.ThrowIfCancellationRequested();
			DhmgsArchiveData archive = await Task.Run(
				() => DhmgsArchiveReader.Read(archiveBytes, cancellationToken),
				cancellationToken);
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Extracting, 1f));

			cancellationToken.ThrowIfCancellationRequested();
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Decoding, 0f));
			cancellationToken.ThrowIfCancellationRequested();
			ParsedArchive parsed = await Task.Run(
				() => ParseArchive(archive, cancellationToken),
				cancellationToken);

			cancellationToken.ThrowIfCancellationRequested();
			return await DecodeArchiveAsync(
				sourceId,
				sourceKey,
				archive,
				parsed,
				progress,
				cancellationToken);
		}

		internal static async Task<DhmgsImportResult> ImportArchiveAsync(
			byte[] archiveBytes,
			string sourceId,
			IProgress<DhmgsImportProgress> progress = null,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sourceId))
			{
				throw new ArgumentException("A source ID is required.", nameof(sourceId));
			}

			cancellationToken.ThrowIfCancellationRequested();
			string sourceKey = CreateSourceKey(sourceId);
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Extracting, 0f));
			DhmgsArchiveData archive = await Task.Run(
				() => DhmgsArchiveReader.Read(archiveBytes, cancellationToken),
				cancellationToken);
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Extracting, 1f));

			cancellationToken.ThrowIfCancellationRequested();
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Decoding, 0f));
			ParsedArchive parsed = await Task.Run(
				() => ParseArchive(archive, cancellationToken),
				cancellationToken);
			return await DecodeArchiveAsync(
				sourceId,
				sourceKey,
				archive,
				parsed,
				progress,
				cancellationToken);
		}

		public static DhmgsImportResult ImportArchive(
			byte[] archiveBytes,
			string sourceId,
			IProgress<DhmgsImportProgress> progress = null,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sourceId))
			{
				throw new ArgumentException("A source ID is required.", nameof(sourceId));
			}

			cancellationToken.ThrowIfCancellationRequested();
			string sourceKey = CreateSourceKey(sourceId);
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Extracting, 0f));
			cancellationToken.ThrowIfCancellationRequested();
			DhmgsArchiveData archive = DhmgsArchiveReader.Read(archiveBytes, cancellationToken);
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Extracting, 1f));

			cancellationToken.ThrowIfCancellationRequested();
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Decoding, 0f));
			cancellationToken.ThrowIfCancellationRequested();
			ParsedArchive parsed = ParseArchive(archive, cancellationToken);
			return DecodeArchive(sourceId, sourceKey, archive, parsed, progress, cancellationToken);
		}

		private static ParsedArchive ParseArchive(
			DhmgsArchiveData archive,
			CancellationToken cancellationToken)
		{
			DhmgsManifestDto manifest = DhmgsManifestDto.Parse(archive.ManifestJson, cancellationToken);
			DhmgsSequenceDto sequence = DhmgsSequenceDto.Parse(
				archive.SequenceJson,
				manifest.FrameCount,
				cancellationToken);
			return new ParsedArchive
			{
				Manifest = manifest,
				Sequence = sequence,
			};
		}

		private static DhmgsImportResult DecodeArchive(
			string sourceId,
			string sourceKey,
			DhmgsArchiveData archive,
			ParsedArchive parsed,
			IProgress<DhmgsImportProgress> progress,
			CancellationToken cancellationToken)
		{
			DhmgsMotionAsset motion = DhmgsMotionDecoder.Decode(
				sourceId,
				parsed.Manifest,
				parsed.Sequence,
				cancellationToken);
			try
			{
				progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Decoding, 1f));
				cancellationToken.ThrowIfCancellationRequested();

				return new DhmgsImportResult
				{
					SourceId = sourceId,
					SourceKey = sourceKey,
					Motion = motion,
					Manifest = parsed.Manifest,
					VideoBytes = archive.VideoBytes,
					ManifestJson = archive.ManifestJson,
					SequenceJson = archive.SequenceJson,
				};
			}
			catch
			{
				UnityEngine.Object.DestroyImmediate(motion);
				throw;
			}
		}

		private static async Task<DhmgsImportResult> DecodeArchiveAsync(
			string sourceId,
			string sourceKey,
			DhmgsArchiveData archive,
			ParsedArchive parsed,
			IProgress<DhmgsImportProgress> progress,
			CancellationToken cancellationToken)
		{
			DhmgsMotionAsset motion = await DhmgsMotionDecoder.DecodeAsync(
				sourceId,
				parsed.Manifest,
				parsed.Sequence,
				value => progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Decoding, value)),
				cancellationToken);
			try
			{
				progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Decoding, 1f));
				cancellationToken.ThrowIfCancellationRequested();
				return new DhmgsImportResult
				{
					SourceId = sourceId,
					SourceKey = sourceKey,
					Motion = motion,
					Manifest = parsed.Manifest,
					VideoBytes = archive.VideoBytes,
					ManifestJson = archive.ManifestJson,
					SequenceJson = archive.SequenceJson,
				};
			}
			catch
			{
				UnityEngine.Object.DestroyImmediate(motion);
				throw;
			}
		}

		internal static string CreateSourceKey(string sourceId)
		{
			if (string.IsNullOrWhiteSpace(sourceId))
			{
				throw new ArgumentException("A source ID is required.", nameof(sourceId));
			}

			byte[] bytes = Encoding.UTF8.GetBytes(sourceId);
			byte[] hash;
			using (SHA256 sha256 = SHA256.Create())
			{
				hash = sha256.ComputeHash(bytes);
			}

			StringBuilder key = new StringBuilder("source-", "source-".Length + hash.Length * 2);
			for (int index = 0; index < hash.Length; index++)
			{
				key.Append(hash[index].ToString("x2"));
			}
			return key.ToString();
		}

		internal static string ResolvePreviewVideoPath(DhmgsImportResult result)
		{
			if (result?.VideoBytes == null)
			{
				return null;
			}
			if (result.VideoBytes.LongLength > DhmgsArchiveLimits.MaxVideoBytes)
			{
				throw new ArgumentException(
					$"Preview video exceeds the {DhmgsArchiveLimits.MaxVideoBytes} byte limit.",
					nameof(result));
			}
			return ResolveImportCachePath(result, "video.mp4");
		}

		internal static string ResolveImportCachePath(DhmgsImportResult result, string fileName)
		{
			if (result == null)
			{
				throw new ArgumentNullException(nameof(result));
			}
			if (fileName != "manifest.json" && fileName != "sequence.json" && fileName != "video.mp4")
			{
				throw new ArgumentException("The cache file name is not allowed.", nameof(fileName));
			}

			string sourceKey = string.IsNullOrEmpty(result.SourceKey)
				? CreateSourceKey(result.SourceId)
				: result.SourceKey;
			ValidateSourceKey(sourceKey);

			string projectRoot = Directory.GetParent(Path.GetFullPath(Application.dataPath))?.FullName;
			if (string.IsNullOrEmpty(projectRoot))
			{
				throw new InvalidOperationException("Could not resolve the Unity project root.");
			}

			string cacheRoot = Path.GetFullPath(Path.Combine(projectRoot, "Library", "Mnnh.Dhmgs"));
			string directory = Path.GetFullPath(Path.Combine(cacheRoot, sourceKey));
			EnsureContainedPath(cacheRoot, directory);
			Directory.CreateDirectory(directory);
			string path = Path.GetFullPath(Path.Combine(directory, fileName));
			EnsureContainedPath(cacheRoot, path);
			return path;
		}

		private static void ValidateSourceKey(string sourceKey)
		{
			const int Sha256HexLength = 64;
			const string Prefix = "source-";
			if (sourceKey == null || sourceKey.Length != Prefix.Length + Sha256HexLength
				|| !sourceKey.StartsWith(Prefix, StringComparison.Ordinal))
			{
				throw new InvalidOperationException("The import result contains an invalid source key.");
			}

			for (int index = Prefix.Length; index < sourceKey.Length; index++)
			{
				char value = sourceKey[index];
				if ((value < '0' || value > '9') && (value < 'a' || value > 'f'))
				{
					throw new InvalidOperationException("The import result contains an invalid source key.");
				}
			}
		}

		private static void EnsureContainedPath(string root, string path)
		{
			StringComparison comparison = Path.DirectorySeparatorChar == '\\'
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal;
			string rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			if (!path.StartsWith(rootWithSeparator, comparison))
			{
				throw new InvalidOperationException("The preview cache path escaped the project-local cache root.");
			}
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

	[Flags]
	internal enum DhmgsImportCacheArtifacts
	{
		None = 0,
		SourceJson = 1 << 0,
		PreviewVideo = 1 << 1,
		All = SourceJson | PreviewVideo,
	}

	internal sealed class DhmgsImportCacheTransaction : IDisposable
	{
		private const int WriteBufferSize = 64 * 1024;
		private const int TextCharBufferSize = 16 * 1024;

		private enum TransactionState
		{
			Prepared,
			Published,
			Completed,
			RolledBack,
		}

		private sealed class CacheFile
		{
			public string DestinationPath;
			public string TemporaryPath;
			public string BackupPath;
			public bool RemoveDestination;
			public bool HadOriginal;
			public bool Published;
		}

		private sealed class Preparation
		{
			public string ManifestJson;
			public string SequenceJson;
			public byte[] VideoBytes;
			public string ManifestPath;
			public string SequencePath;
			public string PreviewVideoPath;
			public string PreviewVideoDestinationPath;
		}

		private static readonly Encoding CacheUtf8 = new UTF8Encoding(false, true);

		private readonly CacheFile[] _files;
		private TransactionState _state;

		private DhmgsImportCacheTransaction(
			CacheFile[] files,
			string manifestPath,
			string sequencePath,
			string previewVideoPath)
		{
			_files = files;
			ManifestPath = manifestPath;
			SequencePath = sequencePath;
			PreviewVideoPath = previewVideoPath;
			_state = TransactionState.Prepared;
		}

		public string ManifestPath { get; }
		public string SequencePath { get; }
		public string PreviewVideoPath { get; }

		public static DhmgsImportCacheTransaction Prepare(
			DhmgsImportResult result,
			DhmgsImportCacheArtifacts artifacts,
			CancellationToken cancellationToken = default)
		{
			Preparation preparation = ResolvePreparation(result, artifacts, cancellationToken);
			return PrepareResolved(preparation, cancellationToken);
		}

		public static async Task<DhmgsImportCacheTransaction> PrepareAsync(
			DhmgsImportResult result,
			DhmgsImportCacheArtifacts artifacts,
			CancellationToken cancellationToken = default)
		{
			Preparation preparation = ResolvePreparation(result, artifacts, cancellationToken);
			return await Task.Run(
				() => PrepareResolved(preparation, cancellationToken),
				cancellationToken);
		}

		private static Preparation ResolvePreparation(
			DhmgsImportResult result,
			DhmgsImportCacheArtifacts artifacts,
			CancellationToken cancellationToken)
		{
			if (result == null)
			{
				throw new ArgumentNullException(nameof(result));
			}
			if ((artifacts & ~DhmgsImportCacheArtifacts.All) != 0)
			{
				throw new ArgumentOutOfRangeException(nameof(artifacts));
			}

			cancellationToken.ThrowIfCancellationRequested();
			Preparation preparation = new Preparation();
			if ((artifacts & DhmgsImportCacheArtifacts.SourceJson) != 0)
			{
				if (string.IsNullOrWhiteSpace(result.ManifestJson))
				{
					throw new InvalidOperationException("The import result has no manifest.json content to cache.");
				}
				if (string.IsNullOrWhiteSpace(result.SequenceJson))
				{
					throw new InvalidOperationException("The import result has no sequence.json content to cache.");
				}

				preparation.ManifestJson = result.ManifestJson;
				preparation.SequenceJson = result.SequenceJson;
				preparation.ManifestPath = DhmgsImportPipeline.ResolveImportCachePath(result, "manifest.json");
				preparation.SequencePath = DhmgsImportPipeline.ResolveImportCachePath(result, "sequence.json");
			}

			if ((artifacts & DhmgsImportCacheArtifacts.PreviewVideo) != 0)
			{
				bool hasVideo = result.VideoBytes != null && result.VideoBytes.Length > 0;
				preparation.PreviewVideoDestinationPath = hasVideo
					? DhmgsImportPipeline.ResolvePreviewVideoPath(result)
					: DhmgsImportPipeline.ResolveImportCachePath(result, "video.mp4");
				if (hasVideo)
				{
					preparation.VideoBytes = result.VideoBytes;
					preparation.PreviewVideoPath = preparation.PreviewVideoDestinationPath;
				}
			}
			return preparation;
		}

		private static DhmgsImportCacheTransaction PrepareResolved(
			Preparation preparation,
			CancellationToken cancellationToken)
		{
			List<CacheFile> files = new List<CacheFile>();
			try
			{
				if (preparation.ManifestPath != null)
				{
					files.Add(PrepareTextFile(
						preparation.ManifestPath,
						preparation.ManifestJson,
						"manifest.json",
						DhmgsArchiveLimits.MaxManifestBytes,
						cancellationToken));
					files.Add(PrepareTextFile(
						preparation.SequencePath,
						preparation.SequenceJson,
						"sequence.json",
						DhmgsArchiveLimits.MaxSequenceBytes,
						cancellationToken));
				}

				if (preparation.PreviewVideoDestinationPath != null)
				{
					files.Add(preparation.VideoBytes != null
						? PrepareFile(
							preparation.PreviewVideoDestinationPath,
							preparation.VideoBytes,
							cancellationToken)
						: PrepareRemoval(preparation.PreviewVideoDestinationPath));
				}

				return new DhmgsImportCacheTransaction(
					files.ToArray(),
					preparation.ManifestPath,
					preparation.SequencePath,
					preparation.PreviewVideoPath);
			}
			catch
			{
				CleanupPreparedFiles(files);
				throw;
			}
		}

		public void Publish(CancellationToken cancellationToken = default)
		{
			RequireState(TransactionState.Prepared, nameof(Publish));
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				foreach (CacheFile file in _files)
				{
					PublishFile(file);
				}
				_state = TransactionState.Published;
			}
			catch (Exception publishException)
			{
				try
				{
					RollbackFiles();
					_state = TransactionState.RolledBack;
				}
				catch (Exception rollbackException)
				{
					throw new AggregateException(
						"Publishing the DHMGS cache failed, and rollback was incomplete.",
						publishException,
						rollbackException);
				}
				throw;
			}
		}

		public void Complete()
		{
			RequireState(TransactionState.Published, nameof(Complete));
			_state = TransactionState.Completed;
			foreach (CacheFile file in _files)
			{
				TryDelete(file.TemporaryPath);
				TryDelete(file.BackupPath);
			}
		}

		public void Rollback()
		{
			if (_state == TransactionState.RolledBack || _state == TransactionState.Completed)
			{
				return;
			}

			RollbackFiles();
			_state = TransactionState.RolledBack;
		}

		public void Dispose()
		{
			if (_state == TransactionState.Prepared || _state == TransactionState.Published)
			{
				Rollback();
				return;
			}

			foreach (CacheFile file in _files)
			{
				TryDelete(file.TemporaryPath);
				TryDelete(file.BackupPath);
			}
		}

		private static CacheFile PrepareFile(
			string destinationPath,
			byte[] bytes,
			CancellationToken cancellationToken)
		{
			CacheFile file = CreateCacheFile(destinationPath);
			try
			{
				WriteTemporaryFile(file.TemporaryPath, bytes, cancellationToken);
				return file;
			}
			catch
			{
				TryDelete(file.TemporaryPath);
				TryDelete(file.BackupPath);
				throw;
			}
		}

		private static CacheFile PrepareTextFile(
			string destinationPath,
			string text,
			string label,
			long byteLimit,
			CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				throw new InvalidOperationException($"The import result has no {label} content to cache.");
			}

			CacheFile file = CreateCacheFile(destinationPath);
			try
			{
				WriteTemporaryText(
					file.TemporaryPath,
					text,
					label,
					byteLimit,
					cancellationToken);
				return file;
			}
			catch
			{
				TryDelete(file.TemporaryPath);
				TryDelete(file.BackupPath);
				throw;
			}
		}

		private static CacheFile PrepareRemoval(string destinationPath)
		{
			CacheFile file = CreateCacheFile(destinationPath);
			file.RemoveDestination = true;
			return file;
		}

		private static CacheFile CreateCacheFile(string destinationPath)
		{
			string operationId = Guid.NewGuid().ToString("N");
			return new CacheFile
			{
				DestinationPath = destinationPath,
				TemporaryPath = destinationPath + "." + operationId + ".tmp",
				BackupPath = destinationPath + "." + operationId + ".bak",
			};
		}

		private static void PublishFile(CacheFile file)
		{
			if (file.RemoveDestination)
			{
				if (File.Exists(file.DestinationPath))
				{
					File.Move(file.DestinationPath, file.BackupPath);
					file.HadOriginal = true;
				}
				else
				{
					file.HadOriginal = false;
				}
				file.Published = true;
				return;
			}

			if (File.Exists(file.DestinationPath))
			{
				File.Replace(file.TemporaryPath, file.DestinationPath, file.BackupPath);
				file.HadOriginal = true;
			}
			else
			{
				try
				{
					File.Move(file.TemporaryPath, file.DestinationPath);
					file.HadOriginal = false;
				}
				catch (IOException) when (File.Exists(file.DestinationPath))
				{
					File.Replace(file.TemporaryPath, file.DestinationPath, file.BackupPath);
					file.HadOriginal = true;
				}
			}
			file.Published = true;
		}

		private void RollbackFiles()
		{
			List<Exception> failures = null;
			for (int index = _files.Length - 1; index >= 0; index--)
			{
				CacheFile file = _files[index];
				try
				{
					if (file.Published)
					{
						RestoreFile(file);
						file.Published = false;
					}
					DeleteIfExists(file.TemporaryPath);
					DeleteIfExists(file.BackupPath);
				}
				catch (Exception exception)
				{
					failures ??= new List<Exception>();
					failures.Add(exception);
				}
			}

			if (failures != null)
			{
				throw new AggregateException("DHMGS cache rollback was incomplete.", failures);
			}
		}

		private static void RestoreFile(CacheFile file)
		{
			if (file.HadOriginal)
			{
				if (!File.Exists(file.BackupPath))
				{
					throw new IOException($"The cache backup required for rollback is missing: {file.BackupPath}");
				}

				if (File.Exists(file.DestinationPath))
				{
					File.Replace(file.BackupPath, file.DestinationPath, null);
				}
				else
				{
					File.Move(file.BackupPath, file.DestinationPath);
				}
			}
			else
			{
				DeleteIfExists(file.DestinationPath);
			}
		}

		private static void CleanupPreparedFiles(IEnumerable<CacheFile> files)
		{
			foreach (CacheFile file in files)
			{
				TryDelete(file.TemporaryPath);
				TryDelete(file.BackupPath);
			}
		}

		private static void WriteTemporaryFile(
			string path,
			byte[] bytes,
			CancellationToken cancellationToken)
		{
			using FileStream stream = new FileStream(
				path,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None,
				WriteBufferSize,
				FileOptions.WriteThrough);
			int offset = 0;
			while (offset < bytes.Length)
			{
				cancellationToken.ThrowIfCancellationRequested();
				int count = Math.Min(WriteBufferSize, bytes.Length - offset);
				stream.Write(bytes, offset, count);
				offset += count;
			}
			stream.Flush(true);
		}

		private static void WriteTemporaryText(
			string path,
			string text,
			string label,
			long byteLimit,
			CancellationToken cancellationToken)
		{
			using FileStream stream = new FileStream(
				path,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None,
				WriteBufferSize,
				FileOptions.WriteThrough);
			Encoder encoder = CacheUtf8.GetEncoder();
			char[] chars = new char[TextCharBufferSize];
			byte[] bytes = new byte[CacheUtf8.GetMaxByteCount(TextCharBufferSize)];
			int textOffset = 0;
			long totalBytes = 0L;

			while (textOffset < text.Length)
			{
				cancellationToken.ThrowIfCancellationRequested();
				int charCount = Math.Min(chars.Length, text.Length - textOffset);
				text.CopyTo(textOffset, chars, 0, charCount);
				textOffset += charCount;
				bool flush = textOffset == text.Length;
				int charsOffset = 0;
				while (charsOffset < charCount)
				{
					encoder.Convert(
						chars,
						charsOffset,
						charCount - charsOffset,
						bytes,
						0,
						bytes.Length,
						flush,
						out int charsUsed,
						out int bytesUsed,
						out _);
					if (bytesUsed > byteLimit - totalBytes)
					{
						throw new InvalidOperationException($"{label} exceeds the {byteLimit} byte cache limit.");
					}

					stream.Write(bytes, 0, bytesUsed);
					totalBytes += bytesUsed;
					charsOffset += charsUsed;
				}
			}
			stream.Flush(true);
		}

		private void RequireState(TransactionState expected, string operation)
		{
			if (_state != expected)
			{
				throw new InvalidOperationException(
					$"Cannot {operation} a DHMGS cache transaction in state {_state}.");
			}
		}

		private static void DeleteIfExists(string path)
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}

		private static void TryDelete(string path)
		{
			try
			{
				DeleteIfExists(path);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}
}
