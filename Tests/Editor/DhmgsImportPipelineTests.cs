using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor;

using UnityEngine;
using Object = UnityEngine.Object;

namespace Mnnh.Dhmgs.Editor.Tests
{
	public sealed class DhmgsImportPipelineTests
	{
		[Test]
		public async Task ImportService_EmbeddedArchiveUsesAnImmutableRequestAndExactCachePolicy()
		{
			string sourceId = $"service-{Guid.NewGuid():N}";
			string outputRoot = $"Assets/__DhmgsService_{Guid.NewGuid():N}";
			string cacheDirectory = GetProjectCacheDirectory(sourceId);
			byte[] archive = CreateArchive(
				frameCountInManifest: 2,
				frameCountInSequence: 2,
				includeVideo: true,
				videoBytes: Array.Empty<byte>());
			DhmgsImportJobRequest request = new DhmgsImportJobRequest(
				sourceId,
				archive,
				outputRoot,
				null,
				DhmgsBakeOutputs.None,
				DhmgsVideoPolicy.CacheIfPresent);
			Array.Clear(archive, 0, archive.Length);
			Directory.CreateDirectory(cacheDirectory);
			string staleVideoPath = Path.Combine(cacheDirectory, "video.mp4");
			File.WriteAllBytes(staleVideoPath, new byte[] { 1, 2, 3 });
			var progress = new List<DhmgsImportJobProgress>();
			DhmgsImportJobResult result = null;

			try
			{
				result = await DhmgsImportService.ImportAsync(request, progress.Add, CancellationToken.None);

				Assert.That(result.Motion, Is.Not.Null);
				Assert.That(result.Motion.frameCount, Is.EqualTo(2));
				Assert.That(AssetDatabase.Contains(result.Motion), Is.True);
				Assert.That(result.GenericClip, Is.Null);
				Assert.That(result.HumanoidInPlaceClip, Is.Null);
				Assert.That(result.HumanoidRootMotionClip, Is.Null);
				Assert.That(File.Exists(result.SourceManifestPath), Is.True);
				Assert.That(File.Exists(result.SourceSequencePath), Is.True);
				Assert.That(result.PreviewVideoPath, Is.Null);
				Assert.That(File.Exists(staleVideoPath), Is.False,
					"CacheIfPresent must remove a stale preview video when the new archive has no usable video bytes.");
				Assert.That(result.Diagnostics.Any(item => item.Code == "video.not-present"), Is.True);

				Assert.That(progress, Is.Not.Empty);
				Assert.That(progress.Select(item => item.JobId).Distinct().Single(), Is.EqualTo(result.JobId));
				Assert.That(progress.Select(item => item.Revision).Distinct().Count(), Is.EqualTo(1));
				Assert.That(progress.Last().Stage, Is.EqualTo(DhmgsImportJobStage.Completed));
				Assert.That(progress.Last().CanCancel, Is.False);
				Assert.That(progress.Zip(progress.Skip(1), (left, right) => right.OverallProgress >= left.OverallProgress)
					.All(value => value), Is.True, "Overall progress must be monotonic.");

				File.WriteAllBytes(staleVideoPath, new byte[] { 4, 5, 6 });
				DhmgsImportJobResult ignoredVideo = await DhmgsImportService.ImportAsync(
					new DhmgsImportJobRequest(
						sourceId,
						CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
						outputRoot,
						null,
						DhmgsBakeOutputs.None,
						DhmgsVideoPolicy.Ignore),
					null,
					CancellationToken.None);
				Assert.That(ignoredVideo.PreviewVideoPath, Is.Null);
				Assert.That(File.ReadAllBytes(staleVideoPath), Is.EqualTo(new byte[] { 4, 5, 6 }),
					"Ignore must not read, replace, or remove an existing preview-video cache entry.");
			}
			finally
			{
				AssetDatabase.DeleteAsset(outputRoot);
				if (Directory.Exists(cacheDirectory))
				{
					Directory.Delete(cacheDirectory, true);
				}
			}
		}

		[Test]
		public async Task ImportService_BakesRequestedGenericAndHumanoidOutputsEndToEnd()
		{
			string sourceId = $"service-bake-{Guid.NewGuid():N}";
			string outputRoot = $"Assets/__DhmgsServiceBake_{Guid.NewGuid():N}";
			string cacheDirectory = GetProjectCacheDirectory(sourceId);
			GameObject neutral = DhmgsImportService.LoadBundledSmplxBakeSource();
			try
			{
				DhmgsImportJobResult result = await DhmgsImportService.ImportAsync(
					new DhmgsImportJobRequest(
						sourceId,
						CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
						outputRoot,
						neutral,
						DhmgsBakeOutputs.Generic | DhmgsBakeOutputs.Humanoid,
						DhmgsVideoPolicy.Ignore),
					null,
					CancellationToken.None);

				Assert.That(result.Motion, Is.Not.Null);
				Assert.That(result.GenericClip, Is.Not.Null);
				Assert.That(result.HumanoidInPlaceClip, Is.Not.Null);
				Assert.That(result.HumanoidRootMotionClip, Is.Not.Null);
				Assert.That(AssetDatabase.Contains(result.Motion), Is.True);
				Assert.That(AssetDatabase.Contains(result.GenericClip), Is.True);
				Assert.That(AssetDatabase.Contains(result.HumanoidInPlaceClip), Is.True);
				Assert.That(AssetDatabase.Contains(result.HumanoidRootMotionClip), Is.True);
			}
			finally
			{
				AssetDatabase.DeleteAsset(outputRoot);
				if (Directory.Exists(cacheDirectory))
				{
					Directory.Delete(cacheDirectory, true);
				}
			}
		}

