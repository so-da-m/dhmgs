#if UNITY_EDITOR
using System.IO;

using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// 다운로드/변환 결과(DhmgsBakeResult)를 프로젝트 자산으로 영구 베이크한다.
	/// Assets/Mnnh.Dhmgs/Content/Generated/{id}/ 에 SO + json 원본 + Generic(선택) + Humanoid 2종을 저장.
	/// 씬(DhmgsRuntimeUI)과 에디터 창(DhmgsWindow)이 공유한다(중복 제거). 에디터 전용.
	/// 영상(mp4)은 자산이 아니라 persistentDataPath라 여기 포함하지 않음(DhmgsBakeService.WriteVideo).
	/// </summary>
	public static class DhmgsAssetBaker
	{
		public const string RootRelative = "Assets/Mnnh.Dhmgs/Content/Generated";

		/// <summary>SO + json + 클립을 저장하고 SO 자산 경로를 돌려준다. summary는 상태 표시용.</summary>
		public static string Persist(DhmgsBakeResult result, int attemptId, GameObject genericModel, GameObject humanoidModel,
			bool bakeGeneric, bool bakeHumanoid, out AnimationClip humanoidClip, out string summary)
		{
			string relDir = $"{RootRelative}/{attemptId}";
			EnsureAssetFolder(relDir);

			string soPath = $"{relDir}/{attemptId}.asset";
			if (AssetDatabase.LoadAssetAtPath<DhmgsMotionAsset>(soPath) != null)
			{
				AssetDatabase.DeleteAsset(soPath);
			}
			AssetDatabase.CreateAsset(result.asset, soPath);

			string absDir = Path.Combine(Application.dataPath, relDir.Substring("Assets/".Length));
			File.WriteAllText(Path.Combine(absDir, "manifest.json"), result.manifestJson);
			File.WriteAllText(Path.Combine(absDir, "sequence.json"), result.sequenceJson);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			string clipNote = BakeClips(result.asset, relDir, attemptId, genericModel, humanoidModel, bakeGeneric, bakeHumanoid, out humanoidClip);
			summary = $"[{attemptId}] 저장: {result.asset.frameCount}프레임 @ {result.asset.fps:F2}fps\n{soPath}{clipNote}";
			return soPath;
		}

		private static string BakeClips(DhmgsMotionAsset asset, string relDir, int attemptId,
			GameObject genericModel, GameObject humanoidModel, bool bakeGeneric, bool bakeHumanoid, out AnimationClip humanoidClip)
		{
			humanoidClip = null;
			string note = string.Empty;

			if (bakeGeneric)
			{
				if (genericModel == null)
				{
					note += "\nGeneric 건너뜀: 모델 미지정";
				}
				else
				{
					AnimationClip clip = DhmgsClipBaker.BakeGeneric(asset, genericModel, out string error);
					note += SaveClip(clip, relDir, $"{attemptId}_generic", error, "Generic");
				}
			}
			if (bakeHumanoid)
			{
				if (humanoidModel == null)
				{
					note += "\nHumanoid 건너뜀: 모델 미지정";
				}
				else
				{
					if (!DhmgsClipBaker.BakeHumanoidVariants(asset, humanoidModel,
						out AnimationClip inPlaceClip, out AnimationClip rootMotionClip, out string error))
					{
						note += $"\nHumanoid 실패: {error}";
					}
					else
					{
						note += SaveClip(inPlaceClip, relDir, $"{attemptId}_humanoid_inplace", null, "Humanoid In-Place");
						note += SaveClip(rootMotionClip, relDir, $"{attemptId}_humanoid_rootmotion", null, "Humanoid Root Motion");
						humanoidClip = inPlaceClip;
					}
				}
			}
			return note;
		}

		private static string SaveClip(AnimationClip clip, string relDir, string baseName, string error, string label)
		{
			if (clip == null)
			{
				return $"\n{label} 실패: {error}";
			}
			string clipPath = $"{relDir}/{baseName}.anim";
			clip.name = baseName;
			if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null)
			{
				AssetDatabase.DeleteAsset(clipPath);
			}
			AssetDatabase.CreateAsset(clip, clipPath);
			AssetDatabase.SaveAssets();
			return $"\n{label} 완료: {clipPath}";
		}

		private static void EnsureAssetFolder(string relDir)
		{
			string[] parts = relDir.Split('/');
			string current = parts[0];
			for (int i = 1; i < parts.Length; i++)
			{
				string next = $"{current}/{parts[i]}";
				if (!AssetDatabase.IsValidFolder(next))
				{
					AssetDatabase.CreateFolder(current, parts[i]);
				}
				current = next;
			}
		}
	}
}
#endif
