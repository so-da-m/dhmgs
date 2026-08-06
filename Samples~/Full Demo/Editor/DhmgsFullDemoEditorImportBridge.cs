using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Mnnh.Dhmgs.Editor;

using UnityEditor;

using UnityEngine;
using UnityEngine.Video;

namespace Mnnh.Dhmgs.Samples.Editor
{
	[InitializeOnLoad]
	internal static class DhmgsFullDemoEditorImportBridge
	{
		private const string GenericRigGuid = "615fc734a2d080f4d954f707b0aaa53e";
		private const string SkinMaterialGuid = "f7b072368c25ef54e8b35a56058f4183";

		private sealed class PreviewSession
		{
			public DhmgsSampleImportContext Context;
			public GameObject Host;
			public DhmgsFullDemoEditorPreview Preview;
			public DhmgsCameraOverlay Overlay;
			public VideoPlayer VideoPlayer;
			public float VideoOpacity = 1f;
		}

		private static readonly Dictionary<int, PreviewSession> Sessions =
			new Dictionary<int, PreviewSession>();

		static DhmgsFullDemoEditorImportBridge()
		{
			DhmgsFullDemoImportGateway.Register(ImportAndSaveAsync, SetVideoOpacity, StopPreview);
			AssemblyReloadEvents.beforeAssemblyReload += Unregister;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
		}

		private static void Unregister()
		{
			StopAllSessions(resetCameraTarget: false);
			DhmgsFullDemoImportGateway.Unregister(ImportAndSaveAsync);
			AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			if (state == PlayModeStateChange.ExitingPlayMode ||
				state == PlayModeStateChange.EnteredEditMode)
			{
				StopAllSessions(resetCameraTarget: false);
			}
		}

		private static async Task<DhmgsSampleImportResult> ImportAndSaveAsync(
			DhmgsSampleImportContext context,
			string baseUrl,
			int attemptId,
			IProgress<DhmgsSampleImportProgress> progress,
			CancellationToken cancellationToken)
		{
			ThrowIfUnavailable(context, cancellationToken);
			ResetPreview(context);

			IProgress<DhmgsImportProgress> pipelineProgress =
				new Progress<DhmgsImportProgress>(value => Report(progress, value));
			DhmgsImportResult import = await DhmgsImportPipeline.ImportAsync(
				new DhmgsImportRequest(baseUrl, attemptId),
				pipelineProgress,
				cancellationToken);

			ThrowIfUnavailable(context, cancellationToken);
			string genericRigPath = AssetDatabase.GUIDToAssetPath(GenericRigGuid);
			GameObject genericRig = AssetDatabase.LoadAssetAtPath<GameObject>(genericRigPath);
			string skinMaterialPath = AssetDatabase.GUIDToAssetPath(SkinMaterialGuid);
			Material skinMaterial = AssetDatabase.LoadAssetAtPath<Material>(skinMaterialPath);
			DhmgsAssetWriteResult written = DhmgsAssetWriter.Write(
				import,
				new DhmgsAssetWriteOptions
				{
					OutputRoot = DhmgsAssetWriter.DefaultOutputRoot,
					GenericRig = genericRig,
					BakeGenericClip = genericRig != null,
					BakeHumanoidClips = false,
					CancellationToken = cancellationToken,
				},
				pipelineProgress);

			ThrowIfUnavailable(context, cancellationToken);
			string videoPath = DhmgsImportPipeline.WritePreviewVideo(import);
			IDhmgsPlaybackClock clock = new DhmgsRealtimeClock(loop: true);
			bool hasVideoPreview = false;
			PreviewSession session = null;
			if (!string.IsNullOrEmpty(videoPath) && context.PreviewCamera != null)
			{
				session = GetOrCreateSession(context);
				DhmgsCameraOverlay overlay = EnsureOverlay(session);
				bool videoPrepared = false;
				IDhmgsPlaybackClock preparedClock = null;
				IDhmgsPlaybackClock videoClock = await session.Preview.PrepareClockAsync(
					overlay.Activate(import.Motion, videoPath, value =>
					{
						preparedClock = value;
						videoPrepared = true;
					}),
					() => videoPrepared,
					() => preparedClock,
					() => AbortOverlay(overlay),
					cancellationToken,
					20f);

				ThrowIfUnavailable(context, cancellationToken);
				if (videoClock != null)
				{
					clock = videoClock;
					hasVideoPreview = true;
				}
			}

			ThrowIfUnavailable(context, cancellationToken);
			if (genericRig != null && written.GenericClip != null)
			{
				session ??= GetOrCreateSession(context);
				session.Preview.PlayGeneric(
					genericRig,
					skinMaterial,
					written.GenericClip,
					clock,
					import.Motion.fps,
					ResolveIsolationPosition(context));
				if (context.BakedPreviewCamera != null)
				{
					context.BakedPreviewCamera.SetTarget(session.Preview);
				}
			}

			string videoNote = videoPath == null
				? "Preview video: not present"
				: $"Preview video: {videoPath}";
			return new DhmgsSampleImportResult(
				import.Motion,
				clock,
				$"{written.Summary}\n{videoNote}",
				hasVideoPreview);
		}