		[Test]
		public void ImportService_RestoresThePreviousCacheWhenAssetPublishFails()
		{
			string sourceId = $"rollback-service-{Guid.NewGuid():N}";
			string outputRoot = $"Assets/__DhmgsServiceRollback_{Guid.NewGuid():N}";
			string cacheDirectory = GetProjectCacheDirectory(sourceId);
			string manifestPath = Path.Combine(cacheDirectory, "manifest.json");
			string sequencePath = Path.Combine(cacheDirectory, "sequence.json");
			Directory.CreateDirectory(cacheDirectory);
			File.WriteAllText(manifestPath, "old-manifest", Encoding.UTF8);
			File.WriteAllText(sequencePath, "old-sequence", Encoding.UTF8);

			DhmgsImportJobRequest request = new DhmgsImportJobRequest(
				sourceId,
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				outputRoot,
				null,
				DhmgsBakeOutputs.None,
				DhmgsVideoPolicy.Ignore);
			DhmgsAssetWriter.PublishCheckpointForTests = checkpoint =>
			{
				if (checkpoint == "motion")
				{
					throw new IOException("Injected service publish failure.");
				}
			};

			try
			{
				Assert.ThrowsAsync<IOException>(async () =>
					await DhmgsImportService.ImportAsync(request, null, CancellationToken.None));
				Assert.That(File.ReadAllText(manifestPath, Encoding.UTF8), Is.EqualTo("old-manifest"));
				Assert.That(File.ReadAllText(sequencePath, Encoding.UTF8), Is.EqualTo("old-sequence"));
				Assert.That(AssetDatabase.IsValidFolder(outputRoot), Is.False);
				Assert.That(DhmgsImportService.IsBusy, Is.False);
			}
			finally
			{
				DhmgsAssetWriter.PublishCheckpointForTests = null;
				AssetDatabase.DeleteAsset(outputRoot);
				if (Directory.Exists(cacheDirectory))
				{
					Directory.Delete(cacheDirectory, true);
				}
			}
		}

		[Test]
		public void ImportService_ValidatesTheBundledSmplxBakeSourceAndRejectsYBot()
		{
			GameObject neutral = DhmgsImportService.LoadBundledSmplxBakeSource();
			GameObject yBot = AssetDatabase.LoadAssetAtPath<GameObject>(
				AssetDatabase.GUIDToAssetPath("121420fccd4c4bba93c676f768fdf0a5"));
			Assert.That(neutral, Is.Not.Null);
			Assert.That(yBot, Is.Not.Null);
			Avatar neutralAvatar = DhmgsBakeSourceValidator.ResolveHumanoidAvatar(neutral);
			Assert.That(neutralAvatar, Is.Not.Null);
			HumanBone[] neutralHumanBones = neutralAvatar.humanDescription.human;
			Assert.That(neutralHumanBones, Is.Not.Null.And.Not.Empty);
			string hipsHumanName = HumanTrait.BoneName[(int)HumanBodyBones.Hips];
			Assert.That(
				neutralHumanBones.Single(bone => bone.humanName == hipsHumanName).boneName,
				Is.EqualTo("pelvis"));

			DhmgsImportValidation valid = DhmgsImportService.Validate(new DhmgsImportJobRequest(
				"http://127.0.0.1:8090",
				1,
				"Assets/__DhmgsValidation",
				neutral,
				DhmgsBakeOutputs.Generic | DhmgsBakeOutputs.Humanoid));
			DhmgsImportValidation invalid = DhmgsImportService.Validate(new DhmgsImportJobRequest(
				"http://127.0.0.1:8090",
				1,
				"Assets/__DhmgsValidation",
				yBot,
				DhmgsBakeOutputs.Humanoid));
			DhmgsImportValidation noBake = DhmgsImportService.Validate(new DhmgsImportJobRequest(
				"http://127.0.0.1:8090",
				1,
				"Assets/__DhmgsValidation",
				null,
				DhmgsBakeOutputs.None));
			DhmgsImportValidation invalidEndpoint = DhmgsImportService.Validate(new DhmgsImportJobRequest(
				"http://127.0.0.1:8090?token=secret",
				1,
				"Assets/__DhmgsValidation",
				null,
				DhmgsBakeOutputs.None,
				(DhmgsVideoPolicy)999));
			DhmgsImportValidation invalidSourceId = DhmgsImportService.Validate(new DhmgsImportJobRequest(
				"..",
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"Assets/__DhmgsValidation",
				null,
				DhmgsBakeOutputs.None,
				DhmgsVideoPolicy.Ignore));

			Assert.That(valid.CanRun, Is.True, string.Join("\n", valid.Diagnostics.Select(item => item.Message)));
			Assert.That(invalid.CanRun, Is.False);
			Assert.That(invalid.Diagnostics.Any(item => item.Code == "bake.source.bones-missing"), Is.True);
			Assert.That(noBake.CanRun, Is.True, string.Join("\n", noBake.Diagnostics.Select(item => item.Message)));
			Assert.That(invalidEndpoint.CanRun, Is.False);
			Assert.That(invalidEndpoint.Diagnostics.Any(item => item.Code == "request.url.invalid"), Is.True);
			Assert.That(invalidEndpoint.Diagnostics.Any(item => item.Code == "request.video-policy.invalid"), Is.True);
			Assert.That(invalidSourceId.CanRun, Is.False);
			Assert.That(invalidSourceId.Diagnostics.Any(item => item.Code == "request.source-id.invalid"), Is.True);
		}

