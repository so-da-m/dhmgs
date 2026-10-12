using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

namespace Mnnh.Dhmgs.Editor
{
	internal sealed class DhmgsSmplxAvatarRemovalWindow : EditorWindow
	{
		private readonly List<DhmgsSmplxAvatarRemovalEntry> _entries =
			new List<DhmgsSmplxAvatarRemovalEntry>();
		private DropdownField _targetField;
		private HelpBox _sceneHelp;
		private HelpBox _details;
		private Button _removeButton;

		[MenuItem("DHMGS/등록된 SMPLX 아바타 제거...")]
		internal static void Open()
		{
			DhmgsSmplxAvatarRemovalWindow window = GetWindow<DhmgsSmplxAvatarRemovalWindow>();
			window.titleContent = new GUIContent("SMPLX 아바타 제거");
			window.minSize = new Vector2(600f, 350f);
			window.Show();
		}

		private void OnEnable()
		{
			EditorApplication.projectChanged += Refresh;
		}

		private void OnDisable()
		{
			EditorApplication.projectChanged -= Refresh;
		}

		public void CreateGUI()
		{
			VisualElement root = rootVisualElement;
			root.style.paddingLeft = 14f;
			root.style.paddingRight = 14f;
			root.style.paddingTop = 12f;
			root.style.paddingBottom = 12f;

			Label title = new Label("등록된 SMPLX 아바타 제거");
			title.style.fontSize = 18f;
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.marginBottom = 4f;
			root.Add(title);

			Label description = new Label(
				"Full Demo에 등록한 사용자 아바타를 선택하면, "
				+ "씬 목록과 인스턴스를 정리하고 생성된 래퍼 프리팹만 휴지통으로 이동합니다.");
			description.style.whiteSpace = WhiteSpace.Normal;
			description.style.marginBottom = 10f;
			root.Add(description);

			_sceneHelp = new HelpBox(string.Empty, HelpBoxMessageType.Info);
			root.Add(_sceneHelp);

			_targetField = new DropdownField("제거 대상", new List<string> { "없음" }, 0);
			_targetField.style.marginTop = 8f;
			_targetField.RegisterValueChangedCallback(_ => UpdateSelection());
			root.Add(_targetField);

			_details = new HelpBox(string.Empty, HelpBoxMessageType.Info);
			_details.style.marginTop = 8f;
			_details.style.flexGrow = 1f;
			root.Add(_details);

			VisualElement actions = new VisualElement();
			actions.style.flexDirection = FlexDirection.Row;
			actions.style.justifyContent = Justify.FlexEnd;
			actions.style.marginTop = 10f;
			Button refreshButton = new Button(Refresh) { text = "새로고침" };
			refreshButton.style.width = 100f;
			actions.Add(refreshButton);
			_removeButton = new Button(RemoveSelected) { text = "씬과 래퍼 제거..." };
			_removeButton.style.width = 160f;
			_removeButton.style.marginLeft = 6f;
			actions.Add(_removeButton);
			root.Add(actions);

			Refresh();
		}

		private void Refresh()
		{
			if (_targetField == null)
			{
				return;
			}

			_entries.Clear();
			IReadOnlyList<DhmgsSmplxAvatarRemovalEntry> found =
				DhmgsSmplxAvatarRemoval.FindRemovableRegistrations(
					out string scenePath,
					out string reason);
			_entries.AddRange(found);
			List<string> choices = _entries.Count == 0
				? new List<string> { "제거할 아바타 없음" }
				: _entries.Select(FormatChoice).ToList();
			_targetField.choices = choices;
			_targetField.index = 0;
			_targetField.SetEnabled(_entries.Count > 0);

			if (!string.IsNullOrEmpty(scenePath))
			{
				_sceneHelp.text = "Full Demo: " + scenePath;
				_sceneHelp.messageType = HelpBoxMessageType.Info;
			}
			else
			{
				_sceneHelp.text = reason;
				_sceneHelp.messageType = HelpBoxMessageType.Warning;
			}
			if (_entries.Count == 0 && !string.IsNullOrEmpty(reason))
			{
				_details.text = reason;
				_details.messageType = HelpBoxMessageType.Info;
			}
			UpdateSelection();
		}

		private void UpdateSelection()
		{
			DhmgsSmplxAvatarRemovalEntry entry = SelectedEntry;
			if (entry == null)
			{
				_removeButton?.SetEnabled(false);
				return;
			}

			_removeButton.SetEnabled(true);
			if (entry.IsBroken)
			{
				_details.text =
					$"래퍼 파일이 이미 없는 깨진 등록입니다.\n"
					+ $"씬 목록 {entry.AvatarEntryCount}개와 누락된 인스턴스 {entry.SceneInstanceCount}개를 정리합니다.\n\n"
					+ "원본 FBX, 메시, 머티리얼, 텍스처는 삭제하지 않습니다.";
				_details.messageType = HelpBoxMessageType.Warning;
				_removeButton.text = "남은 씬 등록 정리...";
				return;
			}

			_details.text =
				$"아바타: {entry.AvatarName}\n"
				+ $"Full Demo 목록: {entry.AvatarEntryCount}개\n"
				+ $"씬 인스턴스: {entry.SceneInstanceCount}개\n"
				+ $"래퍼 프리팹: {entry.PrefabPath}\n\n"
				+ "원본 FBX와 의존 에셋은 그대로 유지됩니다.";
			_details.messageType = HelpBoxMessageType.Info;
			_removeButton.text = "씬과 래퍼 제거...";
		}

