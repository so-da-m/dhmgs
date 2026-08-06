using System.Collections;
using System.IO;

using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>다운로드 + 파싱 + 변환 결과(메모리 SO). 자산 저장은 에디터 창이 담당.</summary>
	public sealed class DhmgsBakeResult
	{
		public bool ok;
		public string error;
		public DhmgsMotionAsset asset;   // 메모리 인스턴스
		public DhmgsManifest manifest;
		public byte[] videoMp4;          // null이면 영상 없음
		public string manifestJson;      // 원본 보관용
		public string sequenceJson;      // 원본 보관용
	}

	/// <summary>
	/// attempt_id → 다운로드 → SimpleJSON 파싱 → SmplxToUnity 변환 → 메모리 SO.
	/// 런타임 안전(AssetDatabase 미사용). 영상은 persistentDataPath에 파일로 떨군다(import 안 함, 경로 재생).
	/// </summary>
	public static class DhmgsBakeService
	{
		public const string DefaultBaseUrl = "http://192.168.1.128:8090";
		public const int DefaultTimeoutSec = 15;

		public static IEnumerator DownloadAndBuild(string baseUrl, int attemptId, int timeoutSec, DhmgsBakeResult result)
		{
			DhmgsZipResult zip = new DhmgsZipResult();
			yield return DhmgsClient.Download(baseUrl, attemptId, timeoutSec, zip);
			if (!zip.ok)
			{
				result.error = zip.error;
				yield break;
			}

			if (!DhmgsManifest.TryParse(zip.manifestJson, out DhmgsManifest manifest, out string manifestError))
			{
				result.error = manifestError;
				yield break;
			}
			if (!DhmgsSequence.TryParse(zip.sequenceJson, manifest.frameCount, out DhmgsSequence sequence, out string sequenceError))
			{
				result.error = sequenceError;
				yield break;
			}

			DhmgsMotionAsset asset = ScriptableObject.CreateInstance<DhmgsMotionAsset>();
			SmplxToUnity.Fill(asset, attemptId.ToString(), manifest, sequence);
			asset.name = $"dhmgs-{attemptId}";

			result.manifest = manifest;
			result.asset = asset;
			result.videoMp4 = zip.videoMp4;
			result.manifestJson = zip.manifestJson;
			result.sequenceJson = zip.sequenceJson;
			result.ok = true;
		}

		/// <summary>
		/// 이미 받은 zip bytes(예: dh-test POST 응답)에서 모션 SO를 만든다. 네트워크 없음(동기).
		/// DownloadAndBuild의 다운로드 이후 단계와 동일. id는 자산 이름/식별용(attempt_id 불필요).
		/// </summary>
		public static bool BuildFromZip(byte[] zipBytes, string id, DhmgsBakeResult result)
		{
			DhmgsZipResult zip = new DhmgsZipResult();
			if (!DhmgsClient.TryExtract(zipBytes, zip, out string extractError))
			{
				result.error = extractError;
				return false;
			}
			if (!DhmgsManifest.TryParse(zip.manifestJson, out DhmgsManifest manifest, out string manifestError))
			{
				result.error = manifestError;
				return false;
			}
			if (!DhmgsSequence.TryParse(zip.sequenceJson, manifest.frameCount, out DhmgsSequence sequence, out string sequenceError))
			{
				result.error = sequenceError;
				return false;
			}

			DhmgsMotionAsset asset = ScriptableObject.CreateInstance<DhmgsMotionAsset>();
			SmplxToUnity.Fill(asset, id, manifest, sequence);
			asset.name = $"dhmgs-{id}";

			result.manifest = manifest;
			result.asset = asset;
			result.videoMp4 = zip.videoMp4;
			result.manifestJson = zip.manifestJson;
			result.sequenceJson = zip.sequenceJson;
			result.ok = true;
			return true;
		}

		/// <summary>영상 mp4를 persistentDataPath/Dhmgs/{id}/video.mp4에 쓰고 경로 반환. mp4 없으면 null.</summary>
		public static string WriteVideo(int attemptId, byte[] mp4)
		{
			if (mp4 == null)
			{
				return null;
			}
			string dir = VideoDir(attemptId);
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, "video.mp4");
			File.WriteAllBytes(path, mp4);
			return path;
		}

		private static string VideoDir(int attemptId)
		{
			return Path.Combine(Application.persistentDataPath, "Dhmgs", attemptId.ToString());
		}
	}
}
