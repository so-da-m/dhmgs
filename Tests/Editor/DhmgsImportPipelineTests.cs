using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

using NUnit.Framework;

using UnityEditor;

using UnityEngine;
using Object = UnityEngine.Object;

namespace Mnnh.Dhmgs.Editor.Tests
{
	public sealed class DhmgsImportPipelineTests
	{
		[Test]
		public void ImportArchive_DecodesCanonicalMotionAsset()
		{
			byte[] archive = CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1);
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
		public void AxisAngleConversion_PreservesEstablishedCoordinateContract()
		{
			Vector3 converted = DhmgsMotionDecoder.AxisAngleToUnity(new Vector3(1f, 2f, 3f));
			Assert.That(converted, Is.EqualTo(new Vector3(1f, -2f, -3f)));
		}

		[Test]
		public void AssetWriter_ExposesSavedGenericClip()
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
				DhmgsAssetWriteResult written = DhmgsAssetWriter.Write(
					import,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = outputRoot,
						GenericRig = genericRig,
						BakeGenericClip = true,
						BakeHumanoidClips = false,
					});

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
				Assert.Throws<OperationCanceledException>(() =>
					DhmgsAnimationClipBaker.BakeGeneric(
						import.Motion,
						genericRig,
						out _,
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
				Assert.Throws<OperationCanceledException>(() => DhmgsAssetWriter.Write(
					import,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = outputRoot,
						BakeGenericClip = false,
						BakeHumanoidClips = false,
						CancellationToken = cancellation.Token,
					}));
				Assert.That(AssetDatabase.IsValidFolder(outputRoot), Is.False);
			}
			finally
			{
				AssetDatabase.DeleteAsset(outputRoot);
				Object.DestroyImmediate(import.Motion);
			}
		}

		[Test]
		public void AssetWriter_StopsWhenProgressCancelsBeforeCreatingFolders()
		{
			string outputRoot = $"Assets/__DhmgsProgressCancelledWriter_{Guid.NewGuid():N}";
			DhmgsImportResult import = DhmgsImportPipeline.ImportArchive(
				CreateArchive(frameCountInManifest: 1, frameCountInSequence: 1),
				"progress-cancel-writer");

			try
			{
				using CancellationTokenSource cancellation = new CancellationTokenSource();
				Assert.Throws<OperationCanceledException>(() => DhmgsAssetWriter.Write(
					import,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = outputRoot,
						BakeGenericClip = false,
						BakeHumanoidClips = false,
						CancellationToken = cancellation.Token,
					},
					new CancelOnFirstProgress(cancellation)));
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
				Assert.Throws<ArgumentException>(() => DhmgsAssetWriter.Write(
					import,
					new DhmgsAssetWriteOptions
					{
						OutputRoot = outputRoot,
						BakeGenericClip = false,
						BakeHumanoidClips = false,
					}));
				Assert.That(AssetDatabase.IsValidFolder(outputRoot), Is.False);
			}
			finally
			{
				AssetDatabase.DeleteAsset(outputRoot);
				Object.DestroyImmediate(import.Motion);
			}
		}

		private static byte[] CreateArchive(int frameCountInManifest, int frameCountInSequence)
		{
			string[] bodyNames = Enumerable.Range(0, 21).Select(index => $"body_{index}").ToArray();
			string[] handNames = Enumerable.Range(0, 15).Select(index => $"finger_{index}").ToArray();
			string zeroOffsets = string.Join(",", Enumerable.Repeat("0", 45));
			string manifest = $@"{{
  ""schema_version"": ""5.0"",
  ""frame_rate"": {{ ""numerator"": 30, ""denominator"": 1 }},
  ""frame_count"": {frameCountInManifest},
  ""skeleton"": {{
    ""joint_names"": {{
      ""body"": [{Quoted(bodyNames)}],
      ""left_hand"": [{Quoted(handNames)}],
      ""right_hand"": [{Quoted(handNames)}]
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

		private sealed class CancelOnFirstProgress : IProgress<DhmgsImportProgress>
		{
			private readonly CancellationTokenSource _cancellation;

			public CancelOnFirstProgress(CancellationTokenSource cancellation)
			{
				_cancellation = cancellation;
			}

			public void Report(DhmgsImportProgress value)
			{
				_cancellation.Cancel();
			}
		}
	}
}