		private void RemoveSelected()
		{
			DhmgsSmplxAvatarRemovalEntry entry = SelectedEntry;
			if (entry == null)
			{
				return;
			}

			string title = entry.IsBroken ? "깨진 아바타 등록 정리" : "SMPLX 아바타 제거";
			string body = entry.IsBroken
				? $"'{entry.AvatarName}'의 남은 Full Demo 등록을 정리할까요?\n\n"
					+ $"• 깨진 아바타 목록 {entry.AvatarEntryCount}개\n"
					+ $"• 누락된 씬 인스턴스 {entry.SceneInstanceCount}개\n\n"
					+ "원본 FBX와 머티리얼은 삭제하지 않습니다."
				: $"'{entry.AvatarName}'을(를) 제거할까요?\n\n"
					+ $"• Full Demo 아바타 목록과 씬 인스턴스\n"
					+ $"• 생성된 래퍼 프리팹: {entry.PrefabPath}\n\n"
					+ "원본 FBX, 메시, 머티리얼, 텍스처는 삭제하지 않습니다.\n"
					+ "래퍼 프리팹은 휴지통으로 이동하며 Unity Undo로는 되돌릴 수 없습니다.";
			string accept = entry.IsBroken ? "씬 등록 정리" : "씬 + 래퍼 제거";
			if (!EditorUtility.DisplayDialog(title, body, accept, "취소"))
			{
				return;
			}

			try
			{
				DhmgsSmplxAvatarRemovalResult result =
					DhmgsSmplxAvatarRemoval.Remove(entry, !entry.IsBroken);
				ShowSuccess(result);
				Refresh();
			}
			catch (DhmgsSmplxAvatarExternalReferenceException referenceException)
			{
				string preview = string.Join("\n", referenceException.ReferencePaths.Take(5)
					.Select(path => "• " + path));
				if (referenceException.ReferencePaths.Count > 5)
				{
					preview += $"\n• 외 {referenceException.ReferencePaths.Count - 5}개";
				}
				bool sceneOnly = EditorUtility.DisplayDialog(
					"래퍼 프리팹을 삭제할 수 없음",
					"다른 에셋이 이 래퍼를 참조합니다:\n"
					+ preview
					+ "\n\n래퍼는 유지하고 Full Demo에서만 제거할까요?",
					"Full Demo에서만 제거",
					"취소");
				if (sceneOnly)
				{
					try
					{
						DhmgsSmplxAvatarRemovalResult result =
							DhmgsSmplxAvatarRemoval.Remove(entry, false);
						ShowSuccess(result);
						Refresh();
					}
					catch (Exception exception)
					{
						ShowFailure(exception);
					}
				}
			}
			catch (Exception exception)
			{
				ShowFailure(exception);
			}
		}

		private static void ShowSuccess(DhmgsSmplxAvatarRemovalResult result)
		{
			string wrapperState = result.WrapperMovedToTrash
				? "\n래퍼 프리팹을 휴지통으로 이동했습니다."
				: result.WrapperKept
					? "\n래퍼 프리팹은 다른 참조를 위해 유지했습니다."
					: string.Empty;
			EditorUtility.DisplayDialog(
				"아바타 제거 완료",
				$"Full Demo 목록 {result.RemovedAvatarEntries}개와 씬 인스턴스 "
				+ $"{result.RemovedSceneInstances}개를 정리했습니다.{wrapperState}\n\n"
				+ "원본 FBX와 의존 에셋은 그대로 있습니다.",
				"확인");
		}

		private static void ShowFailure(Exception exception)
		{
			Debug.LogException(exception);
			EditorUtility.DisplayDialog("아바타 제거 실패", exception.Message, "확인");
		}

		private DhmgsSmplxAvatarRemovalEntry SelectedEntry
		{
			get
			{
				int index = _targetField?.index ?? -1;
				return index >= 0 && index < _entries.Count ? _entries[index] : null;
			}
		}

		private static string FormatChoice(DhmgsSmplxAvatarRemovalEntry entry)
		{
			return entry.IsBroken
				? $"{entry.AvatarName} — 래퍼 파일 없음 / 씬 정리 필요"
				: $"{entry.AvatarName} — {entry.PrefabPath}";
		}
	}
}
