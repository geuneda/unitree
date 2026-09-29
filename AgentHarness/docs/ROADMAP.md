# ROADMAP — Three.js 환경 대비 아직 남은 격차

하네스 1차 버전(2026-09-28) 기준으로 **아직 해결하지 못한 문제**를 성질 1~5와 이식성(P, 다른 버전·기존 프로젝트·macOS)별로 기록한다.
하네스를 고치는 작업은 아래 "작업 순서"에서 다음 워크플로우를 골라 시작하고, 항목을 해결하면 체크하고 **검증 방법과 측정값**을 남긴다.

- 표기: `[ ]` 미해결 · `[~]` 부분 해결 · `[x]` 해결(아래 "해결됨"으로 옮김)
- 기준: 각 항목은 "Three.js 환경의 어떤 성질을 복원하는가"로 판단한다.
- 측정 기준 머신/상태: Unity 6000.3.11f1, URP 17.3, Code Optimization=Debug, 에디터 GUI 1개. 버전별 기대값은 CLAUDE.md "Unity 버전".
- 하네스를 고친 뒤에는 아래 "검증 매트릭스"를 돌린다: 1–8 = `tools/selftest.ps1`, 9 = `tools/fresh-clone-test.ps1 -SelfTest`(버전별 `-UnityVersion`),
  10 = `tools/attach-test.ps1`(기존 프로젝트 클론별).

## 작업 순서 — 워크플로우 단위

아래 성질별 항목을 **한 번에 착수·검증·커밋하는 작업 묶음(W1…)**으로 나눴다. 요청은 "W1 진행해"처럼 워크플로우 단위로 한다.
- 묶는 기준: 같은 파일을 고치거나 같은 검증으로 확인되는 항목. 순서 기준: **검증 도구를 먼저 믿을 수 있게 만들고(W1–W3), 그다음 결과물을 바꾼다(W4~)**.
- 항목의 현상·방향·완료 기준은 성질별 절에 그대로 두고, 여기에는 묶음·순서·선행·추가로 볼 것만 적는다.
- 공통 마무리: 매트릭스 1–10 녹색 + 샷 PNG 확인 → 항목을 "해결됨"으로 옮기고 측정값 기록 → 이 표의 상태 갱신 → 커밋(메시지에 항목 ID).
- 하네스 변경은 에디터 트리에서 selftest로 검증하므로 워크플로우는 한 번에 하나씩 진행한다. 선행이 없는 W5·W7·W9는 앞당겨도 된다.
- 크기: S = 파일 1–2개 · M = 여러 파일 또는 새 커맨드 · L = 조사가 필요하거나 새 하위 시스템.

| 순서 | 워크플로우 | 항목 | 크기 | 선행 | 상태 |
|---|---|---|---|---|---|
| W1 | 시나리오 입력 격리 | G3-6 | S | — | 완료 (2026-09-29) |
| W2 | 캡처가 화면 전체를 본다 | G3-1, G3-5 | M | W1 | 대기 |
| W3 | 시각 회귀와 움직임 | G3-4, G3-3 | M | W1, W2 | 대기 |
| W4 | 렌더 설정을 코드로 | G1-1, P-4, G4-3, G4-2 | L | W3 | 대기 |
| W5 | 루프 속도 | G2-3, G2-1 | L | — | 대기 |
| W6 | 콘텐츠 헬퍼(a/b/c로 나눠 진행) | G1-3, G1-4, G4-1, G4-4 | L | W3, W4 (W6b는 W2) | 대기 |
| W7 | 에디터 밖·여러 에디터 | G2-2, G1-2, G5-1 | L | — | 대기 |
| W8 | 실제 성능 측정 | G3-2 | M | W1 | 대기 |
| W9 | 병렬 작업의 공유 지점 | G5-4, G5-3 | M | — | 대기 |
| 상시 | 업스트림·외부 의존 | O-1, O-5, O-6, P-4 신고 | S | 새 버전이 나올 때 | — |
| 마지막 | macOS | P-3 | L | 실제 Mac | 대기 |

### W1 시나리오 입력 격리 (G3-6) — 완료 (2026-09-29, 아래 "해결됨")
- 왜 먼저: `play.events`가 매번 같아야 매트릭스 1과 W3의 기준 이미지가 의미 있다. 지금은 루프 ~25회에 1회 어긋난다.
- 고치는 곳: `Runtime/ScriptedInput.cs`(시나리오 동안 가상 장치 밖의 장치를 `InputSystem.DisableDevice`, 끝나면 복구),
  `Runtime/ScenarioRunner.cs`(걸러 낸 실제 입력 수를 `play`에 보고).
- 같이 볼 것: 구 Input Manager shim(`Tools~/templates/HarnessInput.cs`)도 `Input.GetKey(key) || Held(key)`라 실제 입력이 섞인다.
  시나리오 재생 중에는 실제 입력을 빼도록 훅에 시작·끝을 알린다. shim은 게임 소유 파일(uninstall이 남김)이라 이미 붙인 프로젝트의 갱신 방법도 정한다.
- 추가 검증: 루프 도중 실제 키보드로 스페이스 연타 → `play.events` 매번 같음. 플레이가 실패·중단돼도 장치가 다시 켜지는지(수동 플레이에서 키보드가 죽지 않는지).

### W2 캡처가 화면 전체를 본다 (G3-1 → G3-5)
- 왜: 기존 프로젝트는 UI가 화면의 전부인 경우가 많은데(P-5, 사내 프로젝트 A) 지금은 `"screen"`으로만 찍히고 크기가 사용자 레이아웃을 따른다.
  W3의 기준 이미지 비교도 고정 해상도·UI 포함 캡처가 있어야 된다.
- 고치는 곳: `Runtime/HarnessCapture.cs`(UI 합성, 이미지 통계), `Runtime/ShotPreset.cs`, `Editor/HarnessCaptureCommand.cs`.
- 주의: UI Toolkit은 `PanelSettings.targetTexture`로 되지만 uGUI Screen Space - Overlay 캔버스는 다른 방법이 필요하다(기존 프로젝트는 대부분 uGUI).
  Game 뷰 크기는 사용자 전역 설정이라 코드로 바꾸지 않는다.
- 추가 검증: 스모크 씬 `"auto"` 캡처에 HUD가 1280x720으로 찍힘. attach-test에서 사내 프로젝트 A의 부트·로비 화면이 `"auto"`로 찍힘.
  셰이더 없는 머티리얼 주입 → `shotStats`에 마젠타 판정(selftest 항목으로 추가).

### W3 시각 회귀와 움직임 (G3-4, G3-3)
- 고치는 곳: `Tools~/loop.ps1`·`Tools~/Harness.psm1`(report.json에 diff 점수), 샘플의 `golden/`,
  `Runtime/ScenarioRunner.cs`·`Runtime/HarnessCapture.cs`(N프레임 연속 캡처 → 스프라이트 시트/GIF).
- 먼저 정할 것: 기준 이미지는 Unity 버전별로 둔다(P-4처럼 버전마다 렌더가 다르다). 머신·GPU 차이 허용치(P-3의 부동소수점 문제와 같은 기준).
  의도한 변경일 때 기준 이미지를 갱신하는 명령.
- 추가 검증: 같은 코드로 3회 → diff가 허용치 안. 셰이더 한 줄 수정 → 점수가 움직이고 report에 보임.
- W4 전에 하는 이유: W4는 렌더 설정을 통째로 코드로 옮긴다. "옮기기 전과 같게 나오는지"를 이걸로 확인한다.

### W4 렌더 설정을 코드로 (G1-1 → P-4 → G4-3 → G4-2)
- 순서: G1-1(`ISettingsStep`: RP·Renderer 에셋을 코드로 생성, 설정값을 fingerprint에) → P-4(프레임 디버거로 6.3과 주광 그림자 패스 비교,
  우회가 필요하면 그 설정을 G1-1 코드에 버전 조건으로 둔다) → G4-3(`ctx.LitMaterial`이 키워드 자동 설정) →
  G4-2(반사 큐브맵 → SH → `RenderSettings.ambientProbe`, Trilight 우회 제거).
- 고치는 곳: `Editor/Build/BuildContext.cs`, `Editor/Build/HarnessBuild.cs`, `Editor/Build/SceneFingerprint.cs`, `Editor/HarnessSetup.cs`,
  샘플 `Assets/Game/Stage/Builders/`.
- 먼저 정할 것: `Assets/Settings/*.asset`을 생성물(gitignore)로 둘지, 커밋된 채 코드가 덮어쓸지. 새 클론에서 에디터가 처음 열릴 때 RP 에셋이
  없어도 되는지가 관건(매트릭스 9).
- 주의: 기존 프로젝트(`setup: attach`)의 RP 에셋은 덮어쓰지 않는다. 설정 스텝은 `setup: harness`에서만 적용하고 attach에서는 `recommendations`만.
- 추가 검증: RP/Renderer 에셋 삭제 → 루프 1회로 재생성, fingerprint 동일. W3 기준 이미지와 diff(옮기기 전과 동일).
  6.6 새 클론 selftest 1–8 녹색(P-4). attach-test 뒤 기존 프로젝트의 RP 에셋 무변경.