		private static PreviewSession GetOrCreateSession(DhmgsSampleImportContext context)
		{
			ThrowIfUnavailable(context, CancellationToken.None);
			if (Sessions.TryGetValue(context.SessionId, out PreviewSession existing))
			{
				if (existing.Host != null && existing.Preview != null &&
					existing.Context.Owner == context.Owner)
				{
					return existing;
				}
				DisposeSession(existing, resetCameraTarget: false);
				Sessions.Remove(context.SessionId);
			}

			GameObject host = new GameObject($"DHMGS Editor Preview - {context.Owner.name}")
			{
				hideFlags = HideFlags.DontSave,
			};
			host.transform.SetParent(context.Owner.transform, false);
			DhmgsFullDemoEditorPreview preview = host.AddComponent<DhmgsFullDemoEditorPreview>();
			if (preview == null)
			{
				DestroySessionObject(host);
				throw new InvalidOperationException("Could not create the isolated DHMGS preview host.");
			}

			PreviewSession session = new PreviewSession
			{
				Context = context,
				Host = host,
				Preview = preview,
			};
			Sessions.Add(context.SessionId, session);
			return session;
		}

		private static DhmgsCameraOverlay EnsureOverlay(PreviewSession session)
		{
			if (session.Overlay != null)
			{
				return session.Overlay;
			}
			if (session.Context.PreviewCamera == null)
			{
				throw new InvalidOperationException("The Full Demo preview camera is not assigned.");
			}

			session.VideoPlayer = session.Host.AddComponent<VideoPlayer>();
			session.Overlay = session.Host.AddComponent<DhmgsCameraOverlay>();
			if (session.VideoPlayer == null || session.Overlay == null)
			{
				throw new InvalidOperationException("Could not create the isolated video preview components.");
			}
			session.VideoPlayer.audioOutputMode = VideoAudioOutputMode.None;

			SerializedObject serialized = new SerializedObject(session.Overlay);
			serialized.FindProperty("_camera").objectReferenceValue = session.Context.PreviewCamera;
			serialized.FindProperty("_videoPlayer").objectReferenceValue = session.VideoPlayer;
			serialized.FindProperty("_loop").boolValue = true;
			serialized.FindProperty("_videoAlpha").floatValue = session.VideoOpacity;
			serialized.FindProperty("_surfaceLayer").stringValue = "Ignore Raycast";
			serialized.ApplyModifiedPropertiesWithoutUndo();
			return session.Overlay;
		}

