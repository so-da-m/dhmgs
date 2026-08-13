# MNNH DHMGS

DHMGS 결과를 Unity 에셋으로 가져오고 SMPL-X 리그에서 재생하기 위한 사내용 Unity Package입니다.

기능 경계는 다음과 같습니다.

- 다운로드, ZIP/JSON 해석, 좌표 변환, 에셋 저장, AnimationClip 베이킹은 Unity Editor에서만 실행됩니다.
- Player 빌드에는 이미 생성된 `DhmgsMotionAsset`, Humanoid `.anim`, 경량 재생 코드만 포함할 수 있습니다.
- 영상은 Editor 미리보기 보조물이며 현재 Player 패키징 범위에 포함하지 않습니다.

## 설치

Unity Package Manager의 **Add package from git URL**에서 릴리스 태그를 지정합니다.

```text
https://github.com/so-da-m/dhmgs.git#v0.7.1
```

사내 private 저장소이므로 설치하는 PC의 Git 자격 증명에 저장소 읽기 권한이 있어야 합니다. Git LFS는 사용하지 않습니다.

## Full Demo 샘플

Package Manager에서 **MNNH DHMGS > Samples > Full Demo > Import**를 누른 뒤 아래 씬을 엽니다.

```text
Assets/Samples/MNNH DHMGS/0.7.1/Full Demo/Scenes/DhmgsFullDemo.unity
```

패키지를 업데이트한 뒤 새 버전의 Full Demo를 다시 Import할 때는 먼저
`Assets/Samples/MNNH DHMGS/<이전 버전>/Full Demo`를 제거하세요. Unity는 샘플을
버전별 폴더에 복사하므로 이전 사본을 남기면 같은 asmdef와 스크립트 GUID가 중복될 수 있습니다.

Play Mode의 compact Canvas UI에서 다음 기능을 확인할 수 있습니다.

- Attempt ID 입력 후 **Fetch** 한 번으로 다운로드, 저장, Humanoid 베이크, YBot 리타게팅 재생
- `None`, Female, Male, Neutral 또는 의상 기준 체형인 Garment Female 아바타 선택
- 짝이 맞는 `Garment Female` / `Garment Clothes` 선택과 아바타/의상 투명도 조절
- 0.25x~2.0x 재생 속도 및 Editor 영상 투명도 조절
- 최신순 상태 로그
- 우상단 `SMPL-X PLAYBACK` 뷰와 우하단 `UNITY HUMANOID PLAYBACK` 뷰의 orbit, pan, zoom
- 각 재생 뷰의 **RESET** 버튼으로 해당 카메라만 초기 시점과 자동 프레이밍으로 복원

서버 주소는 `DhmgsFullDemoController` Inspector의 **Default Base Url**에서 설정하며, Canvas에는 현재 연결 대상과 Attempt ID만 표시합니다.

**Fetch**는 Editor에서만 활성화됩니다. 지정한 Attempt ID를 다운로드해 영구 모션 에셋과 Humanoid AnimationClip으로 저장하고 즉시 재생합니다.

```text
Assets/Mnnh.Dhmgs/Content/Generated/{attemptId}/{attemptId}.asset
Assets/Mnnh.Dhmgs/Content/Generated/{attemptId}/{attemptId}_humanoid_inplace.anim
Assets/Mnnh.Dhmgs/Content/Generated/{attemptId}/{attemptId}_humanoid_rootmotion.anim
```

영상이 포함된 결과는 Editor에서 메인 카메라에 동기 미리보기로 표시됩니다. 우상단은 SMPL-X 직접 재생이고, 우하단은 패키지의 Humanoid SMPL-X로 베이크한 in-place 클립을 포함된 YBot에 Unity Mecanim 방식으로 리타게팅한 결과입니다. 우하단은 Fetch 전에는 비워 둡니다.

Player 빌드에서는 Fetch·베이크·영상 조절·우하단 Editor 비교 뷰가 비활성화됩니다. 생성된 Humanoid `.anim`과 `DhmgsMotionAsset`은 일반 Unity 에셋이므로 빌드에서 사용할 수 있고, 씬/프리팹이 참조하는 아바타·의상 선택/투명도/속도 조절과 Direct RenderTexture 뷰도 그대로 동작합니다.