### W5 루프 속도 (G2-3 → G2-1)
- 먼저: G2-1 메모의 재측정. 에디터 1개일 때 컴파일 / 도메인 리로드 / Pipeline 재응답 구간을 나눠 잰다(다른 에디터가 떠 있으면 4.7–19s로 흔들렸다).
- 순서: G2-3(리로드 직후 빌더·fingerprint 워밍업, 작음) → G2-1(`loop.ps1 -Hot`: Pipeline `[CodeReload]`/`reload_file`로 Tick 본문 핫패치, 플레이 상태 유지).
- 고치는 곳: `Tools~/loop.ps1`, `Tools~/Harness.psm1`, `Editor/Build/HarnessBuild.cs`, 필요하면 `Runtime/GameRoot.cs`.
- 주의: 핫패치 API는 실험판 Pipeline(O-1)에 있다. 패키지를 올리면 깨질 수 있으니 매트릭스에 `-Hot` 루프를 넣는다. W3과 `loop.ps1`을 같이 고치므로 둘은 이어서 한다.
- 추가 검증: 모듈 Tick 본문 수정 → 2초 안에 반영된 캡처. "1차 버전 기준선" 표를 다시 재서 갱신.

### W6 콘텐츠 헬퍼 (W6a G1-3 · W6b G1-4 · W6c G4-1 → G4-4)
- W6a 파티클·애니메이션: `ctx.Particles`, 코드로 만든 AnimationClip, Playables 재생 헬퍼. 스모크 씬에서 움직임을 W3 연속 캡처로 확인.
- W6b UI 킷: `UI/`에 공용 USS 변수·버튼·게이지·토스트, 폰트, 바인딩 예제. HUD가 `"auto"` 캡처에 찍혀야 확인할 수 있으므로 W2 뒤.
- W6c 절차적 생성: GPU 베이크 경로(Blit/Compute → RT → PNG)를 먼저 두고, 그 위에 SDF·스플라인/튜브·스캐터·데칼·절차적 스카이.
  GPU 베이크 결과는 GPU·드라이버마다 다를 수 있다 → fingerprint에 무엇을 넣을지 정한다.
- 공통: 셋 다 `Editor/Build/BuildContext.cs`에 헬퍼를 더한다. 따로 진행하려면 헬퍼별 파일(`partial class`)로 나눈다.
  새 콘텐츠는 W4의 `ctx.LitMaterial`을 쓰고, 기존 샷 회귀가 없는지 W3 기준 이미지로 본다.

### W7 에디터 밖·여러 에디터 (G2-2 → G1-2 → G5-1) — 조사부터
- 순서: G2-2 조사(상주 batchmode 에디터에서 GPU 렌더·캡처가 되는가. 안 되면 GUI 에디터를 `-automated`로 띄워 모달만 막는다)가 먼저다.
  batchmode 렌더가 안 되면 G1-2(복제 프로젝트 + batchmode 빌더)와 G5-1(에디터 풀로 루프 분산)은 GUI 에디터 N개가 된다.
- 고치는 곳: `Tools~/open.ps1`, `Tools~/Harness.psm1`(락·에디터 선택), `Tools~/loop.ps1`.
- 비용: 에디터마다 라이선스 좌석, 복제마다 `Library/`(디스크·첫 임포트 시간), O-7 같은 전역 자원 충돌.
- 추가 검증: 루프 2개 동시 실행 시 대기가 사라짐(지금 두 번째가 3.55s 대기). 매트릭스 6의 기대값이 "대기"에서 "병렬"로 바뀌면 selftest도 고친다.

### W8 실제 성능 측정 (G3-2)
- 개발 빌드 플레이어 + 런타임 Pipeline 서버로 같은 시나리오를 돌리는 `harness_perf`(fps, 프레임 p95, batches).
- 고치는 곳: `Editor/HarnessReleaseBuild.cs`(개발 빌드 + `AGENTHARNESS_RUNTIME`), `Runtime/ScenarioRunner.cs`, `Tools~/`에 새 진입점.
- 추가 검증: 에디터 플레이 FPS와 플레이어 FPS를 나란히 기록. 출시 빌드에는 여전히 `Harness.*`가 없음(매트릭스 10).

### W9 병렬 작업의 공유 지점 (G5-4 → G5-3)
- G5-4: 이벤트 파일을 발행 모듈별로 나누는 lint(`Editor/HarnessLint.cs`)와 이름 충돌 검사. 계약 파일도 owners.json처럼 추가한 worktree를 기록해
  병합 전까지는 그 worktree만 고치게 한다(`Tools~/submit.ps1`, `Tools~/land.ps1`).
- G5-3: 남은 부분(검사 집합 밖 모듈은 에디터 DLL 기준)이 worktree 흐름에서 실제로 문제가 되는지부터 본다. 아니면 `[~]`인 채로 닫는다.
- 추가 검증: 매트릭스 7·8에 "두 worktree가 같은 이벤트 이름을 추가 → 두 번째 submit/land 거부"를 더한다.
- 에이전트 여럿을 붙여 쓰기 시작하면 앞당긴다.

### 상시: 업스트림·외부 의존 (O-1, O-5, O-6, P-4 신고)
- 코드보다 신고와 재검증: Pipeline에 2건(`RuntimeInputCommand.cs`의 `ENABLE_INPUT_SYSTEM` 조건, 출시 빌드 의존), Unity에 P-4 최소 재현(W4 조사 결과로),
  O-6 Unity Search 예외.
- 계기: Pipeline 새 버전이나 Unity 6000.x 새 패치 → 매트릭스(9는 그 버전으로) 재검증 → 우회 코드(`Invoke-HarnessRecompile` 세대 번호,
  install의 Input System 추가, `HarnessReleaseBuild`)를 걷어낼 수 있는지 본다.

