using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine.Networking;

namespace Mnnh.Dhmgs.Editor
{
	internal sealed class DhmgsArchiveData
	{
		public string ManifestJson { get; set; }
		public string SequenceJson { get; set; }
		public byte[] VideoBytes { get; set; }
	}

	internal static class DhmgsArchiveLimits
	{
		public const long MaxArchiveBytes = 256L * 1024L * 1024L;
		public const long MaxManifestBytes = 4L * 1024L * 1024L;
		public const long MaxIdentityBytes = 4L * 1024L * 1024L;
		public const long MaxSequenceBytes = 256L * 1024L * 1024L;
		public const long MaxVideoBytes = 512L * 1024L * 1024L;
		public const long MaxTotalUncompressedBytes = 768L * 1024L * 1024L;
	}

	internal static class DhmgsHttpClient
	{
		public static async Task<byte[]> DownloadAsync(
			string url,
			int timeoutSeconds,
			IProgress<DhmgsImportProgress> progress,
			CancellationToken cancellationToken)
		{
			using UnityWebRequest request = UnityWebRequest.Get(url);
			request.timeout = timeoutSeconds;
			request.redirectLimit = 0;
			UnityWebRequestAsyncOperation operation = request.SendWebRequest();

			while (!operation.isDone)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					request.Abort();
					cancellationToken.ThrowIfCancellationRequested();
				}

