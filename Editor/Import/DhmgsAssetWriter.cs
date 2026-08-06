using System;
using System.IO;
using System.Text;
using System.Threading;

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
		public CancellationToken CancellationToken { get; set; } = default;
	}

	public sealed class DhmgsAssetWriteResult
	{
		public string MotionAssetPath { get; internal set; }
		public AnimationClip GenericClip { get; internal set; }
		public AnimationClip HumanoidInPlaceClip { get; internal set; }
		public string Summary { get; internal set; }
	}

	public static class DhmgsAssetWriter
	{
		public const string DefaultOutputRoot = "Assets/Mnnh.Dhmgs/Content/Generated";

		public static DhmgsAssetWriteResult Write(
			DhmgsImportResult import,
			DhmgsAssetWriteOptions options,
			IProgress<DhmgsImportProgress> progress = null,
			CancellationToken cancellationToken = default)
		{
			if (import?.Motion == null || !import.Motion.IsValid)
			{
				throw new ArgumentException("The import result has no valid motion.", nameof(import));
			}
			options ??= new DhmgsAssetWriteOptions();
			if (!cancellationToken.CanBeCanceled)
			{
				cancellationToken = options.CancellationToken;
			}
			cancellationToken.ThrowIfCancellationRequested();
			string outputRoot = ValidateOutputRoot(options.OutputRoot);

			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.WritingAssets, 0f));
			cancellationToken.ThrowIfCancellationRequested();
			string assetId = Sanitize(import.SourceId);
			ValidateAssetId(assetId);
			string outputDirectory = $"{outputRoot}/{assetId}";
			EnsureAssetFolder(outputDirectory);
			cancellationToken.ThrowIfCancellationRequested();

			string motionPath = $"{outputDirectory}/{assetId}.asset";
			import.Motion = WriteMotion(import.Motion, motionPath);
			cancellationToken.ThrowIfCancellationRequested();
			WriteSourceJson(
				outputDirectory,
				import.ManifestJson,
				import.SequenceJson,
				cancellationToken);
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.WritingAssets, 0.35f));
			cancellationToken.ThrowIfCancellationRequested();

			string notes = BakeClips(
				import.Motion,
				outputDirectory,
				assetId,
				options,
				out AnimationClip genericClip,
				out AnimationClip humanoidInPlaceClip,
				cancellationToken);

			cancellationToken.ThrowIfCancellationRequested();
			AssetDatabase.SaveAssets();
			cancellationToken.ThrowIfCancellationRequested();
			AssetDatabase.Refresh();
			progress?.Report(new DhmgsImportProgress(DhmgsImportStage.WritingAssets, 1f));

			return new DhmgsAssetWriteResult
			{
				MotionAssetPath = motionPath,
				GenericClip = genericClip,
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

		private static void WriteSourceJson(
			string outputDirectory,
			string manifestJson,
			string sequenceJson,
			CancellationToken cancellationToken)
		{
			string absoluteDirectory = Path.Combine(
				Application.dataPath,
				outputDirectory.Substring("Assets/".Length));
			Encoding utf8WithoutBom = new UTF8Encoding(false);
			cancellationToken.ThrowIfCancellationRequested();
			File.WriteAllText(Path.Combine(absoluteDirectory, "manifest.json"), manifestJson, utf8WithoutBom);
			cancellationToken.ThrowIfCancellationRequested();
			File.WriteAllText(Path.Combine(absoluteDirectory, "sequence.json"), sequenceJson, utf8WithoutBom);
			cancellationToken.ThrowIfCancellationRequested();
		}

		private static string BakeClips(
			DhmgsMotionAsset motion,
			string outputDirectory,
			string assetId,
			DhmgsAssetWriteOptions options,
			out AnimationClip genericClip,
			out AnimationClip humanoidInPlaceClip,
			CancellationToken cancellationToken)
		{
			genericClip = null;
			humanoidInPlaceClip = null;
			string notes = string.Empty;
			cancellationToken.ThrowIfCancellationRequested();

			if (options.BakeGenericClip)
			{
				if (options.GenericRig == null)
				{
					notes += "\nGeneric skipped: no rig selected.";
				}
				else
				{
					AnimationClip clip = null;
					try
					{
						clip = DhmgsAnimationClipBaker.BakeGeneric(
							motion,
							options.GenericRig,
							out string error,
							cancellationToken);
						if (clip == null)
						{
							notes += $"\nGeneric failed: {error}";
						}
						else
						{
							cancellationToken.ThrowIfCancellationRequested();
							genericClip = SaveClipAsset(clip, outputDirectory, $"{assetId}_generic");
							notes += $"\nGeneric: {outputDirectory}/{assetId}_generic.anim";
						}
					}
					catch
					{
						DestroyTransientClip(clip);
						throw;
					}
				}
			}

			cancellationToken.ThrowIfCancellationRequested();
			if (options.BakeHumanoidClips)
			{
				if (options.HumanoidRig == null)
				{
					notes += "\nHumanoid skipped: no rig selected.";
				}
				else
				{
					AnimationClip inPlace = null;
					AnimationClip rootMotion = null;
					try
					{
						if (!DhmgsAnimationClipBaker.BakeHumanoidVariants(
							motion,
							options.HumanoidRig,
							out inPlace,
							out rootMotion,
							out string error,
							cancellationToken))
						{
							notes += $"\nHumanoid failed: {error}";
						}
						else
						{
							cancellationToken.ThrowIfCancellationRequested();
							humanoidInPlaceClip = SaveClipAsset(
								inPlace,
								outputDirectory,
								$"{assetId}_humanoid_inplace");
							cancellationToken.ThrowIfCancellationRequested();
							SaveClipAsset(rootMotion, outputDirectory, $"{assetId}_humanoid_rootmotion");
							notes += $"\nHumanoid In-Place: {outputDirectory}/{assetId}_humanoid_inplace.anim";
							notes += $"\nHumanoid Root Motion: {outputDirectory}/{assetId}_humanoid_rootmotion.anim";
						}
					}
					catch
					{
						DestroyTransientClip(inPlace);
						DestroyTransientClip(rootMotion);
						throw;
					}
				}
			}

			return notes;
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

		internal static string ValidateOutputRoot(string path)
		{
			if (string.IsNullOrWhiteSpace(path)
				|| path.IndexOf('\\') >= 0
				|| !path.StartsWith("Assets/", StringComparison.Ordinal))
			{
				throw new ArgumentException("The output root must be inside Assets/.", nameof(path));
			}

			string normalized = path.TrimEnd('/');
			if (normalized.Length <= "Assets/".Length)
			{
				throw new ArgumentException("The output root must be a folder below Assets/.", nameof(path));
			}
			string[] segments = normalized.Split('/');
			foreach (string segment in segments)
			{
				if (segment.Length == 0 || segment == "." || segment == "..")
				{
					throw new ArgumentException(
						"The output root must use canonical asset-path segments without '.' or '..'.",
						nameof(path));
				}
			}

			string relative = normalized.Substring("Assets/".Length)
				.Replace('/', Path.DirectorySeparatorChar);
			string assetsDirectory = Path.GetFullPath(Application.dataPath)
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string absolute = Path.GetFullPath(Path.Combine(assetsDirectory, relative));
			string containmentPrefix = assetsDirectory + Path.DirectorySeparatorChar;
			if (!absolute.StartsWith(containmentPrefix, StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException("The output root must remain inside Assets/.", nameof(path));
			}
			return normalized;
		}

		private static string Sanitize(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				throw new ArgumentException("The source ID must not be empty.", nameof(value));
			}
			string sanitized = value;
			foreach (char invalid in Path.GetInvalidFileNameChars())
			{
				sanitized = sanitized.Replace(invalid, '_');
			}
			sanitized = sanitized.Replace('/', '_').Replace('\\', '_');
			return sanitized;
		}

		private static void ValidateAssetId(string value)
		{
			if (string.IsNullOrWhiteSpace(value)
				|| value == "."
				|| value == ".."
				|| value.IndexOf('/') >= 0
				|| value.IndexOf('\\') >= 0)
			{
				throw new ArgumentException("The source ID cannot be a dot or traversal segment.", nameof(value));
			}
		}

		private static void DestroyTransientClip(AnimationClip clip)
		{
			if (clip != null && !AssetDatabase.Contains(clip))
			{
				UnityEngine.Object.DestroyImmediate(clip);
			}
		}
	}
}
