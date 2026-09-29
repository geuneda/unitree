# ROADMAP — Three.js 환경 대비 아직 남은 격차

하네스 1차 버전(2026-09-28) 기준으로 **아직 해결하지 못한 문제**를 성질 1~5와 이식성(P, 다른 버전·기존 프로젝트·macOS)별로 기록한다.
하네스를 고치는 작업을 시작하기 전에 여기서 고르고, 해결하면 체크하고 **검증 방법과 측정값**을 남긴다.

- 표기: `[ ]` 미해결 · `[~]` 부분 해결 · `[x]` 해결(아래 "해결됨"으로 옮김)
- 기준: 각 항목은 "Three.js 환경의 어떤 성질을 복원하는가"로 판단한다.
- 측정 기준 머신/상태: Unity 6000.3.11f1, URP 17.3, Code Optimization=Debug, 에디터 GUI 1개.

## 1차 버전 기준선 (비교용)

| 항목 | 값 |
|---|---|
| 루프: 코드 변경 없음 | 3.5–3.8s (build 0.7s 캐시 적중, play 2.6s) |
| 루프: 셰이더만 수정 | ~4s (도메인 리로드 없음) |
| 루프: 모듈 C# 1줄 수정 | ~9.2s (compile+reload 4.1s, build 1.9s, play 2.8s) |
| 루프: C# 컴파일 에러 보고 | ~1s |
| compile-check csc / msbuild | 어셈블리당 ~0.1s / 웜 0.5–2s, 콜드 10–75s |
| 스모크 씬 렌더 | batches ~46, SetPass ~43, tris ~60만 |

---

## 성질 1 — 모든 게 텍스트

- [ ] **G1-1 프로젝트 설정과 URP 에셋이 여전히 YAML**
  - 현상: `ProjectSettings/*.asset`, `Assets/Settings/PC_RPAsset.asset`, `PC_Renderer.asset`(그림자 거리·캐스케이드, MSAA, HDR, SSAO 같은 Renderer Feature, 품질 레벨)은 템플릿 그대로다. 바꾸려면 GUI나 일회성 eval이 필요하다.
  - 방향: 프로젝트 설정용 코드 빌더(예: `ISettingsStep`)를 두고 `harness_setup`/`harness_build`가 RP·Renderer 에셋을 코드로 생성·덮어쓰기. 설정값을 fingerprint에 포함.
  - 완료 기준: RP/Renderer 에셋을 지워도 루프 한 번으로 동일하게 재생성되고, 설정 변경이 코드 diff로만 나타난다.

- [ ] **G1-2 빌더가 에디터 안에서만 실행된다**
  - 현상: 빌더 결과(씬·생성 에셋)를 보려면 반드시 떠 있는 에디터와 루프가 필요하다. 에디터 없이 가능한 건 컴파일 체크까지다.
  - 방향: 조사 필요. 같은 프로젝트를 두 에디터가 열 수 없으므로(프로젝트 잠금) 복제 프로젝트 + batchmode 빌드 등을 검토.

- [ ] **G1-3 파티클·애니메이션·타임라인용 코드 헬퍼가 없다**
  - 현상: `BuildContext`에는 메시·머티리얼·텍스처·Volume·UI·샷 헬퍼만 있다. ParticleSystem, AnimationClip/Animator, Timeline은 Unity API로 직접 쓸 수 있지만 장황하고 틀리기 쉽다.
  - 방향: `ctx.Particles(path, preset => ...)`, 코드로 AnimationClip 커브 생성, Animator 대신 Playables 기반 재생 헬퍼.
  - 완료 기준: 스모크 씬에 코드로만 만든 파티클 1개 + 애니메이션 1개가 캡처에 보인다.

- [~] **G1-4 UI Toolkit 경로는 있지만 얇다**
  - 현상: UXML/USS + `ctx.UIDocument()` + 기본 테마(.tss)는 동작한다. 재사용 컴포넌트, 폰트, 바인딩 예제가 없다.
  - 방향: `Assets/Harness/UI/`에 공용 USS 변수·컴포넌트(버튼, 게이지, 토스트) 추가.

## 성질 2 — 루프가 초 단위

- [ ] **G2-1 C# 1줄 수정에 ~9초 (컴파일 + 도메인 리로드 ~4초가 고정비)**
  - 현상: Vite HMR(~0.1s) 수준은 불가. 루프 시간의 절반이 Unity 컴파일/리로드다.
  - 방향: Pipeline 패키지의 `[CodeReload]` / `reload_file`(메서드 본문 핫패치, 도메인 리로드 없음)을 `loop.ps1 -Hot` 모드로 통합. 플레이 중 상태를 유지한 채 Tick 본문만 교체하고 캡처.
  - 완료 기준: 모듈 Tick 본문 수정 → 2초 이내에 반영된 캡처.
  - 2026-09-29 관측: 다른 프로젝트의 Unity 에디터가 함께 떠 있을 때 `compileSec`이 4.7–19s로 흔들렸다. 구간을 재 보니 컴파일 ~1.7s,
    도메인 리로드부터 Pipeline 서버가 다시 응답할 때까지 ~7s(Unity 로그의 `Domain Reload Profiling`은 ~3s). 에디터 1개일 때 다시 잴 것.

- [ ] **G2-2 GUI 에디터가 떠 있어야 하고, 모달 다이얼로그가 뜨면 멈춘다**
  - 현상: 에디터가 `-automated`로 실행되지 않아 다이얼로그가 메인 스레드를 막을 수 있다(Pipeline descriptor의 `info` 경고). 라이선스 좌석도 점유.
  - 방향: `tools/open-editor.ps1`로 `-automated` 실행, 또는 상주 batchmode 에디터에서 GPU 렌더·캡처가 되는지 검증.

