using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using NUnit.Framework;

using UnityEngine;

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
	}
}
