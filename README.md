# MNNH DHMGS

DHMGS 결과를 Unity 에셋으로 가져오고 SMPL-X 리그에서 재생하기 위한 사내용 Unity Package입니다.

핵심 경계는 명확합니다.

- 다운로드, ZIP/JSON 해석, 좌표 변환, 에셋 저장, AnimationClip 베이크는 **Unity Editor에서만** 실행됩니다.
- Player 빌드는 이미 생성된 `DhmgsMotionAsset`과 AnimationClip만 소비합니다.
- 영상은 Editor 정합 미리보기 보조물입니다. 현재 Player 워크플로에는 포함하지 않습니다.

## 설치

Unity Package Manager의 **Add package from git URL**에서 다음처럼 태그를 고정합니다.

```text
https://github.com/mn-nh/mnnh-dhmgs.git#v0.2.0
```

사내 private 저장소이므로 설치하는 PC의 Git 자격 증명에 저장소 읽기 권한이 있어야 합니다. 업데이트는 `package.json` 또는 Package Manager의 Git URL 태그를 새 버전으로 바꾸는 방식으로 수행합니다.

Git LFS는 사용하지 않습니다. 포함된 기본 FBX 세 개는 각각 GitHub의 단일 파일 제한보다 작습니다.

## 포함 범위

패키지 저장소에는 다음만 들어갑니다.

```text
Package/
├─ Runtime/                 # 빌드 가능한 재생 코어
│  └─ UI/                  # 선택적 런타임 UI 어셈블리
├─ Editor/
│  ├─ Import/              # 다운로드·해석·변환·에셋 저장·클립 베이크
│  └─ Preview/             # 영상/휴머노이드 Editor 미리보기
├─ Tests/Editor/
├─ SmplxRig/Raw/
│  ├─ SmplxFemale.fbx
│  ├─ SmplxMale.fbx
│  └─ SmplxNeutral.fbx
└─ package.json
```

Kyra, 의상, 프로젝트 전용 prefab/scene/font/controller와 생성 결과는 패키지 밖의 소비 프로젝트에 둡니다. 이 프로젝트의 기본 위치는 다음과 같습니다.

```text
Assets/Mnnh.Dhmgs/
├─ Package/                 # 이 Git/UPM 패키지
└─ Content/                 # 현재 프로젝트에만 속하는 선택 콘텐츠
   └─ Generated/            # 가져오기 결과
```

`PromptGenTest`와 `DhTest`는 패키지에 포함되지 않습니다. `DhTest.Editor`만 소비 프로젝트의 Editor 전용 어댑터로 `Mnnh.Dhmgs.Editor`를 참조합니다.

## Editor 가져오기

1. `DHMGS > Importer`를 엽니다.
2. 사내 DHMGS 서버의 Base URL과 attempt ID를 입력합니다.
3. 필요할 때만 Generic/Humanoid 베이크용 rig를 선택합니다.
4. **Import**를 누릅니다.

기본 서버 주소는 사내 주소 `http://192.168.1.128:8090`이며 EditorPrefs에 로컬 저장됩니다. 기본 결과 위치는 아래와 같습니다.

```text
Assets/Mnnh.Dhmgs/Content/Generated/{attemptId}/
├─ {attemptId}.asset
├─ manifest.json
├─ sequence.json
└─ 선택한 AnimationClip들
```

선택적으로 저장하는 MP4는 Unity 에셋이 아니라 다음 로컬 경로에만 기록됩니다.

```text
Application.persistentDataPath/Dhmgs/{attemptId}/video.mp4
```

프로젝트 설정은 자동 변경하지 않습니다. HTTP 허용이나 미리보기 레이어가 필요하면 `DHMGS > Project` 메뉴의 명시적 명령을 사용합니다.

## Player에서 재생

씬이나 prefab에서 생성된 `DhmgsMotionAsset`을 직렬화 참조하면 Unity가 Player 빌드에 포함합니다. 런타임 재생의 최소 코드는 다음과 같습니다.

```csharp
using Mnnh.Dhmgs;

player.Play(motionAsset, new DhmgsRealtimeClock(loop: true));
```

- `DhmgsMotionPlayer`: 재생 상태, clock, 활성 avatar/clothes를 조정합니다.
- `DhmgsRigDriver`: 한 SMPL-X rig에 특정 프레임을 적용합니다.
- `DhmgsMotionAsset`: 변환이 끝난 빌드 안전 데이터입니다.
- `DhmgsMotionPlayerPanel`: 이미 가져온 asset을 재생하는 선택적 UI입니다. 다운로드 기능은 없습니다.

직접 참조하지 않은 동적 에셋을 빌드에서 찾는 정책(Addressables, Resources, 자체 catalog)은 소비 프로젝트가 정합니다. 패키지는 특정 배포 방식을 강제하지 않습니다.

## 공개 Editor API

EditorWindow가 아닌 다른 사내 도구에서도 동일한 파이프라인을 호출할 수 있습니다.

```csharp
DhmgsImportResult imported = await DhmgsImportPipeline.ImportAsync(
    new DhmgsImportRequest(baseUrl, attemptId));

DhmgsAssetWriteResult written = DhmgsAssetWriter.Write(
    imported,
    new DhmgsAssetWriteOptions());
```

다운로드 없이 이미 받은 ZIP 바이트를 처리하려면 `DhmgsImportPipeline.ImportArchive`를 사용합니다. 네트워크, 파싱, 변환과 Unity 에셋 저장이 분리되어 있어 UI나 다른 Editor 도구가 필요한 단계만 조합할 수 있습니다.

## 릴리스

- `package.json` 버전과 Git 태그를 함께 올립니다: `0.2.0` ↔ `v0.2.0`.
- 소비 프로젝트는 floating branch 대신 태그를 고정합니다.
- `Content/Generated`와 프로젝트 전용 rig/scene은 패키지 저장소에 커밋하지 않습니다.
- 100 MiB가 넘는 새 바이너리는 이 저장소에 넣지 말고 별도 콘텐츠 전달 방식을 사용합니다.
