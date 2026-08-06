using System;

using Mnnh.Dhmgs.ThirdParty.SimpleJson;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal sealed class SmplxFrameDto
	{
		public Vector3 Root;
		public Vector3[] Body;
		public Vector3 Jaw;
		public Vector3 LeftEye;
		public Vector3 RightEye;
		public Vector3[] LeftHand;
		public Vector3[] RightHand;
		public Vector3 Translation;
	}

	internal sealed class DhmgsSequenceDto
	{
		public SmplxFrameDto[] Frames;

		public static DhmgsSequenceDto Parse(string json, int expectedFrameCount)
		{
			if (string.IsNullOrWhiteSpace(json))
			{
				throw new DhmgsImportException(DhmgsImportStage.Decoding, "sequence.json is empty.");
			}

			try
			{
				JSONNode root = JSON.Parse(json);
				JSONNode framesNode = root?["frames"];
				int frameCount = framesNode != null ? framesNode.Count : 0;
				if (frameCount == 0)
				{
					throw new FormatException("sequence.json contains no frames.");
				}
				if (expectedFrameCount > 0 && frameCount != expectedFrameCount)
				{
					throw new FormatException($"Frame count mismatch: sequence={frameCount}, manifest={expectedFrameCount}.");
				}

				SmplxFrameDto[] frames = new SmplxFrameDto[frameCount];
				for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
				{
					JSONNode node = framesNode[frameIndex];
					frames[frameIndex] = new SmplxFrameDto
					{
						Root = RequireVector3(node["root_pose"], frameIndex, "root_pose"),
						Body = RequireVector3Array(node["body_pose"], 21, frameIndex, "body_pose"),
						Jaw = RequireVector3(node["jaw_pose"], frameIndex, "jaw_pose"),
						LeftEye = RequireVector3(node["left_eye_pose"], frameIndex, "left_eye_pose"),
						RightEye = RequireVector3(node["right_eye_pose"], frameIndex, "right_eye_pose"),
						LeftHand = RequireVector3Array(node["left_hand_pose"], 15, frameIndex, "left_hand_pose"),
						RightHand = RequireVector3Array(node["right_hand_pose"], 15, frameIndex, "right_hand_pose"),
						Translation = RequireVector3(node["translation"], frameIndex, "translation"),
					};
				}

				return new DhmgsSequenceDto { Frames = frames };
			}
			catch (DhmgsImportException)
			{
				throw;
			}
			catch (Exception exception)
			{
				throw new DhmgsImportException(DhmgsImportStage.Decoding, "Failed to parse sequence.json.", exception);
			}
		}

		private static Vector3 RequireVector3(JSONNode node, int frameIndex, string label)
		{
			int count = node != null ? node.Count : 0;
			if (count < 3)
			{
				throw new FormatException($"Frame {frameIndex} {label}: expected [3], got [{count}].");
			}
			return new Vector3(node[0].AsFloat, node[1].AsFloat, node[2].AsFloat);
		}

		private static Vector3[] RequireVector3Array(JSONNode node, int expectedCount, int frameIndex, string label)
		{
			int count = node != null ? node.Count : 0;
			if (count != expectedCount)
			{
				throw new FormatException($"Frame {frameIndex} {label}: expected [{expectedCount}][3], got [{count}].");
			}

			Vector3[] values = new Vector3[count];
			for (int index = 0; index < count; index++)
			{
				values[index] = RequireVector3(node[index], frameIndex, $"{label}[{index}]");
			}
			return values;
		}
	}
}
