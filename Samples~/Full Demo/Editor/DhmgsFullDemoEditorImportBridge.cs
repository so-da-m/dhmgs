using System;
using System.Threading;
using System.Threading.Tasks;

using Mnnh.Dhmgs.Editor;

using UnityEditor;

namespace Mnnh.Dhmgs.Samples.Editor
{
	[InitializeOnLoad]
	internal static class DhmgsFullDemoEditorImportBridge
	{
		static DhmgsFullDemoEditorImportBridge()
		{
			DhmgsFullDemoImportGateway.Register(ImportAndSaveAsync);
			AssemblyReloadEvents.beforeAssemblyReload += Unregister;
		}

		private static void Unregister()
		{
			DhmgsFullDemoImportGateway.Unregister(ImportAndSaveAsync);
		}

		private static async Task<DhmgsMotionAsset> ImportAndSaveAsync(
			string baseUrl,
			int attemptId,
			IProgress<DhmgsSampleImportProgress> progress,
			CancellationToken cancellationToken)
		{
			IProgress<DhmgsImportProgress> pipelineProgress =
				new Progress<DhmgsImportProgress>(value => Report(progress, value));
			DhmgsImportResult import = await DhmgsImportPipeline.ImportAsync(
				new DhmgsImportRequest(baseUrl, attemptId),
				pipelineProgress,
				cancellationToken);

			DhmgsAssetWriter.Write(
				import,
				new DhmgsAssetWriteOptions
				{
					OutputRoot = DhmgsAssetWriter.DefaultOutputRoot,
					BakeGenericClip = false,
					BakeHumanoidClips = false,
				},
				pipelineProgress);
			return import.Motion;
		}

		private static void Report(
			IProgress<DhmgsSampleImportProgress> progress,
			DhmgsImportProgress value)
		{
			if (progress == null)
			{
				return;
			}

			float mapped = value.Stage switch
			{
				DhmgsImportStage.Downloading => value.Value * 0.65f,
				DhmgsImportStage.Extracting => 0.65f + value.Value * 0.1f,
				DhmgsImportStage.Decoding => 0.75f + value.Value * 0.1f,
				DhmgsImportStage.WritingAssets => 0.85f + value.Value * 0.15f,
				_ => value.Value,
			};
			progress.Report(new DhmgsSampleImportProgress(mapped, value.Stage.ToString()));
		}
	}
}
