using System;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	[InitializeOnLoad]
	internal static class DhmgsProjectValidator
	{
		internal const string FallbackPackageVersion = "0.7.1";
		private const string AutoConfigurationKeyPrefix = "Mnnh.Dhmgs.AutoProjectConfiguration";

		static DhmgsProjectValidator()
		{
			if (!ShouldAutoConfigure(
				Application.isBatchMode,
				AssetDatabase.IsAssetImportWorkerProcess()))
			{
				return;
			}

			EditorApplication.delayCall += AutoConfigureAfterImport;
		}

		[MenuItem("DHMGS/Project/Validate")]
		private static void Validate()
		{
			ValidateProject(logSuccess: true);
		}

		internal static bool ValidateProject(bool logSuccess)
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

			if (valid && logSuccess)
			{
				Debug.Log("[DHMGS] Project validation passed.");
			}
			return valid;
		}

		[MenuItem("DHMGS/Project/Allow Insecure HTTP")]
		private static void AllowInsecureHttp()
		{
			bool changed = EnsureInsecureHttpAllowed();
			Debug.Log(changed
				? "[DHMGS] Insecure HTTP is now allowed for this project."
				: "[DHMGS] Insecure HTTP was already allowed for this project.");
		}

		private static void AutoConfigureAfterImport()
		{
			EditorApplication.delayCall -= AutoConfigureAfterImport;
			if (!ShouldAutoConfigure(
				Application.isBatchMode,
				AssetDatabase.IsAssetImportWorkerProcess()))
			{
				return;
			}
			if (EditorApplication.isCompiling || EditorApplication.isUpdating)
			{
				EditorApplication.delayCall += AutoConfigureAfterImport;
				return;
			}

			try
			{
				string packageVersion = ResolvePackageVersion();
				string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
				string configurationKey = BuildAutoConfigurationKey(projectRoot, packageVersion);
				ConfigureProject(packageVersion, configurationKey);
			}
			catch (Exception exception)
			{
				Debug.LogException(new InvalidOperationException(
					"[DHMGS] Automatic project setup failed. " +
					"Use the DHMGS/Project menu to retry it manually.",
					exception));
			}
		}

		internal static void ConfigureProject(string packageVersion, string configurationKey)
		{
			bool changed = EnsureInsecureHttpAllowed();
			if (EditorPrefs.GetBool(configurationKey, false))
			{
				if (changed)
				{
					Debug.Log("[DHMGS] Required Insecure HTTP setting was restored for this project.");
				}
				return;
			}

			bool valid = ValidateProject(logSuccess: false);
			EditorPrefs.SetBool(configurationKey, true);
			Debug.Log(
				$"[DHMGS] Package {packageVersion} project setup completed; " +
				(changed ? "Insecure HTTP was enabled." : "Insecure HTTP was already enabled.") +
				(valid ? string.Empty : " Review the validation warning above."));
		}

		internal static bool EnsureInsecureHttpAllowed()
		{
			if (PlayerSettings.insecureHttpOption == InsecureHttpOption.AlwaysAllowed)
			{
				return false;
			}
			PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
			return true;
		}

		internal static bool ShouldAutoConfigure(bool isBatchMode, bool isAssetImportWorkerProcess)
		{
			return !isBatchMode && !isAssetImportWorkerProcess;
		}

		private static string ResolvePackageVersion()
		{
			UnityEditor.PackageManager.PackageInfo packageInfo =
				UnityEditor.PackageManager.PackageInfo.FindForAssembly(
					typeof(DhmgsProjectValidator).Assembly);
			return packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.version)
				? packageInfo.version
				: FallbackPackageVersion;
		}

		internal static string BuildAutoConfigurationKey(string projectRoot, string packageVersion)
		{
			if (string.IsNullOrWhiteSpace(projectRoot))
			{
				throw new ArgumentException("Project root is required.", nameof(projectRoot));
			}
			if (string.IsNullOrWhiteSpace(packageVersion))
			{
				throw new ArgumentException("Package version is required.", nameof(packageVersion));
			}

			string normalizedProjectRoot = Path.GetFullPath(projectRoot)
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				.Replace('\\', '/');
			return $"{AutoConfigurationKeyPrefix}.{Hash128.Compute(normalizedProjectRoot)}.{packageVersion}";
		}
	}
}
