using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	internal static class DhmgsProjectValidator
	{
		[MenuItem("DHMGS/Project/Validate")]
		private static void Validate()
		{
			bool valid = true;
			if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
			{
				valid = false;
				Debug.LogWarning("[DHMGS] The current HTTP endpoint requires Insecure HTTP to be allowed. Use DHMGS/Project/Allow Insecure HTTP when needed.");
			}

			if (AssetDatabase.FindAssets("t:TMP_Settings").Length == 0)
			{
				valid = false;
				Debug.LogWarning("[DHMGS] TMP Essential Resources are not imported. Import them only if the optional playback UI is used.");
			}

			if (valid)
			{
				Debug.Log("[DHMGS] Project validation passed.");
			}
		}

		[MenuItem("DHMGS/Project/Allow Insecure HTTP")]
		private static void AllowInsecureHttp()
		{
			PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
			AssetDatabase.SaveAssets();
			Debug.Log("[DHMGS] Insecure HTTP is now allowed for this project.");
		}
	}
}