		[Test]
		public void ImportService_RejectsAnAvatarThatDoesNotDescribeTheSelectedSmplxHierarchy()
		{
			GameObject neutral = DhmgsImportService.LoadBundledSmplxBakeSource();
			GameObject yBot = AssetDatabase.LoadAssetAtPath<GameObject>(
				AssetDatabase.GUIDToAssetPath("121420fccd4c4bba93c676f768fdf0a5"));
			Avatar yBotAvatar = DhmgsBakeSourceValidator.ResolveHumanoidAvatar(yBot);
			string folderName = $"__DhmgsAvatarValidation_{Guid.NewGuid():N}";
			string folder = $"Assets/{folderName}";
			string prefabPath = $"{folder}/MismatchedAvatar.prefab";
			GameObject instance = null;
			try
			{
				Assert.That(neutral, Is.Not.Null);
				Assert.That(yBotAvatar, Is.Not.Null);
				Assert.That(AssetDatabase.CreateFolder("Assets", folderName), Is.Not.Empty);
				instance = PrefabUtility.InstantiatePrefab(neutral) as GameObject;
				Assert.That(instance, Is.Not.Null);
				Animator animator = instance.GetComponentInChildren<Animator>(true)
					?? instance.AddComponent<Animator>();
				animator.avatar = yBotAvatar;
				GameObject mismatched = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
				Assert.That(mismatched, Is.Not.Null);

				DhmgsImportValidation validation = DhmgsImportService.Validate(new DhmgsImportJobRequest(
					"http://127.0.0.1:8090",
					1,
					"Assets/__DhmgsValidation",
					mismatched,
					DhmgsBakeOutputs.Humanoid));

				Assert.That(validation.CanRun, Is.False);
				Assert.That(validation.Diagnostics.Any(item => item.Code == "bake.source.avatar-mismatch"), Is.True);
			}
			finally
			{
				if (instance != null)
				{
					Object.DestroyImmediate(instance);
				}
				AssetDatabase.DeleteAsset(folder);
			}
		}

		[Test]
		public async Task AssetWriterAsync_PreservesMotionGuidAndRemovesDisabledOutputs()
		{
			string outputRoot = $"Assets/__DhmgsTransactionalWriter_{Guid.NewGuid():N}";
			GameObject bakeSource = DhmgsImportService.LoadBundledSmplxBakeSource();
			DhmgsImportResult firstImport = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"transactional");
			DhmgsImportResult secondImport = null;
			try
			{
				DhmgsAssetWriteResult first = await DhmgsAssetWriter.WriteAsync(
					firstImport,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = outputRoot,
						GenericRig = bakeSource,
						BakeGenericClip = true,
						BakeHumanoidClips = false,
					},
					null,
					CancellationToken.None);
				string motionGuid = AssetDatabase.AssetPathToGUID(first.MotionAssetPath);
				string genericPath = AssetDatabase.GetAssetPath(first.GenericClip);
				Assert.That(motionGuid, Is.Not.Empty);
				Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(genericPath), Is.Not.Null);

