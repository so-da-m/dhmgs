using System;

using SimpleJSON;

using UnityEngine;

namespace Mnnh.Dhmgs
{
	/// <summary>
	/// manifest.json (schema 5.x). 서버가 단일 진실원이므로 joint_names와 hand_pose_offset를 여기서 읽는다.
	/// SimpleJSON으로 파싱(중첩 객체/배열이라 JsonUtility 불가).
	/// </summary>
	public sealed class DhmgsManifest
	{
		public string schemaVersion;
		public float fps;
		public int frameCount;

		public string[] bodyJointNames;       // 21
		public string[] leftHandJointNames;   // 15
		public string[] rightHandJointNames;  // 15

		public float[] leftHandOffset;        // 45 (= 15 joints * 3), native space
		public float[] rightHandOffset;       // 45

		// projection (카메라 오버레이용, 픽셀 단위). 없으면 0 → 오버레이 비활성.
		public int imageWidth;
		public int imageHeight;
		public Vector2 focalLength;           // fx, fy
		public Vector2 principalPoint;        // cx, cy

		public static bool TryParse(string json, out DhmgsManifest manifest, out string error)
		{
			manifest = null;
			error = null;

			if (string.IsNullOrEmpty(json))
			{
				error = "manifest.json이 비어 있음";
				return false;
			}

			try
			{
				JSONNode root = JSON.Parse(json);
				if (root == null)
				{
					error = "manifest.json 파싱 실패(null)";
					return false;
				}

				DhmgsManifest m = new DhmgsManifest
				{
					schemaVersion = root["schema_version"].Value,
				};

				JSONNode rate = root["frame_rate"];
				float numerator = ReadFloat(rate, "numerator", "num");
				float denominator = ReadFloat(rate, "denominator", "den");
				m.fps = denominator > 0f ? numerator / denominator : 0f;

				m.frameCount = root["frame_count"].AsInt;
				if (m.frameCount <= 0)
				{
					error = $"frame_count가 유효하지 않음: {m.frameCount}";
					return false;
				}

				JSONNode jointNames = root["skeleton"]["joint_names"];
				m.bodyJointNames = ReadStringArray(jointNames["body"]);
				m.leftHandJointNames = ReadStringArray(jointNames["left_hand"]);
				m.rightHandJointNames = ReadStringArray(jointNames["right_hand"]);

				if (m.bodyJointNames.Length != 21
					|| m.leftHandJointNames.Length != 15
					|| m.rightHandJointNames.Length != 15)
				{
					error = $"joint_names 개수 불일치 (body={m.bodyJointNames.Length}, lhand={m.leftHandJointNames.Length}, rhand={m.rightHandJointNames.Length})";
					return false;
				}

				JSONNode handOffset = root["skeleton"]["hand_pose_offset"];
				m.leftHandOffset = ReadFloatArray(handOffset["left"]);
				m.rightHandOffset = ReadFloatArray(handOffset["right"]);

				if (m.leftHandOffset.Length != 45 || m.rightHandOffset.Length != 45)
				{
					error = $"hand_pose_offset 길이 불일치 (left={m.leftHandOffset.Length}, right={m.rightHandOffset.Length})";
					return false;
				}

				// projection (선택). 없으면 0으로 두고 오버레이 비활성.
				JSONNode projection = root["projection"];
				if (projection != null && projection.HasKey("image"))
				{
					JSONNode image = projection["image"];
					m.imageWidth = image["width"].AsInt;
					m.imageHeight = image["height"].AsInt;
					JSONNode focal = projection["focal_length"];
					m.focalLength = new Vector2(focal["x"].AsFloat, focal["y"].AsFloat);
					JSONNode princpt = projection["principal_point"];
					m.principalPoint = new Vector2(princpt["x"].AsFloat, princpt["y"].AsFloat);
				}

				manifest = m;
				return true;
			}
			catch (Exception e)
			{
				error = $"manifest 파싱 예외: {e.Message}";
				return false;
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
			if (node.HasKey(fallbackKey))
			{
				return node[fallbackKey].AsFloat;
			}
			return 0f;
		}

		private static string[] ReadStringArray(JSONNode node)
		{
			int count = node != null ? node.Count : 0;
			string[] result = new string[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = node[i].Value;
			}
			return result;
		}

		private static float[] ReadFloatArray(JSONNode node)
		{
			int count = node != null ? node.Count : 0;
			float[] result = new float[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = node[i].AsFloat;
			}
			return result;
		}
	}
}
