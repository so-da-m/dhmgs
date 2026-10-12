using System;

using UnityEditor;
using UnityEditor.UIElements;

using UnityEngine;
using UnityEngine.UIElements;

namespace Mnnh.Dhmgs.Editor
{
	internal sealed class DhmgsSmplxAvatarRegistrationWindow : EditorWindow
	{
		private ObjectField _sourceField;
		private TextField _nameField;
		private TextField _pathField;
		private Toggle _addToDemoToggle;
		private EnumField _referenceField;
		private VisualElement _validationContainer;
		private Button _registerButton;
		private HelpBox _sceneHelp;
		private DhmgsSmplxAvatarValidationResult _validation;
		private bool _pathIsAutomatic = true;

		[MenuItem("DHMGS/새 SMPLX 아바타 등록...")]
		private static void Open()
		{
			DhmgsSmplxAvatarRegistrationWindow window = GetWindow<DhmgsSmplxAvatarRegistrationWindow>();
			window.titleContent = new GUIContent("SMPLX 아바타 등록");
			window.minSize = new Vector2(540f, 550f);
			window.Show();
		}

		private void OnEnable()
		{
			EditorApplication.projectChanged += OnProjectChanged;
		}

		private void OnDisable()
		{
			EditorApplication.projectChanged -= OnProjectChanged;
		}