샘플 씬과 샘플 폴더는 패키지 내부 코드·리그·머티리얼만 참조합니다. `Assets/Mnnh.Dhmgs/Content`, `PromptGenTest`, `DhTest` 또는 프로젝트 전용 씬·프리팹·머티리얼·폰트·입력 액션을 참조하지 않습니다.

## 포함 범위

```text
Package/
├─ Runtime/                  # Player 빌드에서 사용하는 재생 코어
│  ├─ UI/                   # 선택적 런타임 UGUI 패널
│  └─ EditorPreview/        # Editor 전용 영상·Humanoid 미리보기 컴포넌트
├─ Editor/
│  └─ Import/               # 다운로드, 해석, 변환, 에셋 저장, 베이킹
├─ Samples~/Full Demo/      # 자체 완결형 재생, YBot 리타게팅 및 Editor 가져오기 샘플
├─ Tests/Editor/
├─ SmplxRig/
│  ├─ SmplxFemale.prefab, SmplxMale.prefab, SmplxNeutral.prefab
│  ├─ SmplxChatGarmentFemale.prefab, SmplxChatGarmentClothes.prefab
│  ├─ Meshes/               # Git LFS 없이 포함한 standalone 아바타·의상 Mesh
│  └─ Raw/
│     ├─ SmplxFemale.fbx
│     ├─ SmplxMale.fbx
│     └─ SmplxNeutral.fbx
└─ package.json
```

현재 프로젝트 전용 결과는 패키지 밖에 저장합니다.

```text
Assets/Mnnh.Dhmgs/
├─ Package/                 # 독립 Git/UPM 패키지
└─ Content/                 # 현재 프로젝트에서만 사용하는 선택 콘텐츠
   └─ Generated/            # Importer 및 Full Demo 결과
```

`PromptGenTest`와 `DhTest`는 패키지에 포함하지 않습니다. `DhTest.Editor`는 필요할 때만 `Mnnh.Dhmgs.Editor`를 참조하는 로컬 Editor 어셈블리입니다.

## Editor 가져오기

1. `DHMGS > Importer`를 엽니다.
2. 사내 DHMGS 서버의 Base URL과 Attempt ID를 입력합니다.
3. 패키지 기본 Neutral 또는 호환되는 단일 **SMPL-X Bake Source**를 고르고 필요한 출력만 선택합니다. YBot 같은 일반 Humanoid는 베이크 소스가 아니라 결과 클립의 리타게팅 대상입니다.
4. **Import**를 누릅니다.

기본 결과 위치는 다음과 같습니다.

```text
Assets/Mnnh.Dhmgs/Content/Generated/{attemptId}/
├─ {attemptId}.asset
├─ {attemptId}_generic.anim                 # 선택 사항
├─ {attemptId}_humanoid_inplace.anim        # 선택 사항
└─ {attemptId}_humanoid_rootmotion.anim     # 선택 사항
```

서버 원문과 선택적으로 저장한 MP4는 Unity 에셋이 아닌 로컬 캐시 경로에 기록합니다.

```text
Library/Mnnh.Dhmgs/source-{sourceIdSha256}/
├─ manifest.json
├─ sequence.json
└─ video.mp4                 # CacheIfPresent/Require에서 영상이 있을 때
```

서버 ZIP의 선택적 `identity.json`은 4 MiB 제한과 UTF-8 검사를 거쳐 호환 메타데이터로 허용하지만, 현재 표준 체형 재생에는 사용하지 않으며 `Assets`나 로컬 캐시에 복사하지 않습니다.

`CacheIfPresent`는 새 결과에 영상이 없으면 같은 source의 오래된 `video.mp4`도 제거합니다. `Ignore`는 영상 캐시를 읽거나 변경하지 않습니다. Importer 창의 입력, 출력 옵션, 마지막 SMPL-X 소스는 프로젝트 로컬 `UserSettings/DhmgsImporterSettings.asset`에 저장됩니다.

