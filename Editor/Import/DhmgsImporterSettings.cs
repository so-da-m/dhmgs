using UnityEditor;

using UnityEngine;

namespace Mnnh.Dhmgs.Editor
{
	[FilePath("UserSettings/DhmgsImporterSettings.asset", FilePathAttribute.Location.ProjectFolder)]
	internal sealed class DhmgsImporterSettings : ScriptableSingleton<DhmgsImporterSettings>
	{
		[SerializeField] private string _baseUrl = DhmgsImportService.DefaultBaseUrl;
		[SerializeField] private string _outputRoot = DhmgsImportService.DefaultOutputRoot;
		[SerializeField] private string _attemptIdText = string.Empty;
		[SerializeField] private string _smplxBakeSourceGuid = string.Empty;
		[SerializeField] private bool _bakeGeneric = true;
		[SerializeField] private bool _bakeHumanoid = true;
		[SerializeField] private bool _cachePreviewVideo = true;

		internal string BaseUrl => string.IsNullOrEmpty(_baseUrl)
			? DhmgsImportService.DefaultBaseUrl
			: _baseUrl;

		internal string OutputRoot => string.IsNullOrEmpty(_outputRoot)
			? DhmgsImportService.DefaultOutputRoot
			: _outputRoot;

		internal string AttemptIdText => _attemptIdText ?? string.Empty;
		internal bool BakeGeneric => _bakeGeneric;
		internal bool BakeHumanoid => _bakeHumanoid;
		internal bool CachePreviewVideo => _cachePreviewVideo;

		internal GameObject LoadSmplxBakeSource()
		{
			if (!string.IsNullOrEmpty(_smplxBakeSourceGuid))
			{
				string path = AssetDatabase.GUIDToAssetPath(_smplxBakeSourceGuid);
				GameObject stored = string.IsNullOrEmpty(path)
					? null
					: AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (stored != null)
				{
					return stored;
				}
			}

			return DhmgsImportService.LoadBundledSmplxBakeSource();
		}

		internal void Store(
			string baseUrl,
			string outputRoot,
			string attemptIdText,
			GameObject smplxBakeSource,
			bool bakeGeneric,
			bool bakeHumanoid,
			bool cachePreviewVideo)
		{
			_baseUrl = baseUrl ?? string.Empty;
			_outputRoot = outputRoot ?? string.Empty;
			_attemptIdText = attemptIdText ?? string.Empty;
			_bakeGeneric = bakeGeneric;
			_bakeHumanoid = bakeHumanoid;
			_cachePreviewVideo = cachePreviewVideo;

			string sourcePath = smplxBakeSource != null
				? AssetDatabase.GetAssetPath(smplxBakeSource)
				: string.Empty;
			_smplxBakeSourceGuid = string.IsNullOrEmpty(sourcePath)
				? string.Empty
				: AssetDatabase.AssetPathToGUID(sourcePath);
			Save(true);
		}
	}
}
