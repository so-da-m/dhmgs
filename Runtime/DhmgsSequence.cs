using System;

using SimpleJSON;

using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>한 프레임의 raw SMPL-X 포즈(axis-angle rad, native 좌표). 변환 전 상태.</summary>
	public sealed class DhmgsRawFrame
	{
		public Vector3 root;
		public Vector3[] body;       // 21
		public Vector3 jaw;
		public Vector3 leftEye;
		public Vector3 rightEye;
		public Vector3[] leftHand;   // 15
		public Vector3[] rightHand;  // 15
		public Vector3 translation;
	}

	/// <summary>sequence.json의 frames[]를 raw 포즈 배열로 파싱한다. expression/source_frame_indices는 무시.</summary>
	public sealed class DhmgsSequence
	{
		public DhmgsRawFrame[] frames;

		public static bool TryParse(string json, int expectedFrameCount, out DhmgsSequence sequence, out string error)
		{
			sequence = null;
			error = null;

			if (string.IsNullOrEmpty(json))
			{
				error = "sequence.json이 비어 있음";
				return false;
			}

			try
			{
				JSONNode root = JSON.Parse(json);
				JSONNode framesNode = root["frames"];
				int frameCount = framesNode != null ? framesNode.Count : 0;
				if (frameCount == 0)
				{
					error = "sequence.json frames가 비어 있음";
					return false;
				}
				if (expectedFrameCount > 0 && frameCount != expectedFrameCount)
				{
					error = $"frames 개수({frameCount}) != manifest.frame_count({expectedFrameCount})";
					return false;
				}

				DhmgsRawFrame[] frames = new DhmgsRawFrame[frameCount];
				for (int f = 0; f < frameCount; f++)
				{
					JSONNode node = framesNode[f];
					DhmgsRawFrame frame = new DhmgsRawFrame
					{
						root = RequireVector3(node["root_pose"], f, "root_pose"),
						body = RequireVector3Array(node["body_pose"], 21, f, "body_pose"),
						jaw = RequireVector3(node["jaw_pose"], f, "jaw_pose"),
						leftEye = RequireVector3(node["left_eye_pose"], f, "left_eye_pose"),
						rightEye = RequireVector3(node["right_eye_pose"], f, "right_eye_pose"),
						leftHand = RequireVector3Array(node["left_hand_pose"], 15, f, "left_hand_pose"),
						rightHand = RequireVector3Array(node["right_hand_pose"], 15, f, "right_hand_pose"),
						translation = RequireVector3(node["translation"], f, "translation"),
					};

					frames[f] = frame;
				}

				sequence = new DhmgsSequence { frames = frames };
				return true;
			}
			catch (Exception e)
			{
				error = $"sequence 파싱 예외: {e.Message}";
				return false;
			}
		}

		private static Vector3 RequireVector3(JSONNode node, int frameIndex, string label)
		{
			int count = node != null ? node.Count : 0;
			if (count < 3)
			{
				throw new FormatException($"frame {frameIndex} {label}: axis-angle [3] 기대, 실제 {count}");
			}
			return new Vector3(node[0].AsFloat, node[1].AsFloat, node[2].AsFloat);
		}

		private static Vector3[] RequireVector3Array(JSONNode node, int expectedCount, int frameIndex, string label)
		{
			int count = node != null ? node.Count : 0;
			if (count != expectedCount)
			{
				throw new FormatException($"frame {frameIndex} {label}: [{expectedCount}][3] 기대, 실제 {count}");
			}
			Vector3[] result = new Vector3[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = RequireVector3(node[i], frameIndex, $"{label}[{i}]");
			}
			return result;
		}
	}
}
