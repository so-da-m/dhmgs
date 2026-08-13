using System;
using System.Threading;

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

		public static DhmgsSequenceDto Parse(
			string json,
			int expectedFrameCount,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.IsNullOrWhiteSpace(json))
			{
				throw new DhmgsImportException(DhmgsImportStage.Decoding, "sequence.json is empty.");
			}

			try
			{
				JSONNode root = JSON.Parse(json);
				cancellationToken.ThrowIfCancellationRequested();
				if (root == null || !root.IsObject)
				{
					throw new FormatException("The sequence root must be an object.");
				}
				if (!root.HasKey("frames") || !root["frames"].IsArray)
				{
					throw new FormatException("sequence.frames must be an array.");
				}

				JSONNode framesNode = root["frames"];
				int frameCount = framesNode.Count;
				if (frameCount == 0)
				{
					throw new FormatException("sequence.json contains no frames.");
				}
				if (frameCount > DhmgsManifestDto.MaxFrameCount)
				{
					throw new FormatException(
						$"sequence.json contains {frameCount} frames; the limit is {DhmgsManifestDto.MaxFrameCount}.");
				}
				if (expectedFrameCount > 0 && frameCount != expectedFrameCount)
				{
					throw new FormatException($"Frame count mismatch: sequence={frameCount}, manifest={expectedFrameCount}.");
				}

				SmplxFrameDto[] frames = new SmplxFrameDto[frameCount];
				for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					JSONNode node = framesNode[frameIndex];
					if (node == null || !node.IsObject)
					{
						throw new FormatException($"Frame {frameIndex} must be an object.");
					}

					frames[frameIndex] = new SmplxFrameDto
					{
						Root = RequireVector3(node, "root_pose", frameIndex, "root_pose"),
						Body = RequireVector3Array(
							node,
							"body_pose",
							21,
							frameIndex,
							"body_pose",
							cancellationToken),
						Jaw = RequireVector3(node, "jaw_pose", frameIndex, "jaw_pose"),
						LeftEye = RequireVector3(node, "left_eye_pose", frameIndex, "left_eye_pose"),
						RightEye = RequireVector3(node, "right_eye_pose", frameIndex, "right_eye_pose"),
						LeftHand = RequireVector3Array(
							node,
							"left_hand_pose",
							15,
							frameIndex,
							"left_hand_pose",
							cancellationToken),
						RightHand = RequireVector3Array(
							node,
							"right_hand_pose",
							15,
							frameIndex,
							"right_hand_pose",
							cancellationToken),
						Translation = RequireVector3(node, "translation", frameIndex, "translation"),
					};
				}

				cancellationToken.ThrowIfCancellationRequested();
				return new DhmgsSequenceDto { Frames = frames };
			}
			catch (OperationCanceledException)
			{
				throw;
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

		private static Vector3 RequireVector3(
			JSONNode parent,
			string key,
			int frameIndex,
			string label)
		{
			if (!parent.HasKey(key))
			{
				throw new FormatException($"Frame {frameIndex} {label} is required.");
			}
			return RequireVector3(parent[key], frameIndex, label);
		}

		private static Vector3 RequireVector3(JSONNode node, int frameIndex, string label)
		{
			if (node == null || !node.IsArray)
			{
				throw new FormatException($"Frame {frameIndex} {label} must be an array.");
			}
			if (node.Count != 3)
			{
				throw new FormatException($"Frame {frameIndex} {label}: expected [3], got [{node.Count}].");
			}

			return new Vector3(
				RequireFiniteFloat(node[0], frameIndex, $"{label}[0]"),
				RequireFiniteFloat(node[1], frameIndex, $"{label}[1]"),
				RequireFiniteFloat(node[2], frameIndex, $"{label}[2]"));
		}

		private static Vector3[] RequireVector3Array(
			JSONNode parent,
			string key,
			int expectedCount,
			int frameIndex,
			string label,
			CancellationToken cancellationToken)
		{
			if (!parent.HasKey(key))
			{
				throw new FormatException($"Frame {frameIndex} {label} is required.");
			}

			JSONNode node = parent[key];
			if (!node.IsArray)
			{
				throw new FormatException($"Frame {frameIndex} {label} must be an array.");
			}
			if (node.Count != expectedCount)
			{
				throw new FormatException(
					$"Frame {frameIndex} {label}: expected [{expectedCount}][3], got [{node.Count}].");
			}

			Vector3[] values = new Vector3[node.Count];
			for (int index = 0; index < node.Count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				values[index] = RequireVector3(node[index], frameIndex, $"{label}[{index}]");
			}
			return values;
		}

		private static float RequireFiniteFloat(JSONNode node, int frameIndex, string label)
		{
			if (node == null || !node.IsNumber)
			{
				throw new FormatException($"Frame {frameIndex} {label} must be a number.");
			}

			double value = node.AsDouble;
			if (double.IsNaN(value)
				|| double.IsInfinity(value)
				|| value < -float.MaxValue
				|| value > float.MaxValue)
			{
				throw new FormatException($"Frame {frameIndex} {label} must be a finite float.");
			}
			return (float)value;
		}
	}
}
