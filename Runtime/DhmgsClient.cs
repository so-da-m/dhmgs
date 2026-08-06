using System;
using System.Collections;
using System.IO;
using System.IO.Compression;

using UnityEngine.Networking;

namespace Mnnh.Dhmgs
{
	/// <summary>다운로드한 zip에서 추출한 결과(manifest/sequence/영상).</summary>
	public sealed class DhmgsZipResult
	{
		public bool ok;
		public string error;
		public string manifestJson;
		public string sequenceJson;
		public byte[] videoMp4; // null이면 영상 없음(옵션)
	}

	/// <summary>
	/// GET {base}/{id} → zip 다운로드(비동기) → ZipArchive로 manifest/sequence/fitting-cut 추출.
	/// IEnumerator라 런타임 StartCoroutine과 에디터 펌프가 동일하게 구동한다.
	/// </summary>
	public static class DhmgsClient
	{
		public static IEnumerator Download(string baseUrl, int attemptId, int timeoutSec, DhmgsZipResult result)
		{
			string url = DhmgsEndpoints.Attempt(baseUrl, attemptId);
			using (UnityWebRequest req = UnityWebRequest.Get(url))
			{
				req.timeout = timeoutSec;
				yield return req.SendWebRequest();

				if (req.result != UnityWebRequest.Result.Success)
				{
					result.error = $"요청 실패 {req.responseCode}: {url} ({req.error})";
					yield break;
				}

				if (!TryExtract(req.downloadHandler.data, result, out string error))
				{
					result.error = error;
					yield break;
				}
				result.ok = true;
			}
		}

		/// <summary>이미 받은 zip bytes에서 manifest/sequence/영상을 추출한다(다운로드 무관, 동기 재사용용).</summary>
		public static bool TryExtract(byte[] zipBytes, DhmgsZipResult result, out string error)
		{
			error = null;
			if (zipBytes == null || zipBytes.Length == 0)
			{
				error = "응답 본문이 비어 있음";
				return false;
			}

			try
			{
				using (MemoryStream ms = new MemoryStream(zipBytes))
				using (ZipArchive archive = new ZipArchive(ms, ZipArchiveMode.Read))
				{
					result.manifestJson = ReadEntryText(archive, "manifest.json");
					result.sequenceJson = ReadEntryText(archive, "sequence.json");
					result.videoMp4 = ReadEntryBytes(archive, "fitting-cut.mp4");

					if (result.manifestJson == null)
					{
						error = "zip에 manifest.json 없음";
						return false;
					}
					if (result.sequenceJson == null)
					{
						error = "zip에 sequence.json 없음";
						return false;
					}
					return true;
				}
			}
			catch (Exception e)
			{
				error = $"zip 해제 실패: {e.Message}";
				return false;
			}
		}

		private static ZipArchiveEntry FindEntry(ZipArchive archive, string fileName)
		{
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				// 루트 평탄을 가정하지만, 경로가 붙어도 파일명으로 매칭한다.
				if (string.Equals(Path.GetFileName(entry.FullName), fileName, StringComparison.OrdinalIgnoreCase))
				{
					return entry;
				}
			}
			return null;
		}

		private static string ReadEntryText(ZipArchive archive, string fileName)
		{
			ZipArchiveEntry entry = FindEntry(archive, fileName);
			if (entry == null)
			{
				return null;
			}
			using (Stream stream = entry.Open())
			using (StreamReader reader = new StreamReader(stream))
			{
				return reader.ReadToEnd();
			}
		}

		private static byte[] ReadEntryBytes(ZipArchive archive, string fileName)
		{
			ZipArchiveEntry entry = FindEntry(archive, fileName);
			if (entry == null)
			{
				return null;
			}
			using (Stream stream = entry.Open())
			using (MemoryStream ms = new MemoryStream())
			{
				stream.CopyTo(ms);
				return ms.ToArray();
			}
		}
	}
}