				ThrowIfDownloadTooLarge(request, url);
				float value = request.downloadProgress < 0f ? 0f : request.downloadProgress;
				progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Downloading, value));
				await Task.Yield();
			}

			cancellationToken.ThrowIfCancellationRequested();
			ThrowIfDownloadTooLarge(request, url);
			if (request.result != UnityWebRequest.Result.Success)
			{
				throw new DhmgsImportException(
					DhmgsImportStage.Downloading,
					$"Request failed ({request.responseCode}): {url} ({request.error})");
			}

			byte[] data = request.downloadHandler.data;
			if (data == null || data.LongLength > DhmgsArchiveLimits.MaxArchiveBytes)
			{
				throw new DhmgsImportException(
					DhmgsImportStage.Downloading,
					$"The downloaded archive exceeds the {DhmgsArchiveLimits.MaxArchiveBytes} byte limit.");
			}

			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Downloading, 1f));
			return data;
		}

		private static void ThrowIfDownloadTooLarge(UnityWebRequest request, string url)
		{
			string contentLengthHeader = request.GetResponseHeader("Content-Length");
			bool declaredTooLarge = ulong.TryParse(
				contentLengthHeader,
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out ulong contentLength)
				&& contentLength > (ulong)DhmgsArchiveLimits.MaxArchiveBytes;
			if (!declaredTooLarge && request.downloadedBytes <= (ulong)DhmgsArchiveLimits.MaxArchiveBytes)
			{
				return;
			}

			request.Abort();
			throw new DhmgsImportException(
				DhmgsImportStage.Downloading,
				$"Download exceeded the {DhmgsArchiveLimits.MaxArchiveBytes} byte limit: {url}");
		}
	}

	internal static class DhmgsArchiveReader
	{
		private const string ManifestName = "manifest.json";
		private const string IdentityName = "identity.json";
		private const string SequenceName = "sequence.json";
		private const string VideoName = "fitting-cut.mp4";
		private const int CopyBufferSize = 64 * 1024;

		private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

		public static DhmgsArchiveData Read(
			byte[] archiveBytes,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (archiveBytes == null || archiveBytes.Length == 0)
			{
				throw new DhmgsImportException(DhmgsImportStage.Extracting, "The downloaded archive is empty.");
			}
			if (archiveBytes.LongLength > DhmgsArchiveLimits.MaxArchiveBytes)
			{
				throw new DhmgsImportException(
					DhmgsImportStage.Extracting,
					$"The archive exceeds the {DhmgsArchiveLimits.MaxArchiveBytes} byte limit.");
			}

			try
			{
				using MemoryStream stream = new MemoryStream(archiveBytes, false);
				using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read);
				Dictionary<string, ZipArchiveEntry> entries = IndexEntries(archive, cancellationToken);

				if (!entries.TryGetValue(ManifestName, out ZipArchiveEntry manifestEntry))
				{
					throw new DhmgsImportException(DhmgsImportStage.Extracting, "manifest.json is missing from the archive.");
				}
				if (!entries.TryGetValue(SequenceName, out ZipArchiveEntry sequenceEntry))
				{
					throw new DhmgsImportException(DhmgsImportStage.Extracting, "sequence.json is missing from the archive.");
				}

				string manifestJson = ReadText(
					manifestEntry,
					DhmgsArchiveLimits.MaxManifestBytes,
					cancellationToken);
				string sequenceJson = ReadText(
					sequenceEntry,
					DhmgsArchiveLimits.MaxSequenceBytes,
					cancellationToken);
				if (entries.TryGetValue(IdentityName, out ZipArchiveEntry identityEntry))
				{
					ReadText(identityEntry, DhmgsArchiveLimits.MaxIdentityBytes, cancellationToken);
				}
				byte[] videoBytes = entries.TryGetValue(VideoName, out ZipArchiveEntry videoEntry)
					? ReadBytes(videoEntry, DhmgsArchiveLimits.MaxVideoBytes, cancellationToken)
					: null;

				cancellationToken.ThrowIfCancellationRequested();
				return new DhmgsArchiveData
				{
					ManifestJson = manifestJson,
					SequenceJson = sequenceJson,
					VideoBytes = videoBytes,
				};
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (DhmgsImportException)
			{
				throw;
			}
			catch (Exception exception)
			{
				throw new DhmgsImportException(DhmgsImportStage.Extracting, "Failed to read the archive.", exception);
			}
		}

		private static Dictionary<string, ZipArchiveEntry> IndexEntries(
			ZipArchive archive,
			CancellationToken cancellationToken)
		{
			Dictionary<string, ZipArchiveEntry> entries =
				new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
			HashSet<string> namesIgnoringCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			long totalUncompressedBytes = 0L;

			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string name = entry.FullName;
				if (string.IsNullOrEmpty(name)
					|| name.IndexOf('/') >= 0
					|| name.IndexOf('\\') >= 0
					|| !TryGetEntryLimit(name, out long entryLimit))
				{
					throw new DhmgsImportException(
						DhmgsImportStage.Extracting,
						$"Archive entry '{name}' is not an allowed root entry.");
				}

				if (!namesIgnoringCase.Add(name))
				{
					throw new DhmgsImportException(
						DhmgsImportStage.Extracting,
						$"Archive entry '{name}' is duplicated.");
				}

				if (entry.Length < 0L || entry.Length > entryLimit)
				{
					throw new DhmgsImportException(
						DhmgsImportStage.Extracting,
						$"Archive entry '{name}' exceeds its {entryLimit} byte limit.");
				}
				if (entry.Length > DhmgsArchiveLimits.MaxTotalUncompressedBytes - totalUncompressedBytes)
				{
					throw new DhmgsImportException(
						DhmgsImportStage.Extracting,
						$"Archive contents exceed the {DhmgsArchiveLimits.MaxTotalUncompressedBytes} byte total limit.");
				}

				totalUncompressedBytes += entry.Length;
				entries.Add(name, entry);
			}

			return entries;
		}

		private static bool TryGetEntryLimit(string name, out long limit)
		{
			switch (name)
			{
				case ManifestName:
					limit = DhmgsArchiveLimits.MaxManifestBytes;
					return true;
				case IdentityName:
					limit = DhmgsArchiveLimits.MaxIdentityBytes;
					return true;
				case SequenceName:
					limit = DhmgsArchiveLimits.MaxSequenceBytes;
					return true;
				case VideoName:
					limit = DhmgsArchiveLimits.MaxVideoBytes;
					return true;
				default:
					limit = 0L;
					return false;
			}
		}

		private static string ReadText(
			ZipArchiveEntry entry,
			long limit,
			CancellationToken cancellationToken)
		{
			using MemoryStream output = ReadToMemory(entry, limit, cancellationToken);
			try
			{
				return StrictUtf8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
			}
			catch (DecoderFallbackException exception)
			{
				throw new DhmgsImportException(
					DhmgsImportStage.Extracting,
					$"Archive entry '{entry.FullName}' is not valid UTF-8.",
					exception);
			}
		}

		private static byte[] ReadBytes(
			ZipArchiveEntry entry,
			long limit,
			CancellationToken cancellationToken)
		{
			using MemoryStream output = ReadToMemory(entry, limit, cancellationToken);
			return output.ToArray();
		}

		private static MemoryStream ReadToMemory(
			ZipArchiveEntry entry,
			long limit,
			CancellationToken cancellationToken)
		{
			int initialCapacity = entry.Length > int.MaxValue ? 0 : (int)entry.Length;
			MemoryStream output = new MemoryStream(initialCapacity);
			try
			{
				using Stream input = entry.Open();
				byte[] buffer = new byte[CopyBufferSize];
				long totalBytes = 0L;

				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();
					int read = input.Read(buffer, 0, buffer.Length);
					if (read == 0)
					{
						break;
					}
					if (read < 0 || read > limit - totalBytes)
					{
						throw new DhmgsImportException(
							DhmgsImportStage.Extracting,
							$"Archive entry '{entry.FullName}' exceeded its {limit} byte limit while reading.");
					}

					output.Write(buffer, 0, read);
					totalBytes += read;
				}

				if (totalBytes != entry.Length)
				{
					throw new DhmgsImportException(
						DhmgsImportStage.Extracting,
						$"Archive entry '{entry.FullName}' length changed while reading.");
				}

				output.Position = 0L;
				return output;
			}
			catch
			{
				output.Dispose();
				throw;
			}
		}
	}
}