- [ ] **G2-3 도메인 리로드 직후 첫 `harness_build`가 ~2초 (JIT 워밍업)**
  - 방향: 리로드 직후 빌더/fingerprint 코드를 미리 한 번 실행(워밍업), 또는 fingerprint 비용 축소.


## 성질 3 — 에이전트가 화면을 본다

- [ ] **G3-1 오프스크린 캡처에 스크린 공간 UI가 안 찍힌다**
  - 현상: 프리셋 캡처는 카메라 오프스크린 렌더라 UI Toolkit/오버레이 UI가 빠진다. `"screen"` 캡처는 Game 뷰 탭이 보일 때만 되고 해상도가 Game 뷰 크기를 따른다(1차 검증 때 568x562).
  - 방향: `PanelSettings.targetTexture`로 UI를 RT에 렌더해 합성하거나, Game 뷰 해상도를 1280x720으로 고정.
  - 완료 기준: `"auto"` 프리셋 캡처에도 HUD가 1280x720으로 찍힌다.

- [ ] **G3-2 에디터 플레이 모드 FPS는 실제 성능을 대표하지 못한다**
  - 현상: 에디터 오버헤드, autotick, Debug 코드 최적화가 섞인다. 지금은 변경 전후 비교에만 쓸 수 있다.
  - 방향: 개발 빌드 플레이어 + 런타임 Pipeline 서버로 같은 시나리오를 돌리는 `harness_perf`.

- [ ] **G3-3 정지 이미지만 나온다**
  - 현상: 움직임은 여러 컷과 EventBus 카운트로 추론해야 한다.
  - 방향: 시나리오에 연속 캡처(N프레임) → 스프라이트 시트/GIF 출력.

- [ ] **G3-4 시각 회귀 검사가 없다**
  - 방향: `golden/` 기준 이미지와 픽셀 diff(SSIM 등) 점수를 report.json에 포함.

- [ ] **G3-5 이미지 판정이 휴리스틱이다**
  - 현상: `blank`는 밝기 표준편차·색 버킷 수로만 판정. 셰이더 실패로 인한 마젠타(핑크) 머티리얼은 따로 잡지 못한다(컴파일 에러는 `harness_shaders`가 잡음).
  - 방향: 마젠타 픽셀 비율 통계 추가.

## 성질 4 — 에셋 없이도 완성도