대화형 Unity Editor에서 패키지를 설치하거나 업데이트하면 Insecure HTTP를 허용하고, 이 PC의 사용자 환경에서 프로젝트 경로와 패키지 버전 조합당 한 번 프로젝트 검사를 실행합니다. 이후 설정이 꺼진 채 Editor 도메인이 다시 로드되면 다운로드 의존성을 위해 자동 복구합니다. 이 설정은 프로젝트 전체에 영구 적용되며 패키지를 제거해도 자동 복원되지 않습니다. `DHMGS > Project > Validate`와 `DHMGS > Project > Allow Insecure HTTP` 수동 명령도 그대로 남아 있어 언제든 다시 검사하거나 설정할 수 있습니다. TMP Essential Resources가 없으면 검사 경고를 남기지만 자동 임포트하지 않습니다. Humanoid 미리보기는 별도 프로젝트 레이어를 요구하지 않습니다.

## Player에서 재생

씬이나 프리팹에서 생성된 `DhmgsMotionAsset`을 직접 참조하면 Unity가 Player 빌드에 포함합니다.

```csharp
using Mnnh.Dhmgs;

player.Play(motionAsset, new DhmgsRealtimeClock(loop: true));
```

- `DhmgsMotionPlayer`: 재생 상태, clock, 활성 avatar/clothes를 관리합니다.
- `DhmgsRigDriver`: SMPL-X 리그에 특정 프레임을 적용합니다.
- `DhmgsMotionAsset`: 변환이 끝난 빌드 안전 모션 데이터입니다.
- `DhmgsMotionPlayerPanel`: 이미 가져온 asset을 재생하는 선택적 UI입니다. 다운로드 기능은 없습니다.

Addressables, Resources 또는 자체 catalog처럼 동적 에셋을 빌드에서 찾는 정책은 소비 프로젝트가 결정합니다.

## 공개 Editor API

다른 사내 Editor 도구와 Full Demo도 Window와 동일한 단일 서비스 계약을 사용합니다.

```csharp
DhmgsImportJobRequest request = new(
    baseUrl,
    attemptId,
    DhmgsImportService.DefaultOutputRoot,
    DhmgsImportService.LoadBundledSmplxBakeSource(),
    DhmgsBakeOutputs.Generic | DhmgsBakeOutputs.Humanoid,
    DhmgsVideoPolicy.CacheIfPresent);

DhmgsImportValidation validation = DhmgsImportService.Validate(request);
if (!validation.CanRun)
    throw new InvalidOperationException(validation.Diagnostics[0].Message);

DhmgsImportJobResult result = await DhmgsImportService.ImportAsync(
    request,
    progress => Debug.Log($"{progress.Stage}: {progress.OverallProgress:P0}"),
    cancellationToken);
```

이미 메모리에 받은 ZIP도 같은 서비스와 저장 규칙을 통과시킬 수 있습니다.

```csharp
DhmgsImportJobRequest request = new(
    sourceId,
    archiveBytes,
    DhmgsImportService.DefaultOutputRoot,
    DhmgsImportService.LoadBundledSmplxBakeSource(),
    DhmgsBakeOutputs.Humanoid,
    DhmgsVideoPolicy.Ignore);
```

`DhmgsImportService`가 검증, 다운로드, 제한된 아카이브 해석, cooperative 베이크, 로컬 캐시, prepare/publish 저장을 한 작업으로 소유합니다. 저수준 파서와 writer는 패키지 내부 구현이며, 요청한 출력이 하나라도 실패하면 성공 결과를 반환하지 않습니다. 게시 직전까지 취소할 수 있고 게시 구간에서는 기존 에셋 GUID를 보존하며 오류 시 이전 직렬화 상태를 복구합니다.

## 릴리스 규칙

- `package.json` 버전과 Git 태그를 함께 올립니다. 예: `0.7.1` ↔ `v0.7.1`.
- 소비 프로젝트는 floating branch 대신 릴리스 태그에 고정합니다.
- `Content/Generated`와 프로젝트 전용 rig/scene은 패키지 저장소에 커밋하지 않습니다.
- 100 MiB가 넘는 단일 바이너리는 저장소에 넣지 않고 별도 콘텐츠 전달 방식을 사용합니다.
