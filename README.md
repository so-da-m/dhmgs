# MNNH DHMGS

DHMGS 결과를 Unity 에셋으로 가져오고 SMPL-X 리그에서 재생하기 위한 사내용 Unity Package입니다.

기능 경계는 다음과 같습니다.

- 다운로드, ZIP/JSON 해석, 좌표 변환, 에셋 저장, AnimationClip 베이크는 Unity Editor에서만 실행됩니다.
- Player 빌드에는 이미 생성된 `DhmgsMotionAsset`과 런타임 재생 코드만 포함할 수 있습니다.
- 영상은 Editor 미리보기 보조물이며 현재 Player 런타임에는 포함하지 않습니다.

## 설치

Unity Package Manager의 **Add package from git URL**에서 릴리스 태그를 지정합니다.

```text
https://github.com/mn-nh/mnnh-dhmgs.git#v0.3.0
```

사내 private 저장소이므로 설치하는 PC의 Git 자격 증명에 저장소 읽기 권한이 있어야 합니다. Git LFS는 사용하지 않습니다.

## Full Demo 샘플

Package Manager에서 **MNNH DHMGS > Samples > Full Demo > Import**를 누른 뒤 아래 씬을 엽니다.

```text
Assets/Samples/MNNH DHMGS/0.3.0/Full Demo/Scenes/DhmgsFullDemo.unity
```

Play Mode에서 다음 기능을 바로 확인할 수 있습니다.

- 패키지에 포함된 `DhmgsMotionAsset` 재생/정지
- Female, Male, Neutral 기본 SMPL-X 아바타 전환
- 0.25x~2.0x 재생 속도 조절
- Player 빌드에서 동작하는 런타임 재생 경로

샘플 씬은 샘플 폴더와 패키지 안의 코드·기본 FBX만 참조합니다. `Content`, `PromptGenTest`, `DhTest` 또는 프로젝트 전용 프리팹/폰트/씬은 참조하지 않습니다. 추가 모션을 다운로드하고 베이크하려면 Editor에서 `DHMGS > Importer`를 사용합니다.

## 포함 범위

```text
Package/
├─ Runtime/                  # Player 빌드에서 사용하는 재생 코어
│  └─ UI/                   # 선택적 런타임 UGUI 어댑터
├─ Editor/
│  ├─ Import/               # 다운로드, 해석, 변환, 에셋 저장, 베이크
│  └─ Preview/              # 영상/이미지 Editor 미리보기
├─ Samples~/Full Demo/      # 자체 완결형 런타임 샘플
├─ Tests/Editor/
├─ SmplxRig/Raw/
│  ├─ SmplxFemale.fbx
│  ├─ SmplxMale.fbx
│  └─ SmplxNeutral.fbx
└─ package.json
```

현재 프로젝트 전용 결과는 패키지 밖에 저장합니다.

```text
Assets/Mnnh.Dhmgs/
├─ Package/                 # 독립 Git/UPM 패키지
└─ Content/                 # 현재 프로젝트에서만 사용하는 선택 콘텐츠
   └─ Generated/            # Importer 결과
```

`PromptGenTest`와 `DhTest`는 패키지에 포함되지 않습니다. `DhTest.Editor`는 필요할 때만 `Mnnh.Dhmgs.Editor`를 참조하는 로컬 Editor 어댑터입니다.

## Editor 가져오기

1. `DHMGS > Importer`를 엽니다.
2. 사내 DHMGS 서버의 Base URL과 attempt ID를 입력합니다.
3. 필요할 때만 Generic/Humanoid 베이크용 리그를 선택합니다.
4. **Import**를 누릅니다.

기본 결과 위치는 다음과 같습니다.

```text
Assets/Mnnh.Dhmgs/Content/Generated/{attemptId}/
├─ {attemptId}.asset
├─ manifest.json
├─ sequence.json
└─ 선택한 AnimationClip들
```

선택적으로 저장한 MP4는 Unity 에셋이 아니라 로컬 캐시 경로에 기록합니다.

```text
Application.persistentDataPath/Dhmgs/{attemptId}/video.mp4
```

프로젝트 설정은 자동 변경하지 않습니다. HTTP 허용이나 미리보기 레이어가 필요하면 `DHMGS > Project` 메뉴의 명시적 명령을 사용합니다.

## Player에서 재생

씬 또는 프리팹에서 생성된 `DhmgsMotionAsset`을 직접 참조하면 Unity가 Player 빌드에 포함합니다.

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

다른 사내 Editor 도구에서도 같은 파이프라인을 호출할 수 있습니다.

```csharp
DhmgsImportResult imported = await DhmgsImportPipeline.ImportAsync(
    new DhmgsImportRequest(baseUrl, attemptId));

DhmgsAssetWriteResult written = DhmgsAssetWriter.Write(
    imported,
    new DhmgsAssetWriteOptions());
```

이미 받은 ZIP 바이트를 처리하려면 `DhmgsImportPipeline.ImportArchive`를 사용합니다. 네트워크, 해석, 변환, Unity 에셋 저장 단계가 분리되어 있어 필요한 단계만 조합할 수 있습니다.

## 릴리스 규칙

- `package.json` 버전과 Git 태그를 함께 올립니다. 예: `0.3.0` ↔ `v0.3.0`.
- 소비 프로젝트는 floating branch 대신 릴리스 태그에 고정합니다.
- `Content/Generated`와 프로젝트 전용 rig/scene은 패키지 저장소에 커밋하지 않습니다.
- 100 MiB가 넘는 단일 바이너리는 저장소에 넣지 않고 별도 콘텐츠 전달 방식을 사용합니다.