- [ ] **G4-1 CPU(C#) 텍스처 베이크가 느리다**
  - 현상: 512² 지형 베이크 ~1초(Debug). 빌드 캐시로 가렸지만 해상도를 올리면 느려진다. 지금 지형 텍스처(512 / 320m)는 흐릿하다.
  - 방향: GPU 베이크 경로(Blit/Compute 셰이더 → RT → PNG), 지형 디테일 텍스처 타일링.

- [ ] **G4-2 스카이박스 앰비언트는 라이팅 베이크가 필요해서 Trilight로 우회 중**
  - 방향: `ctx.BakeSkyReflection()` 큐브맵에서 SH를 계산해 `RenderSettings.ambientProbe`를 코드로 설정.

- [ ] **G4-3 코드로 만든 URP Lit 머티리얼은 키워드를 수동으로 켜야 한다**
  - 현상: `_NORMALMAP` 등을 직접 `EnableKeyword`. 빠뜨리면 조용히 틀린 결과.
  - 방향: `ctx.LitMaterial(...)` 헬퍼가 텍스처 설정에 맞춰 키워드 자동 설정.

- [ ] **G4-4 절차적 라이브러리가 기본 수준이다**
  - 방향: SDF 형상, 스플라인/튜브, 식생·바위 스캐터, 데칼, 절차적 스카이.

## 성질 5 — 병렬 작업이 쉽다

- [ ] **G5-1 에디터 1개 → 루프가 직렬화된다**
  - 현상: 뮤텍스로 안전하게 줄을 세우지만, 에이전트 N명이면 대기가 선형으로 는다(2개 동시 실행 시 두 번째가 3.55s 대기).

- [~] **G5-3 compile-check는 다른 모듈의 최신 변경을 모른다**
  - 2026-09-29 부분 해결(G5-2 작업 중): 한 실행에서 검사하는 어셈블리는 의존 순서로 컴파일해 방금 만든 DLL을 참조한다(체인).
    `-Module`은 참조하는 프로젝트 어셈블리(`Game.Contracts`)를 자동으로 포함하므로 worktree에서 추가한 이벤트 타입이 보인다.
  - 남은 것: 검사 집합 밖(다른 모듈, `-IncludeHarness` 없는 Harness)은 여전히 에디터가 마지막으로 컴파일한 DLL 기준.
    worktree 흐름에서는 그게 곧 submit이 합쳐질 에디터 트리 상태라 문제가 적다.

- [ ] **G5-4 `Assets/Game/Contracts`가 공유 지점이다**
  - 현상: "추가만" 규칙으로 버티는 중. 같은 이벤트 이름을 두 에이전트가 동시에 만들면 충돌.
    submit은 에디터 트리에 이미 있는 계약 파일의 변경을 거부하므로, 에이전트가 자기가 막 추가한(아직 병합 안 된) 계약도 submit으로는 고칠 수 없다.
  - 방향: 이벤트 파일을 발행 모듈별로 분리하는 규칙 강제(lint), 이름 충돌 검사. owners.json처럼 계약 파일도 추가한 worktree를 기록해
    병합 전까지는 그 worktree만 고칠 수 있게.

## 이식성 — `npm install three`처럼 어디에나 붙는다

Three.js는 `npm install three` 한 줄로 이미 있는 프로젝트에 붙고, 버전 범위(semver)로 의존하며, OS를 가리지 않는다.
이 하네스는 지금 **"이 저장소를 클론해서 그 안에서 시작"하는 방식만** 된다. Unity 버전은 6000.3.11f1 하나, OS는 Windows 하나에서만 검증했다.

순서: **P-1(버전) → P-2(기존 프로젝트) → P-3(macOS, 나중)**. 기존 프로젝트는 저마다 다른 6.x 버전을 쓰므로 P-2는 P-1이 먼저 필요하다.
세 항목 모두 검증은 O-8(새 클론 검증 자동화)을 버전·OS·대상 프로젝트별로 돌리는 방식이라 O-8을 같이 진행한다.

- [ ] **P-1 Unity 버전이 6000.3.11f1로 고정돼 있다**
  - 현상:
    - 검증한 버전이 하나뿐이다. `ProjectVersion.txt`와 `manifest.json`(URP 17.3.0, Input System 1.19.0)이 이 버전 기준이고,
      기준선 표와 검증 매트릭스 기대값(fingerprint `b012cf35…`, 컴파일 61행·런타임 68행·셰이더 87행)도 이 버전에서 잰 값이다.
    - 하한: `com.unity.pipeline` 0.8.0-exp.1의 `package.json`이 `"unity": "6000.0"`이다. 2022.3 LTS 같은 Unity 6 미만 버전은
      에디터 연결 방식(Pipeline HTTP 서버 + `[CliCommand]`)을 바꾸지 않는 한 지원할 수 없다.
    - 도구: `compile-check.ps1`은 `ProjectVersion.txt`의 버전으로 에디터를 찾고(Hub 기본 경로 → `unity editors --installed`),
      `Library/Bee/artifacts/*/<Asm>.rsp` 형식에 기댄다. 버전마다 rsp 형식이 같은지 확인하지 않았다.
    - 코드: 이미 버전 차이를 한 곳 우회했다(`enterPlayModeOptionsEnabled`가 6.x에서 obsolete → 리플렉션). 다른 버전에서 컴파일되고 동작하는지는 확인하지 않았다.
    - O-1의 recompile 상태 레이스 우회는 Pipeline 0.8.0-exp.1의 동작에 맞춘 것이다.
    - 프로젝트를 다른 버전 에디터로 열면 업그레이드 확인 모달이 뜬다(G2-2와 같은 문제). `unity open`만으로는 끝까지 진행되지 않을 수 있다.
  - 방향:
    - 지원 범위를 "Unity 6.0 LTS 이상"으로 선언하고 검증 대상 목록을 둔다(예: 6.0 LTS 최신 패치, 6.3 LTS, 최신 정식).
    - 버전별 API 차이는 `#if UNITY_6000_x_OR_NEWER`와 asmdef `versionDefines`(URP 17.0–17.x)로 가른다.
    - 샘플 프로젝트(이 저장소)는 한 버전으로 고정해 두되, `Assets/Harness/`와 `tools/`에는 버전 문자열을 하드코딩하지 않는다.
      compile-check는 실행 중인 에디터 프로세스의 경로를 먼저 쓴다(실제로 컴파일하는 버전이 그 에디터다).
    - 매트릭스 기대값은 버전별로 기록한다. 줄 번호는 모든 버전에서 같아야 한다. fingerprint는 버전마다 달라도 되지만, 같은 버전 안에서는 3회 동일해야 한다.
    - O-8 스크립트에 `-UnityVersion`을 두고 목록의 버전마다 반복한다.
  - 완료 기준: 목록의 각 버전에서 새 클론(해당 버전으로 업그레이드) → `harness_setup` → 매트릭스 1–8 녹색.
    `Assets/Harness/`·`tools/`에서 `6000.` 검색 결과 0건.

- [ ] **P-2 기존 Unity 프로젝트에 붙일 수 없다**
  - 현상: 하네스는 "이 저장소 = 프로젝트"를 전제한다. `Assets/Harness/`와 `tools/`를 기존 프로젝트에 복사하는 방법은 검증하지 않았고, 그대로 복사하면 다음과 부딪친다.
    - 배포 형태: 소스 폴더를 복사하는 것뿐이다. 하네스 버전을 추적하거나 업데이트할 경로가 없다.
    - 필수 의존: `com.unity.pipeline`(실험판), URP, Input System. `Harness.Runtime`이 URP(`HarnessCapture`의 `UniversalAdditionalCameraData`)와
      Input System(`ScriptedInput`)을 직접 참조하므로 Built-in·HDRP 프로젝트나 구 Input Manager만 쓰는 프로젝트에서는 컴파일되지 않는다.
    - **`harness_setup`이 파괴적이고 전역 설정을 바꾼다.** URP 템플릿 샘플(`Assets/Scenes/SampleScene.unity`, `Assets/Readme.asset`,
      `Assets/TutorialInfo`, `Assets/Settings/SampleSceneProfile.asset`)을 지우는데, 기존 프로젝트에 같은 경로가 있으면 사용자 파일이 지워진다.
      `PlayerSettings.runInBackground`·`enableFrameTimingStats`는 출시 빌드 설정까지 바꾼다. Domain Reload off는 프로젝트 전체에 적용되므로,
      static을 초기화하지 않는 기존 코드가 두 번째 플레이부터 오동작한다(규칙 5를 기존 코드는 지키지 않는다).
    - 런타임 주입: `Harness.Runtime`은 모든 플랫폼에 포함되고, `GameRoot.Boot`가 `AfterSceneLoad`에서 무조건 `[GameRoot]`를 만든다.
      기존 게임의 모든 씬과 출시 빌드에도 들어간다.
    - 씬: `harness_play`·`harness_capture`는 빌더가 만든 `Assets/Scenes/Main.unity`만 연다. 손으로 만든 씬, 여러 씬, 부트 씬부터 시작하는 흐름은 돌릴 수 없다.
    - 경로·모듈 규약이 하드코딩돼 있다: `HarnessPaths`(`Assets/Game`, `Assets/Generated`, `Main.unity`), `HarnessLogParse.ModuleOf`(에러의 `module`),
      `HarnessBuild`(빌더는 `Assets/Game/*/Builders/`만 찾음), lint·submit·land 모두 `Assets/Game/<Module>/` 기준.
      기존 코드(대개 `Assembly-CSharp`)는 에러에 `module`이 빈 값으로 나오고 submit할 수 없다.
    - 규칙: "YAML 직접 수정 금지"와 "uGUI 프리팹 금지"를 그대로 두면 기존 프로젝트의 씬·프리팹·uGUI를 다룰 수 없다.
      기존 자산은 에디터 API로만 고친다는 식의 규칙과 기존 프로젝트용 CLAUDE.md 템플릿이 필요하다.
    - Windows 경로 60자 제한(F-5) 때문에 긴 경로에 있는 기존 프로젝트는 위치를 옮기라고 요구하게 된다.
  - 방향:
    - `Assets/Harness/`를 UPM 패키지로 분리한다(예: `com.geuneda.agentharness`, git URL `?path=`). 이 저장소의 샘플 프로젝트도 그 패키지를 쓴다(도그푸딩).
      도구 스크립트는 패키지의 `Tools~/`에 넣어 패키지 버전과 함께 움직이게 하고, 프로젝트에는 얇은 진입점만 둔다.
    - 설치 스크립트(`install.ps1 -Project <경로>`)가 패키지 추가, 진입점, `.gitignore` 항목(`HarnessOut/`, 생성물), 설정 파일을 만든다.
      `-WhatIf`로 바꿀 목록부터 보여 주고, 제거 스크립트로 원상 복구할 수 있게 한다.
    - 텍스트 설정 파일(예: `ProjectSettings/AgentHarness.json`)에 모듈 루트, 생성물 경로, 플레이할 씬(빌드 씬 / 기존 씬 경로 / 빌드 설정의 첫 씬),
      Domain Reload 정책을 둔다. `HarnessPaths` 상수를 이 설정으로 바꾼다.
    - `harness_setup`을 둘로 나눈다. 새 프로젝트용은 지금처럼 동작하고, 붙이기용은 아무것도 지우지 않으며 전역 설정은 보고만 하고 동의할 때만 바꾼다.
      Domain Reload가 켜진 상태에서도 루프가 돌게 하고, 느려진 만큼은 `timings`로 보고한다.
    - URP·Input System 의존은 `versionDefines`로 선택 사항으로 만든다. 없으면 해당 기능(카메라 데이터 복사, 입력 재생)만 꺼지고 컴파일은 된다.
    - 런타임 주입을 막는다. `GameRoot`는 등록된 모듈이 있을 때만 만들고, 출시 빌드에서는 하네스가 빠지게 한다(Editor·Development 빌드 한정 또는 define).
    - 모듈 개념을 "설정한 폴더 = 모듈"로 일반화해서 기존 코드 폴더도 에러 `module` 귀속, lint, submit/land 대상이 되게 한다.
  - 완료 기준: 하네스를 모르는 기존 프로젝트 2개(템플릿이 아닌 URP 게임 1개 + Built-in 프로젝트 1개)에 설치 스크립트를 한 번 실행한다.
    기존 파일 삭제·변경이 없어야 하고(`git status`에 설치가 추가한 파일만), 기존 씬으로 루프가 녹색이어야 한다(캡처·콘솔·FPS).
    출시 빌드에 `Harness.*` 어셈블리가 없어야 하고, 제거 스크립트를 돌리면 `git status`가 깨끗해야 한다.

- [ ] **P-3 Windows에서만 동작한다 (macOS 지원은 나중)** — 기존 O-2를 옮겨 왔다.
  - 작업 방식: P-1·P-2를 Windows에서 끝낸 뒤 실제 Mac에서 진행한다. 그 전까지 Windows 작업에서는 새 코드에
    백슬래시 경로 리터럴이나 Windows 전용 호출(`powershell.exe`, `C:\...`, `.exe` 경로)을 늘리지 않는 것만 지킨다.
  - 현상:
    - 모든 도구가 Windows PowerShell 5.1 전용이다. 5.1은 BOM 없는 UTF-8을 깨뜨리므로 스크립트를 ASCII로만 쓴다.
      `submit.ps1`은 compile-check 게이트를 `powershell.exe`로 직접 실행한다.
    - 경로: `Join-Path $root 'Library\Harness\submit'`처럼 백슬래시 리터럴이 흔하고, 그 결과를 `[IO.File]::ReadAllText` 같은 .NET API에 그대로 넘긴다
      (예: Pipeline 디스크립터 `Library\Pipeline\.unity-pipeline-port`). macOS에서는 `\`가 경로 구분자가 아니다.
      `compile-check.ps1`은 `C:\Program Files\Unity\Hub\Editor\<ver>\Editor\Unity.exe`, `Data\NetCoreRuntime\dotnet.exe`,
      `Data\DotNetSdkRoslyn\csc.dll`을 가정한다(macOS는 `Unity.app/Contents/...` 아래).
    - 락: `Global\AgentHarnessEditor_<id>` 이름 있는 Mutex를 쓴다. .NET은 Unix에서도 이름 있는 뮤텍스를 지원하지만,
      보유 프로세스가 죽었을 때 `AbandonedMutexException`이 오는지 확인하지 않았다. submit/land 저널 복구가 이 동작에 기댄다.
    - msbuild 백엔드(vswhere)는 Windows 전용이다. macOS에서는 csc 백엔드만 쓴다.
    - 에디터: Metal에서 셰이더 에러 형식이 `harness_shaders` 파싱과 맞는지, 포커스 없는 에디터(App Nap)에서도 플레이·캡처가 진행되는지
      (F-1과 같은 종류의 문제) 확인해야 한다.
    - 결정성: Apple Silicon(ARM64) JIT의 부동소수점 결과가 x64와 달라서 fingerprint가 OS·CPU마다 다를 수 있다(F-6처럼 절차적 베이크 결과가 바뀜).
      기준을 "같은 머신 안에서 결정적"으로 둘지 먼저 정해야 한다.
    - README에 macOS용 Unity CLI 설치 방법이 없다.
  - 방향: 도구를 PowerShell 7(pwsh, 크로스플랫폼)로 옮긴다. Windows에서도 pwsh 7을 요구할지, 5.1 호환을 유지할지 정해야 한다.
    경로는 `/`와 다단 `Join-Path`로 통일하고, 에디터·dotnet·csc 경로는 `unity editors --installed`나 실행 중인 에디터 프로세스에서 얻는다.
    Unix에서는 락을 파일 락(배타 핸들)으로 바꾸는 것도 검토한다.
  - 완료 기준: Apple Silicon Mac에서 새 클론 → `harness_setup` → 매트릭스 1–8 녹색(macOS 기준값은 따로 기록). 같은 스크립트로 Windows 매트릭스도 녹색.

## 하네스 자체

- [ ] **O-1 Pipeline 패키지 0.8.0-exp.1(실험판) 의존**
  - 현상: `recompile` 상태 레이스(직전 실패 후 옛 실패 보고 / `triggered` 고착)를 `Invoke-HarnessRecompile`의 컴파일 세대 번호 + idle 판정으로 우회 중.
  - 할 일: 패키지를 업그레이드할 때마다 loop 검증 매트릭스(아래)를 다시 돌린다.
- **O-2 스크립트가 Windows PowerShell 5.1 전용** → P-3으로 옮겼다(2026-09-29).
- [ ] **O-3 하네스 자체의 자동 테스트가 없다.** 아래 매트릭스를 스크립트(`tools/selftest.ps1`)로 만든다.
- [ ] **O-4 Pipeline `quit` 커맨드가 편집 모드에서 실패한다** (`PipelineQuitScheduler`가 DontDestroyOnLoad 호출).
  에디터를 코드로 닫는 믿을 만한 방법이 없다. `EditorApplication.delayCall` 경유 `Exit`도 백그라운드 에디터에선 실행되지 않는다.
  2026-09-29 클론 검증 중 메인 에디터가 정상 종료 절차로 꺼졌는데 원인을 로그로 특정하지 못했다.
  확인된 우회: `eval`로 `EditorApplication.Exit(0)`를 **직접** 호출하면 백그라운드 에디터도 정상 종료된다 → `harness_quit`으로 만들 것.
  2026-09-29 재확인: `uc.ps1 eval_file`로 3회 모두 1–2s 안에 정상 종료(응답은 빈 본문이라 `unexpected reply (HTTP 200)`).
- [ ] **O-5 `%TEMP%` 아래 프로젝트에서 Burst JIT DLL 로드가 막힌다** (LoadLibrary error 4551 = Windows 애플리케이션 제어 정책).
  editorErrors로만 보고된다. 프로젝트를 Temp에 두지 말 것.
- [ ] **O-6 도메인 리로드 직후 Unity Search 인덱서 예외** (`UnityEditor.Search.SearchInit.IndexationOnStartup`, ArgumentOutOfRange).
  에디터 내부 에러라 `editorErrors`로 분류(루프 실패 아님). Unity 쪽 수정 전까지 유지.
- [ ] **O-7 에디터 두 개를 같이 띄웠을 때 전역 `Editor.log`가 1.4GB까지 커졌다** ("Access version should be odd when acquiring lock" 반복).
  원인 미확인. 에디터마다 `-logFile`을 따로 주는 실행 스크립트로 막는다.
- [ ] **O-8 새 클론 검증을 자동화한다.** 짧은 경로에 클론 → 에디터 실행 → `harness_setup` → loop 3회를 스크립트로(`tools/fresh-clone-test.ps1`).
  사람이 수동으로 돌려서 아래 해결됨 항목들을 찾았다.
  P-1~P-3 검증도 이 스크립트를 버전(`-UnityVersion`)·OS·대상 프로젝트별로 돌리는 방식이라 먼저 만들어 둘 것.

### 검증 매트릭스 (하네스를 고친 뒤 매번)

1. `loop.ps1` 3회 연속 녹색, `build.fingerprint`·`play.events` 동일, PNG를 Read로 확인
2. C# 컴파일 에러 주입 → `stage=compile`, file/line/module 정확 → 원복 후 녹색
3. 런타임 예외 주입 → `stage=runtime`, 정확한 줄 → 원복 후 녹색
4. HLSL 에러 주입 → `stage=shader`, 재임포트 없는 다음 루프에서도 검출 → 원복 후 녹색
5. 리셋 없는 static 추가 → `stage=lint` → 원복
6. 루프 2개 동시 실행 → 두 번째가 대기 후 성공
7. worktree 격리(G5-2): 에이전트 worktree 2개. A가 깨진 코드를 `submit.ps1 -SkipCheck` → `stage=compile` + `reverted` + `restore.ok`,
   그 사이 B의 `submit.ps1`은 락 대기 후 녹색. 게이트(`-SkipCheck` 없이)는 에디터 트리를 건드리지 않고 거부. submit 도중 kill →
   다음 `loop.ps1`에 `recoveredSubmit`, 녹색. 끝나면 메인 트리 `git status`로 테스트 사본이 남지 않았는지 확인
8. land(G5-5): 새 모듈을 submit → 커밋 → `land.ps1` 녹색(에디터 트리 `git status` 깨끗, stash 버림, 소유 해제), 그 사이 다른 worktree의
   submit은 락 대기 후 녹색. 컴파일 에러 커밋 land → `stage=compile` + `land.reverted` + `restore.ok`, HEAD·`git status` 동일.
   land를 병합 직후 kill → 다음 `loop.ps1`에 `recoveredLand`, 녹색, HEAD·`git status` 동일. `.meta` 미커밋·충돌 → `stage=land` 거부, 무변경.
   테스트 병합 커밋은 끝나면 `git reset --mixed <테스트 전 커밋>`으로 걷어내고 테스트 모듈 파일을 지운다

---

## 해결됨

(해결한 항목을 여기로 옮기고 날짜, 방법, 검증 결과, 측정값을 적는다.)

- [x] **F-6 에디터를 재시작하면 예외 줄 번호와 build fingerprint가 바뀜** (2026-09-29)
  - 현상: 메모리 부족으로 에디터가 꺼진 뒤 다시 열자 같은 코드의 fingerprint가 `b012cf35…` → `6b977ecd…`, 런타임 예외 줄이 68 → 74(메서드 끝).
    새 세션끼리는 결정적이었다(재시작 2회, 모듈 추가·삭제 리로드, `no_cache` 재빌드 모두 `6b977ecd…`).
  - 원인: `harness_setup`이 바꾸는 `CompilationPipeline.codeOptimization`은 **에디터 세션 동안만** 유지된다. 재시작하면 사용자 전역
    "Code Optimization On Startup"(Release)로 돌아간다. Release JIT에서는 절차적 메시·텍스처의 float 결과가 달라진다
    (`fingerprint.prev.txt` diff: `SpinnerKnot.asset`, `TerrainMesh.asset`, `TerrainAlbedo.png` 세 줄만 다름). `build.warnings`에만 보고돼 놓쳤다.
  - 수정: `HarnessCodeOptimization`([InitializeOnLoad], Harness.Editor)이 세션이 시작될 때 이 프로젝트만 Debug로 되돌린다(배치 모드 제외,
    재컴파일 1회). 전역 EditorPrefs는 다른 프로젝트에 영향을 주므로 건드리지 않는다. 빌드 덤프가 바뀌면 직전 덤프를
    `Library/Harness/fingerprint.prev.txt`로 남겨 다음에는 바로 diff할 수 있게 했다.
  - 검증: `EditorApplication.Exit(0)` → `unity open` → ready 직후 첫 루프부터 녹색, fingerprint `b012cf35…`, 경고 없음(2회).
    그 세션에서 매트릭스 1–6 녹색: fingerprint 3회 동일, 컴파일 61행·**런타임 68행**·셰이더 87행 정확, lint, 동시 루프 두 번째 2.96s 대기 후 녹색.
    루프(변경 없음) 3.4–3.5s.

- [x] **F-7 에디터를 막 열면 첫 루프가 `stage=editor`로 실패** (2026-09-29)
  - 원인: `unity status`가 ready여도 Pipeline 서버가 잠시 503 "Server Busy"를 준다. 루프 시작 ping은 연결 끊김·401(`unreachable`)만
    재시도하고 busy는 바로 실패로 처리했다(재시작 직후 루프 2회 연속 0.2s 만에 `stage=editor`).
  - 수정: 에디터 프로세스가 살아 있으면 busy도 기다린다(최대 120s, F-6의 Debug 재컴파일 포함). 기다린 시간은 `timings.editorWaitSec`.
  - 검증: 재시작 → ready 직후 루프가 26.9s(`editorWaitSec`) 기다린 뒤 녹색. 3회 재시작 모두 첫 루프 녹색.

- [x] **G5-5 worktree 브랜치 병합(landing)이 수동이다** (2026-09-29)
  - 원인: submit한 파일은 에디터 트리에 미커밋 사본으로 남아 `git merge`가 "untracked working tree files would be overwritten"으로 거부한다.
    손으로 하던 절차(그 경로만 stash → merge → drop)는 락 없이 에디터 트리를 건드리고, 병합 결과를 검증하지 않고, 실패해도 되돌리지 않았다.
    "한 모듈 = 한 에이전트"도 규칙뿐이라 두 worktree가 같은 모듈을 번갈아 submit하면 조용히 덮어썼다.
  - 방법: `tools/land.ps1 [-Branch agent/foo]` (worktree에서는 인자 없이 그 브랜치). submit과 같은 트랜잭션 모양.
    - 락 전: 브랜치를 체크아웃한 worktree에 미커밋 파일이 있으면 거부(커밋된 것만 병합됨).
    - 락 안, 무변경 거부: 에디터 트리 detached/병합 중/staged · `git merge-tree --write-tree`(객체 저장소 안에서만 병합)로 충돌 ·
      브랜치가 `Assets/`에 추가하는 파일·폴더의 `.meta` 누락(새 클론이 다른 GUID를 얻게 됨) · 건드리는 모듈에 다른 살아 있는 worktree의 미병합 submit
      (`-Takeover`) · 덮어쓸 미커밋 변경 중 이 브랜치의 submit 사본도 아니고 병합 결과와도 다른 것(`land.foreign`, 예: 에디터 트리 직접 수정).
    - 저널(`Library/Harness/land/pending.json`) → 병합 경로 + 병합하는 모듈의 미커밋 사본만 `git stash push -u --pathspec-from-file`
      → `refs/agentharness/land/<runId>`로 옮김 → `git merge` → 루프. 녹색이면 stash 버림 + 소유 해제, 빨가면 병합한 경로만
      `git reset --keep` + stash 복원 + 재컴파일. 도중에 죽으면 다음 락 보유자(`Enter-HarnessLock`)가 저널로 되돌림(`recoveredLand`).
    - 소유권: submit이 모듈별로 마지막에 반영한 worktree를 `Library/Harness/submit/owners.json`에 기록. 다른 살아 있는 worktree가 올린 미병합 변경이
      에디터 트리에 남은 모듈은 submit·land 모두 거부(`-Takeover`로 인수). 에디터 트리 브랜치에 worktree가 모르는 그 모듈 커밋이 있으면
      submit 거부(미러링이 병합된 작업을 되돌리므로) → `git merge master`.
    - git은 `Invoke-HarnessGit`(Process, UTF-8, stderr 캡처, 종료코드)으로 호출한다. 경로 목록은 `--pathspec-from-file`(명령줄 길이 제한 없음).
  - 만들다 겪은 것:
    - `refs/stash`는 모든 worktree가 공유한다 → 에이전트의 `git stash pop`이 land 중인 stash를 가져갈 수 있어 전용 ref로 옮긴다.
    - .NET이 리다이렉트한 자식 stdin에 콘솔 인코딩의 BOM을 먼저 써서 `git hash-object --stdin-paths`의 첫 경로가 깨졌다 → 인자로 넘긴다.
    - 처음엔 남의 미커밋 변경도 stash에 넣고 병합 후 stash 목록에 남겼는데, 그 stash는 이미 추적되는 파일과 겹쳐 `git stash apply`로 깨끗이
      돌아오지 않았다 → 병합 전에 계산해 거부하는 쪽으로 바꿨다(land가 대신 치우는 미커밋 파일 = 같은 내용이거나 이 브랜치의 submit 사본뿐).
  - 검증(worktree 2개 `wt-a`/`wt-b`, 에디터 1개; **다른 프로젝트 에디터도 실행 중**이라 compile+reload가 6–20 s로 기준선 ~4 s보다 느림):
    - **완료 기준**: submit(녹색) → 커밋 → land가 fast-forward 6.7 s(검사 1.1 / stash+merge 0.5 / compile 0.06 / build 0.9 / play 2.9),
      병합 커밋 5.2–6.2 s. 에디터 트리 `git status` 깨끗, stash 버림, `releasedOwners`. 병합한 모듈의 이벤트가 `play.events`에 보임.
    - B의 land 0.3 s 뒤 A가 새 모듈 submit → A는 락 4.2 s 대기 후 녹색, B 녹색.
    - 컴파일 에러 커밋 land → `stage=compile`(정확한 줄) → `reset --keep` + stash 복원 + 복구 컴파일 ok. HEAD·`git status`·파일 내용 동일,
      다른 에이전트의 미병합 모듈(LandC) 그대로.
    - 병합 직후(1.9–2.8 s) land kill → 다음 `loop.ps1`이 `recoveredLand`(reset --keep + stash apply) 후 녹색, HEAD·`git status` 동일.
    - 무변경 거부: 충돌 0.8–1.3 s(`conflicts`), `.meta` 미커밋 1.0–2.0 s(파일·폴더·Contracts `.meta` 4개), worktree 미커밋 0.9 s,
      남의 미병합 submit 1.6–2.0 s, 에디터 트리 직접 수정 2.0 s(`foreign`), 이미 병합된 브랜치 1.3 s(녹색, 아무것도 안 함).
    - submit: 남의 미병합 모듈 1.9 s 거부 → `-Takeover` 녹색. worktree에 없는 master 커밋이 있는 모듈 1.7 s 거부.
    - 서로 다른 줄을 고친 두 브랜치를 차례로 land → 3-way 병합으로 두 변경 모두 반영.
    - 검증 매트릭스 1–8 녹색: fingerprint 3회 동일(`b012cf35…`), 컴파일 61행·런타임 68행·셰이더 87행 정확, 재임포트 없는 셰이더 재검출,
      lint, 동시 루프 두 번째 대기 3.41 s 후 녹색, worktree 격리(7), land(8). 루프(변경 없음) 3.9–4.0 s.

- [x] **G5-2 작업 트리를 공유해야 해서, 남의 컴파일 에러가 모든 루프를 막는다** (2026-09-29)
  - 원인: 에디터는 프로젝트 경로 하나에 묶여 있고, Unity는 컴파일 에러가 하나라도 있으면 도메인 리로드를 안 한다.
    여러 에이전트가 에디터 트리를 직접 고치면 한 명의 쓰다 만 코드가 모두의 루프를 `stage=compile`로 멈춘다.
  - 방법(방향 a): 에이전트는 각자 `git worktree`(Library/ 없음)에서 작업하고 `tools/submit.ps1 -Module <M>`으로만 에디터 트리에 넣는다.
    - `Harness.psm1`이 작업 루트(worktree)와 에디터 루트를 구분한다. `Library/`가 없는 체크아웃은 `git worktree list`의 메인 worktree
      같은 하위 경로를 에디터 트리로 쓴다(`AGENTHARNESS_EDITOR_ROOT`로 재정의). 락·HTTP 디스크립터는 항상 에디터 트리 기준.
    - submit = compile-check 게이트(락 없음) → 락 → 백업+저널 → `Assets/Game/<M>/` 미러링, Contracts는 새 파일만 → 루프
      (loop.ps1 본문을 `Invoke-HarnessLoop`으로 옮겨 공유) → 녹색이면 유지 + Unity가 만든 `.meta`를 worktree로 되복사,
      아니면 되돌리고 재컴파일. 컴파일 실패는 항상 되돌림, 나머지 실패는 기본 되돌림(`-KeepOnFail`).
    - 저널(`Library/Harness/submit/pending.json`): submit이 도중에 죽으면 다음 락 보유자(`Enter-HarnessLock`)가 자동 롤백 → `recoveredSubmit`.
    - worktree에서 `loop.ps1`은 거부(`stage=submit`) — 에디터가 컴파일하는 건 worktree가 아니다.
    - compile-check: worktree 소스 + 에디터 트리의 응답 파일/DLL, 의존 체인(G5-3 부분 해결), 새 어셈블리는 응답 파일 합성.
    - 함께 고친 잠복 버그: 도메인 리로드 때 Pipeline 서버가 토큰을 바꾸는 사이 401(`success` 없음)이 와서 StrictMode 모듈이 예외를
      던지고 루프 시작 ping이 `stage=editor`로 실패했다(락이 리로드 직후 넘어가는 submit에서 드러남). 401은 `unreachable`로 정규화,
      루프 시작 시 에디터 프로세스가 살아 있으면 최대 60s 재시도.
  - 검증(에이전트 worktree 2개 `wt-a`/`wt-b`, 에디터 1개; 이 시점 다른 프로젝트 에디터도 실행 중):
    - 게이트: A가 `SmokeModule.cs`에 CS0103 → submit이 0.96s에 `stage=compile`(file/line/module 정확), 에디터 트리 무변경.
    - **완료 기준**: A가 같은 코드를 `-SkipCheck`로 강제 submit하고 0.5s 뒤 B가 새 모듈 `Probe` + 새 계약 `ProbeEvents.cs`를 submit.
      A: `stage=compile`(SmokeModule.cs:61), 되돌림 + 복구 컴파일 ok, 7.4s(sync 0.13 / compile 1.28 / restore 5.66).
      B: 게이트 1.09s → 락 대기 5.71s → **녹색**(`ProbeEcho`=2, `SpinnerLap`=2, modules Stage·Smoke·Probe), `.meta` 4개 worktree로 되복사, 18.8s.
    - 런타임 예외 submit → `stage=runtime`(SmokeModule.cs:68 정확) → 되돌림, `restore.ok`. `-KeepOnFail`이면 유지, 원본 재submit으로 원복(미러링).
    - 기존 Contracts 파일 수정 → `stage=submit`으로 거부(1.1s), 저널·복사 없음.
    - 동기화 직후(0.8s) submit 프로세스 kill → 깨진 코드가 에디터 트리에 남은 상태에서 `loop.ps1` → `recoveredSubmit` + 녹색.
    - compile-check(worktree, 웜): `-Module Smoke` 0.54s(Contracts 포함 3개), 새 모듈 `Probe` 합성 검사 0.41s, 합성 경로도 CS1061을 정확히 잡음.
    - 검증 매트릭스 1–6 녹색: fingerprint 3회 동일(`b012cf35…`), 컴파일 61행·런타임 68행·셰이더 87행 정확, 재임포트 없는 셰이더 재검출,
      lint, 동시 루프 두 번째 대기 3.15s 후 녹색. 루프(변경 없음) 4.1–5.2s.
  - 남은 것: 브랜치 병합 자동화와 "한 모듈 = 한 에이전트" 강제는 G5-5(2026-09-29 해결). 루프 자체는 여전히 직렬(G5-1).

- [x] **G2-4 msbuild compile-check 콜드 스타트 10–75초** (2026-09-29, G5-2 작업 중)
  - 수정: `compile-check.ps1` 기본 백엔드를 `csc`로(에디터와 같은 응답 파일·Roslyn). msbuild는 `-Backend msbuild`로 남김.
  - 측정: `-Module Smoke` 웜 0.54–0.6s(3개 어셈블리), 첫 실행(컴파일러 서버 콜드) 2.7s. `-Module Smoke,Stage -IncludeHarness` 7개 1.1s.

- [x] **F-1 백그라운드 에디터에서 `harness_play`가 진입하지 않음** (2026-09-29)
  - 원인: `EditorApplication.delayCall`은 "인스펙터 갱신 후" 호출되는데, 포커스 없는 에디터는 갱신이 없어 영원히 대기 → 73s 타임아웃.
    포커스가 있던 원래 에디터에서는 드러나지 않았다.
  - 수정: `EditorApplication.isPlaying = true` 직접 설정(자체적으로 프레임 끝까지 지연됨).
  - 검증: 새 클론(57자 경로, `editorFocused=false`)에서 loop 3회 녹색, 약 4.3s/회.
- [x] **F-2 새 프로젝트의 첫 빌드만 fingerprint가 다름** (2026-09-29)
  - 원인: URP Lit `.mat`은 처음 생성될 때만 URP 임포트 후처리기가 검증(RenderType 태그, MOTIONVECTORS 패스 비활성,
    `_BaseColor`→`_Color` 동기화)하고, 이후 제자리 덮어쓰기는 검증 없이 복사.
  - 수정: `BuildContext.Material()`이 매번 `ShaderGUI.ValidateMaterial`을 호출(`BuildContext.ValidateMaterial`).
  - 검증: 생성물·캐시 삭제 후 빌드 1회차와 2·3회차 fingerprint 동일(`e0a075e5…`).
- [x] **F-3 에디터 내부 에러가 루프를 실패시킴** (2026-09-29)
  - 수정: 스택과 메시지에 `Assets/`가 없는 에러는 `editorErrors`로 분리(보고는 하되 `ok`에 영향 없음).
    런타임 예외·컴파일 에러는 그대로 실패로 잡히는 것을 재확인.
- [x] **F-4 새 클론에서 에디터만 열어도 ProjectSettings가 modified** (2026-09-29)
  - 원인: Unity는 LF로 쓰고 git `core.autocrlf`는 CRLF로 체크아웃. 수정: 저장소 루트 `.gitattributes`(`* text=auto eol=lf`).
- [x] **F-5 긴 경로에 두면 Unity가 패키지 파일을 못 읽음** (2026-09-29)
  - 149자 경로에서 `DirectoryNotFoundException` 대량 + 플레이 실패, 49·57자 경로 정상. README 요구사항에 "60자 이하" 명시.
