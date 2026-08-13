using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

using Mnnh.Dhmgs.ThirdParty.SimpleJson;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal sealed class DhmgsManifestDto
	{
		internal const int MaxFrameCount = 100000;

		private const int BodyJointCount = 21;
		private const int HandJointCount = 15;
		private const int HandOffsetCount = HandJointCount * 3;

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

		public static DhmgsManifestDto Parse(
			string json,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.IsNullOrWhiteSpace(json))
			{
				throw new DhmgsImportException(DhmgsImportStage.Decoding, "manifest.json is empty.");
			}

			try
			{
				JSONNode root = JSON.Parse(json);
				cancellationToken.ThrowIfCancellationRequested();
				if (root == null || !root.IsObject)
				{
					throw new FormatException("The manifest root must be an object.");
				}

				string schemaVersion = ReadRequiredString(root, "schema_version", "schema_version");
				ValidateSchemaVersion(schemaVersion);

				JSONNode frameRate = ReadRequiredObject(root, "frame_rate", "frame_rate");
				float numerator = ReadFloat(frameRate, "numerator", "num", "frame_rate numerator");
				float denominator = ReadFloat(frameRate, "denominator", "den", "frame_rate denominator");
				if (numerator <= 0f || denominator <= 0f)
				{
					throw new FormatException($"Invalid frame rate: {numerator}/{denominator}.");
				}

				float fps = numerator / denominator;
				RequireFinite(fps, "frame rate");
				if (fps <= 0f)
				{
					throw new FormatException($"Invalid frame rate: {numerator}/{denominator}.");
				}
				int frameCount = ReadRequiredInt(root, "frame_count", "frame_count");
				if (frameCount <= 0 || frameCount > MaxFrameCount)
				{
					throw new FormatException(
						$"Invalid frame_count: {frameCount}. Expected a value from 1 to {MaxFrameCount}.");
				}

				DhmgsManifestDto manifest = new DhmgsManifestDto
				{
					SchemaVersion = schemaVersion,
					Fps = fps,
					FrameCount = frameCount,
				};

				JSONNode skeleton = ReadRequiredObject(root, "skeleton", "skeleton");
				JSONNode jointNames = ReadRequiredObject(skeleton, "joint_names", "skeleton.joint_names");
				manifest.BodyJointNames = ReadStringArray(
					ReadRequiredArray(jointNames, "body", "skeleton.joint_names.body"),
					BodyJointCount,
					"body joint_names",
					cancellationToken);
				manifest.LeftHandJointNames = ReadStringArray(
					ReadRequiredArray(jointNames, "left_hand", "skeleton.joint_names.left_hand"),
					HandJointCount,
					"left_hand joint_names",
					cancellationToken);
				manifest.RightHandJointNames = ReadStringArray(
					ReadRequiredArray(jointNames, "right_hand", "skeleton.joint_names.right_hand"),
					HandJointCount,
					"right_hand joint_names",
					cancellationToken);
				ValidateJointNames(manifest, cancellationToken);

				JSONNode handOffsets = ReadRequiredObject(
					skeleton,
					"hand_pose_offset",
					"skeleton.hand_pose_offset");
				manifest.LeftHandOffset = ReadFloatArray(
					ReadRequiredArray(handOffsets, "left", "skeleton.hand_pose_offset.left"),
					HandOffsetCount,
					"left hand_pose_offset",
					cancellationToken);
				manifest.RightHandOffset = ReadFloatArray(
					ReadRequiredArray(handOffsets, "right", "skeleton.hand_pose_offset.right"),
					HandOffsetCount,
					"right hand_pose_offset",
					cancellationToken);

				if (root.HasKey("projection"))
				{
					ReadProjection(root, manifest);
				}

				cancellationToken.ThrowIfCancellationRequested();
				return manifest;
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
				throw new DhmgsImportException(DhmgsImportStage.Decoding, "Failed to parse manifest.json.", exception);
			}
		}

		private static void ValidateSchemaVersion(string schemaVersion)
		{
			string[] parts = schemaVersion.Split('.');
			if ((parts.Length != 2 && parts.Length != 3)
				|| !string.Equals(parts[0], "5", StringComparison.Ordinal)
				|| !string.Equals(parts[1], "0", StringComparison.Ordinal))
			{
				throw new FormatException(
					$"Unsupported schema_version '{schemaVersion}'. Expected 5.0 or 5.0.x.");
			}

			if (parts.Length == 3
				&& !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
			{
				throw new FormatException(
					$"Unsupported schema_version '{schemaVersion}'. Expected 5.0 or 5.0.x.");
			}
		}

		private static void ValidateJointNames(
			DhmgsManifestDto manifest,
			CancellationToken cancellationToken)
		{
			HashSet<string> names = new HashSet<string>(StringComparer.Ordinal)
			{
				"jaw",
				"left_eye_smplhf",
				"right_eye_smplhf",
			};
			AddUniqueNames(names, manifest.BodyJointNames, "body joint_names", cancellationToken);
			AddUniqueNames(names, manifest.LeftHandJointNames, "left_hand joint_names", cancellationToken);
			AddUniqueNames(names, manifest.RightHandJointNames, "right_hand joint_names", cancellationToken);
		}

		private static void AddUniqueNames(
			HashSet<string> names,
			string[] values,
			string label,
			CancellationToken cancellationToken)
		{
			for (int index = 0; index < values.Length; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!names.Add(values[index]))
				{
					throw new FormatException($"{label}[{index}] duplicates joint name '{values[index]}'.");
				}
			}
		}

		private static void ReadProjection(JSONNode root, DhmgsManifestDto manifest)
		{
			JSONNode projection = ReadRequiredObject(root, "projection", "projection");
			JSONNode image = ReadRequiredObject(projection, "image", "projection.image");
			manifest.ImageWidth = ReadRequiredInt(image, "width", "projection.image.width");
			manifest.ImageHeight = ReadRequiredInt(image, "height", "projection.image.height");
			if (manifest.ImageWidth <= 0 || manifest.ImageHeight <= 0)
			{
				throw new FormatException("projection image dimensions must be positive.");
			}

			JSONNode focalLength = ReadRequiredObject(projection, "focal_length", "projection.focal_length");
			JSONNode principalPoint = ReadRequiredObject(projection, "principal_point", "projection.principal_point");
			manifest.FocalLength = new Vector2(
				ReadRequiredFloat(focalLength, "x", "projection.focal_length.x"),
				ReadRequiredFloat(focalLength, "y", "projection.focal_length.y"));
			manifest.PrincipalPoint = new Vector2(
				ReadRequiredFloat(principalPoint, "x", "projection.principal_point.x"),
				ReadRequiredFloat(principalPoint, "y", "projection.principal_point.y"));
		}

		private static JSONNode ReadRequiredObject(JSONNode parent, string key, string label)
		{
			JSONNode node = ReadRequiredNode(parent, key, label);
			if (!node.IsObject)
			{
				throw new FormatException($"{label} must be an object.");
			}
			return node;
		}

		private static JSONNode ReadRequiredArray(JSONNode parent, string key, string label)
		{
			JSONNode node = ReadRequiredNode(parent, key, label);
			if (!node.IsArray)
			{
				throw new FormatException($"{label} must be an array.");
			}
			return node;
		}

		private static JSONNode ReadRequiredNode(JSONNode parent, string key, string label)
		{
			if (parent == null || !parent.IsObject || !parent.HasKey(key))
			{
				throw new FormatException($"{label} is required.");
			}
			return parent[key];
		}

		private static string ReadRequiredString(JSONNode parent, string key, string label)
		{
			JSONNode node = ReadRequiredNode(parent, key, label);
			if (!node.IsString || string.IsNullOrWhiteSpace(node.Value))
			{
				throw new FormatException($"{label} must be a non-empty string.");
			}
			return node.Value;
		}

		private static int ReadRequiredInt(JSONNode parent, string key, string label)
		{
			double value = ReadRequiredDouble(parent, key, label);
			if (value < int.MinValue || value > int.MaxValue || Math.Truncate(value) != value)
			{
				throw new FormatException($"{label} must be an integer.");
			}
			return (int)value;
		}

		private static float ReadFloat(
			JSONNode parent,
			string primaryKey,
			string fallbackKey,
			string label)
		{
			if (parent != null && parent.IsObject && parent.HasKey(primaryKey))
			{
				return ReadRequiredFloat(parent, primaryKey, label);
			}
			if (parent != null && parent.IsObject && parent.HasKey(fallbackKey))
			{
				return ReadRequiredFloat(parent, fallbackKey, label);
			}
			throw new FormatException($"{label} is required.");
		}

		private static float ReadRequiredFloat(JSONNode parent, string key, string label)
		{
			double value = ReadRequiredDouble(parent, key, label);
			if (value < -float.MaxValue || value > float.MaxValue)
			{
				throw new FormatException($"{label} is outside the supported float range.");
			}

			float result = (float)value;
			RequireFinite(result, label);
			return result;
		}

		private static double ReadRequiredDouble(JSONNode parent, string key, string label)
		{
			JSONNode node = ReadRequiredNode(parent, key, label);
			if (!node.IsNumber)
			{
				throw new FormatException($"{label} must be a number.");
			}

			double value = node.AsDouble;
			if (double.IsNaN(value) || double.IsInfinity(value))
			{
				throw new FormatException($"{label} must be finite.");
			}
			return value;
		}

		private static string[] ReadStringArray(
			JSONNode node,
			int expectedCount,
			string label,
			CancellationToken cancellationToken)
		{
			if (node.Count != expectedCount)
			{
				throw new FormatException($"{label}: expected {expectedCount}, got {node.Count}.");
			}

			string[] values = new string[node.Count];
			for (int index = 0; index < node.Count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				JSONNode valueNode = node[index];
				if (!valueNode.IsString || string.IsNullOrWhiteSpace(valueNode.Value))
				{
					throw new FormatException($"{label}[{index}] must be a non-empty string.");
				}
				if (!string.Equals(valueNode.Value, valueNode.Value.Trim(), StringComparison.Ordinal))
				{
					throw new FormatException($"{label}[{index}] must not have leading or trailing whitespace.");
				}
				values[index] = valueNode.Value;
			}
			return values;
		}

		private static float[] ReadFloatArray(
			JSONNode node,
			int expectedCount,
			string label,
			CancellationToken cancellationToken)
		{
			if (node.Count != expectedCount)
			{
				throw new FormatException($"{label}: expected {expectedCount}, got {node.Count}.");
			}

			float[] values = new float[node.Count];
			for (int index = 0; index < node.Count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				JSONNode valueNode = node[index];
				if (!valueNode.IsNumber)
				{
					throw new FormatException($"{label}[{index}] must be a number.");
				}

				double value = valueNode.AsDouble;
				if (double.IsNaN(value)
					|| double.IsInfinity(value)
					|| value < -float.MaxValue
					|| value > float.MaxValue)
				{
					throw new FormatException($"{label}[{index}] must be a finite float.");
				}
				values[index] = (float)value;
			}
			return values;
		}

		private static void RequireFinite(float value, string label)
		{
			if (float.IsNaN(value) || float.IsInfinity(value))
			{
				throw new FormatException($"{label} must be finite.");
			}
		}
	}
}
