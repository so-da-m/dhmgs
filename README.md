# MNNH DHMGS

Unity에서 DHMGS 결과를 다운로드하고, 모션 자산으로 베이크하며, SMPL-X 리그에서 재생하기 위한 사내 Unity Package Manager 패키지입니다.

## 패키지 루트

배포 저장소에는 기본적으로 다음 항목만 필수입니다.

- `package.json`
- `Runtime/` 및 `Runtime.meta`
- `Editor/` 및 `Editor.meta`
- `SmplxRig/Raw/` 의 기본 `SmplxFemale.fbx`, `SmplxMale.fbx`, `SmplxNeutral.fbx`
- `README.md`, `CHANGELOG.md`, `LICENSE.md`, `Third Party Notices.md`

기본 SMPL-X Female/Male/Neutral FBX 세 개는 패키지에 포함됩니다. Kyra, 의상, Humanoid 변형, 프리팩, 폰트, 신, 컨트롤러는 패키지 밖이지만 같은 DHMGS 상위 폴더인 `Assets/Mnnh.Dhmgs/Content`에 선택 콘텐츠로 분리되어 있습니다. GitHub 패키지 저장소에는 `Package` 내용만 올립니다.

현재 Unity 프로젝의 DHMGS 구조는 다음과 같습니다.

```text
Assets/Mnnh.Dhmgs/
├─ Package/             # Git/UPM으로 배포할 패키지
└─ Content/             # 소비 프로젝에만 남는 콘텐츠
   ├─ Generated/        # 다운로드·베이크 결과
   ├─ SmplxRig/         # 선택 모델/프리팩
   ├─ Rig/
   ├─ Prefabs/
   ├─ Fonts/
   └─ Scene/
```

## 명시적 의존성

`package.json`은 현재 검증 프로젝트에서 사용 중인 다음 의존성을 고정합니다.

- Unity UI / TextMesh Pro: `com.unity.ugui` 2.5.0
- Input System: `com.unity.inputsystem` 1.19.0
- Universal Render Pipeline: `com.unity.render-pipelines.universal` 17.5.0
- Animation, Playables/Director, Video, UnityWebRequest 내장 모듈

기준 Unity 버전은 `6000.5.1f1`입니다. 다른 Unity 버전을 지원하려면 의존성 버전을 낮추기 전에 깨끗한 프로젝트에서 임포트와 Player 빌드를 다시 검증해야 합니다.

## 선택 콘텐츠 규칙

`Assets/Mnnh.Dhmgs/Content`에서 릴리스에 포함할 자산을 고를 때는 다음 규칙을 지킵니다.

1. Unity Editor에서 자산을 이동하여 `.meta` GUID를 보존합니다.
2. 프리팩만 고르지 말고 `AssetDatabase.GetDependencies` 결과에 나오는 FBX, material, font asset까지 함께 포함합니다.
3. `Scene/` 과 `Base.controller`는 프로젝트 로컬 자산을 참조하는 데모 콘텐츠이므로, 외부 GUID 참조를 정리한 후에만 릴리스에 포함합니다.
4. 기본 FBX는 개별 크기가 GitHub의 100 MiB 제한보다 작으므로 일반 Git 바이너리로 추적합니다. 100 MiB를 넘는 추가 자산은 패키지에 포함하지 마십시오.
5. 콘텐츠를 고른 뒤 새 Unity 프로젝트에 Git URL로 설치하여 missing script, missing material, missing prefab이 없는지 확인합니다.

`Samples~` 폴더로 옮기는 것만으로는 Git 다운로드 크기가 줄지 않습니다. 대용량 리그를 제외하려면 릴리스 저장소에서 해당 파일을 실제로 제외하거나 별도 콘텐츠 저장소로 분리해야 합니다.

## Git URL 설치

패키지 폴더 자체가 GitHub 저장소 루트인 경우:

```text
https://github.com/<organization>/mnnh-dhmgs.git#v0.1.0
```

Unity 프로젝트 저장소의 `Assets/Mnnh.Dhmgs/Package` 하위 폴더로 배포하는 경우:

```text
https://github.com/<organization>/<repository>.git?path=/Assets/Mnnh.Dhmgs/Package#v0.1.0
```

전체 Unity 프로젝트를 저장소로 삼으면 UPM이 패키지 하위 폴더만 사용하더라도 Git은 저장소 전체를 가져옵니다. 배포용으로는 이 패키지 폴더만 담은 별도 저장소를 권장합니다.

## 다운로드 및 생성 경로

- Editor에서 생성하는 ScriptableObject, JSON, AnimationClip: `Assets/Mnnh.Dhmgs/Content/Generated/{attemptId}`
- 영상 MP4: `Application.persistentDataPath/Dhmgs/{attemptId}/video.mp4`

Git/UPM 패키지는 설치 후 읽기 전용일 수 있으므로 다운로드 결과를 `Package` 안에 쓰지 않습니다. Editor 자산은 같은 DHMGS 상위 폴더의 `Content/Generated`에, Player에서 쓰는 영상은 플랫폼이 허용하는 `persistentDataPath`에 저장합니다.

## 설치 시 프로젝트 변경

Editor 부트스트랩은 현재 다음 작업을 자동으로 수행합니다.

- TMP Essential Resources 임포트
- `DhmgsHumanoid` 레이어 생성
- Insecure HTTP를 `AlwaysAllowed`로 설정

서버 URL 기본값은 코드와 프리팩에 직렬화되어 있으므로 사내 환경에 맞게 확인해야 합니다.

## 릴리스

- 패키지 버전과 Git tag를 함께 올립니다: `0.1.0` ↔ `v0.1.0`.
- 소비 프로젝트의 `packages-lock.json`은 설치된 Git commit을 고정합니다.
- 편집 가능한 개발은 로컬/embedded package에서 하고, 배포 버전은 tag로 고정합니다.