### 마지막: macOS (P-3)
- 실제 Apple Silicon Mac이 있을 때 한다. 그 전까지 모든 워크플로우에서 새 코드에 백슬래시 경로·`powershell.exe`·`C:\` 경로를 늘리지 않는다.
- 결정성 기준("같은 머신 안에서 결정적")은 W3의 기준 이미지 정책을 정할 때 같이 정한다.

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
  - 방향: `PanelSettings.targetTexture`로 UI를 RT에 렌더해 합성한다. Game 뷰 해상도 고정은 쓰지 않는다(Game 뷰 크기는 사용자 전역 설정이라 코드로 바꾸면 사용자 레이아웃이 바뀐다).
  - 완료 기준: `"auto"` 프리셋 캡처에도 HUD가 1280x720으로 찍힌다.
  - 2026-09-29(P-5): 기존 프로젝트는 UI가 화면의 전부인 경우가 많다(사내 프로젝트 A의 부트·로그인·타이틀·로비). 그 화면은 `"screen"`으로만 찍히고, 크기는
    사용자 레이아웃의 Game 뷰(그때 366x415)라 샷 통계·픽셀 좌표가 레이아웃마다 달라진다. 좌표는 `"mouseSpace": "normalized"`와 `click`의 `target`으로 피했다.

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

- **G3-6 실제 키보드·게임패드 입력이 시나리오 재생에 섞인다** → 2026-09-29 해결(W1, 아래 "해결됨").

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
이 하네스는 이제 UPM 패키지(`com.geuneda.agentharness`, git URL `?path=`)이고 **설치 스크립트 한 번으로 기존 프로젝트에 붙였다 뗄 수 있다**(P-2).
붙인 뒤의 격차(구 Input Manager 입력, `Assembly-CSharp` 검사, 부트 → 메뉴 → 레벨 흐름, 캡처 포즈, 머신 간 제거)는 P-5에서 메웠고, 공개 프로젝트 2개와
사내 대형 프로젝트 1개에서 검증했다.
Unity는 6.0 LTS 이상(6000.0.84f1·6000.3.11f1에서 매트릭스 전부, 6000.6.3f1에서 렌더링 한 가지를 빼고 녹색 — P-1, P-4), OS는 Windows 하나에서만 검증했다.

순서: **P-1(버전, 2026-09-29 해결) → P-2(기존 프로젝트, 2026-09-29 해결) → P-5(붙인 뒤의 격차, 2026-09-29 해결) → P-3(macOS, 나중)**.
버전은 `tools/fresh-clone-test.ps1 -SelfTest -UnityVersion <v>`, 기존 프로젝트는 `tools/attach-test.ps1 -Project <클론>`으로 검증한다.

- **P-1 Unity 버전이 6000.3.11f1로 고정돼 있다** → 2026-09-29 해결(아래 "해결됨"). 6.6에서 남은 렌더링 문제는 P-4.
- **P-2 기존 Unity 프로젝트에 붙일 수 없다** → 2026-09-29 해결(아래 "해결됨"). 붙인 뒤에도 남은 것은 P-5.

- **P-5 기존 프로젝트에 붙였을 때 아직 안 되는 것** → 2026-09-29 해결(아래 "해결됨").

- [ ] **P-4 Unity 6.6(URP 17.6)에서 샘플 씬의 조명이 검게 나온다** (2026-09-29, P-1 검증 중 발견)
  - 현상: 6000.6.3f1로 연 새 클론에서 에디터 세션의 **첫 플레이만** 정상이고(overview meanLuma 48.3, 6.3은 50.3), 그 뒤의 플레이와
    편집 모드 캡처는 URP Lit 표면(지형·받침대)의 확산광·앰비언트가 0이 되어 거의 검다(closeup 25.0, horizon 15.6, overview 0.5).
    하늘과 커스텀 HLSL 매듭은 그려진다. 루프는 녹색이지만 `shotStats[].dark`(98% 검정)가 잡고, `selftest.ps1` 1번이 빨갛다.
    Game 뷰 자체도 검다(`screen` 캡처) → 하네스 캡처 경로(`SubmitRenderRequest`) 문제가 아니다.
  - 확인한 것(같은 세션에서): **태양 그림자를 끄거나 하드 그림자로 바꾸면** 밝아진다(정상인지는 미확인: 6.3보다 훨씬 밝은 125.9).
    소프트 그림자 + 캐스케이드 2–4개면 검정, 1개면 한 번은 밝고 한 번은 검정(비결정적). SSAO·Forward/Forward+/Deferred·Domain Reload·
    빌드 캐시·GPU Resident Drawer·Game 뷰 크기·URP 에셋 재직렬화·파이프라인 재생성·`ScriptableRendererData.SetDirty`와는 무관.
    머티리얼 값·라이트·앰비언트 프로브는 6.3과 같다. 하네스 없는 빈 씬(기본 카메라·방향광 + 평면·큐브)에서도 6.6만 소프트 그림자가 더 어둡다
    (Soft 114.7 / Hard 138.0 / None 138.1; 6.3은 137.8 / 138.0 / 138.1) → URP 17.6 쪽 문제로 보인다.
  - 2026-09-29(W1 매트릭스, 다른 에디터 없이): 새 클론 루프는 그대로(첫 루프 130.2/56.8/48.3, 2·3번 25.0/15.6/0.5)였는데, 같은 에디터 세션에서
    이어진 selftest 1번 루프 3회는 **6.3에 가까운 밝기**(61.2/56.8/48.3; 6.3은 65.1/60.5/50.3)로 녹색이었다(이전 실행은 여기서 검어 빨강).
    한 세션 안에서 검정 → 정상으로 돌아오기도 한다 = 비결정적. 해결로 치지 않는다(W4에서 원인부터).
  - 방향: 프레임 디버거/RenderDoc으로 주광 그림자 패스(캐스케이드 아틀라스, `_MainLightShadowParams`)를 6.3과 비교한다. Unity 쪽 버그면
    최소 재현 프로젝트로 신고하고, 그 전까지 6.6에서는 소프트 그림자 캐스케이드를 쓰지 않는 설정을 샘플에 둘지 정한다.
    6000.6.x 새 패치가 나오면 `fresh-clone-test.ps1 -UnityVersion <v> -SelfTest`로 다시 본다.
  - 완료 기준: 6.6에서 새 클론 selftest 1–8 녹색(샷에 `dark` 없음, 6.3과 육안 동일).

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
    - O-4·O-7·O-8에서 만든 `open.ps1`·`quit.ps1`·`fresh-clone-test.ps1`은 경로를 `/`로 쓰고, 자식 PowerShell을 현재 호스트(`Get-HarnessPowerShell`)로,
      에디터를 `unity editors --installed`의 위치로 띄운다(`.app`이면 `Contents/MacOS/Unity`). 그래도 macOS에서 확인할 것:
      `Temp/UnityLockfile` 잠금 검사(`Test-HarnessProjectOpen`, Unix에서 .NET `FileShare.None`은 flock), 종료 직후 pid 판정(`HasExited`),
      전역 로그 위치(`~/Library/Logs/Unity/Editor.log` — `-logFile`로 우회하므로 영향은 없어야 한다).
  - 방향: 도구를 PowerShell 7(pwsh, 크로스플랫폼)로 옮긴다. Windows에서도 pwsh 7을 요구할지, 5.1 호환을 유지할지 정해야 한다.
    경로는 `/`와 다단 `Join-Path`로 통일하고, 에디터·dotnet·csc 경로는 `unity editors --installed`나 실행 중인 에디터 프로세스에서 얻는다.
    Unix에서는 락을 파일 락(배타 핸들)으로 바꾸는 것도 검토한다.
  - 완료 기준: Apple Silicon Mac에서 새 클론 → `harness_setup` → 매트릭스 1–8 녹색(macOS 기준값은 따로 기록). 같은 스크립트로 Windows 매트릭스도 녹색.

## 하네스 자체

- [ ] **O-1 Pipeline 패키지 0.8.0-exp.1(실험판) 의존**
  - 현상: `recompile` 상태 레이스(직전 실패 후 옛 실패 보고 / `triggered` 고착)를 `Invoke-HarnessRecompile`의 컴파일 세대 번호 + idle 판정으로 우회 중.
  - 2026-09-29(P-2): Active Input Handling이 New/Both이고 Input System 패키지가 없는 프로젝트에서 Pipeline 런타임(`RuntimeInputCommand.cs`)이
    컴파일되지 않는다(`#if ENABLE_INPUT_SYSTEM`만 보고, 자기 asmdef의 `PIPELINE_HAS_INPUT_SYSTEM_PACKAGE`는 다른 곳에만 씀) → 에디터가 Safe Mode 대화상자에서 멈춤.
    install.ps1이 그 조합이면 `com.unity.inputsystem`을 더해 우회. 출시 빌드에 `Unity.Pipeline.Attributes`·`Newtonsoft.Json`을 넣는 것도
    `HarnessReleaseBuild`로 우회 중. 둘 다 Pipeline 쪽에 신고할 것(최신 0.8.0-exp.1, 레지스트리 확인).
  - 2026-09-29(P-5): 이미 옛 Pipeline(0.6.0-exp.1)을 직접 의존하는 프로젝트가 있었다(사내 프로젝트 A). install이 하네스가 요구하는 버전으로 올리고
    `installReplaced`에 남긴다. 그 프로젝트의 다른 Pipeline 사용처(에이전트 도구 등)는 0.8로 돈다.
  - 할 일: 패키지를 업그레이드할 때마다 loop 검증 매트릭스(아래)를 다시 돌린다.
- **O-2 스크립트가 Windows PowerShell 5.1 전용** → P-3으로 옮겼다(2026-09-29).
- **O-3 하네스 자체의 자동 테스트가 없다** → 2026-09-29 해결(`tools/selftest.ps1`, 아래 "해결됨"). PNG 눈 확인만 사람·에이전트 몫으로 남았다.
- **O-4 에디터를 코드로 닫을 방법이 없다** → 2026-09-29 해결(아래 "해결됨").
- [ ] **O-5 `%TEMP%` 아래 프로젝트에서 Burst JIT DLL 로드가 막힌다** (LoadLibrary error 4551 = Windows 애플리케이션 제어 정책).
  editorErrors로만 보고된다. 프로젝트를 Temp에 두지 말 것.
- [ ] **O-6 도메인 리로드 직후 Unity Search 인덱서 예외** (`UnityEditor.Search.SearchInit.IndexationOnStartup`, ArgumentOutOfRange).
  에디터 내부 에러라 `editorErrors`로 분류(루프 실패 아님). Unity 쪽 수정 전까지 유지.
- **O-7 두 에디터가 전역 `Editor.log`를 같이 쓴다**, **O-8 새 클론 검증 자동화** → 2026-09-29 해결(아래 "해결됨").

### 검증 매트릭스 (하네스를 고친 뒤 매번)

**1–8은 `tools/selftest.ps1` 한 번**(에디터 트리, 하네스 변경은 임시 커밋 후; ~3.5분), **9는 `tools/fresh-clone-test.ps1 -SelfTest`**
(새 클론에서 루프 3회 + 1–8, 지원 버전마다 `-UnityVersion`; 버전당 ~5분), **10은 `tools/attach-test.ps1`**(기존 프로젝트 클론마다; 0.5–1분).
아래는 각 항목이 검사하는 것이다. 샷 PNG는 여전히 Read로 확인한다.

1. `loop.ps1` 3회 연속 녹색, `build.fingerprint`·`play.events` 동일, PNG를 Read로 확인(selftest: blank·dark 샷 없음 + `compile-check -IncludeHarness`
   + 시나리오 도구 루프 한 번: `waitScene`·`waitTarget`·UI Toolkit `click`·KeyCode 키 이름·포즈/카메라 캡처
   + 실제 입력 격리(G3-6): 플레이 동안 실제 키보드 장치에 스페이스를 넣어도 `play.events` 그대로·`isolatedDevices` 누름 > 0, 실패한 플레이·중간에
   멈춘 플레이 뒤에도 실제 장치가 다시 켜짐)
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
   (selftest는 이미 병합됨·미커밋·에디터 트리 직접 수정 거부까지 보고, 끝나면 worktree·`selftest/*` 브랜치·테스트 커밋을 스스로 걷어낸다)
9. 새 클론(O-8): `tools/`·`ProjectSettings/`·`Packages/`·`.gitignore`·에디터 시작 코드를 바꿨으면 임시 커밋 후
   `tools/fresh-clone-test.ps1 -SelfTest -ExpectFingerprint <1의 fingerprint>` 녹색(클론이 메인 트리와 같은 fingerprint), `shots/`를 Read로 확인.
   지원 버전(CLAUDE.md "Unity 버전")마다 `-UnityVersion <v>`로도 돌린다(fingerprint는 그 버전의 값)
10. 기존 프로젝트(P-2): 하네스 패키지·설치/제거 스크립트·런타임을 바꿨으면, 기준선 커밋이 있는 테스트 클론마다
   `tools/attach-test.ps1 -Project <클론> [-Scene ...] [-Module ...]` 녹색 — install → 설치분만 바뀜 → 기존 씬으로 루프 3회 녹색(fingerprint·events 동일)
   → 출시 빌드에 `Harness.*` 없음 → uninstall 뒤 `git status` 비어 있음. `shots/`를 Read로 확인. 지금 쓰는 클론(`../ah-p2`, 기준선 커밋 포함):
   BagelGame(`-Module Game=Assets/Game,UI=Assets/UI`), Fluid-Sim(`-Scene "Assets/Scenes/Fluid Particles.unity"`), 사내 프로젝트 A(비공개 클론, 이 머신에만;
   `-Scenario`로 부트 대화상자를 기다리는 시나리오, `-KnownErrors`, `-NoBuild`). 배포 경로를 바꿨으면
   `-Source git+file:///<저장소>?path=/AgentHarness/Packages/com.geuneda.agentharness#<브랜치>`(커밋된 것, 부트스트랩 포함)로도.