				secondImport = DhmgsImportPipeline.ImportArchive(
					CreateArchive(frameCountInManifest: 2, frameCountInSequence: 2),
					"transactional");
				DhmgsAssetWriteResult second = await DhmgsAssetWriter.WriteAsync(
					secondImport,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = outputRoot,
						BakeGenericClip = false,
						BakeHumanoidClips = false,
					},
					null,
					CancellationToken.None);

				Assert.That(second.Motion.frameCount, Is.EqualTo(2));
				Assert.That(AssetDatabase.AssetPathToGUID(second.MotionAssetPath), Is.EqualTo(motionGuid));
				Assert.That(second.GenericClip, Is.Null);
				Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(genericPath), Is.Null,
					"Outputs disabled by the new request must not survive as stale canonical files.");
				string importFolder = Path.GetDirectoryName(second.MotionAssetPath)?.Replace('\\', '/');
				string[] remainingPaths = AssetDatabase.FindAssets(string.Empty, new[] { importFolder })
					.Select(AssetDatabase.GUIDToAssetPath)
					.ToArray();
				Assert.That(remainingPaths.Any(path => path.Contains("__DhmgsRetired_")), Is.False);
				Assert.That(remainingPaths.Any(path => path.EndsWith("manifest.json", StringComparison.Ordinal)), Is.False);
				Assert.That(remainingPaths.Any(path => path.EndsWith("sequence.json", StringComparison.Ordinal)), Is.False);
				Assert.That(AssetDatabase.IsValidFolder($"{outputRoot}/__DhmgsTransactions"), Is.False,
					"A successful publish must remove its transaction staging folders.");
			}
			finally
			{
				AssetDatabase.DeleteAsset(outputRoot);
				DestroyTransientMotion(firstImport);
				DestroyTransientMotion(secondImport);
			}
		}

		[Test]
		public void AssetWriterAsync_CancellationAtThePublishBoundaryLeavesAssetsUntouched()
		{
			string outputRoot = $"Assets/__DhmgsBoundaryCancel_{Guid.NewGuid():N}";
			DhmgsImportResult import = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"boundary-cancel");
			using CancellationTokenSource cancellation = new CancellationTokenSource();
			try
			{
				Assert.ThrowsAsync<OperationCanceledException>(async () =>
					await DhmgsAssetWriter.WriteAsync(
						import,
						new DhmgsAssetWriteOptions
						{
							OutputRoot = outputRoot,
							BakeGenericClip = false,
							BakeHumanoidClips = false,
						},
						(stage, _, _, _) =>
						{
							if (stage == DhmgsImportJobStage.PreparingAssets)
							{
								cancellation.Cancel();
							}
						},
						cancellation.Token));
				Assert.That(AssetDatabase.IsValidFolder(outputRoot), Is.False);
			}
			finally
			{
				AssetDatabase.DeleteAsset(outputRoot);
				DestroyTransientMotion(import);
			}
		}

		[Test]
		public async Task AssetWriterAsync_ReportsCleanupFailureOutsideTheCanonicalOutputSet()
		{
			string outputRoot = $"Assets/__DhmgsCleanupWriter_{Guid.NewGuid():N}";
			GameObject bakeSource = DhmgsImportService.LoadBundledSmplxBakeSource();
			DhmgsImportResult firstImport = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"cleanup");
			DhmgsImportResult secondImport = null;
			try
			{
				DhmgsAssetWriteResult first = await DhmgsAssetWriter.WriteAsync(
					firstImport,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = outputRoot,
						GenericRig = bakeSource,
						BakeGenericClip = true,
						BakeHumanoidClips = false,
					},
					null,
					CancellationToken.None);
				string genericPath = AssetDatabase.GetAssetPath(first.GenericClip);

				secondImport = DhmgsImportPipeline.ImportArchive(
					CreateArchive(frameCountInManifest: 2, frameCountInSequence: 2),
					"cleanup");
				DhmgsAssetWriter.PublishCheckpointForTests = checkpoint =>
				{
					if (checkpoint == "cleanup")
					{
						throw new IOException("Injected cleanup failure.");
					}
				};
				DhmgsAssetWriteResult second = await DhmgsAssetWriter.WriteAsync(
					secondImport,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = outputRoot,
						BakeGenericClip = false,
						BakeHumanoidClips = false,
					},
					null,
					CancellationToken.None);

				Assert.That(second.CleanupWarnings, Is.Not.Empty);
				Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(genericPath), Is.Null,
					"A cleanup recovery copy must never remain at the canonical output path.");
				string[] recoveryClips = AssetDatabase.FindAssets("t:AnimationClip", new[] { outputRoot })
					.Select(AssetDatabase.GUIDToAssetPath)
					.Where(path => path.Contains("/__DhmgsTransactions/"))
					.ToArray();
				Assert.That(recoveryClips, Is.Not.Empty,
					"A failed cleanup must retain the recoverable asset outside the canonical attempt folder.");
			}
			finally
			{
				DhmgsAssetWriter.PublishCheckpointForTests = null;
				AssetDatabase.DeleteAsset(outputRoot);
				DestroyTransientMotion(firstImport);
				DestroyTransientMotion(secondImport);
			}
		}

		[Test]
		public async Task AssetWriterAsync_RestoresExistingMotionAfterPublishFailure()
		{
			string outputRoot = $"Assets/__DhmgsRollbackWriter_{Guid.NewGuid():N}";
			DhmgsImportResult firstImport = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"rollback");
			DhmgsImportResult secondImport = null;
			try
			{
				DhmgsAssetWriteResult first = await DhmgsAssetWriter.WriteAsync(
					firstImport,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = outputRoot,
						BakeGenericClip = false,
						BakeHumanoidClips = false,
					},
					null,
					CancellationToken.None);
				string guid = AssetDatabase.AssetPathToGUID(first.MotionAssetPath);

				secondImport = DhmgsImportPipeline.ImportArchive(
					CreateArchive(frameCountInManifest: 2, frameCountInSequence: 2),
					"rollback");
				DhmgsAssetWriter.PublishCheckpointForTests = checkpoint =>
				{
					if (checkpoint == "motion")
					{
						throw new IOException("Injected publish failure.");
					}
				};

				Assert.ThrowsAsync<IOException>(async () =>
					await DhmgsAssetWriter.WriteAsync(
						secondImport,
						new DhmgsAssetWriteOptions
						{
							OutputRoot = outputRoot,
							BakeGenericClip = false,
							BakeHumanoidClips = false,
						},
						null,
						CancellationToken.None));

				DhmgsMotionAsset restored = AssetDatabase.LoadAssetAtPath<DhmgsMotionAsset>(first.MotionAssetPath);
				Assert.That(restored, Is.Not.Null);
				Assert.That(restored.frameCount, Is.EqualTo(1));
				Assert.That(AssetDatabase.AssetPathToGUID(first.MotionAssetPath), Is.EqualTo(guid));
			}
			finally
			{
				DhmgsAssetWriter.PublishCheckpointForTests = null;
				AssetDatabase.DeleteAsset(outputRoot);
				DestroyTransientMotion(firstImport);
				DestroyTransientMotion(secondImport);
			}
		}

		[Test]
		public void AssetWriterAsync_RejectsAnOccupiedCanonicalPathWithoutDeletingIt()
		{
			string folderName = $"__DhmgsWrongType_{Guid.NewGuid():N}";
			string outputRoot = $"Assets/{folderName}";
			const string sourceId = "wrong-type";
			string outputDirectory = $"{outputRoot}/{sourceId}";
			string motionPath = $"{outputDirectory}/{sourceId}.asset";
			AssetDatabase.CreateFolder("Assets", folderName);
			AssetDatabase.CreateFolder(outputRoot, sourceId);
			TextAsset sentinel = new TextAsset("sentinel");
			AssetDatabase.CreateAsset(sentinel, motionPath);
			string sentinelGuid = AssetDatabase.AssetPathToGUID(motionPath);
			DhmgsImportResult import = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				sourceId);

			try
			{
				Assert.ThrowsAsync<InvalidOperationException>(async () =>
					await DhmgsAssetWriter.WriteAsync(
						import,
						new DhmgsAssetWriteOptions
						{
							OutputRoot = outputRoot,
							BakeGenericClip = false,
							BakeHumanoidClips = false,
						},
						null,
						CancellationToken.None));
				Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(motionPath), Is.SameAs(sentinel));
				Assert.That(AssetDatabase.AssetPathToGUID(motionPath), Is.EqualTo(sentinelGuid));
			}
			finally
			{
				AssetDatabase.DeleteAsset(outputRoot);
				DestroyTransientMotion(import);
			}
		}

		[Test]
		public void ImportArchive_DecodesCanonicalMotionAsset()
		{
			byte[] archive = CreateArchive(
				frameCountInManifest: 1,
				frameCountInSequence: 1,
				identityJson: "{\"subject\":\"internal-test\"}");
			DhmgsImportResult result = DhmgsImportPipeline.ImportArchive(archive, "golden");

			try
			{
				Assert.That(result.Motion.IsValid, Is.True);
				Assert.That(result.Motion.attemptId, Is.EqualTo("golden"));
				Assert.That(result.Motion.fps, Is.EqualTo(30f));
				Assert.That(result.Motion.frameCount, Is.EqualTo(1));
				Assert.That(result.Motion.boneCount, Is.EqualTo(54));
				Assert.That(result.Motion.pelvisPositions[0], Is.EqualTo(new Vector3(1f, -2f, 3f)));
				Assert.That(Quaternion.Angle(result.Motion.pelvisRotations[0], Quaternion.Euler(180f, 180f, 0f)), Is.LessThan(0.001f));
				Assert.That(result.VideoBytes, Is.Null);
			}
			finally
			{
				Object.DestroyImmediate(result.Motion);
			}
		}

		[Test]
		public void ImportArchive_RejectsFrameCountMismatch()
		{
			byte[] archive = CreateArchive(frameCountInManifest: 2, frameCountInSequence: 1);
			DhmgsImportException exception = Assert.Throws<DhmgsImportException>(
				() => DhmgsImportPipeline.ImportArchive(archive, "mismatch"));

			Assert.That(exception.Stage, Is.EqualTo(DhmgsImportStage.Decoding));
		}

		[Test]
		public void ImportArchive_RejectsUnsupportedSchemaVersion()
		{
			byte[] archive = CreateArchive(
				frameCountInManifest: 1,
				frameCountInSequence: 1,
				schemaVersion: "6.0");
			DhmgsImportException exception = Assert.Throws<DhmgsImportException>(
				() => DhmgsImportPipeline.ImportArchive(archive, "unsupported-schema"));
			Assert.That(exception.Stage, Is.EqualTo(DhmgsImportStage.Decoding));
		}

		[Test]
		public void ImportArchive_RejectsNestedOrUnexpectedEntries()
		{
			using MemoryStream stream = new MemoryStream();
			using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
			{
				WriteEntry(archive, "nested/manifest.json", "{}");
				WriteEntry(archive, "sequence.json", "{}");
			}

			DhmgsImportException exception = Assert.Throws<DhmgsImportException>(
				() => DhmgsImportPipeline.ImportArchive(stream.ToArray(), "nested-entry"));
			Assert.That(exception.Stage, Is.EqualTo(DhmgsImportStage.Extracting));
		}

		[TestCase("unknown.json")]
		[TestCase("Identity.json")]
		[TestCase("nested/identity.json")]
		public void ImportArchive_RejectsUnknownOrNonCanonicalMetadataWhileAllowingIdentityJson(
			string unexpectedEntryName)
		{
			byte[] archive = CreateArchive(
				frameCountInManifest: 1,
				frameCountInSequence: 1,
				identityJson: "{}",
				unexpectedEntryName: unexpectedEntryName);
			DhmgsImportException exception = Assert.Throws<DhmgsImportException>(
				() => DhmgsImportPipeline.ImportArchive(archive, "unexpected-root-entry"));
			Assert.That(exception.Stage, Is.EqualTo(DhmgsImportStage.Extracting));
			Assert.That(exception.Message, Does.Contain(unexpectedEntryName));
		}

		[Test]
		public void ImportArchive_RejectsDuplicateIdentityMetadata()
		{
			byte[] archive = CreateArchive(
				frameCountInManifest: 1,
				frameCountInSequence: 1,
				identityJson: "{}",
				duplicateIdentity: true);
			DhmgsImportException exception = Assert.Throws<DhmgsImportException>(
				() => DhmgsImportPipeline.ImportArchive(archive, "duplicate-identity"));
			Assert.That(exception.Stage, Is.EqualTo(DhmgsImportStage.Extracting));
			Assert.That(exception.Message, Does.Contain("duplicated"));
		}

		[Test]
		public void ImportArchive_RejectsOversizedIdentityMetadata()
		{
			string oversized = new string('x', checked((int)DhmgsArchiveLimits.MaxIdentityBytes + 1));
			byte[] archive = CreateArchive(
				frameCountInManifest: 1,
				frameCountInSequence: 1,
				identityJson: oversized);
			DhmgsImportException exception = Assert.Throws<DhmgsImportException>(
				() => DhmgsImportPipeline.ImportArchive(archive, "oversized-identity"));
			Assert.That(exception.Stage, Is.EqualTo(DhmgsImportStage.Extracting));
			Assert.That(exception.Message, Does.Contain("identity.json"));
		}

		[Test]
		public void AxisAngleConversion_PreservesEstablishedCoordinateContract()
		{
			Vector3 converted = DhmgsMotionDecoder.AxisAngleToUnity(new Vector3(1f, 2f, 3f));
			Assert.That(converted, Is.EqualTo(new Vector3(1f, -2f, -3f)));
		}

		[Test]
		public async Task AssetWriter_ExposesSavedGenericClip()
		{
			const string genericRigGuid = "615fc734a2d080f4d954f707b0aaa53e";
			string outputRoot = $"Assets/__DhmgsWriterTests_{System.Guid.NewGuid():N}";
			DhmgsImportResult import = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 2, frameCountInSequence: 2),
				"writer");
			GameObject genericRig = AssetDatabase.LoadAssetAtPath<GameObject>(
				AssetDatabase.GUIDToAssetPath(genericRigGuid));

			try
			{
				Assert.That(genericRig, Is.Not.Null, "The package Neutral SMPL-X rig is missing.");
				DhmgsAssetWriteResult written = await DhmgsAssetWriter.WriteAsync(
					import,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = outputRoot,
						GenericRig = genericRig,
						BakeGenericClip = true,
						BakeHumanoidClips = false,
					},
					null,
					CancellationToken.None);

				string expectedPath = $"{outputRoot}/writer/writer_generic.anim";
				Assert.That(written.GenericClip, Is.Not.Null);
				Assert.That(import.Motion.name, Is.EqualTo("writer"));
				Assert.That(AssetDatabase.GetAssetPath(written.GenericClip), Is.EqualTo(expectedPath));
				Assert.That(
					AssetDatabase.LoadAssetAtPath<AnimationClip>(expectedPath),
					Is.SameAs(written.GenericClip));

				EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(written.GenericClip);
				Assert.That(bindings, Is.Not.Empty);
				Assert.That(bindings.Length % 4, Is.Zero);
				Assert.That(bindings.Length, Is.LessThanOrEqualTo((import.Motion.boneCount + 1) * 4));
				Assert.That(bindings.All(binding =>
					binding.type == typeof(Transform)
					&& binding.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)), Is.True);
				Assert.That(bindings.All(binding =>
					AnimationUtility.GetEditorCurve(written.GenericClip, binding).length == import.Motion.frameCount), Is.True);

				GameObject preview = Object.Instantiate(
					genericRig,
					new Vector3(1000f, 2f, -3f),
					Quaternion.identity);
				try
				{
					Vector3 rootPosition = preview.transform.position;
					written.GenericClip.SampleAnimation(preview, 0f);
					Assert.That(preview.transform.position, Is.EqualTo(rootPosition),
						"Generic clips must not contain root-position curves.");
					Transform pelvis = preview.GetComponentsInChildren<Transform>(true)
						.First(transform => transform.name == "pelvis");
					Assert.That(
						Quaternion.Angle(pelvis.localRotation, import.Motion.pelvisRotations[0]),
						Is.LessThan(0.001f));
				}
				finally
				{
					Object.DestroyImmediate(preview);
				}
			}
			finally
			{
				AssetDatabase.DeleteAsset(outputRoot);
				if (import.Motion != null && !AssetDatabase.Contains(import.Motion))
				{
					Object.DestroyImmediate(import.Motion);
				}
			}
		}

		[Test]
		public async Task AssetWriter_BakesHumanoidClipsFromRawBundledNeutralModel()
		{
			const string humanoidRigGuid = "615fc734a2d080f4d954f707b0aaa53e";
			string outputRoot = $"Assets/__DhmgsHumanoidWriterTests_{Guid.NewGuid():N}";
			DhmgsImportResult import = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 2, frameCountInSequence: 2),
				"humanoid-writer");
			string rigPath = AssetDatabase.GUIDToAssetPath(humanoidRigGuid);
			GameObject humanoidRig = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);

			try
			{
				Assert.That(humanoidRig, Is.Not.Null, "The bundled Neutral SMPL-X model is missing.");
				Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(rigPath)
					.OfType<Avatar>()
					.FirstOrDefault(candidate => candidate.isValid && candidate.isHuman);
				Assert.That(avatar, Is.Not.Null,
					"The bundled Neutral SMPL-X FBX must import as a valid Humanoid bake source.");

				DhmgsAssetWriteResult written = await DhmgsAssetWriter.WriteAsync(
					import,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = outputRoot,
						HumanoidRig = humanoidRig,
						BakeGenericClip = false,
						BakeHumanoidClips = true,
					},
					null,
					CancellationToken.None);

				string inPlacePath = $"{outputRoot}/humanoid-writer/humanoid-writer_humanoid_inplace.anim";
				string rootMotionPath = $"{outputRoot}/humanoid-writer/humanoid-writer_humanoid_rootmotion.anim";
				Assert.That(written.HumanoidInPlaceClip, Is.Not.Null, written.Summary);
				Assert.That(written.HumanoidRootMotionClip, Is.Not.Null, written.Summary);
				Assert.That(AssetDatabase.GetAssetPath(written.HumanoidInPlaceClip), Is.EqualTo(inPlacePath));
				Assert.That(AssetDatabase.GetAssetPath(written.HumanoidRootMotionClip), Is.EqualTo(rootMotionPath));
				Assert.That(AssetDatabase.LoadAssetAtPath<AnimationClip>(rootMotionPath),
					Is.SameAs(written.HumanoidRootMotionClip));

				EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(written.HumanoidInPlaceClip);
				Assert.That(bindings.Any(binding =>
					binding.type == typeof(Animator) && binding.propertyName == "RootT.y"), Is.True);
				Assert.That(bindings.Any(binding =>
					binding.type == typeof(Animator) && binding.propertyName.Contains("Spine")), Is.True);
			}
			finally
			{
				AssetDatabase.DeleteAsset(outputRoot);
				if (import.Motion != null && !AssetDatabase.Contains(import.Motion))
				{
					Object.DestroyImmediate(import.Motion);
				}
			}
		}

		[Test]
		public void AnimationClipBaker_RejectsPreCancelledWork()
		{
			const string genericRigGuid = "615fc734a2d080f4d954f707b0aaa53e";
			DhmgsImportResult import = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 2, frameCountInSequence: 2),
				"cancel-bake");
			GameObject genericRig = AssetDatabase.LoadAssetAtPath<GameObject>(
				AssetDatabase.GUIDToAssetPath(genericRigGuid));

			try
			{
				Assert.That(genericRig, Is.Not.Null, "The package Neutral SMPL-X rig is missing.");
				using CancellationTokenSource cancellation = new CancellationTokenSource();
				cancellation.Cancel();
				Assert.ThrowsAsync<OperationCanceledException>(async () =>
					await DhmgsAnimationClipBaker.BakeGenericAsync(
						import.Motion,
						genericRig,
						null,
						cancellation.Token));
			}
			finally
			{
				Object.DestroyImmediate(import.Motion);
			}
		}

		[Test]
		public void AssetWriter_RejectsPreCancelledWorkBeforeCreatingFolders()
		{
			string outputRoot = $"Assets/__DhmgsCancelledWriter_{Guid.NewGuid():N}";
			DhmgsImportResult import = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"cancel-writer");

			try
			{
				using CancellationTokenSource cancellation = new CancellationTokenSource();
				cancellation.Cancel();
				Assert.ThrowsAsync<OperationCanceledException>(async () =>
					await DhmgsAssetWriter.WriteAsync(
						import,
						new DhmgsAssetWriteOptions
						{
							OutputRoot = outputRoot,
							BakeGenericClip = false,
							BakeHumanoidClips = false,
							CancellationToken = cancellation.Token,
						},
						null,
						cancellation.Token));
				Assert.That(AssetDatabase.IsValidFolder(outputRoot), Is.False);
			}
			finally
			{
				AssetDatabase.DeleteAsset(outputRoot);
				Object.DestroyImmediate(import.Motion);
			}
		}

		[TestCase("Assets/")]
		[TestCase("Assets/../__DhmgsTraversal")]
		[TestCase("Assets/./__DhmgsTraversal")]
		[TestCase("Assets/Safe/../../__DhmgsTraversal")]
		[TestCase("Assets/Safe//__DhmgsTraversal")]
		[TestCase("Assets\\Safe\\..\\__DhmgsTraversal")]
		[TestCase("Assets/CON/Dhmgs")]
		[TestCase("Assets/AUX.txt/Dhmgs")]
		[TestCase("Assets/COM1/Dhmgs")]
		[TestCase("Assets/TrailingDot./Dhmgs")]
		[TestCase("Assets/TrailingSpace /Dhmgs")]
		public void AssetWriter_RejectsNonCanonicalOutputRoot(string outputRoot)
		{
			Assert.Throws<ArgumentException>(() => DhmgsAssetWriter.ValidateOutputRoot(outputRoot));
		}

		[TestCase(".")]
		[TestCase("..")]
		public void AssetWriter_RejectsDotSourceId(string sourceId)
		{
			string outputRoot = $"Assets/__DhmgsSafeWriter_{Guid.NewGuid():N}";
			DhmgsImportResult import = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				sourceId);

			try
			{
				Assert.ThrowsAsync<ArgumentException>(async () =>
					await DhmgsAssetWriter.WriteAsync(
						import,
						new DhmgsAssetWriteOptions
						{
							OutputRoot = outputRoot,
							BakeGenericClip = false,
							BakeHumanoidClips = false,
						},
						null,
						CancellationToken.None));
				Assert.That(AssetDatabase.IsValidFolder(outputRoot), Is.False);
			}
			finally
			{
				AssetDatabase.DeleteAsset(outputRoot);
				Object.DestroyImmediate(import.Motion);
			}
		}

		[Test]
		public void AssetWriter_UsesTheSourceHashWhenReadableIdsWouldCollide()
		{
			DhmgsImportResult slash = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"same/name");
			DhmgsImportResult backslash = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"same\\name");
			DhmgsImportResult lower = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"same");
			DhmgsImportResult upper = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"SAME");
			DhmgsImportResult reserved = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"__DhmgsTransactions");
			try
			{
				string slashId = DhmgsAssetWriter.CreateAssetId(slash);
				string backslashId = DhmgsAssetWriter.CreateAssetId(backslash);
				string lowerId = DhmgsAssetWriter.CreateAssetId(lower);
				string upperId = DhmgsAssetWriter.CreateAssetId(upper);
				string reservedId = DhmgsAssetWriter.CreateAssetId(reserved);
				Assert.That(slashId, Does.StartWith("source-"));
				Assert.That(backslashId, Does.StartWith("source-"));
				Assert.That(slashId, Is.Not.EqualTo(backslashId));
				Assert.That(lowerId, Does.StartWith("source-"));
				Assert.That(upperId, Does.StartWith("source-"));
				Assert.That(lowerId, Is.Not.EqualTo(upperId));
				Assert.That(reservedId, Does.StartWith("source-"));
				Assert.That(reservedId, Is.Not.EqualTo("__DhmgsTransactions").IgnoreCase);
			}
			finally
			{
				DestroyTransientMotion(slash);
				DestroyTransientMotion(backslash);
				DestroyTransientMotion(lower);
				DestroyTransientMotion(upper);
				DestroyTransientMotion(reserved);
			}
		}

		private static byte[] CreateArchive(
			int frameCountInManifest,
			int frameCountInSequence,
			string schemaVersion = "5.0",
			bool includeVideo = false,
			byte[] videoBytes = null,
			string identityJson = null,
			string unexpectedEntryName = null,
			bool duplicateIdentity = false)
		{
			string[] bodyNames =
			{
				"left_hip", "right_hip", "spine1", "left_knee", "right_knee", "spine2",
				"left_ankle", "right_ankle", "spine3", "left_foot", "right_foot", "neck",
				"left_collar", "right_collar", "head", "left_shoulder", "right_shoulder",
				"left_elbow", "right_elbow", "left_wrist", "right_wrist",
			};
			string[] leftHandNames =
			{
				"left_index1", "left_index2", "left_index3",
				"left_middle1", "left_middle2", "left_middle3",
				"left_pinky1", "left_pinky2", "left_pinky3",
				"left_ring1", "left_ring2", "left_ring3",
				"left_thumb1", "left_thumb2", "left_thumb3",
			};
			string[] rightHandNames = leftHandNames
				.Select(name => name.Replace("left_", "right_"))
				.ToArray();
			string zeroOffsets = string.Join(",", Enumerable.Repeat("0", 45));
			string manifest = $@"{{
  ""schema_version"": ""{schemaVersion}"",
  ""frame_rate"": {{ ""numerator"": 30, ""denominator"": 1 }},
  ""frame_count"": {frameCountInManifest},
  ""skeleton"": {{
    ""joint_names"": {{
      ""body"": [{Quoted(bodyNames)}],
			  ""left_hand"": [{Quoted(leftHandNames)}],
			  ""right_hand"": [{Quoted(rightHandNames)}]
    }},
    ""hand_pose_offset"": {{
      ""left"": [{zeroOffsets}],
      ""right"": [{zeroOffsets}]
    }}
  }}
}}";

			string bodyPose = string.Join(",", Enumerable.Repeat("[0,0,0]", 21));
			string handPose = string.Join(",", Enumerable.Repeat("[0,0,0]", 15));
			string frame = $@"{{
  ""root_pose"": [0,0,0],
  ""body_pose"": [{bodyPose}],
  ""jaw_pose"": [0,0,0],
  ""left_eye_pose"": [0,0,0],
  ""right_eye_pose"": [0,0,0],
  ""left_hand_pose"": [{handPose}],
  ""right_hand_pose"": [{handPose}],
  ""translation"": [1,2,3]
}}";
			string sequence = $"{{\"frames\":[{string.Join(",", Enumerable.Repeat(frame, frameCountInSequence))}]}}";

			using MemoryStream stream = new MemoryStream();
			using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
			{
				WriteEntry(archive, "manifest.json", manifest);
				WriteEntry(archive, "sequence.json", sequence);
				if (identityJson != null)
				{
					WriteEntry(archive, "identity.json", identityJson);
					if (duplicateIdentity)
					{
						WriteEntry(archive, "identity.json", identityJson);
					}
				}
				if (includeVideo)
				{
					ZipArchiveEntry video = archive.CreateEntry("fitting-cut.mp4");
					using Stream output = video.Open();
					byte[] bytes = videoBytes ?? Array.Empty<byte>();
					output.Write(bytes, 0, bytes.Length);
				}
				if (unexpectedEntryName != null)
				{
					WriteEntry(archive, unexpectedEntryName, "{}");
				}
			}
			return stream.ToArray();
		}

		private static string Quoted(string[] values)
		{
			return string.Join(",", values.Select(value => $"\"{value}\""));
		}

		private static void WriteEntry(ZipArchive archive, string name, string content)
		{
			ZipArchiveEntry entry = archive.CreateEntry(name);
			using StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
			writer.Write(content);
		}

		private static void DestroyTransientMotion(DhmgsImportResult import)
		{
			if (import?.Motion != null && !AssetDatabase.Contains(import.Motion))
			{
				Object.DestroyImmediate(import.Motion);
				import.Motion = null;
			}
		}

		private static string GetProjectCacheDirectory(string sourceId)
		{
			string projectRoot = Directory.GetParent(Path.GetFullPath(Application.dataPath))?.FullName;
			Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
			return Path.Combine(
				projectRoot,
				"Library",
				"Mnnh.Dhmgs",
				DhmgsImportPipeline.CreateSourceKey(sourceId));
		}

	}
}
