using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal sealed class DhmgsAssetWriteOptions
	{
		public string OutputRoot { get; set; } = DhmgsAssetWriter.DefaultOutputRoot;
		public bool BakeHumanoidClips { get; set; } = true;
		public CancellationToken CancellationToken { get; set; } = default;
	}

	internal sealed class DhmgsAssetWriteResult
	{
		public DhmgsMotionAsset Motion { get; internal set; }
		public string MotionAssetPath { get; internal set; }
		public AnimationClip HumanoidInPlaceClip { get; internal set; }
		public AnimationClip HumanoidRootMotionClip { get; internal set; }
		public string Summary { get; internal set; }
		public IReadOnlyList<string> CleanupWarnings { get; internal set; } = Array.Empty<string>();
	}

	internal sealed class DhmgsPreparedAssetWrite : IDisposable
	{
		private readonly DhmgsImportResult _import;
		private readonly string _outputRoot;
		private readonly string _assetId;
		private readonly bool _publishHumanoid;
		private bool _published;
		private bool _disposed;

		internal DhmgsPreparedAssetWrite(
			DhmgsImportResult import,
			string outputRoot,
			string assetId,
			AnimationClip humanoidInPlaceClip,
			AnimationClip humanoidRootMotionClip,
			bool publishHumanoid)
		{
			_import = import;
			_outputRoot = outputRoot;
			_assetId = assetId;
			HumanoidInPlaceClip = humanoidInPlaceClip;
			HumanoidRootMotionClip = humanoidRootMotionClip;
			_publishHumanoid = publishHumanoid;
		}

		internal AnimationClip HumanoidInPlaceClip { get; private set; }
		internal AnimationClip HumanoidRootMotionClip { get; private set; }

		internal DhmgsAssetWriteResult Publish()
		{
			if (_disposed)
			{
				throw new ObjectDisposedException(nameof(DhmgsPreparedAssetWrite));
			}
			if (_published)
			{
				throw new InvalidOperationException("The prepared DHMGS assets were already published.");
			}

			DhmgsAssetWriteResult result = DhmgsAssetWriter.PublishPrepared(
				_import,
				_outputRoot,
				_assetId,
				HumanoidInPlaceClip,
				HumanoidRootMotionClip,
				_publishHumanoid);
			_published = true;
			HumanoidInPlaceClip = result.HumanoidInPlaceClip;
			HumanoidRootMotionClip = result.HumanoidRootMotionClip;
			return result;
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			if (!_published)
			{
				DhmgsAssetWriter.DestroyTransientClip(HumanoidInPlaceClip);
				DhmgsAssetWriter.DestroyTransientClip(HumanoidRootMotionClip);
			}
		}
	}

	internal static class DhmgsAssetWriter
	{
		public const string DefaultOutputRoot = "Assets/Mnnh.Dhmgs/Content/Generated";

		private sealed class AssetBackup : IDisposable
		{
			public AssetBackup(UnityEngine.Object target, UnityEngine.Object snapshot)
			{
				Target = target;
				Snapshot = snapshot;
			}

			public UnityEngine.Object Target { get; }
			public UnityEngine.Object Snapshot { get; }

			public void Restore()
			{
				if (Target == null || Snapshot == null)
				{
					return;
				}
				EditorUtility.CopySerialized(Snapshot, Target);
				EditorUtility.SetDirty(Target);
				AssetDatabase.SaveAssetIfDirty(Target);
			}

			public void Dispose()
			{
				if (Snapshot != null)
				{
					UnityEngine.Object.DestroyImmediate(Snapshot);
				}
			}
		}

		private readonly struct MovedAsset
		{
			public MovedAsset(string originalPath, string temporaryPath)
			{
				OriginalPath = originalPath;
				TemporaryPath = temporaryPath;
			}

			public string OriginalPath { get; }
			public string TemporaryPath { get; }
		}

		internal static async Task<DhmgsAssetWriteResult> WriteAsync(
			DhmgsImportResult import,
			DhmgsAssetWriteOptions options,
			Action<DhmgsImportJobStage, float, string, bool> onProgress,
			CancellationToken cancellationToken)
		{
			using DhmgsPreparedAssetWrite prepared = await PrepareAsync(
				import,
				options,
				onProgress,
				cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			onProgress?.Invoke(DhmgsImportJobStage.Publishing, 0f, "Publishing assets (cancellation disabled)", false);
			DhmgsAssetWriteResult result = prepared.Publish();
			onProgress?.Invoke(DhmgsImportJobStage.Publishing, 1f, "Published build-safe assets", false);
			return result;
		}

		internal static async Task<DhmgsPreparedAssetWrite> PrepareAsync(
			DhmgsImportResult import,
			DhmgsAssetWriteOptions options,
			Action<DhmgsImportJobStage, float, string, bool> onProgress,
			CancellationToken cancellationToken)
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
			string assetId = CreateAssetId(import);

			AnimationClip humanoidInPlaceClip = null;
			AnimationClip humanoidRootMotionClip = null;
			try
			{
				if (options.BakeHumanoidClips)
				{
					onProgress?.Invoke(DhmgsImportJobStage.BakingHumanoid, 0f, "Baking Humanoid clips", true);
					DhmgsHumanoidBakeArtifacts humanoid =
						await DhmgsAnimationClipBaker.BakeHumanoidVariantsAsync(
							import.Motion,
							value => onProgress?.Invoke(
								DhmgsImportJobStage.BakingHumanoid,
								value,
								"Baking Humanoid clips",
								true),
							cancellationToken);
					if (humanoid == null || humanoid.InPlace == null || humanoid.RootMotion == null)
					{
						throw new InvalidOperationException(
							string.IsNullOrEmpty(humanoid?.Error)
								? "Humanoid baking did not produce both required clips."
								: humanoid.Error);
					}
					humanoidInPlaceClip = humanoid.InPlace;
					humanoidRootMotionClip = humanoid.RootMotion;
				}

				cancellationToken.ThrowIfCancellationRequested();
				onProgress?.Invoke(DhmgsImportJobStage.PreparingAssets, 1f, "Prepared all import artifacts", true);
				return new DhmgsPreparedAssetWrite(
					import,
					outputRoot,
					assetId,
					humanoidInPlaceClip,
					humanoidRootMotionClip,
					options.BakeHumanoidClips);
			}
			catch
			{
				DestroyTransientClip(humanoidInPlaceClip);
				DestroyTransientClip(humanoidRootMotionClip);
				throw;
			}
		}

		internal static DhmgsAssetWriteResult PublishPrepared(
			DhmgsImportResult import,
			string outputRoot,
			string assetId,
			AnimationClip preparedHumanoidInPlace,
			AnimationClip preparedHumanoidRootMotion,
			bool publishHumanoid)
		{
			string outputDirectory = $"{outputRoot}/{assetId}";
			string transactionDirectory =
				$"{outputRoot}/__DhmgsTransactions/{Guid.NewGuid():N}";
			var createdFolders = new List<string>();
			var transactionFolders = new List<string>();

			string motionPath = $"{outputDirectory}/{assetId}.asset";
			string humanoidInPlacePath = $"{outputDirectory}/{assetId}_humanoid_inplace.anim";
			string humanoidRootMotionPath = $"{outputDirectory}/{assetId}_humanoid_rootmotion.anim";

			var backups = new List<AssetBackup>();
			var createdPaths = new List<string>();
			var removedOutputs = new List<MovedAsset>();
			bool stateCommitted = false;
			try
			{
				EnsureAssetFolder(outputDirectory, createdFolders);
				StageRemoval(
					$"{outputDirectory}/manifest.json",
					transactionDirectory,
					transactionFolders,
					removedOutputs);
				StageRemoval(
					$"{outputDirectory}/sequence.json",
					transactionDirectory,
					transactionFolders,
					removedOutputs);
				if (!publishHumanoid)
				{
					StageRemoval(
						humanoidInPlacePath,
						transactionDirectory,
						transactionFolders,
						removedOutputs);
					StageRemoval(
						humanoidRootMotionPath,
						transactionDirectory,
						transactionFolders,
						removedOutputs);
				}

				DhmgsMotionAsset motion = PublishNativeAsset(
					import.Motion,
					motionPath,
					assetId,
					() => ScriptableObject.CreateInstance<DhmgsMotionAsset>(),
					backups,
					createdPaths);
				import.Motion = motion;

				AnimationClip humanoidInPlace = publishHumanoid
					? PublishNativeAsset(
						preparedHumanoidInPlace,
						humanoidInPlacePath,
						$"{assetId}_humanoid_inplace",
						() => new AnimationClip(),
						backups,
						createdPaths)
					: null;
				AnimationClip humanoidRootMotion = publishHumanoid
					? PublishNativeAsset(
						preparedHumanoidRootMotion,
						humanoidRootMotionPath,
						$"{assetId}_humanoid_rootmotion",
						() => new AnimationClip(),
						backups,
						createdPaths)
					: null;

				ValidatePublishedAsset(motionPath, motion);
				if (publishHumanoid)
				{
					ValidatePublishedAsset(humanoidInPlacePath, humanoidInPlace);
					ValidatePublishedAsset(humanoidRootMotionPath, humanoidRootMotion);
				}
				stateCommitted = true;

				var cleanupWarnings = new List<string>();
				foreach (MovedAsset removed in removedOutputs)
				{
					try
					{
						DeleteRetiredOutput(removed.TemporaryPath);
					}
					catch (Exception cleanupError)
					{
						cleanupWarnings.Add(cleanupError.Message);
					}
				}
				for (int index = transactionFolders.Count - 1; index >= 0; index--)
				{
					try
					{
						DeleteFolderIfEmpty(transactionFolders[index]);
					}
					catch (Exception cleanupError)
					{
						cleanupWarnings.Add(cleanupError.Message);
					}
				}

				string summary = $"[{import.SourceId}] Saved {motion.frameCount} frames @ {motion.fps:F2} fps\n{motionPath}";
				if (humanoidInPlace != null)
				{
					summary += $"\nHumanoid In-Place: {humanoidInPlacePath}";
					summary += $"\nHumanoid Root Motion: {humanoidRootMotionPath}";
				}

				return new DhmgsAssetWriteResult
				{
					Motion = motion,
					MotionAssetPath = motionPath,
					HumanoidInPlaceClip = humanoidInPlace,
					HumanoidRootMotionClip = humanoidRootMotion,
					Summary = summary,
					CleanupWarnings = cleanupWarnings.AsReadOnly(),
				};
			}
			catch (Exception publishError)
			{
				if (stateCommitted)
				{
					throw new InvalidOperationException(
						"DHMGS assets were published, but retired-output cleanup was incomplete. " +
						"The published canonical assets are valid; inspect the __DhmgsTransactions recovery folder and retry.",
						publishError);
				}
				var rollbackErrors = new List<Exception>();
				for (int index = backups.Count - 1; index >= 0; index--)
				{
					try
					{
						backups[index].Restore();
					}
					catch (Exception rollbackError)
					{
						rollbackErrors.Add(rollbackError);
					}
				}
				for (int index = createdPaths.Count - 1; index >= 0; index--)
				{
					try
					{
						string createdPath = createdPaths[index];
						if (!AssetDatabase.DeleteAsset(createdPath)
							&& AssetDatabase.LoadMainAssetAtPath(createdPath) != null)
						{
							throw new InvalidOperationException(
								$"Could not remove newly published asset '{createdPath}' during rollback.");
						}
					}
					catch (Exception rollbackError)
					{
						rollbackErrors.Add(rollbackError);
					}
				}
				for (int index = removedOutputs.Count - 1; index >= 0; index--)
				{
					try
					{
						RestoreMovedAsset(removedOutputs[index]);
					}
					catch (Exception rollbackError)
					{
						rollbackErrors.Add(rollbackError);
					}
				}
				for (int index = transactionFolders.Count - 1; index >= 0; index--)
				{
					try
					{
						DeleteFolderIfEmpty(transactionFolders[index]);
					}
					catch (Exception rollbackError)
					{
						rollbackErrors.Add(rollbackError);
					}
				}
				for (int index = createdFolders.Count - 1; index >= 0; index--)
				{
					try
					{
						DeleteFolderIfEmpty(createdFolders[index]);
					}
					catch (Exception rollbackError)
					{
						rollbackErrors.Add(rollbackError);
					}
				}

				if (rollbackErrors.Count == 0)
				{
					throw;
				}
				throw new AggregateException(
					"DHMGS asset publishing failed and rollback was incomplete.",
					new[] { publishError }.Concat(rollbackErrors));
			}
			finally
			{
				foreach (AssetBackup backup in backups)
				{
					backup.Dispose();
				}
			}
		}

		private static T PublishNativeAsset<T>(
			T source,
			string path,
			string name,
			Func<T> createSnapshot,
			ICollection<AssetBackup> backups,
			ICollection<string> createdPaths)
			where T : UnityEngine.Object
		{
			if (source == null)
			{
				throw new InvalidOperationException($"The prepared asset for '{path}' is missing.");
			}

			UnityEngine.Object occupied = AssetDatabase.LoadMainAssetAtPath(path);
			T existing = occupied as T;
			if (occupied != null && existing == null)
			{
				throw new InvalidOperationException(
					$"Cannot publish '{path}' because it is occupied by {occupied.GetType().Name}, not {typeof(T).Name}.");
			}
			if (existing == null)
			{
				source.name = name;
				createdPaths.Add(path);
				AssetDatabase.CreateAsset(source, path);
				AssetDatabase.SaveAssetIfDirty(source);
				return source;
			}

			T snapshot = createSnapshot();
			EditorUtility.CopySerialized(existing, snapshot);
			backups.Add(new AssetBackup(existing, snapshot));
			EditorUtility.CopySerialized(source, existing);
			existing.name = name;
			EditorUtility.SetDirty(existing);
			AssetDatabase.SaveAssetIfDirty(existing);
			if (!AssetDatabase.Contains(source))
			{
				UnityEngine.Object.DestroyImmediate(source);
			}
			return existing;
		}

		private static void ValidatePublishedAsset<T>(string path, T expected)
			where T : UnityEngine.Object
		{
			T loaded = AssetDatabase.LoadAssetAtPath<T>(path);
			if (loaded == null || loaded != expected)
			{
				throw new InvalidOperationException($"Published asset validation failed for '{path}'.");
			}
		}

		private static void StageRemoval(
			string path,
			string transactionDirectory,
			ICollection<string> transactionFolders,
			ICollection<MovedAsset> removedOutputs)
		{
			if (AssetDatabase.LoadMainAssetAtPath(path) == null)
			{
				return;
			}
			EnsureAssetFolder(transactionDirectory, transactionFolders);
			string temporaryPath = $"{transactionDirectory}/{Path.GetFileName(path)}";
			string error = AssetDatabase.MoveAsset(path, temporaryPath);
			if (!string.IsNullOrEmpty(error))
			{
				throw new InvalidOperationException($"Could not stage removal of '{path}': {error}");
			}
			removedOutputs.Add(new MovedAsset(path, temporaryPath));
		}

		private static void RestoreMovedAsset(MovedAsset moved)
		{
			if (AssetDatabase.LoadMainAssetAtPath(moved.TemporaryPath) == null)
			{
				if (AssetDatabase.LoadMainAssetAtPath(moved.OriginalPath) != null)
				{
					return;
				}
				throw new InvalidOperationException(
					$"Both the original and rollback copy are missing for '{moved.OriginalPath}'.");
			}
			string error = AssetDatabase.MoveAsset(moved.TemporaryPath, moved.OriginalPath);
			if (!string.IsNullOrEmpty(error))
			{
				throw new InvalidOperationException(
					$"Could not restore '{moved.OriginalPath}' during rollback: {error}");
			}
		}

		private static void DeleteRetiredOutput(string path)
		{
			if (AssetDatabase.LoadMainAssetAtPath(path) == null)
			{
				return;
			}
			if (!AssetDatabase.DeleteAsset(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
			{
				throw new IOException($"Could not delete retired output '{path}'.");
			}
		}

		private static void EnsureAssetFolder(string path)
		{
			EnsureAssetFolder(path, null);
		}

		private static void EnsureAssetFolder(string path, ICollection<string> createdFolders)
		{
			string[] parts = path.Split('/');
			string current = parts[0];
			for (int index = 1; index < parts.Length; index++)
			{
				string next = $"{current}/{parts[index]}";
				if (!AssetDatabase.IsValidFolder(next))
				{
					string guid = AssetDatabase.CreateFolder(current, parts[index]);
					if (string.IsNullOrEmpty(guid) || !AssetDatabase.IsValidFolder(next))
					{
						throw new IOException($"Could not create asset folder '{next}'.");
					}
					createdFolders?.Add(next);
				}
				current = next;
			}
		}

		private static void DeleteFolderIfEmpty(string assetPath)
		{
			if (!AssetDatabase.IsValidFolder(assetPath))
			{
				return;
			}
			string relative = assetPath.Substring("Assets".Length)
				.TrimStart('/', '\\')
				.Replace('/', Path.DirectorySeparatorChar);
			string absolute = Path.Combine(Application.dataPath, relative);
			if (Directory.Exists(absolute) && Directory.EnumerateFileSystemEntries(absolute).Any())
			{
				return;
			}
			if (!AssetDatabase.DeleteAsset(assetPath) && AssetDatabase.IsValidFolder(assetPath))
			{
				throw new IOException($"Could not remove empty rollback folder '{assetPath}'.");
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
				if (segment.Length == 0
					|| segment == "."
					|| segment == ".."
					|| segment.TrimEnd(' ', '.') != segment
					|| segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
					|| IsReservedWindowsPathSegment(segment))
				{
					throw new ArgumentException(
						"The output root contains a non-canonical or reserved asset-path segment.",
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

		internal static string CreateAssetId(DhmgsImportResult import)
		{
			if (import == null || string.IsNullOrWhiteSpace(import.SourceId))
			{
				throw new ArgumentException("The source ID must not be empty.", nameof(import));
			}
			if (import.SourceId == "." || import.SourceId == "..")
			{
				throw new ArgumentException("The source ID cannot be a dot or traversal segment.", nameof(import));
			}

			if (int.TryParse(
					import.SourceId,
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out int attemptId)
				&& attemptId >= 0
				&& import.SourceId == attemptId.ToString(CultureInfo.InvariantCulture))
			{
				return import.SourceId;
			}

			string sourceKey = string.IsNullOrEmpty(import.SourceKey)
				? DhmgsImportPipeline.CreateSourceKey(import.SourceId)
				: import.SourceKey;
			return sourceKey;
		}

		private static bool IsReservedWindowsPathSegment(string segment)
		{
			string stem = segment.Split('.')[0];
			if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
				|| stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
				|| stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
				|| stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			return stem.Length == 4
				&& stem[3] >= '1'
				&& stem[3] <= '9'
				&& (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
					|| stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
		}

		internal static void DestroyTransientClip(AnimationClip clip)
		{
			if (clip != null && !AssetDatabase.Contains(clip))
			{
				UnityEngine.Object.DestroyImmediate(clip);
			}
		}
	}
}