---

## 해결됨

(해결한 항목을 여기로 옮기고 날짜, 방법, 검증 결과, 측정값을 적는다.)

- [x] **G3-6 실제 키보드·게임패드 입력이 시나리오 재생에 섞인다** (2026-09-29, W1)
  - 재현: 에디터 안에서 실제(native) 키보드 장치에 스페이스 누름·뗌을 입력 업데이트 6번마다 넣는 eval(플레이 동안만; OS 키 입력이 Input System에 들어온
    뒤의 경로와 같다). 고치기 전: 60개(누름 30) → `SpinDirectionChanged=30`(정상 1)인데 루프는 녹색. 원인은 추정대로 `<Keyboard>/space` 바인딩이 가상·실제
    키보드를 둘 다 받고, 시나리오가 입력을 에디터 포커스와 무관하게 받게(`IgnoreFocus`, `AllDeviceInputAlwaysGoesToGameView`) 하기 때문.
  - 방법:
    - `RealInputIsolation`(`Runtime/ScriptedInput.cs`): 러너가 시작한 첫 프레임부터 끝날 때까지 실제 Input System 장치(`device.native`)를 끈다.
      `DisableDevice(keepSendingEvents: true)`(TouchSimulation 방식) + `InputSystem.onEvent`에서 그 장치의 이벤트를 handled로 표시 → 상태가 바뀌지 않는다
      (`InputUser`/PlayerInput 자동 전환도 꺼진 장치의 이벤트는 무시). 끌 때 하드 리셋(눌린 키 없음, 포인터 0,0 — 실제 커서 위치가 UI hover에 남지 않고,
      가상 마우스와의 값 비교에서도 진다). 원래 꺼져 있던 장치(게임·TouchSimulation이 끈 것, 에디터가 백그라운드라 꺼진 것)는 건드리지 않고, 도중에 켜지는
      장치(포커스 복귀, 새 게임패드)는 그때 끈다. 끝나면 자기가 끈 것만 켠다. Input System의 `LeavePlayMode`는 `DisableDevice`로 끈 장치를 켜지 않으므로
      러너 `Finish`가 시간·입력 복구를 맨 먼저 하고(뒤에서 예외가 나도), 에디터가 `EnteredEditMode`에 남은 것을 한 번 더 켠다(`RestoreAll`, 켰으면 경고).
    - 보고: `play.isolatedDevices[{name, presses}]` = 끈 장치와 막은 키·버튼 누름 수. 처음엔 이벤트 수를 셌는데 입력이 없어도 장치마다 1이 나왔다 —
      Input System이 플레이 진입(`SyncAllDevicesAfterEnteringPlayMode`)과 에디터 포커스 때 모든 장치에 sync를 요청해 오는 상태 이벤트다. 그래서
      누름(`HasButtonPress`)만 센다(마우스 이동·sync도 막지만 세지 않음).
    - 구 Input Manager: 러너가 모든 `[AgentHarnessInput]` 훅에 `"begin"`/`"end"`를 보낸다(입력 이벤트가 없는 시나리오에도, 던지는 훅은 경고).
      `HarnessInput.cs`는 그 사이 `UnityEngine.Input`을 읽지 않고 시나리오 입력만 준다(마우스는 시나리오가 옮기기 전까지 화면 중앙; 문자열 키·`inputString`·
      터치는 없음). `"begin"`을 보내지 않는 옛 하네스에서도 첫 입력 이벤트부터 같게 동작한다. `Input.`으로 직접 읽는 코드는 여전히 막을 수 없다.
    - 이미 붙인 프로젝트의 shim: 게임 소유 파일이라 자동으로 바뀌지 않는다 → install을 `-InputShim`으로 다시 돌리면 고치지 않은 옛 템플릿
      (SHA-256 목록 `Tools~/templates/HarnessInput.previous.txt`)을 새 것으로 바꾸고(`modified`), 고친 사본은 두고 경고한다. uninstall도 그 목록을 "그대로"로 본다.
  - 검증:
    - 샘플(6000.3.11f1): 같은 주입 60개(누름 30) → `SpinDirectionChanged=1`, `isolatedDevices` Keyboard 30 · Mouse 0. 조용한 루프는 0 · 0. 플레이 중 eval로
      Keyboard·Mouse 비활성 확인. waitTarget 시간 초과로 실패한 플레이와 중간에 `editor_stop`한 플레이 뒤에도 둘 다 다시 켜짐. selftest 1번에 이 넷을 넣었다
      (1번 29.5 s).
    - 구 Input Manager(Fluid-Sim, 6000.0.84f1, Both): install `-InputShim` → P-5와 같은 19곳 `Input.` → `HarnessInput.` → 루프 3회 모두 P-5와 같은 결과
      (일시정지 뒤 두 샷 19.9/59.2, 드래그 뒤 12.0, `inputBackends` inputSystem+hook, `isolatedDevices` Keyboard·Mouse, 루프 2·3번 1.76–1.78 s). 플레이 중 eval:
      시나리오가 마우스를 옮기기 전 `HarnessInput.mousePosition` = 화면 중앙(183, 207.5), `Input.mousePosition` = 실제 커서(-1326, 465); 끝난 뒤엔 둘 다 실제 커서.
      install 갱신: 옛 템플릿(CRLF) → `modified`, 고친 사본 → `kept` + 경고. uninstall은 옛·현재 템플릿이면 제거, 고친 사본은 유지. 되돌린 뒤 `git status` 비어 있음.
    - 한계: OS 수준 키 입력(SendInput)으로는 시험하지 않았다(사용자 화면의 앞 창에 키가 간다). 주입은 Input System 장치 이벤트라 백엔드 뒤 경로만 같다.
    - 매트릭스(에디터를 하나씩만 띄우고 순서대로): 샘플 selftest 1–8 녹색 205.5 s(fingerprint `977545a7…`, 줄 61/68/87, 샷 65.1/60.5/50.3 그대로).
      9: 새 클론 6000.3.11f1 녹색 299.9 s(`977545a7…` = 메인 트리), 6000.0.84f1 녹색 292.0 s(`d9a6d092…`), 6000.6.3f1 녹색 299.4 s(`0ba32228…`; 이번엔 1번도
      녹색이었지만 클론 루프 2·3은 그대로 검었다 → P-4 미해결, P-4에 관찰 기록) — 세 버전 모두 줄 61/68/87, 실제 입력 검사 `Keyboard=30`.
      10: BagelGame 녹색 57.6 s(`619be553…`, `Managed/` 132개·`Harness.*` 0개), Fluid-Sim 녹색 31.2 s(`54880f05…`, 103개·0개), 사내 프로젝트 A 녹색 98.3 s
      (`6664b723…`, 로그인 서버 선택 PlayerPrefs 그대로) — 루프마다 `isolatedDevices` Keyboard·Mouse(누름 0), 제거 뒤 `git status` 비어 있음.

