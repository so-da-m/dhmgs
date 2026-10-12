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
		private const string BundledNeutralSmplxBakeSourceGuid = "615fc734a2d080f4d954f707b0aaa53e";
		private const string BundledYBotPreviewAvatarGuid = "121420fccd4c4bba93c676f768fdf0a5";

		private sealed class PreviewSession
		{
			public DhmgsSampleImportContext Context;
			public GameObject Host;
			public DhmgsFullDemoEditorPreview Preview;
			public DhmgsHumanoidPreview HumanoidPreview;
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
			Action<DhmgsSampleImportProgress> onProgress,
			CancellationToken cancellationToken)
		{
			ThrowIfUnavailable(context, cancellationToken);
			ResetPreview(context);
			if (context.HumanoidPreviewCamera == null)
			{
				throw new InvalidOperationException(
					"The Full Demo Humanoid preview camera is not assigned.");
			}

			try
			{
				return await ImportAndSaveCoreAsync(
					context,
					baseUrl,
					attemptId,
					onProgress,
					cancellationToken);
			}
			catch
			{
				ResetPreview(context);
				throw;
			}
		}

		private static async Task<DhmgsSampleImportResult> ImportAndSaveCoreAsync(
			DhmgsSampleImportContext context,
			string baseUrl,
			int attemptId,
			Action<DhmgsSampleImportProgress> onProgress,
			CancellationToken cancellationToken)
		{
			GameObject bundledNeutralSmplxBakeSource = LoadRequiredModel(
				BundledNeutralSmplxBakeSourceGuid,
				"bundled Neutral SMPL-X bake source");
			GameObject bundledYBotPreviewAvatar = LoadRequiredModel(
				BundledYBotPreviewAvatarGuid,
				"bundled Y Bot preview avatar");

			DhmgsImportJobResult job = await DhmgsImportService.ImportAsync(
				new DhmgsImportJobRequest(
					baseUrl,
					attemptId,
					DhmgsImportService.DefaultOutputRoot,
					bundledNeutralSmplxBakeSource,
					DhmgsBakeOutputs.Humanoid,
					DhmgsVideoPolicy.CacheIfPresent),
				value => Report(onProgress, value),
				cancellationToken);

			ThrowIfUnavailable(context, cancellationToken);
			if (job.HumanoidInPlaceClip == null)
			{
				throw new InvalidOperationException("Humanoid bake did not produce an in-place clip.");
			}

			ThrowIfUnavailable(context, cancellationToken);
			string videoPath = job.PreviewVideoPath;
			IDhmgsPlaybackClock clock = new DhmgsRealtimeClock(loop: true);
			bool hasVideoPreview = false;
			PreviewSession session = null;
			if (!string.IsNullOrEmpty(videoPath) && context.PreviewCamera != null)
			{
				session = GetOrCreateSession(context);
				DhmgsCameraOverlay overlay = EnsureOverlay(session);
				bool videoPrepared = false;
				IDhmgsPlaybackClock preparedClock = null;
				IDhmgsPlaybackClock videoClock;
				bool previousRunInBackground = Application.runInBackground;
				try
				{
					Application.runInBackground = true;
					videoClock = await session.Preview.PrepareClockAsync(
						overlay.Activate(job.Motion, videoPath, value =>
						{
							preparedClock = value;
							videoPrepared = true;
						}),
						() => videoPrepared,
						() => preparedClock,
						() => AbortOverlay(overlay),
						cancellationToken,
						20f);
				}
				finally
				{
					Application.runInBackground = previousRunInBackground;
				}

				ThrowIfUnavailable(context, cancellationToken);
				if (videoClock != null)
				{
					clock = videoClock;
					hasVideoPreview = true;
				}
			}

			ThrowIfUnavailable(context, cancellationToken);
			session ??= GetOrCreateSession(context);
			bool humanoidPreviewStarted = session.HumanoidPreview.Play(
				bundledYBotPreviewAvatar,
				job.HumanoidInPlaceClip,
				clock,
				job.Motion.fps,
				ResolveIsolationPosition(context));
			if (!humanoidPreviewStarted)
			{
				throw new InvalidOperationException(
					"Could not play the baked Humanoid clip on the bundled Y Bot avatar.");
			}
			if (context.HumanoidPreviewCamera != null)
			{
				context.HumanoidPreviewCamera.SetTarget(session.HumanoidPreview);
			}

			return new DhmgsSampleImportResult(
				job.Motion,
				clock,
				BuildSummary(job),
				hasVideoPreview);
		}

		private static string BuildSummary(DhmgsImportJobResult job)
		{
			List<string> lines = new List<string>
			{
				$"[{job.Motion.attemptId}] Saved {job.Motion.frameCount} frames @ {job.Motion.fps:F2} fps",
				job.MotionAssetPath,
				$"Humanoid In-Place: {AssetDatabase.GetAssetPath(job.HumanoidInPlaceClip)}",
				$"Humanoid Root Motion: {AssetDatabase.GetAssetPath(job.HumanoidRootMotionClip)}",
				string.IsNullOrEmpty(job.PreviewVideoPath)
					? "Preview video: not present"
					: $"Preview video: {job.PreviewVideoPath}",
				$"Hand offset max diff: L={job.LeftHandOffsetMaxDiff:F4}, R={job.RightHandOffsetMaxDiff:F4}",
			};

			foreach (DhmgsImportDiagnostic diagnostic in job.Diagnostics)
			{
				if (diagnostic.Severity != DhmgsImportSeverity.Info)
				{
					lines.Add($"[{diagnostic.Severity}] {diagnostic.Message}");
				}
			}
			return string.Join("\n", lines);
		}

		private static PreviewSession GetOrCreateSession(DhmgsSampleImportContext context)
		{
			ThrowIfUnavailable(context, CancellationToken.None);
			if (Sessions.TryGetValue(context.SessionId, out PreviewSession existing))
			{
				if (existing.Host != null && existing.Preview != null && existing.HumanoidPreview != null &&
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
			DhmgsHumanoidPreview humanoidPreview = host.AddComponent<DhmgsHumanoidPreview>();
			if (preview == null || humanoidPreview == null)
			{
				DestroySessionObject(host);
				throw new InvalidOperationException("Could not create the isolated DHMGS preview host.");
			}

			PreviewSession session = new PreviewSession
			{
				Context = context,
				Host = host,
				Preview = preview,
				HumanoidPreview = humanoidPreview,
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
				if (context.HumanoidPreviewCamera != null)
				{
					context.HumanoidPreviewCamera.SetTarget(null);
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
				if (context != null && context.HumanoidPreviewCamera != null)
				{
					context.HumanoidPreviewCamera.SetTarget(null);
				}
				return;
			}

			if (session.Preview != null)
			{
				session.Preview.CancelClockPreparation();
			}
			if (session.HumanoidPreview != null)
			{
				session.HumanoidPreview.Stop();
			}
			if (session.Overlay != null)
			{
				AbortOverlay(session.Overlay);
			}
			if (context.HumanoidPreviewCamera != null)
			{
				context.HumanoidPreviewCamera.SetTarget(null);
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
			}
			if (session.HumanoidPreview != null)
			{
				session.HumanoidPreview.Stop();
			}
			if (session.Overlay != null)
			{
				AbortOverlay(session.Overlay);
			}
			if (resetCameraTarget && session.Context != null &&
				session.Context.HumanoidPreviewCamera != null)
			{
				session.Context.HumanoidPreviewCamera.SetTarget(null);
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

		private static GameObject LoadRequiredModel(string guid, string label)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			if (model == null)
			{
				throw new InvalidOperationException($"{label} is missing from the Full Demo sample (GUID {guid}).");
			}
			return model;
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
			Action<DhmgsSampleImportProgress> onProgress,
			DhmgsImportJobProgress value)
		{
			if (onProgress == null)
			{
				return;
			}

			float mapped = Mathf.Clamp01(value.OverallProgress) * 0.85f;
			string status = string.IsNullOrWhiteSpace(value.Message)
				? value.Stage.ToString()
				: value.Message;
			onProgress(new DhmgsSampleImportProgress(mapped, status));
		}
	}
}