		public void CreateGUI()
		{
			VisualElement root = rootVisualElement;
			root.style.paddingLeft = 14f;
			root.style.paddingRight = 14f;
			root.style.paddingTop = 12f;
			root.style.paddingBottom = 12f;

			Label title = new Label("새 SMPLX 아바타 등록");
			title.style.fontSize = 18f;
			title.style.unityFontStyleAndWeight = FontStyle.Bold;
			title.style.marginBottom = 4f;
			root.Add(title);

			Label description = new Label(
				"공식 SMPL-X 골격을 포함한 FBX를 선택하세요. 원본 메시, 머터리얼, 체형과 회사 전용 블렌드셰이프는 그대로 유지됩니다.");
			description.style.whiteSpace = WhiteSpace.Normal;
			description.style.marginBottom = 12f;
			root.Add(description);

			_sourceField = new ObjectField("원본 FBX")
			{
				objectType = typeof(GameObject),
				allowSceneObjects = false,
			};
			_sourceField.tooltip = "프로젝트에 임포트된 FBX의 루트 모델 에셋입니다.";
			_sourceField.RegisterValueChangedCallback(OnSourceChanged);
			root.Add(_sourceField);

			_nameField = new TextField("아바타 이름") { isDelayed = true };
			_nameField.tooltip = "전체 데모의 아바타 선택 목록에 표시할 이름입니다.";
			_nameField.RegisterValueChangedCallback(OnNameChanged);
			root.Add(_nameField);
			HelpBox namePathHelp = new HelpBox(
				"아바타 이름을 바꾸면 자동 프리팹 파일명도 함께 바뀝니다. 별도 파일명을 유지하려면 저장 경로를 직접 지정하세요.",
				HelpBoxMessageType.Info);
			namePathHelp.style.marginTop = 2f;
			root.Add(namePathHelp);

			VisualElement pathRow = new VisualElement();
			pathRow.style.flexDirection = FlexDirection.Row;
			pathRow.style.alignItems = Align.Center;
			_pathField = new TextField("프리팹 저장 경로") { isDelayed = true };
			_pathField.style.flexGrow = 1f;
			_pathField.RegisterValueChangedCallback(_ =>
			{
				_pathIsAutomatic = false;
				RefreshValidation();
			});
			pathRow.Add(_pathField);
			Button browse = new Button(BrowsePrefabPath) { text = "..." };
			browse.tooltip = "프로젝트가 소유할 프리팹 저장 경로를 선택합니다.";
			browse.style.width = 32f;
			browse.style.marginLeft = 4f;
			pathRow.Add(browse);
			root.Add(pathRow);

			_addToDemoToggle = new Toggle("DHMGS 전체 데모에 추가") { value = true };
			_addToDemoToggle.tooltip = "임포트된 전체 데모 씬에 비활성 프리팹 인스턴스를 만들고 아바타 목록에 추가합니다.";
			root.Add(_addToDemoToggle);

			_sceneHelp = new HelpBox(string.Empty, HelpBoxMessageType.Info);
			_sceneHelp.style.marginTop = 2f;
			root.Add(_sceneHelp);
			RefreshFullDemoAvailability();

			Foldout advanced = new Foldout { text = "고급 설정", value = false };
			advanced.style.marginTop = 8f;
			_referenceField = new EnumField(
				"골격 기준",
				DhmgsSmplxSkeletonReference.Neutral);
			_referenceField.tooltip =
				"구조 비교에 사용할 내장 공식 FBX입니다. 기본값은 중립형이며, 체형 비율과 메시 토폴로지는 비교하지 않습니다.";
			_referenceField.RegisterValueChangedCallback(_ => RefreshValidation());
			advanced.Add(_referenceField);
			root.Add(advanced);

			Label validationTitle = new Label("검증 결과");
			validationTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
			validationTitle.style.marginTop = 12f;
			validationTitle.style.marginBottom = 4f;
			root.Add(validationTitle);

			ScrollView validationScroll = new ScrollView(ScrollViewMode.Vertical);
			validationScroll.style.flexGrow = 1f;
			validationScroll.style.minHeight = 190f;
			validationScroll.style.marginBottom = 10f;
			_validationContainer = new VisualElement();
			validationScroll.Add(_validationContainer);
			root.Add(validationScroll);

			VisualElement actions = new VisualElement();
			actions.style.flexDirection = FlexDirection.Row;
			actions.style.justifyContent = Justify.FlexEnd;
			Button validateButton = new Button(RefreshValidation) { text = "검사" };
			validateButton.style.width = 100f;
			actions.Add(validateButton);
			_registerButton = new Button(Register) { text = "등록" };
			_registerButton.style.width = 120f;
			_registerButton.style.marginLeft = 6f;
			actions.Add(_registerButton);
			root.Add(actions);

			Button removeRegisteredButton = new Button(DhmgsSmplxAvatarRemovalWindow.Open)
			{
				text = "등록된 아바타 제거...",
			};
			removeRegisteredButton.tooltip =
				"Full Demo에서 사용자가 등록한 아바타와 생성된 래퍼 프리팹을 안전하게 제거합니다.";
			removeRegisteredButton.style.marginTop = 6f;
			root.Add(removeRegisteredButton);

			GameObject selected = Selection.activeObject as GameObject;
			if (selected != null && AssetDatabase.GetAssetPath(selected).EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
			{
				_sourceField.SetValueWithoutNotify(selected);
				ApplySourceDefaults(selected);
			}
			else
			{
				_nameField.SetValueWithoutNotify(string.Empty);
				_pathField.SetValueWithoutNotify(DhmgsSmplxAvatarRegistrar.BuildDefaultPrefabPath(string.Empty));
				RefreshValidation();
			}
		}

		private void OnSourceChanged(ChangeEvent<UnityEngine.Object> change)
		{
			GameObject source = change.newValue as GameObject;
			if (source != null)
			{
				ApplySourceDefaults(source);
			}
			else
			{
				RefreshValidation();
			}
		}

		private void ApplySourceDefaults(GameObject source)
		{
			string previousName = _nameField.value;
			if (string.IsNullOrWhiteSpace(previousName)
				|| _pathIsAutomatic
				|| previousName.Equals(_validation?.Source?.name, StringComparison.Ordinal))
			{
				_nameField.SetValueWithoutNotify(source.name);
			}
			if (_pathIsAutomatic)
			{
				_pathField.SetValueWithoutNotify(
					DhmgsSmplxAvatarRegistrar.BuildDefaultPrefabPath(_nameField.value));
			}
			RefreshValidation();
		}

		private void OnNameChanged(ChangeEvent<string> change)
		{
			if (_pathIsAutomatic)
			{
				string previousAutomaticPath = DhmgsSmplxAvatarRegistrar.BuildDefaultPrefabPath(change.previousValue);
				_pathIsAutomatic = string.Equals(
					_pathField.value?.Replace('\\', '/'),
					previousAutomaticPath,
					StringComparison.OrdinalIgnoreCase);
			}
			if (_pathIsAutomatic)
			{
				_pathField.SetValueWithoutNotify(
					DhmgsSmplxAvatarRegistrar.BuildDefaultPrefabPath(change.newValue));
			}
			RefreshValidation();
		}

		private void BrowsePrefabPath()
		{
			string current = _pathField.value;
			string currentFolder = string.IsNullOrEmpty(current)
				? DhmgsSmplxAvatarRegistrar.DefaultAvatarFolder
				: System.IO.Path.GetDirectoryName(current)?.Replace('\\', '/');
			string defaultName = string.IsNullOrWhiteSpace(_nameField.value)
				? "새 SMPLX 아바타"
				: _nameField.value.Trim();
			string selected = EditorUtility.SaveFilePanelInProject(
				"SMPLX 아바타 래퍼 저장",
				defaultName,
				"prefab",
				"프로젝트가 소유할 래퍼 프리팹의 저장 위치를 선택하세요.",
				string.IsNullOrEmpty(currentFolder) ? DhmgsSmplxAvatarRegistrar.DefaultAvatarFolder : currentFolder);
			if (string.IsNullOrEmpty(selected))
			{
				return;
			}
			_pathIsAutomatic = false;
			_pathField.SetValueWithoutNotify(selected.Replace('\\', '/'));
			RefreshValidation();
		}

		private void RefreshValidation()
		{
			if (_validationContainer == null)
			{
				return;
			}

			GameObject source = _sourceField?.value as GameObject;
			DhmgsSmplxSkeletonReference reference = _referenceField != null
				? (DhmgsSmplxSkeletonReference)_referenceField.value
				: DhmgsSmplxSkeletonReference.Neutral;
			_validation = DhmgsSmplxAvatarValidator.Validate(source, reference);
			_validationContainer.Clear();

			if (source == null)
			{
				_validationContainer.Add(new HelpBox("검사할 FBX를 선택하세요.", HelpBoxMessageType.Info));
				UpdateRegisterButton();
				return;
			}

			string state = _validation.CanRegister ? "사용 가능" : "등록 불가";
			HelpBoxMessageType stateType = _validation.CanRegister
				? HelpBoxMessageType.Info
				: HelpBoxMessageType.Error;
			_validationContainer.Add(new HelpBox(
				$"DHMGS 모션 재생: {state}\n" +
				$"SMPL-X 골격: {_validation.MatchedJointCount}/{_validation.CanonicalJointCount}\n" +
				$"기본 블렌드셰이프: {_validation.StandardBlendShapeCount}/{DhmgsSmplxAvatarValidator.OfficialBlendShapeCount}\n" +
				$"추가 블렌드셰이프: {_validation.AdditionalBlendShapeCount}",
				stateType));

			foreach (DhmgsAvatarValidationDiagnostic diagnostic in _validation.Diagnostics)
			{
				_validationContainer.Add(new HelpBox(
					diagnostic.Message,
					ToHelpBoxType(diagnostic.Severity)));
			}
			if (_validation.Diagnostics.Count == 0)
			{
				_validationContainer.Add(new HelpBox(
					"공식 SMPL-X 재생 구조를 충족합니다.",
					HelpBoxMessageType.Info));
			}
			UpdateRegisterButton();
		}

		private void RefreshFullDemoAvailability()
		{
			if (_addToDemoToggle == null || _sceneHelp == null)
			{
				return;
			}
			if (DhmgsSmplxAvatarRegistrar.TryResolveFullDemoScene(out string path, out string reason))
			{
				_addToDemoToggle.SetEnabled(true);
				_sceneHelp.text = "대상: " + path;
				_sceneHelp.messageType = HelpBoxMessageType.Info;
			}
			else
			{
				_addToDemoToggle.SetValueWithoutNotify(false);
				_addToDemoToggle.SetEnabled(false);
				_sceneHelp.text = reason;
				_sceneHelp.messageType = HelpBoxMessageType.Warning;
			}
		}

		private void UpdateRegisterButton()
		{
			if (_registerButton == null)
			{
				return;
			}
			bool validFields = !string.IsNullOrWhiteSpace(_nameField?.value)
				&& !string.IsNullOrWhiteSpace(_pathField?.value)
				&& _pathField.value.StartsWith("Assets/", StringComparison.Ordinal)
				&& _pathField.value.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
			_registerButton.SetEnabled(_validation != null && _validation.CanRegister && validFields);
		}

		private void Register()
		{
			RefreshValidation();
			if (_validation == null || !_validation.CanRegister)
			{
				return;
			}

			string path = _pathField.value.Trim().Replace('\\', '/');
			UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
			if (existing != null
				&& !EditorUtility.DisplayDialog(
					"래퍼 프리팹 덮어쓰기",
					$"'{path}' 파일이 이미 있습니다. 에셋 GUID는 유지하고 내용을 교체할까요?",
					"덮어쓰기",
					"취소"))
			{
				return;
			}

			try
			{
				DhmgsSmplxAvatarRegistrationResult result = DhmgsSmplxAvatarRegistrar.Register(
					new DhmgsSmplxAvatarRegistrationOptions
					{
						SourceFbx = _sourceField.value as GameObject,
						AvatarName = _nameField.value,
						PrefabPath = path,
						SkeletonReference = (DhmgsSmplxSkeletonReference)_referenceField.value,
						AddToFullDemo = _addToDemoToggle.value,
					});

				Selection.activeObject = result.Prefab;
				EditorGUIUtility.PingObject(result.Prefab);
				string sceneResult = result.AddedToFullDemo
					? "\n임포트된 DHMGS 전체 데모의 아바타 목록에 추가했습니다."
					: result.AlreadyInFullDemo
						? "\n전체 데모가 이미 이 래퍼를 참조하고 있습니다."
						: string.Empty;
				EditorUtility.DisplayDialog(
					"SMPLX 아바타 등록 완료",
					$"래퍼 프리팹을 만들었습니다:\n{result.PrefabPath}{sceneResult}",
					"확인");
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
				EditorUtility.DisplayDialog("등록 실패", exception.Message, "확인");
			}
		}

		private void OnProjectChanged()
		{
			RefreshFullDemoAvailability();
			RefreshValidation();
		}

		private static HelpBoxMessageType ToHelpBoxType(DhmgsAvatarValidationSeverity severity)
		{
			switch (severity)
			{
				case DhmgsAvatarValidationSeverity.Error:
					return HelpBoxMessageType.Error;
				case DhmgsAvatarValidationSeverity.Warning:
					return HelpBoxMessageType.Warning;
				default:
					return HelpBoxMessageType.Info;
			}
		}
	}
}