		private static void SetVideoOpacity(DhmgsSampleImportContext context, float opacity)
		{
			if (!Application.isPlaying || context == null || !context.IsActive || context.SessionId == 0)
			{
				return;
			}

			PreviewSession session = GetOrCreateSession(context);
			session.VideoOpacity = Mathf.Clamp01(opacity);
			if (session.Overlay != null)
			{
				session.Overlay.SetVideoAlpha(session.VideoOpacity);
			}
		}

		private static void StopPreview(DhmgsSampleImportContext context)
		{
			if (context == null || context.SessionId == 0)
			{
				return;
			}
			if (!Sessions.TryGetValue(context.SessionId, out PreviewSession session))
			{
				if (context.BakedPreviewCamera != null)
				{
					context.BakedPreviewCamera.SetTarget(null);
				}
				return;
			}

			Sessions.Remove(context.SessionId);
			DisposeSession(session, resetCameraTarget: true);
		}

		private static void ResetPreview(DhmgsSampleImportContext context)
		{
			if (context == null || context.SessionId == 0 ||
				!Sessions.TryGetValue(context.SessionId, out PreviewSession session))
			{
				if (context != null && context.BakedPreviewCamera != null)
				{
					context.BakedPreviewCamera.SetTarget(null);
				}
				return;
			}

			if (session.Preview != null)
			{
				session.Preview.CancelClockPreparation();
				session.Preview.ClearGeneric();
			}
			if (session.Overlay != null)
			{
				AbortOverlay(session.Overlay);
			}
			if (context.BakedPreviewCamera != null)
			{
				context.BakedPreviewCamera.SetTarget(null);
			}
		}

		private static void StopAllSessions(bool resetCameraTarget)
		{
			if (Sessions.Count == 0)
			{
				return;
			}

			List<PreviewSession> sessions = new List<PreviewSession>(Sessions.Values);
			Sessions.Clear();
			foreach (PreviewSession session in sessions)
			{
				DisposeSession(session, resetCameraTarget);
			}
		}

		private static void DisposeSession(PreviewSession session, bool resetCameraTarget)
		{
			if (session == null)
			{
				return;
			}
			if (session.Preview != null)
			{
				session.Preview.CancelClockPreparation();
				session.Preview.ClearGeneric();
			}
			if (session.Overlay != null)
			{
				AbortOverlay(session.Overlay);
			}
			if (resetCameraTarget && session.Context != null &&
				session.Context.BakedPreviewCamera != null)
			{
				session.Context.BakedPreviewCamera.SetTarget(null);
			}
			if (session.Host != null)
			{
				DestroySessionObject(session.Host);
			}
		}

		private static void AbortOverlay(DhmgsCameraOverlay overlay)
		{
			if (overlay == null)
			{
				return;
			}
			overlay.CancelPreparation();
			overlay.Deactivate();
		}

		private static Vector3 ResolveIsolationPosition(DhmgsSampleImportContext context)
		{
			Camera camera = context.PreviewCamera;
			if (camera != null)
			{
				float distance = Mathf.Max(1000f, camera.farClipPlane * 2f + 100f);
				return camera.transform.position + camera.transform.right * distance;
			}
			return context.Owner.transform.position + Vector3.right * 10000f;
		}

		private static void ThrowIfUnavailable(
			DhmgsSampleImportContext context,
			CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Application.isPlaying || context == null || !context.IsActive ||
				context.SessionId == 0 || context.Player == null)
			{
				throw new OperationCanceledException("The Full Demo context is no longer active.", cancellationToken);
			}
		}

		private static void DestroySessionObject(GameObject host)
		{
			if (host == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(host);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(host);
			}
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
				DhmgsImportStage.Downloading => value.Value * 0.6f,
				DhmgsImportStage.Extracting => 0.6f + value.Value * 0.1f,
				DhmgsImportStage.Decoding => 0.7f + value.Value * 0.1f,
				DhmgsImportStage.WritingAssets => 0.8f + value.Value * 0.2f,
				_ => value.Value,
			};
			progress.Report(new DhmgsSampleImportProgress(mapped, value.Stage.ToString()));
		}
	}
}