- [x] **P-5 기존 프로젝트에 붙였을 때 아직 안 되는 것** (2026-09-29)
  - **구 Input Manager 입력** → 게임 쪽 훅. 먼저 코드 수정 없는 길을 확인했다: Game 뷰는 OnGUI에서 OS 이벤트를 `EditorGUIUtility.QueueGameViewInputEvent`로
    플레이어 루프에 넘기는데, 합성 이벤트를 그 함수로 직접 넣거나 `GameView.SendEvent`로 보내(Game 뷰 OnGUI 도착은 `globalEventHandler`로 확인, 좌표는
    내부 `gameMouseOffset/Scale`로 역변환) 봐도 `Input.GetKey`에도 게임의 `OnGUI`에도 닿지 않았다(Active Input Handling Old·Both, Game 뷰 포커스 있음).
    `Input.mousePosition`이 Game 뷰 밖의 실제 커서를 따라간다 → 구 Input Manager는 에디터에서 OS 상태를 직접 읽는다. 그래서:
    - 게임의 정적 메서드 `void M(string type, string key, Vector2 value)`에 `[AgentHarnessInput]`을 붙이면 `HarnessInputHooks`(Editor, TypeCache)가 찾아
      `InputHookReplay`로 시나리오 입력을 넘긴다. 속성은 이름으로 찾아서 게임은 하네스를 참조하지 않는다(제거 뒤에도 컴파일됨).
    - `Tools~/templates/HarnessInput.cs`(install `-InputShim` → `Assets/AgentHarness/HarnessInput.cs`): `UnityEngine.Input`과 같은 멤버 이름의 드롭인.
      실제 입력 + 시나리오 입력, 기본 축(Horizontal/Vertical/Fire1-3/Jump/Submit/Cancel/Mouse X·Y/ScrollWheel)까지. uninstall은 템플릿 그대로이고
      아무도 안 쓸 때만 지운다.
    - 키 이름은 Input System 이름과 KeyCode 이름을 둘 다 받는다(`KeyNames`). Input System 가상 장치는 이제 이벤트가 쓰는 종류만 만든다(입력 없는
      시나리오는 장치 0개 — 가상 게임패드가 게임패드 안내를 켜는 게임이 있다).
  - **asmdef 없는 폴더의 compile-check** → compile-check가 Unity 규칙으로 각 `.cs`의 어셈블리를 계산한다(asmdef/asmref 폴더 → 그 어셈블리, 나머지
    `Assets/` → `Assembly-CSharp`/`-Editor`/`-firstpass`). 모듈 코드를 컴파일하는 어셈블리를 통째로 검사하고, 에디터가 컴파일하지 않는 asmdef
    (`includePlatforms`에 Editor 없음)는 뺀다. submit 게이트가 `Assembly-CSharp` 모듈도 막는다.
  - **씬 흐름** → 시나리오 이벤트 `waitScene`(씬 로드까지)·`waitTarget`(GameObject/UI Toolkit 요소가 활성·표시될 때까지): 시나리오 시계를 멈추고
    뒤의 이벤트·캡처를 그 순간 기준으로 민다(기다린 프레임은 FPS 통계에서 뺌, 제한 시간 넘으면 실패). `click`(`target` 이름·경로 또는 좌표 → 이동, 다음
    프레임 누름, `hold` 뒤 뗌): uGUI 사각형 중심, 렌더러/콜라이더 중심, UI Toolkit 요소(스크린 공간 패널, 6.2+ 월드 공간 패널 — 요소 경계는 문서 로컬
    단위·y 위, `UnityCompat.IsWorldSpace`). `"mouseSpace": "normalized"`. 결과에 `play.scenes`(로드 시각)·`waits`·`clicks`·`activeScene`.
    Editor 타임아웃은 대기 제한 시간을 더한다(전에는 83 s에서 잘림), 루프도 그만큼 기다린다.
  - **캡처 프리셋** → 설정 `shots`(이름·씬·`pos`+`lookAt`/`rot`+`fov`)가 ShotPreset과 함께 `"auto"`·이름·`harness_capture`에 쓰이고, 시나리오 캡처에
    `camera`(카메라 이름)·`pos`/`lookAt`/`rot`/`fov`. 카메라 렌더가 비었는데 오버레이 캔버스·UI Toolkit 문서가 있으면 `shotStats[].hint`가 `"screen"`을 권한다.
  - **기존 씬의 fingerprint** → 고치지 않고 정리했다(의도된 한계). 빌더가 없는 씬은 코드의 산출물이 아니라 입력이라, fingerprint = 씬 파일 + 의존 에셋의
    임포트 해시(씬이 쓰는 스크립트 포함)가 "입력이 같은가"를 답한다. 플레이가 같은지는 `play.events`·`waits`·샷 통계로 본다.
  - **머신에만 있던 설치 기록** → 설치가 바꾼 manifest 항목을 커밋되는 설정에도 남긴다(`installAdded`, 새 `installReplaced`). 기록이 없는 uninstall은 하네스만
    쓰던 lock 항목을 지우고 올린 버전을 되돌린다.
  - 사내 프로젝트에서 드러나 함께 고친 것:
    - 프로젝트가 하네스 의존성을 더 낮게 고정하면(사내 프로젝트 A의 `com.unity.pipeline` 0.6.0-exp.1) UPM에서 직접 의존이 이겨 하네스가 옛 Pipeline으로 돈다
      → install이 `package.json` 의존성보다 낮은 직접 의존을 올리고 `installReplaced`에 남긴다(uninstall이 되돌림).
    - 시나리오가 부트보다 짧으면 플레이 모드를 나가며 게임이 부트 취소 에러를 찍어 루프가 빨갰다 → 시나리오가 끝난 순간의 콘솔 번호(`finishedSeq`) 뒤의 에러는
      `teardownErrors`(실패 아님). 프로젝트가 원래 내는 에러(저장소에 없는 SDK 데스크톱 DLL)는 설정 `knownErrors` 정규식 → `knownErrors`(실패 아님).
    - 대기가 끝난 프레임에 같은 대기 이벤트를 한 번 더 처리하던 것(`waits` 이중 기록).
    - 재설치 때 install이 자기가 쓴 `CLAUDE.md` 포인터를 사용자 문서로 보고 경고하던 것.
  - 검증(이 머신; 매트릭스 9와 겹친 구간은 다른 에디터가 함께 돌았음):
    - **구 Input Manager**(Fluid-Sim, 6000.0.84f1, Both): 게임 코드 2파일에서 `Input.` → `HarnessInput.`(19곳), 시나리오 = 0.6 s 스페이스 + 1.6–1.9 s 왼쪽 드래그
      (`mouseSpace: normalized`). 일시정지 뒤 두 샷이 같음(19.9/59.2 두 번), 드래그 뒤 시점이 바뀜(12.0; 입력 없을 때 22.2), `inputBackends` inputSystem+hook,
      3회 같은 결과(±0.1), 루프 1.75 s. 코드를 되돌리고 uninstall → shim 제거, `git status` 비어 있음.
    - **compile-check**: 사내 프로젝트 A의 모든 어셈블리(36개)를 모듈로 잡고 검사 → 전부 컴파일, 계산한 소스 수가 에디터 응답 파일과 같음(`Assembly-CSharp` 4,
      `-Editor` 11, asmref로 모인 시뮬레이션 어셈블리 183, 게임 런타임 2464; 17.3 s). WebGL 전용 asmdef 1개는 `notCompiledInEditor`. 모듈 4개(asmdef·
      asmref·`Assembly-CSharp`·`-Editor`)만이면 5.7 s. Fluid-Sim worktree에서 `Assembly-CSharp` 모듈에 CS1061 → compile-check 0.4 s, submit 게이트 1.04 s에 거부
      (`OrbitCam.cs:48`, module Sim), 에디터 트리 무변경.
    - **씬 흐름**: 사내 프로젝트 A — 부트 → 개발용 로그인 대화상자(UI Toolkit)의 건너뛰기 버튼 `waitTarget` 0.6 s → 테스트 서버 버튼 `click` →
      건너뛰기 `click` → 타이틀의 준비 그룹 `waitTarget` 3.45 s/166프레임(로그인·데이터 로드) → 시작 버튼 `click`(uGUI) → 로비 씬 `waitScene`
      0.39 s(Additive, `scenes`에 t=0.8) → 로비 Game 뷰 캡처. BagelGame — 월드 공간 UI Toolkit `play-button` → 베이글 선택 `select-button` → 코스(10–12 s 루프,
      README 이미지). 샘플 — 포즈 캡처(위에서 내려다본 샷), UI Toolkit `laps` 클릭, 없는 카메라 이름은 카메라 목록과 함께 샷 에러.
    - **다른 머신의 uninstall**: 설치 → 열기 → 커밋 → `Library/AgentHarness/install.json` 삭제 → uninstall. Fluid-Sim(더한 패키지만): 트리가 기준선과 바이트까지 같음.
      사내 프로젝트 A(Pipeline 0.6→0.8을 올림): 처음엔 lock의 버전 한 줄만 달라서 그 줄도 되돌리게 고친 뒤 기준선과 같음, Unity 배치 모드로 다시 열어도(63 s)
      `Packages/` 그대로.
    - **사내 프로젝트 A**(비공개, Unity 6000.3.11f1, URP 17.3, Addressables 2.7, Input System, 네트워크·분석 SDK; 씬 22개(빌드 4), asmdef 35개 + asmref 5개,
      C# 4,400개, Android IL2CPP; 원본의 Library 5.6 GB를 복사한 로컬 클론): 처음 설치해 열기 113 s. attach-test `-NoBuild -Scenario <부트 대화상자 대기>
      -KnownErrors <SDK 초기화 에러 정규식>` 녹색 99.6 s(install 0.5 / open 50.9 / setup 2.4 / 루프 14.9·13.1·13.0 — Domain Reload가 켜져 있어 플레이 진입 7.6–8.3 s /
      quit 3.6 / uninstall 0.5), fingerprint `6664b723…` 3회 동일, 루프마다 knownErrors 1·teardownErrors 1, 제거 뒤 `git status` 비어 있음.
    - 주의(겪은 것): 클론은 원본과 PlayerPrefs를 공유한다 → 첫 시도에서 로그인 대화상자의 저장된 선택(라이브 서버)으로 로그인했다. 이후 시나리오는 테스트 서버를
      명시적으로 누르고, 끝난 뒤 그 PlayerPrefs 값을 원래대로(0) 되돌렸다.
    - 매트릭스: 샘플 selftest 1–8 녹색 209.8 s(fingerprint `977545a7…`, 줄 61/68/87, 샷 65.1/60.5/50.3 그대로; 1번에 시나리오 도구 루프를 더함). 9: 새 클론(최종 커밋) 6000.3.11f1 녹색 294.8 s(`977545a7…` = 메인 트리), 6000.0.84f1 녹색 290.0 s(`d9a6d092…`), 6000.6.3f1 2–8 녹색·1 빨강 279.5 s (P-4의 `dark` 25.0/15.6/0.5 그대로, `0ba32228…`) — 세 버전 모두 줄 61/68/87, 1번의 시나리오 도구 검사 통과. 10: Fluid-Sim 녹색 31.3 s(fingerprint `54880f05…` = P-2, 출시 빌드 `Managed/` 103개·`Harness.*` 0개), BagelGame 녹색 59.1 s(`619be553…` = P-2, `Managed/` 132개·`Harness.*` 0개), 사내 프로젝트 A 녹색 94.3 s(`6664b723…`, 대기 0.61–0.66 s).

- [x] **P-2 기존 Unity 프로젝트에 붙일 수 없다** (2026-09-29)
  - 방법:
    - **UPM 패키지**: `Assets/Harness/` → `Packages/com.geuneda.agentharness/`(Runtime·Editor·UI + `Tools~/`, `package.json`은 `com.unity.pipeline` 의존).
      샘플은 임베드 패키지로 같은 코드를 쓴다(도그푸딩). `.meta`(GUID)를 그대로 옮겨 생성 에셋 참조가 유지된다. 기존 프로젝트는 git URL
      `https://github.com/geuneda/unitree.git?path=/AgentHarness/Packages/com.geuneda.agentharness#<ref>`로 받는다.
    - **얇은 진입점**: 모든 `tools/*.ps1`이 같은 파일(`Tools~/templates/entry.ps1`)이다. 패키지를 임베드 → manifest의 `file:` → `Library/PackageCache`
      순으로 찾아 `Tools~/<같은 이름>`을 부르고, 작업 루트를 `AGENTHARNESS_WORK_ROOT`로 넘긴다(`Harness.psm1`은 더 이상 자기 위치로 프로젝트를 정하지 않음).
      worktree는 에디터 트리의 패키지를 쓴다. git으로 설치하고 한 번도 안 연 체크아웃은 `open.ps1` 진입점이 배치 모드로 한 번 임포트해 패키지를 받는다.
    - **설정 파일** `ProjectSettings/AgentHarness.json`(`Harness.HarnessConfig`, `Get-HarnessConfig`): `setup`(harness|attach), `moduleRoots`, `modules[]`,
      `contracts`, `generatedRoot`, `buildScene`, `playScene`(build|first|경로), `installAdded`. `HarnessPaths` 상수·`ModuleOf`·빌더 탐색·lint·compile-check·
      submit·land가 모두 이 설정을 쓴다. 없으면 기존 프로젝트 기본값(아무것도 소유하지 않음, Build Settings 첫 씬).
    - **attach 모드**: `harness_setup`은 아무것도 바꾸지 않고 `recommendations`만(`{"apply":"domainReload,..."}`로 명시 적용). 템플릿 샘플 삭제·
      Build Settings 변경·폴더 생성은 `setup: harness`에서만. `harness_build`는 빌드 스텝이 없으면 플레이 씬을 열고 에셋 기준 fingerprint만 낸다(`skipped`).
      하네스가 만든 적 없는 buildScene·생성 에셋(`AgentHarnessGenerated` 라벨)은 덮어쓰거나 지우지 않는다. 씬에 저장 안 한 변경이 있으면 씬을 바꾸지 않는다.
      Domain Reload가 켜진 프로젝트도 루프가 돌고 비용은 `timings.playEnterSec`(플레이 요청 → 러너 시작)으로 보고. 그때 lint `static-reset`은 건너뛴다.
    - **선택 의존**: asmdef `versionDefines`(`AGENTHARNESS_URP`·`_RP_CORE`·`_INPUT_SYSTEM`)로 URP 카메라 데이터 복사·`ctx.VolumeProfile`·입력 재생만 빠지고
      Built-in·구 Input Manager 프로젝트에서도 컴파일된다. 없는 asmdef 이름 참조는 Unity가 무시한다(확인).
    - **런타임 주입 없음**: `GameRoot`는 모듈이 등록됐을 때만 만들고(없으면 바로 `HarnessProbe.Ready`), `Harness.Runtime`은 define 제약
      `UNITY_EDITOR || DEVELOPMENT_BUILD || AGENTHARNESS_RUNTIME`. `HarnessReleaseBuild`(IFilterBuildAssemblies)가 출시 빌드에서 하네스 때문에만 들어온
      `Unity.Pipeline.Attributes`·`Newtonsoft.Json`·(`installAdded`의) `Unity.InputSystem`·`.ForUI`를 뺀다: 패키지가 lock에서 하네스 경유로만 닿고,
      빌드의 다른 DLL이 메타데이터에서 실제로 참조하지 않을 때만(엔진 모듈·.NET은 `InternalsVisibleTo`로만 이름을 적으므로 제외; 후보끼리는 고정점).
    - **install.ps1 / uninstall.ps1**(`Tools~`): 위 README·CLAUDE.md "기존 프로젝트에 붙이기". manifest는 Unity 형식을 유지하는 텍스트 편집(정렬 위치에 한 줄),
      lock은 설치 전 바이트를 `Library/AgentHarness/install.json`에 남겨 uninstall이 그대로 복원. `-WhatIf`. 에이전트용 안내서 `tools/AgentHarness.md`.
    - **attach-test.ps1**: 매트릭스 10(위).
    - 모듈 일반화: `modules[]`의 폴더(기존 asmdef 폴더 포함)가 에러 `module`, compile-check 대상, submit/land 단위가 된다. `module-asmdef`·`module-boundary`
      lint는 `moduleRoots` 모듈에만(기존 코드 구조를 실패로 치지 않음).
  - 만들다 드러난 하네스 버그·함정(모든 프로젝트 공통, 고침):
    - 테마를 패키지로 옮기자 빌드마다 Unity가 `Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss`를 새로 만들었다(`PanelSettings.GetOrCreateDefaultTheme`)
      → 생성 중에만 훅을 하네스 테마로 바꾸고, 그래도 생기면 지운다.
    - 오프스크린 캡처가 러너의 `LateUpdate`(실행 순서 -2000)에서 찍혀, `LateUpdate`에서 `Graphics.DrawMeshInstancedIndirect`로 그리는 Fluid-Sim이 검은 화면(`blank`)
      → 캡처를 실행 순서 32000인 `ScenarioCaptureDriver.LateUpdate`로 옮김(Cinemachine처럼 LateUpdate에서 카메라를 움직이는 게임도 맞는 포즈). 샘플 샷 통계 불변.
    - 투명 색으로 지우는 카메라의 PNG가 투명(뷰어에서 흰색)이었다 → RGB24로 저장.
    - compile-check가 "가장 최근" 응답 파일을 써서, 플레이어 빌드 뒤에는 `UNITY_EDITOR` 없는 rsp로 검사해 `#if UNITY_EDITOR` 안의 에러를 놓쳤다
      (BagelGame의 `Bakery.cs` 전체가 그 안) → 에디터 컴파일의 rsp(`…EDbg.dag` > `…E.dag`)만 쓴다.
    - UPM이 git 패키지를 CRLF로 체크아웃해 uninstall의 "템플릿과 같으면 지움" 비교가 틀렸다 → 줄바꿈 정규화.
    - 지원 종료 패키지가 있으면 Unity가 열 때마다 모달을 띄운다(Fluid-Sim의 `com.unity.ide.vscode`) → `open.ps1`이 `deprecatedPackages`로 보고, 대화상자로
      막히면 원인을 에러에 붙인다. Pipeline의 입력 컴파일 버그는 O-1.
  - 검증(이 머신, 샘플 에디터 1개 동시 실행; 테스트 클론은 `../ah-p2`, Unity 업그레이드 변경은 먼저 "기준선" 커밋):
    - **BagelGame**(Unity-Technologies, URP 17.3, Input System, Cinemachine, asmdef 5개, 기존 `Assets/Scenes/Main.unity`·`Assets/Game/` — 하네스 기본 경로와 겹침;
      6000.3.9f1 → 6000.3.11f1): `attach-test -Module Game=Assets/Game,UI=Assets/UI` 녹색 53.6 s(install 0.5 / open 23.6 / 루프 3.7–3.8 s ×3 / 출시 빌드 11.9 /
      quit 2.1 / uninstall 0.4). fingerprint `619be553…` 3회 동일, 메인 메뉴 샷 meanLuma 65.5(육안 확인), FPS ~120, Domain Reload는 원래 꺼져 있어
      playEnterSec 0.4–0.6 s. install 뒤 `git status` = manifest 한 줄 + lock + 새 파일 12개. 출시 빌드 `Managed/` 132개 = 하네스 없는 대조 빌드(`unity build`)
      132개와 목록 동일. uninstall 뒤 `git status` 비어 있음. 기존 코드에 넣은 컴파일 에러·런타임 예외가 `Assets/Game/Bakery/Bakery.cs:137`, `module: Game`으로
      정확히 보고. Input System 입력 재생 5개 적용. worktree에서 `submit -Module Game`(compile-check `BagelGame: ok`) → 녹색 유지, 커밋 → `land` fast-forward 6.5 s
      (`releasedOwners: Game`), 컴파일 에러 submit은 게이트에서 0.96 s 거부(에디터 트리 무변경). 개발 빌드에는 `Harness.Runtime`·Pipeline 런타임이 들어간다(의도).
      빌드는 하네스와 무관하게 URP 에셋·`ProjectSettings.asset`·`GraphicsSettings.asset`·`TimeManager.asset`을 다시 쓴다(대조 빌드도 같음) → attach-test가 `buildRewrote`로 보고·복원.
    - **SebLague/Fluid-Sim**(Built-in, Input System 없음, `Assembly-CSharp`만, 컴퓨트 셰이더, Build Settings 비어 있음, Active Input Handling=Both;
      2022.3.46f1 → 6000.0.84f1, 지원 종료 `com.unity.ide.vscode`는 기준선에서 제거): `-Scene` 없이 설치하면 후보 씬 목록과 함께 거부.
      `attach-test -Scene "Assets/Scenes/Fluid Particles.unity"` 녹색 34.2 s(open 18.6 / 루프 1.7–2.0 s ×3 / 빌드 6.0). fingerprint `54880f05…` 3회 동일,
      입자 샷 23.0/14.9/22.2(GPU 시뮬레이션이라 ±0.1 흔들림), FPS ~220. install이 `com.unity.inputsystem`을 더함(`installAdded`) → 출시 빌드에서 빠짐,
      `Managed/` 103개 = 대조 빌드 103개와 동일. uninstall이 Input System 줄까지 지워 `git status` 비어 있음.
      Domain Reload를 잠시 켠 상태: 루프 3회 녹색, playEnterSec 2.1–2.3 s(끈 상태 0.13 s), 루프 4.0–4.3 s(1.7 s), `harness_setup`이 `domainReload` 권장, lint는 static 검사 생략.
    - **git URL 배포 경로**: `-Source git+file:///…/unitree?path=/AgentHarness/Packages/com.geuneda.agentharness#master`(커밋된 패키지)로 Fluid-Sim
      attach-test 녹색 38.4 s — 패키지가 아직 없을 때 `open.ps1` 진입점의 배치 임포트(부트스트랩) 포함 open 22.7 s.
      푸시한 뒤 기본값 `-Source git`(`https://github.com/geuneda/unitree.git?path=/AgentHarness/Packages/com.geuneda.agentharness#master`)으로도
      Fluid-Sim attach-test 녹색 41.9 s(open 26.0 s, 출시 빌드 `Managed/` 103개·`Harness.*` 0개, uninstall 뒤 `git status` 비어 있음).
    - 샘플: 매트릭스 1–8 녹색 199 s(fingerprint `977545a7…` — 스크립트·테마 경로가 `Packages/…`로 바뀐 것만 다름, 샷 통계 65.1/60.5/50.3 동일, 줄 61/68/87).
      9(`fresh-clone-test -SelfTest -UnityVersion`, 새 클론 = 임베드 패키지): 6000.3.11f1 녹색 306 s(`977545a7…` = 메인 트리, 종료 뒤 `git status` 깨끗),
      6000.0.84f1 녹색 299 s(`d9a6d092…`; `git status`는 P-1과 같은 버전 전환 파일만), 6000.6.3f1 2–8 녹색·1 빨강 311 s(`0ba32228…`, P-4의 `dark` 샷
      25.0/15.6/0.5 그대로). 세 버전 모두 줄 61/68/87. fingerprint는 스크립트·테마 경로(`Assets/Harness` → `Packages/…`)만큼 바뀌었다.

- [x] **P-1 Unity 버전이 6000.3.11f1로 고정돼 있다** (2026-09-29)
  - 지원 범위: **Unity 6.0 LTS 이상**(하한 = `com.unity.pipeline` 0.8.0-exp.1의 `"unity": "6000.0"`). 검증 목록: 6000.0.84f1(6.0 LTS 최신),
    6000.3.11f1(6.3 LTS, 샘플 고정 버전), 6000.6.3f1(최신 정식). 설치는 `unity install <v> --no-cm`(각 ~4 GB, 새 버전 첫 실행에 이용 약관 동의 필요).
  - 버전을 타던 것과 고친 방법:
    - **manifest의 내장 모듈**: 6.0에는 `com.unity.modules.adaptiveperformance`·`vectorgraphics`, 6.6에는 `com.unity.modules.vr`이 없어
      에디터가 `Package ... cannot be found`로 시작하다 종료(코드 1). 템플릿 기본 `com.unity.visualscripting` 1.9.10은 6.6에서 CS0619.
      넷 다 아무도 안 써서 `PackageManager.Client`(eval)로 제거. URP·Core 같은 코어 패키지는 manifest(17.3.0)와 무관하게 에디터 내장 버전
      (6.0 17.0.4, 6.6 17.6.0)으로 해석돼 그대로 둔다.
    - **compile-check**: Hub 기본 경로·`ProjectVersion.txt` 대신 Bee 빌드 그래프(`Library/Bee/*.dag.json`)에 기록된 컴파일 명령에서 dotnet·csc.dll·플래그를
      읽는다. 6.6은 설치 구조가 달라(`Data/DotNetSdk/dotnet.exe` + `Data/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll`) 옛 방식으로는 실패했다.
      응답 파일 형식은 세 버전 모두 같다(`<Asm>.rsp` + 빈 `.rsp2`, `/nostdlib /noconfig /shared`). 그래프 읽기 ~45 ms.
    - **API**: `FindObjectsByType(…, FindObjectsSortMode)`가 6.4부터 obsolete(6.6에서 CS0618 경고 6개)이고 대체 오버로드는 6.3에 없다
      → `Harness.Runtime/UnityCompat.FindObjects`(`#if UNITY_6000_4_OR_NEWER`) 한 곳에서 가른다. asmdef `versionDefines`는 필요 없었다.
    - **열기**: `open.ps1 -UnityVersion <v>`(ProjectVersion.txt를 그 버전으로 바꿔 "다른 버전으로 열기" 모달을 건너뜀), 설치 안 된 버전이면 설치된 목록을 보여 준다.
      새 버전의 이용 약관 창·Safe Mode 창처럼 Pipeline 서버가 뜨기 전의 다이얼로그는 전에는 보이지 않아 타임아웃(새 클론 1800 s)까지 기다렸다
      → 로그가 60 s 멈추고 에디터 프로세스에 진행 창이 아닌 창이 있으면 `dialog.title`로 실패(Win32 `EnumWindows`; `MainWindowTitle`로는 Safe Mode 창이 안 보임).
      `open.ps1`이 띄운 pid를 `Logs/harness-editor.json`에 남겨, 락 파일이 생기기 전에도 다시 부르면 두 번째 에디터를 띄우지 않고 그 에디터를 기다린다.
      `quit.ps1`은 그런 에디터를 "없음(ok)"으로 보고하던 것을 고쳐 창 제목과 함께 실패하고 `-Force`면 종료한다. 실측(6.6, 시작 시 컴파일 에러):
      새로 띄우면 87 s, 이미 떠 있으면 60 s 만에 `Enter Safe Mode?` 보고(전: 400 s 타임아웃까지 대기).
    - 루프 report와 `harness_ping`에 `unityVersion`(실제로 돈 에디터).
  - P-1을 돌리다 드러난 하네스 버그(모든 버전 공통):
    - **런타임 예외가 났는데 루프가 녹색**: `[InitializeOnLoad]` `HarnessConsole`이 에셋 임포트 워커 프로세스에서도 돌아, 워커의 빈 SessionState를 새 세션으로 보고
      `Library/Harness/console.ndjson`을 지우고 seq 1부터 썼다 → 루프의 `since <mark>` 조회가 그 뒤 줄을 놓쳤다(selftest 3번이 새 클론에서 간헐적으로 빨감,
      `SpinnerLap=1`로 예외 발생은 확인). 증거: 그 파일의 `[Pipeline] Failed to persist console log buffer` 줄은 워커 로그에만 있고 `Editor.log`에는 0건.
      `HarnessConsole`·`HarnessPlay`·`HarnessCodeOptimization`은 `AssetDatabase.IsAssetImportWorkerProcess()`면 아무것도 안 하고, seq는 도메인 리로드 때
      SessionState에도 남겨 되돌아가지 않게, 로그 쓰기는 IOException이면 재시도, 에디터 내부 에러 판정은 `\` 경로도 프로젝트 프레임으로 본다.
      수정 뒤 새 클론 3개의 `console.ndjson`에 워커 줄·seq 역행 0건, selftest 3번 녹색.
    - `Mobile_RPAsset`이 옛 직렬화(v12)라 selftest 뒤 에디터 종료 때 URP 17.3이 v13으로 저장해 6.3 새 클론의 `git status`가 더러웠다 → 6.3이 쓰는 모양 그대로 저장해 커밋.
  - 검증(이 머신, 다른 에디터 1개 동시 실행; `fresh-clone-test.ps1 -UnityVersion <v> -SelfTest`, 버전당 ~4.5–5 분):
    - 6000.0.84f1: 매트릭스 1–9 녹색. fingerprint `882e811b…`(3회 동일), 줄 61/68/87, 샷 통계가 6.3과 소수점까지 같음(65.1/60.5/50.3), 육안 동일.
      클론 `git status`에는 버전 전환으로 다시 쓰인 `ProjectVersion.txt`·`packages-lock.json`·`URPProjectSettings.asset`·URP 전역 설정만.
    - 6000.3.11f1: 매트릭스 1–9 녹색. fingerprint `5887385e…`, 줄 61/68/87, 종료 뒤 클론 `git status` 깨끗.
    - 6000.6.3f1: 2–8 녹색, 1 빨강(첫 플레이 뒤 조명이 검다, `dark` 3장) → **P-4**로 남김. fingerprint `4a3c5c8c…`(3회 동일), 줄 61/68/87.
    - `Assets/Harness/`·`tools/`에서 `6000.` 검색 0건(버전 분기는 `UNITY_6000_4_OR_NEWER` 정의 하나).
    - 메인 트리 `selftest.ps1` 1–8 녹색 3.3–3.7 분, 루프(변경 없음) 3.5–4 s로 기준선과 같다.

- [x] **O-3 하네스 자체의 자동 테스트가 없다** (2026-09-29)
  - 방법: `tools/selftest.ps1` = 검증 매트릭스 1–8을 한 번에. 주입은 샘플 모듈의 표식 줄을 바꾸는 방식(컴파일 `m_Time += dt;` 61행, 런타임
    `EventBus.Publish(new SpinnerLap(laps));` 68행, 셰이더 `Frag` 첫 줄 87행, lint용 새 파일)이고, 에러가 **주입한 file/line/module 그대로** 보고돼야 녹색.
    7–8은 저장소 옆에 worktree 2개(`<저장소>-st-a/-b`)와 `selftest/*` 브랜치를 만들어 게이트 거부, 강제 submit 되돌림 + 다른 worktree의 새 모듈·계약
    락 대기 후 유지, 런타임 에러 되돌림, sync 직후 kill → `recoveredSubmit`, 계약 수정 거부, land fast-forward + 그 사이 submit 락 대기, 이미 병합됨,
    미커밋·`.meta` 누락·충돌·에디터 트리 직접 수정 거부, 컴파일 에러 land 되돌림, 병합 직후 kill → `recoveredLand`까지 확인하고, 끝나면 worktree·브랜치·
    테스트 커밋을 걷어내 에디터 트리 브랜치를 시작 커밋으로 되돌린다(detached HEAD면 임시 브랜치를 썼다가 되돌림). 마지막 루프(`final`)로 녹색과
    `git status` 원상을 확인. 첫 빨간 항목에서 멈추고 `-KeepGoing`이면 계속(`fresh-clone-test.ps1 -SelfTest`가 씀). 결과 `HarnessOut/selftest/report.json`.
  - 첫 실행에서 드러난 것: 위 P-1의 console 버그(3번), 6.6 렌더링(1번의 `dark` 검사) — 사람이 매트릭스를 돌릴 때는 둘 다 놓쳤다.
  - 한계: PNG 눈 확인은 여전히 사람·에이전트 몫(`shots`를 Read). (1번 events 비교를 드물게 빨갛게 하던 실제 키보드 입력은 G3-6에서 막았다.)

- [x] **O-8 새 클론 검증을 자동화한다** (2026-09-29)
  - 방법: `tools/fresh-clone-test.ps1`. 이 저장소(기본 HEAD; `-Source`/`-Ref`로 원격도)를 짧은 경로(`<저장소 상위>/ah-fresh`, 프로젝트 경로 60자·`%TEMP%` 밖 검사)에
    클론 → 클론의 `open.ps1`(첫 임포트) → `uc.ps1 harness_setup` → `loop.ps1` ×3 → `quit.ps1` → 클론 `git status` → 삭제. 모든 단계를 **클론의 도구**로,
    자식 PowerShell(현재 호스트, `AGENTHARNESS_EDITOR_ROOT` 제거)에서 돌린다. 판정: 단계 성공, setup 뒤 `issues` 없음, 루프 녹색, 루프끼리 fingerprint·events 동일
    (`-ExpectFingerprint`), `harness_quit`으로 정상 종료, 종료 뒤 `git status` 깨끗. 결과는 `HarnessOut/fresh-clone/`(report.json, loop<N>.json, 마지막 루프 shots/,
    클론 Editor.log). 실패하면 에디터는 닫고 클론은 남긴다(`-Force`로 교체). `-Keep`, P-1용 `-UnityVersion`(설치 확인 + 클론의 `ProjectVersion.txt` 교체; 다른 버전으로는 아직 미실행).
    child 프로세스 실행은 `Invoke-HarnessProcess`(git도 이것을 쓰도록 `Invoke-HarnessGit`을 옮김)로.
  - **찾은 것: 같은 코드·같은 버전인데 새 클론의 fingerprint(`e0a075e5…`)가 메인 트리(`b012cf35…`)와 달랐다.** `-Keep`으로 남긴 클론의 덤프와 diff하니
    URP Lit `.mat` 2개에서 숨은 하위 객체 `AssetVersion`의 **순서만** 달랐다. `LoadAllAssetsAtPath`는 로컬 fileID 순서로 주는데 새로 만든 .mat은 AssetVersion이 앞,
    이력이 있는 .mat은 뒤다. F-2 때 새 클론에서 본 `e0a075e5…`도 이것이었다. `SceneFingerprint.HashAsset`이 객체별 덤프를 정렬해서 해시하도록 고쳤다
    → 메인 트리·남긴 클론·새 클론 모두 `5887385e…`. (Release 값은 다시 재지 못했다: `HarnessCodeOptimization`이 도메인 로드마다 Debug로 되돌려서
    이 프로젝트에서는 Release가 유지되지 않는다.)
  - 측정(이 머신, 다른 에디터 없음, 3회): 전체 108 s 안팎 — 클론 0.4–0.6 s, open(첫 임포트 + Debug 재컴파일) 78–80 s, setup 0.5 s(`created Assets/Generated`),
    루프 8.1–9.0 / 3.6–5.2 / 3.7–4.1 s(자식 PowerShell 포함), quit 2.8–3.4 s, 삭제 7.8–8.3 s. 클론 `Editor.log` 1.1 MB. 종료 뒤 `git status` 깨끗.
    `-ExpectFingerprint 5887385e`로 녹색. `-Force`로 에디터가 열린 채 남은 클론을 닫고 교체. 샷은 메인 트리와 육안 동일.
    첫 루프 `editorErrors`에 O-6(Search 인덱서) 1건.
  - 만들다 겪은 것: `quit.ps1`이 종료를 확인한(`HasExited`) 직후에도 `Get-Process`에 그 pid가 잠깐 남아서, 처음엔 이미 끝난 프로세스를 kill하고
    `killed`로 보고했다 → 살아 있는지는 `HasExited`로 보고, quit이 실패했을 때만 kill.

- [x] **O-4 에디터를 코드로 닫는 믿을 만한 방법이 없다** (2026-09-29)
  - 원인: Pipeline `quit`은 플레이어용(`DontDestroyOnLoad`)이라 편집 모드에서 실패하고, `delayCall`은 포커스 없는 에디터에서 돌지 않는다.
    `eval`로 `Exit(0)`를 직접 부르면 닫히지만 응답이 깨진다(빈 본문).
  - 방법: `harness_quit`(`HarnessQuit.cs`)이 `EditorApplication.update`에 한 번짜리 콜백을 걸고 0.3 s 뒤 `EditorApplication.Exit(0)`.
    update는 Pipeline 디스패처가 명령을 실행하는 곳이라 백그라운드에서도 돌고, 응답은 HTTP 스레드가 메서드 반환 뒤에 쓰므로 지연을 둔다.
    `tools/quit.ps1` = 에디터 락(다른 에이전트의 loop/submit/land가 끝난 뒤) → `harness_quit`(도메인 리로드가 예약을 지울 수 있어 10 s마다 재요청) →
    프로세스 종료 대기. `-Force`면 타임아웃 뒤 kill, busy(`blocked_by_dialog`)면 `dialogs`를 보고. 에디터가 없으면 `method=none`으로 녹색.
    디스크립터의 pid는 프로세스 시작 시각이 디스크립터보다 늦으면(죽은 에디터의 pid 재사용) 무시한다.
  - 검증: 포커스 없는 메인 에디터 3.8 s, 새 클론 에디터 2.8–3.4 s(3회) 모두 `method=harness_quit`. 로그: `[Harness] harness_quit: closing the Editor`
    → 레이아웃 저장 → 정상 종료. 다시 `open.ps1`로 열어 첫 루프 녹색.

- [x] **O-7 에디터 두 개를 같이 띄웠을 때 전역 `Editor.log`가 1.4GB까지 커졌다** (2026-09-29)
  - 원인(확인): `-logFile` 없이 뜬 에디터(`unity open`, Hub)는 모두 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 하나에 쓰고, 나중에 뜬 에디터는 파일
    **처음부터** 쓴다. 두 프로세스가 각자의 위치에 이어 쓰면서 서로 덮어쓴다: 전역 로그 10,755행 중 ~3,100행까지가 AgentHarness 에디터(`harness_quit`까지),
    뒤쪽은 먼저 떠 있던 BunkerRandomDefense 에디터의 종료 기록이었다. 한 프로젝트의 로그를 따로 볼 수 없고, 한쪽이 같은 메시지를 쏟아 내면 파일이 커진다.
    1.4GB를 채운 "Access version should be odd when acquiring lock" 자체의 원인은 여전히 모른다(이번 세션 로그들에는 0건).
  - 방법: `tools/open.ps1`이 설치된 에디터(`unity editors --installed`)를 직접 `-projectPath <p> -logFile <p>/Logs/Editor.log`로 실행한다(직전 로그는
    `Editor-prev.log`). pid를 알고 기다린다: `harness_ping` 응답 + 3 s idle(첫 응답 뒤 Debug 재컴파일 포함)이면 녹색, 에디터가 죽으면 `logTail`과 함께,
    모달 다이얼로그가 20 s 이상이면 `dialog`와 함께 실패. 이미 열려 있으면(디스크립터 pid 또는 `Temp/UnityLockfile` 잠금) 기다리기만 한다.
    CLAUDE.md·README의 `unity open`을 `open.ps1`로 바꿨다.
  - 검증: 메인 에디터를 `open.ps1`로 다시 열기 29 s(domainReloads 2) — 전역 로그 mtime 그대로, `Logs/Editor.log`에만 기록, 도구 호출이 끝나도 에디터 유지.
    첫 루프 4.9 s 녹색(`editorWaitSec` 없음; F-7 때는 ~27 s 대기). 새 클론 에디터가 같이 떠 있는 동안에도 전역 로그 무변화, 각자 `Logs/Editor.log`.
  - 검증 매트릭스 1–9 녹색(이 항목들 전체 기준): fingerprint 3회 동일(`5887385e…`), 컴파일 61행·런타임 68행·셰이더 87행 정확, 재임포트 없는 셰이더 재검출,
    lint, 동시 루프 두 번째 3.69 s 대기 후 녹색. 7: 게이트 1.15 s 거부(에디터 트리 무변화), 강제 submit `stage=compile` + 되돌림 + 복구 ok,
    그 사이 B의 새 모듈 + 계약 submit 락 4.73 s 대기 후 녹색(`ProbeEcho=2`), submit kill → `recoveredSubmit` 녹색. 8: land fast-forward 5.84 s
    (stash 버림, 소유 해제), 그 사이 A의 submit 4.72 s 대기 후 녹색, 컴파일 에러 land → 되돌림 + HEAD·status 동일, 병합 직후 kill → `recoveredLand` 녹색,
    `.meta` 누락 1.06 s·미커밋 0.37 s·충돌 0.82 s·에디터 트리 직접 수정 1.18 s 거부(무변화), 이미 병합된 브랜치 0.79 s 녹색. 9: 위 O-8.
    루프(변경 없음) 3.45–4.0 s. 매트릭스 중 1회 `SpinDirectionChanged=2` → G3-6으로 등록.

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
