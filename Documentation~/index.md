# MNNH DHMGS Package Guide

## 구조

- `Runtime/`: 런타임 API, SMPL-X 적용, 서버 다운로드, 재생, 베이크 로직
- `Editor/`: 임포터 창, Editor coroutine, 프로젝트 부트스트랩
- `Assets/Mnnh.Dhmgs/Content`: 패키지 밖에 보관하는 릴리스 후보 콘텐츠와 `Generated` 결과

## 릴리스 통과 조건

1. `package.json` 파싱 성공
2. 패키지 루트 내 `.meta` 누락 및 GUID 중복 없음
3. 패키지 자산이 소비 프로젝트의 `Assets/` 자산을 참조하지 않음
4. 새 Unity 6000.5.1f1 프로젝트에서 Git URL 설치 성공
5. Editor 컴파일, Player 빌드, 주요 다운로드/재생 흐름 확인
6. Git tag와 `package.json` 버전 일치
