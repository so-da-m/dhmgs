using System;

using Mnnh.Dhmgs.ThirdParty.SimpleJson;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal sealed class DhmgsManifestDto
	{
		public string SchemaVersion;
		public float Fps;
		public int FrameCount;

		public string[] BodyJointNames;
		public string[] LeftHandJointNames;
		public string[] RightHandJointNames;

		public float[] LeftHandOffset;
		public float[] RightHandOffset;

		public int ImageWidth;
		public int ImageHeight;
		public Vector2 FocalLength;
		public Vector2 PrincipalPoint;

		public static DhmgsManifestDto Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
			{
				throw new DhmgsImportException(DhmgsImportStage.Decoding, "manifest.json is empty.");
			}

			try
			{
				JSONNode root = JSON.Parse(json);
				if (root == null)
				{
					throw new FormatException("The manifest root is null.");
				}

				JSONNode frameRate = root["frame_rate"];
				float numerator = ReadFloat(frameRate, "numerator", "num");
				float denominator = ReadFloat(frameRate, "denominator", "den");

				DhmgsManifestDto manifest = new DhmgsManifestDto
				{
					SchemaVersion = root["schema_version"].Value,
					Fps = denominator > 0f ? numerator / denominator : 0f,
					FrameCount = root["frame_count"].AsInt,
				};

				if (manifest.Fps <= 0f)
				{
					throw new FormatException($"Invalid frame rate: {numerator}/{denominator}.");
				}
				if (manifest.FrameCount <= 0)
				{
					throw new FormatException($"Invalid frame_count: {manifest.FrameCount}.");
				}

				JSONNode jointNames = root["skeleton"]["joint_names"];
				manifest.BodyJointNames = ReadStringArray(jointNames["body"]);
				manifest.LeftHandJointNames = ReadStringArray(jointNames["left_hand"]);
				manifest.RightHandJointNames = ReadStringArray(jointNames["right_hand"]);
				RequireLength(manifest.BodyJointNames, 21, "body joint_names");
				RequireLength(manifest.LeftHandJointNames, 15, "left_hand joint_names");
				RequireLength(manifest.RightHandJointNames, 15, "right_hand joint_names");

				JSONNode handOffsets = root["skeleton"]["hand_pose_offset"];
				manifest.LeftHandOffset = ReadFloatArray(handOffsets["left"]);
				manifest.RightHandOffset = ReadFloatArray(handOffsets["right"]);
				RequireLength(manifest.LeftHandOffset, 45, "left hand_pose_offset");
				RequireLength(manifest.RightHandOffset, 45, "right hand_pose_offset");

				JSONNode projection = root["projection"];
				if (projection != null && projection.HasKey("image"))
				{
					manifest.ImageWidth = projection["image"]["width"].AsInt;
					manifest.ImageHeight = projection["image"]["height"].AsInt;
					manifest.FocalLength = new Vector2(
						projection["focal_length"]["x"].AsFloat,
						projection["focal_length"]["y"].AsFloat);
					manifest.PrincipalPoint = new Vector2(
						projection["principal_point"]["x"].AsFloat,
						projection["principal_point"]["y"].AsFloat);
				}

				return manifest;
			}
			catch (DhmgsImportException)
			{
				throw;
			}
			catch (Exception exception)
			{
				throw new DhmgsImportException(DhmgsImportStage.Decoding, "Failed to parse manifest.json.", exception);
			}
		}

		private static float ReadFloat(JSONNode node, string primaryKey, string fallbackKey)
		{
			if (node == null)
			{
				return 0f;
			}
			if (node.HasKey(primaryKey))
			{
				return node[primaryKey].AsFloat;
			}
			return node.HasKey(fallbackKey) ? node[fallbackKey].AsFloat : 0f;
		}

		private static string[] ReadStringArray(JSONNode node)
		{
			int count = node != null ? node.Count : 0;
			string[] values = new string[count];
			for (int index = 0; index < count; index++)
			{
				values[index] = node[index].Value;
			}
			return values;
		}

		private static float[] ReadFloatArray(JSONNode node)
		{
			int count = node != null ? node.Count : 0;
			float[] values = new float[count];
			for (int index = 0; index < count; index++)
			{
				values[index] = node[index].AsFloat;
			}
			return values;
		}

		private static void RequireLength(Array array, int expected, string label)
		{
			if (array.Length != expected)
			{
				throw new FormatException($"{label}: expected {expected}, got {array.Length}.");
			}
		}
	}
}
