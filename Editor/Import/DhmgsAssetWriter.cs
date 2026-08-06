using System;
using System.IO;
using System.Text;

using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	public sealed class DhmgsAssetWriteOptions
	{
		public string OutputRoot { get; set; } = DhmgsAssetWriter.DefaultOutputRoot;
		public GameObject GenericRig { get; set; }
		public GameObject HumanoidRig { get; set; }
		public bool BakeGenericClip { get; set; } = true;
		public bool BakeHumanoidClips { get; set; } = true;
	}

	public sealed class DhmgsAssetWriteResult
	{
		public string MotionAssetPath { get; internal set; }
		public AnimationClip HumanoidInPlaceClip { get; internal set; }
		public string Summary { get; internal set; }
	}

	public static class DhmgsAssetWriter
	{
		public const string DefaultOutputRoot = "Assets/Mnnh.Dhmgs/Content/Generated";

		public static DhmgsAssetWriteResult Write(
			DhmgsImportResult import,
			DhmgsAssetWriteOptions options,
			IProgress<DhmgsImportProgress> progress = null)
		{
			if (import?.Motion == null || !import.Motion.IsValid)
			{
				throw new ArgumentException("The import result has no valid motion.", nameof(import));
			}
			options ??= new DhmgsAssetWriteOptions();
			ValidateOutputRoot(options.OutputRoot);

			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.WritingAssets, 0f));
			string assetId = Sanitize(import.SourceId);
			string outputDirectory = $"{options.OutputRoot.TrimEnd('/')}/{assetId}";
			EnsureAssetFolder(outputDirectory);

			string motionPath = $"{outputDirectory}/{assetId}.asset";
			import.Motion = WriteMotion(import.Motion, motionPath);
			WriteSourceJson(outputDirectory, import.ManifestJson, import.SequenceJson);
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.WritingAssets, 0.35f));

			string notes = BakeClips(
				import.Motion,
				outputDirectory,
				assetId,
				options,
				out AnimationClip humanoidInPlaceClip);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.WritingAssets, 1f));

			return new DhmgsAssetWriteResult
			{
				MotionAssetPath = motionPath,
				HumanoidInPlaceClip = humanoidInPlaceClip,
				Summary = $"[{import.SourceId}] Saved {import.Motion.frameCount} frames @ {import.Motion.fps:F2} fps\n{motionPath}{notes}",
			};
		}

		private static DhmgsMotionAsset WriteMotion(DhmgsMotionAsset source, string path)
		{
			DhmgsMotionAsset existing = AssetDatabase.LoadAssetAtPath<DhmgsMotionAsset>(path);
			if (existing == null)
			{
				AssetDatabase.CreateAsset(source, path);
				return source;
			}

			EditorUtility.CopySerialized(source, existing);
			UnityEngine.Object.DestroyImmediate(source);
			EditorUtility.SetDirty(existing);
			return existing;
		}

		private static void WriteSourceJson(string outputDirectory, string manifestJson, string sequenceJson)
		{
			string absoluteDirectory = Path.Combine(
				Application.dataPath,
				outputDirectory.Substring("Assets/".Length));
			Encoding utf8WithoutBom = new UTF8Encoding(false);
			File.WriteAllText(Path.Combine(absoluteDirectory, "manifest.json"), manifestJson, utf8WithoutBom);
			File.WriteAllText(Path.Combine(absoluteDirectory, "sequence.json"), sequenceJson, utf8WithoutBom);
		}

		private static string BakeClips(
			DhmgsMotionAsset motion,
			string outputDirectory,
			string assetId,
			DhmgsAssetWriteOptions options,
			out AnimationClip humanoidInPlaceClip)
		{
			humanoidInPlaceClip = null;
			string notes = string.Empty;

			if (options.BakeGenericClip)
			{
				if (options.GenericRig == null)
				{
					notes += "\nGeneric skipped: no rig selected.";
				}
				else
				{
					AnimationClip clip = DhmgsAnimationClipBaker.BakeGeneric(motion, options.GenericRig, out string error);
					notes += SaveClip(clip, outputDirectory, $"{assetId}_generic", error, "Generic");
				}
			}

			if (options.BakeHumanoidClips)
			{
				if (options.HumanoidRig == null)
				{
					notes += "\nHumanoid skipped: no rig selected.";
				}
				else if (!DhmgsAnimationClipBaker.BakeHumanoidVariants(
					motion,
					options.HumanoidRig,
					out AnimationClip inPlace,
					out AnimationClip rootMotion,
					out string error))
				{
					notes += $"\nHumanoid failed: {error}";
				}
				else
				{
					humanoidInPlaceClip = SaveClipAsset(inPlace, outputDirectory, $"{assetId}_humanoid_inplace");
					SaveClipAsset(rootMotion, outputDirectory, $"{assetId}_humanoid_rootmotion");
					notes += $"\nHumanoid In-Place: {outputDirectory}/{assetId}_humanoid_inplace.anim";
					notes += $"\nHumanoid Root Motion: {outputDirectory}/{assetId}_humanoid_rootmotion.anim";
				}
			}

			return notes;
		}

		private static string SaveClip(AnimationClip clip, string directory, string name, string error, string label)
		{
			if (clip == null)
			{
				return $"\n{label} failed: {error}";
			}

			SaveClipAsset(clip, directory, name);
			return $"\n{label}: {directory}/{name}.anim";
		}

		private static AnimationClip SaveClipAsset(AnimationClip source, string directory, string name)
		{
			string path = $"{directory}/{name}.anim";
			source.name = name;
			AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
			if (existing == null)
			{
				AssetDatabase.CreateAsset(source, path);
				return source;
			}

			EditorUtility.CopySerialized(source, existing);
			UnityEngine.Object.DestroyImmediate(source);
			EditorUtility.SetDirty(existing);
			return existing;
		}

		private static void EnsureAssetFolder(string path)
		{
			string[] parts = path.Split('/');
			string current = parts[0];
			for (int index = 1; index < parts.Length; index++)
			{
				string next = $"{current}/{parts[index]}";
				if (!AssetDatabase.IsValidFolder(next))
				{
					AssetDatabase.CreateFolder(current, parts[index]);
				}
				current = next;
			}
		}

		private static void ValidateOutputRoot(string path)
		{
			if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
			{
				throw new ArgumentException("The output root must be inside Assets/.", nameof(path));
			}
		}

		private static string Sanitize(string value)
		{
			string sanitized = value;
			foreach (char invalid in Path.GetInvalidFileNameChars())
			{
				sanitized = sanitized.Replace(invalid, '_');
			}
			return sanitized;
		}
	}
}
