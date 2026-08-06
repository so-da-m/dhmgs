using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
			UnityWebRequestAsyncOperation operation = request.SendWebRequest();

			while (!operation.isDone)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					request.Abort();
					cancellationToken.ThrowIfCancellationRequested();
				}

				float value = request.downloadProgress < 0f ? 0f : request.downloadProgress;
				progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Downloading, value));
				await Task.Yield();
			}

			cancellationToken.ThrowIfCancellationRequested();
			if (request.result != UnityWebRequest.Result.Success)
			{
				throw new DhmgsImportException(
					DhmgsImportStage.Downloading,
					$"Request failed ({request.responseCode}): {url} ({request.error})");
			}

			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.Downloading, 1f));
			return request.downloadHandler.data;
		}
	}

	internal static class DhmgsArchiveReader
	{
		public static DhmgsArchiveData Read(byte[] archiveBytes)
		{
			if (archiveBytes == null || archiveBytes.Length == 0)
			{
				throw new DhmgsImportException(DhmgsImportStage.Extracting, "The downloaded archive is empty.");
			}

			try
			{
				using MemoryStream stream = new MemoryStream(archiveBytes, false);
				using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read);
				string manifestJson = ReadText(archive, "manifest.json");
				string sequenceJson = ReadText(archive, "sequence.json");

				if (manifestJson == null)
				{
					throw new DhmgsImportException(DhmgsImportStage.Extracting, "manifest.json is missing from the archive.");
				}
				if (sequenceJson == null)
				{
					throw new DhmgsImportException(DhmgsImportStage.Extracting, "sequence.json is missing from the archive.");
				}

				return new DhmgsArchiveData
				{
					ManifestJson = manifestJson,
					SequenceJson = sequenceJson,
					VideoBytes = ReadBytes(archive, "fitting-cut.mp4"),
				};
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

		private static ZipArchiveEntry Find(ZipArchive archive, string fileName)
		{
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				if (string.Equals(Path.GetFileName(entry.FullName), fileName, StringComparison.OrdinalIgnoreCase))
				{
					return entry;
				}
			}
			return null;
		}

		private static string ReadText(ZipArchive archive, string fileName)
		{
			ZipArchiveEntry entry = Find(archive, fileName);
			if (entry == null)
			{
				return null;
			}

			using Stream stream = entry.Open();
			using StreamReader reader = new StreamReader(stream);
			return reader.ReadToEnd();
		}

		private static byte[] ReadBytes(ZipArchive archive, string fileName)
		{
			ZipArchiveEntry entry = Find(archive, fileName);
			if (entry == null)
			{
				return null;
			}

			using Stream stream = entry.Open();
			using MemoryStream output = new MemoryStream();
			stream.CopyTo(output);
			return output.ToArray();
		}
	}
}
