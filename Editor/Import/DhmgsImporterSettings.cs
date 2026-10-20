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
		[SerializeField] private bool _bakeHumanoid = true;
		[SerializeField] private bool _cachePreviewVideo = true;

		internal string BaseUrl => string.IsNullOrEmpty(_baseUrl)
			? DhmgsImportService.DefaultBaseUrl
			: _baseUrl;

		internal string OutputRoot => string.IsNullOrEmpty(_outputRoot)
			? DhmgsImportService.DefaultOutputRoot
			: _outputRoot;

		internal string AttemptIdText => _attemptIdText ?? string.Empty;
		internal bool BakeHumanoid => _bakeHumanoid;
		internal bool CachePreviewVideo => _cachePreviewVideo;

		internal void Store(
			string baseUrl,
			string outputRoot,
			string attemptIdText,
			bool bakeHumanoid,
			bool cachePreviewVideo)
		{
			_baseUrl = baseUrl ?? string.Empty;
			_outputRoot = outputRoot ?? string.Empty;
			_attemptIdText = attemptIdText ?? string.Empty;
			_bakeHumanoid = bakeHumanoid;
			_cachePreviewVideo = cachePreviewVideo;
			Save(true);
		}
	}
}
