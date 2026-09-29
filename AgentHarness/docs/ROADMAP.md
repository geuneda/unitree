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
| W2 | 캡처가 화면 전체를 본다 | G3-1, G3-5 | M | W1 | 완료 (2026-09-29) |
| W3 | 시각 회귀와 움직임 | G3-4, G3-3, G3-7 | L | W1, W2 | 완료 (2026-09-30) |
| W4 | 렌더 설정을 코드로 | G1-1, P-4, G4-3, G4-2 | L | W3 | 완료 (2026-09-30) |
| W5 | 루프 속도 | G2-3, G2-1 | L | — | 대기 |
| W6 | 콘텐츠 헬퍼(a/b/c로 나눠 진행) | G1-3, G1-4, G4-1, G4-4 | L | W3, W4 (W6b는 W2) | 대기 |
| W7 | 에디터 밖·여러 에디터 | G2-2, G1-2, G5-1 | L | — | 대기 |
| W8 | 플레이어에서 돌리기(성능·실제 화면) | G3-2, G3-8 | L | W1 | 대기 |
| W9 | 병렬 작업의 공유 지점 | G5-4, G5-3 | M | — | 대기 |
| W10 | 렌더 밖의 프로젝트 설정도 코드로 | G1-5 | M | W4 | 대기 |
| W11 | 백그라운드 에디터의 실제 입력 격리 | G3-9 | S | — | 대기 |
| 상시 | 업스트림·외부 의존 | O-1, O-5, O-6, O-9, P-4 신고 | S | 새 버전이 나올 때 | — |
| 마지막 | macOS | P-3 | L | 실제 Mac | 대기 |

### W1 시나리오 입력 격리 (G3-6) — 완료 (2026-09-29, 아래 "해결됨")
- 왜 먼저: `play.events`가 매번 같아야 매트릭스 1과 W3의 기준 이미지가 의미 있다. 지금은 루프 ~25회에 1회 어긋난다.
- 고치는 곳: `Runtime/ScriptedInput.cs`(시나리오 동안 가상 장치 밖의 장치를 `InputSystem.DisableDevice`, 끝나면 복구),
  `Runtime/ScenarioRunner.cs`(걸러 낸 실제 입력 수를 `play`에 보고).
- 같이 볼 것: 구 Input Manager shim(`Tools~/templates/HarnessInput.cs`)도 `Input.GetKey(key) || Held(key)`라 실제 입력이 섞인다.
  시나리오 재생 중에는 실제 입력을 빼도록 훅에 시작·끝을 알린다. shim은 게임 소유 파일(uninstall이 남김)이라 이미 붙인 프로젝트의 갱신 방법도 정한다.
- 추가 검증: 루프 도중 실제 키보드로 스페이스 연타 → `play.events` 매번 같음. 플레이가 실패·중단돼도 장치가 다시 켜지는지(수동 플레이에서 키보드가 죽지 않는지).

### W2 캡처가 화면 전체를 본다 (G3-1 → G3-5) — 완료 (2026-09-29, 아래 "해결됨")
- 왜: 기존 프로젝트는 UI가 화면의 전부인 경우가 많은데(P-5, 사내 프로젝트 A) 지금은 `"screen"`으로만 찍히고 크기가 사용자 레이아웃을 따른다.
  W3의 기준 이미지 비교도 고정 해상도·UI 포함 캡처가 있어야 된다.
- 고치는 곳: `Runtime/HarnessCapture.cs`(UI 합성, 이미지 통계), `Runtime/ShotPreset.cs`, `Editor/HarnessCaptureCommand.cs`.
- 주의: UI Toolkit은 `PanelSettings.targetTexture`로 되지만 uGUI Screen Space - Overlay 캔버스는 다른 방법이 필요하다(기존 프로젝트는 대부분 uGUI).
  Game 뷰 크기는 사용자 전역 설정이라 코드로 바꾸지 않는다.
- 추가 검증: 스모크 씬 `"auto"` 캡처에 HUD가 1280x720으로 찍힘. attach-test에서 사내 프로젝트 A의 부트·로비 화면이 `"auto"`로 찍힘.
  셰이더 없는 머티리얼 주입 → `shotStats`에 마젠타 판정(selftest 항목으로 추가).

### W3 시각 회귀와 움직임 (G3-7 → G3-4, G3-3) — 완료 (2026-09-30, 아래 "해결됨")
- G3-7(캡처에 카메라 스택 포함)을 먼저 한다: 기준 이미지는 플레이어가 보는 화면이어야 하는데, 지금 캡처는 카메라 하나 + UI라 스택의 다른 카메라가
  그리는 3D가 빠진다. 기준 이미지를 만든 뒤에 바꾸면 모든 기준 이미지를 다시 만들어야 한다.
- 고치는 곳: `Tools~/loop.ps1`·`Tools~/Harness.psm1`(report.json에 diff 점수), 샘플의 `golden/`,
  `Runtime/ScenarioRunner.cs`·`Runtime/HarnessCapture.cs`(N프레임 연속 캡처 → 스프라이트 시트/GIF; G3-7 카메라 스택)·`Runtime/CaptureUi.cs`.
- 먼저 정할 것: 기준 이미지는 Unity 버전별로 둔다(P-4처럼 버전마다 렌더가 다르다). 머신·GPU 차이 허용치(P-3의 부동소수점 문제와 같은 기준).
  의도한 변경일 때 기준 이미지를 갱신하는 명령. 캡처에 스크린 공간 UI가 합성되므로(W2) 기준 이미지에 HUD가 들어간다 — 시간·네트워크 값처럼 매번 다른
  글자가 있는 게임은 비교에서 뺄 영역이나 `"ui": false` 샷을 정한다.
- 추가 검증: 같은 코드로 3회 → diff가 허용치 안. 셰이더 한 줄 수정 → 점수가 움직이고 report에 보임.
  G3-7: 스택(Overlay 카메라)과 depth가 다른 Base 카메라가 있는 픽스처 → `"auto"`가 `"screen"`과 같은 레이어를 보여 줌.
  플레이 모드 uGUI(W2에서는 기존 프로젝트로만 확인): selftest가 플레이 중 eval로 오버레이 캔버스를 만들어(샘플은 UI Toolkit만 쓰는 규칙이라 코드로만)
  연속 캡처에도 합성되는지 본다.
- W4 전에 하는 이유: W4는 렌더 설정을 통째로 코드로 옮긴다. "옮기기 전과 같게 나오는지"를 이걸로 확인한다.

### W4 렌더 설정을 코드로 (G1-1 → P-4 → G4-3 → G4-2) — 완료 (2026-09-30, 아래 "해결됨")
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

### W8 플레이어에서 돌리기 (G3-2, G3-8)
- 개발 빌드 플레이어 + 런타임 Pipeline 서버로 같은 시나리오를 돌리는 `harness_perf`(fps, 프레임 p95, batches).
- G3-8: 같은 플레이어를 캡처 크기의 창(`-screen-width`/`-screen-height`, 창 모드)으로 띄우면 화면을 그대로 찍을 수 있다 — UI 재배치가 없고
  `Screen.width`가 캡처 크기이며 카메라 스택도 그대로다. 에디터 캡처(W2 합성)와 나란히 찍어 차이를 보고한다.
- 고치는 곳: `Editor/HarnessReleaseBuild.cs`(개발 빌드 + `AGENTHARNESS_RUNTIME`), `Runtime/ScenarioRunner.cs`(플레이어에서 `"screen"` 캡처),
  `Tools~/`에 새 진입점.
- 추가 검증: 에디터 플레이 FPS와 플레이어 FPS를 나란히 기록. 출시 빌드에는 여전히 `Harness.*`가 없음(매트릭스 10).
  G3-8: 사내 프로젝트 A 로비를 플레이어 720x1280 창으로 찍은 것과 에디터 `"auto"`가 같은 배치(다르면 원인 기록).

### W9 병렬 작업의 공유 지점 (G5-4 → G5-3)
- G5-4: 이벤트 파일을 발행 모듈별로 나누는 lint(`Editor/HarnessLint.cs`)와 이름 충돌 검사. 계약 파일도 owners.json처럼 추가한 worktree를 기록해
  병합 전까지는 그 worktree만 고치게 한다(`Tools~/submit.ps1`, `Tools~/land.ps1`).
- G5-3: 남은 부분(검사 집합 밖 모듈은 에디터 DLL 기준)이 worktree 흐름에서 실제로 문제가 되는지부터 본다. 아니면 `[~]`인 채로 닫는다.
- 추가 검증: 매트릭스 7·8에 "두 worktree가 같은 이벤트 이름을 추가 → 두 번째 submit/land 거부"를 더한다.
- 에이전트 여럿을 붙여 쓰기 시작하면 앞당긴다.

### W10 렌더 밖의 프로젝트 설정도 코드로 (G1-5)
- W4의 `ISettingsStep`/`SettingsContext`를 넓힌다: 품질 레벨 목록과 레벨별 값, Player Settings(색 공간·방향), Physics·Time, Tags/Layers.
- 고치는 곳: `Editor/Build/SettingsContext.cs`(헬퍼), `Editor/HarnessSetup.cs`(harness 프로젝트에서 매번 적용·드리프트 보고), 샘플 `Assets/Game/Stage/Builders/`.
- 주의: ProjectSettings는 생성물로 둘 수 없다(에디터 시작에 필요). attach 프로젝트는 W4처럼 건드리지 않는다.
- 추가 검증: ProjectSettings YAML을 손으로 바꾼 뒤 루프 → 코드 값으로 돌아오고 보고됨. 새 클론 `git status` 깨끗(매트릭스 9).

### W11 백그라운드 에디터의 실제 입력 격리 (G3-9)
- 재현부터: 루프 도중 다른 창을 눌러 에디터 포커스를 뺏고(`fps.editorFocused=false`) 실제 키보드에 스페이스를 넣는 selftest 1번 절차를 돌린다. 이어서 플레이
  도중 포커스를 되돌렸을 때 켜지는 장치가 `OnDeviceChange`로 잡히는지(누름이 `isolatedDevices`에 세어지고 `play.events`가 같은지) 본다.
- 고치는 곳: `Runtime/ScriptedInput.cs`(`RealInputIsolation`), `Tools~/selftest.ps1`(지금은 포커스 없는 시도를 3번까지 다시 한다).

### 상시: 업스트림·외부 의존 (O-1, O-5, O-6, O-9, P-4 신고)
- 코드보다 신고와 재검증: Pipeline에 2건(`RuntimeInputCommand.cs`의 `ENABLE_INPUT_SYSTEM` 조건, 출시 빌드 의존), Unity에 P-4의 원인
  (`Camera.RenderToCubemap(Cubemap)`: 6.6은 CPU 픽셀을 안 채우고 6.3은 sRGB로 인코딩 — 빈 씬 + 스카이박스 + half 큐브맵 한 개로 재현), O-6 Unity Search 예외.
- 계기: Pipeline 새 버전이나 Unity 6000.x 새 패치 → 매트릭스(9는 그 버전으로) 재검증 → 우회 코드(`Invoke-HarnessRecompile` 세대 번호,
  install의 Input System 추가, `HarnessReleaseBuild`)를 걷어낼 수 있는지 본다. O-9: 새 버전에서 selftest 1번의 HUD 검사(`uiError` 없음)를 보고,
  UI Toolkit에 패널을 지금 그리는 공개 API가 생기면 리플렉션을 걷어낸다.

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

- **G1-1 프로젝트 설정과 URP 에셋이 여전히 YAML** → 2026-09-30 해결(W4, 아래 "해결됨"). RP 밖의 프로젝트 설정은 G1-5.

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

- [ ] **G1-5 렌더 파이프라인 밖의 프로젝트 설정은 여전히 YAML** (2026-09-30, W4에서 남은 것)
  - 현상: W4로 URP·Renderer 에셋과 품질 레벨별 파이프라인 배정은 `ISettingsStep` 코드가 됐다. 품질 레벨 자체(목록·이름·레벨별 그림자·LOD·vSync),
    Player Settings(색 공간·해상도·방향), Physics·Time·Tags/Layers, URP 전역 설정(`UniversalRenderPipelineGlobalSettings`)은 여전히 커밋된 YAML이고,
    하네스가 코드로 만지는 것은 `harness_setup`의 몇 가지(Domain Reload, runInBackground, 동기 셰이더 컴파일)뿐이다.
  - 방향: `SettingsContext`에 품질 레벨·Player·Physics·Tags/Layers 헬퍼(에디터 API, 없으면 `SettingsContext.Set`처럼 SerializedObject). ProjectSettings는
    Unity가 시작할 때 필요해서 생성물로 둘 수 없으니 "코드가 매번 같은 값으로 쓴다 + 코드와 다르면 경고" 쪽이다.
  - 완료 기준: 샘플의 품질 레벨·레이어·색 공간이 코드에만 있고, ProjectSettings YAML을 손으로 바꾸면 다음 루프가 코드 값으로 되돌리며 보고한다.

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

- **G3-1 오프스크린 캡처에 스크린 공간 UI가 안 찍힌다** → 2026-09-29 해결(W2, 아래 "해결됨").

- [ ] **G3-2 에디터 플레이 모드 FPS는 실제 성능을 대표하지 못한다**
  - 현상: 에디터 오버헤드, autotick, Debug 코드 최적화가 섞인다. 지금은 변경 전후 비교에만 쓸 수 있다.
  - 방향: 개발 빌드 플레이어 + 런타임 Pipeline 서버로 같은 시나리오를 돌리는 `harness_perf`.

- **G3-3 정지 이미지만 나온다** → 2026-09-30 해결(W3, 아래 "해결됨"). 연속 캡처는 한 장의 시트(GIF는 만들지 않음).

- **G3-4 시각 회귀 검사가 없다** → 2026-09-30 해결(W3, 아래 "해결됨"). 다른 머신의 허용치는 P-3.

- **G3-5 이미지 판정이 휴리스틱이다(마젠타 머티리얼)** → 2026-09-29 해결(W2, 아래 "해결됨"). `blank`·`dark`는 여전히 휴리스틱이다
  (기준 이미지가 있으면 G3-4의 비교가 바뀐 화면을 잡는다).

- **G3-6 실제 키보드·게임패드 입력이 시나리오 재생에 섞인다** → 2026-09-29 해결(W1, 아래 "해결됨").

- **G3-7 캡처는 카메라 하나 + 스크린 공간 UI다** → 2026-09-30 해결(W3, 아래 "해결됨"). 스택 Overlay 카메라의 캔버스는 렌더 요청이 그리지 않아
  합성한다(아래 G3-8의 플레이어 캡처와 비교할 것).

- [ ] **G3-8 에디터 캡처의 UI가 게임의 화면 크기 코드와 어긋날 수 있다** (2026-09-29, W2에서 남은 것)
  - 현상: 에디터에서는 게임이 Game 뷰 크기로 돈다. 캡처는 UI만 캡처 크기로 잠깐 다시 배치하므로 (1) 크기 변화 콜백(`OnRectTransformDimensionsChange`,
    `GeometryChangedEvent`)이 캡처마다 두 번 더 불리고, (2) `Screen.width/height`를 직접 읽어 배치한 UI·카메라(safe area 스크립트, 비율 맞춤 카메라)는
    Game 뷰 기준 그대로 찍힌다. 지금 우회는 그 캡처에 `"ui": false` 또는 `"screen"`.
    (3) (2026-09-30, W3) 스택 Overlay 카메라(UI 카메라)의 캔버스는 URP 렌더 요청이 그리지 않아 숨은 직교 UI 카메라로 그려 카메라들 위에 합성한다 →
    그보다 뒤에 그리는 Base 카메라(미니맵)와의 앞뒤, 원근 UI 카메라로 기울여 그린 캔버스가 게임과 다를 수 있다.
  - 방향: 개발 빌드 플레이어를 캡처 크기의 창으로 띄워 화면을 그대로 찍는 경로(W8) — 재배치 없음, `Screen.width` = 캡처 크기, 카메라 스택 포함.
    에디터 캡처는 빠른 루프용으로 두고 플레이어 캡처와의 차이를 보고한다.
  - 완료 기준: 사내 프로젝트 A 로비를 플레이어 720x1280 창으로 찍은 것과 에디터 `"auto"`가 같은 배치(다르면 원인이 report에 나옴).

- [ ] **G3-9 에디터가 백그라운드일 때 실제 입력 격리가 검증되지 않는다** (2026-09-30, W4 매트릭스에서 발견)
  - 현상: 새 클론 selftest 1번(6.3·6.6)의 실제 입력 단계에서 그 루프만 `fps.editorFocused=false`였고(누군가 다른 창을 씀) `isolatedDevices`가 비었으며, 중간에 멈춘
    플레이 동안 실제 장치가 켜진 채였다(`disabled []`). 넣은 스페이스 60번은 게임에 닿지 않았다(`play.events` 같음). 격리는 켜져 있는 장치만 끈다(백그라운드라
    Input System이 끈 장치는 두고, 포커스 복귀로 켜지면 그때 끈다 — W1 설계). 포커스 없이 켜져 있던 장치를 왜 못 껐는지, 포커스 복귀 경로가 맞게 도는지는 확인하지 않았다.
  - 지금: selftest가 포커스 없는 시도를 3번까지 다시 한다(`realInputTries`, `realInputStopTries`). 포커스가 계속 없으면 여전히 빨갛다.
  - 완료 기준: 에디터가 백그라운드인 채로도, 도중에 포커스가 돌아와도 실제 누름이 게임에 닿지 않고 `isolatedDevices`에 보고된다(selftest에서 포커스를 조작해 확인).

## 성질 4 — 에셋 없이도 완성도

- [ ] **G4-1 CPU(C#) 텍스처 베이크가 느리다**
  - 현상: 512² 지형 베이크 ~1초(Debug). 빌드 캐시로 가렸지만 해상도를 올리면 느려진다. 지금 지형 텍스처(512 / 320m)는 흐릿하다.
  - 방향: GPU 베이크 경로(Blit/Compute 셰이더 → RT → PNG), 지형 디테일 텍스처 타일링.

- **G4-2 스카이박스 앰비언트는 라이팅 베이크가 필요해서 Trilight로 우회 중** → 2026-09-30 해결(W4, 아래 "해결됨").

- **G4-3 코드로 만든 URP Lit 머티리얼은 키워드를 수동으로 켜야 한다** → 2026-09-30 해결(W4, 아래 "해결됨").

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
Unity는 6.0 LTS 이상(6000.0.84f1·6000.3.11f1·6000.6.3f1에서 매트릭스 전부 녹색 — P-1, P-4), OS는 Windows 하나에서만 검증했다.

순서: **P-1(버전, 2026-09-29 해결) → P-2(기존 프로젝트, 2026-09-29 해결) → P-5(붙인 뒤의 격차, 2026-09-29 해결) → P-3(macOS, 나중)**.
버전은 `tools/fresh-clone-test.ps1 -SelfTest -UnityVersion <v>`, 기존 프로젝트는 `tools/attach-test.ps1 -Project <클론>`으로 검증한다.

- **P-1 Unity 버전이 6000.3.11f1로 고정돼 있다** → 2026-09-29 해결(아래 "해결됨"). 6.6에서 남은 렌더링 문제는 P-4.
- **P-2 기존 Unity 프로젝트에 붙일 수 없다** → 2026-09-29 해결(아래 "해결됨"). 붙인 뒤에도 남은 것은 P-5.

- **P-5 기존 프로젝트에 붙였을 때 아직 안 되는 것** → 2026-09-29 해결(아래 "해결됨").

- **P-4 Unity 6.6(URP 17.6)에서 샘플 씬의 조명이 검게 나온다** → 2026-09-30 해결(W4, 아래 "해결됨"). 원인은 그림자가 아니라
  `Camera.RenderToCubemap(Cubemap)`이 6.6에서 CPU 픽셀을 채우지 않는 것(반사 큐브맵에 초기화 안 된 메모리가 저장됨)이었다. Unity 신고는 "상시".

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
      (2026-09-30, W3에서 정함) 기준 이미지(`golden/`)는 Unity 버전별로 두고, 보장은 "같은 머신·같은 버전이면 픽셀까지 같다"(측정 diff 0, 새 클론 포함).
      다른 GPU·드라이버·OS를 위한 허용치(채널 차이 24 초과 픽셀 ≤ 0.01%, 평균 차이 ≤ 0.5, `Editor/HarnessGolden.cs`)는 재지 않았다 → Mac(또는 다른 Windows
      머신)에서 샘플 기준 이미지와의 점수를 재서 허용치를 정하고, 넘으면 머신별 폴더를 둘지 정한다.
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
- [ ] **O-9 UI Toolkit 패널 캡처가 내부 API에 기댄다** (2026-09-29, W2)
  - 현상: 패널을 지금 그리는 공개 API가 없어 `RuntimePanel.Update()`, `UIElementsRuntimeUtility.RepaintPanel/RenderPanel`을 리플렉션으로 부른다
    (`Runtime/CaptureUi.cs` `PanelApi`). 6000.0.84f1·6000.3.11f1·6000.6.3f1에서 동작. 이름·시그니처가 바뀌면 그 샷은 UI Toolkit 없이 찍히고 `uiError`.
  - 할 일(상시): 새 Unity 버전마다 매트릭스 9의 selftest 1번 HUD 검사로 확인. 공개 API가 생기면 교체.

### 검증 매트릭스 (하네스를 고친 뒤 매번)

**1–8은 `tools/selftest.ps1` 한 번**(에디터 트리, 하네스 변경은 임시 커밋 후; ~4분), **9는 `tools/fresh-clone-test.ps1 -SelfTest`**
(새 클론에서 루프 3회 + 1–8, 지원 버전마다 `-UnityVersion`; 버전당 ~5분), **10은 `tools/attach-test.ps1`**(기존 프로젝트 클론마다; 0.5–1분).
아래는 각 항목이 검사하는 것이다. 샷 PNG는 여전히 Read로 확인한다.

1. `loop.ps1` 3회 연속 녹색, `build.fingerprint`·`play.events` 동일, PNG를 Read로 확인(selftest: blank·dark·magenta 샷 없음, 모든 샷 1280x720에
   HUD 합성(`ui`) + `compile-check -IncludeHarness`
   + 기준 이미지(G3-4): 루프 1이 임시 폴더에 쓰고(`-UpdateGolden`) 2·3이 픽셀까지 같음, 커밋된 이 버전의 기준 이미지와 같음(있을 때),
   `harness_golden`의 `ignore`(왼쪽 위 기준)와 같은 major.minor의 다른 패치 폴더 대체
   + 시나리오 도구 루프 한 번: `waitScene`·`waitTarget`·UI Toolkit `click`·KeyCode 키 이름·포즈/카메라 캡처
   + uGUI 합성(G3-1, 편집 모드 픽스처: 오버레이·메인 카메라의 Screen Space - Camera·스택 UI 카메라의 캔버스 → 순서, 색 공간 블렌드 오차 ≤ 2, 되돌림)
   + 카메라(G3-7, 편집 모드 픽스처: 메인 카메라 자식인 스택 Overlay 카메라의 쿼드가 메인·다른 포즈 모두 화면 중앙, 미니맵 Base 카메라가 viewport에,
   앞 depth 카메라는 덮임, `"camera"`로 미니맵만 전체 화면, 메인 카메라 위치·다른 카메라 타깃·스택 되돌림, 씬 dirty 아님)
   + 연속 캡처(G3-3, 플레이 중 eval로 만든 오버레이 캔버스·스택 카메라가 2x2 시트의 모든 프레임에, `motion` > 0)
   + 실제 입력 격리(G3-6): 플레이 동안 실제 키보드 장치에 스페이스를 넣어도 `play.events` 그대로·`isolatedDevices` 누름 > 0, 실패한 플레이·중간에
   멈춘 플레이 뒤에도 실제 장치가 다시 켜짐)
   + 렌더 설정(W4): RP·Renderer 에셋이 생성물이고 루프 2·3은 다시 쓰지 않음, 지우면(그동안 Built-in) 루프 한 번으로 다시 생기고 fingerprint·픽셀·
   `git status`가 같음(G1-1); 반사 큐브맵에 잘못된 텍셀이 없고 가장 밝은 텍셀이 태양 방향 2° 안(P-4); 앰비언트 = 생성된 라이팅 데이터의 큐브맵 SH,
   `AmbientProbe`가 균일 환경을 Flat 앰비언트와 같게·쓰레기 텍셀은 거부(G4-2); `ctx.Material`이 오타·옛 URP 이름·토글 없는 이미션을 경고,
   `ctx.LitMaterial`이 이미션·알파 클립을 켬(G4-3)
2. C# 컴파일 에러 주입 → `stage=compile`, file/line/module 정확 → 원복 후 녹색
3. 런타임 예외 주입 → `stage=runtime`, 정확한 줄 → 원복 후 녹색
4. HLSL 에러 주입 → `stage=shader`, 재임포트 없는 다음 루프에서도 검출 → 원복 후 녹색(그 샷을 이 항목의 기준 이미지로). 셰이더 한 줄(스펙큘러 절반, G3-4)
   → 루프 녹색, golden `changed` + `rect` + diff PNG. 파이프라인이 못 그리는 머티리얼(받침대 → `Standard`, G3-5)
   → 샷 `magenta` + `hint`에 `Smoke/Pedestal` + golden `changed`, 루프는 녹색 → 원복 후 마젠타 없음·golden `same`
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
   루프 요약의 `golden`: 샘플 버전(6000.3.11f1)은 커밋된 기준 이미지와 `same=3`(새 Library의 첫 임포트도 같은 픽셀), 다른 버전은 `missing`.
   지원 버전(CLAUDE.md "Unity 버전")마다 `-UnityVersion <v>`로도 돌린다(fingerprint는 그 버전의 값)
10. 기존 프로젝트(P-2): 하네스 패키지·설치/제거 스크립트·런타임을 바꿨으면, 기준선 커밋이 있는 테스트 클론마다
   `tools/attach-test.ps1 -Project <클론> [-Scene ...] [-Module ...]` 녹색 — install → 설치분만 바뀜 → 기존 씬으로 루프 3회 녹색(fingerprint·events 동일)
   → 출시 빌드에 `Harness.*` 없음 → uninstall 뒤 `git status` 비어 있음. `shots/`를 Read로 확인. 지금 쓰는 클론(`../ah-p2`, 기준선 커밋 포함):
   BagelGame(`-Module Game=Assets/Game,UI=Assets/UI`), Fluid-Sim(`-Scene "Assets/Scenes/Fluid Particles.unity"`), 사내 프로젝트 A(비공개 클론, 이 머신에만;
   `-Scenario ../ah-p2/brd-attach.json`(부트 대화상자까지) 또는 `brd-lobby-auto.json`(테스트 서버로 로비까지, `"auto"`·`"screen"` 나란히; `brd-lobby-w3.json`은
   여기에 로비 연속 캡처를 더한 것 — 끝나면 에디터
   PlayerPrefs `dev.force_login.server_environment`를 0으로), `-KnownErrors '^\[Firebase\] Dependency'`, `-NoBuild`). 배포 경로를 바꿨으면
   `-Source git+file:///<저장소>?path=/AgentHarness/Packages/com.geuneda.agentharness#<브랜치>`(커밋된 것, 부트스트랩 포함)로도.

---

## 해결됨

(해결한 항목을 여기로 옮기고 날짜, 방법, 검증 결과, 측정값을 적는다.)

- [x] **G1-1 프로젝트 설정과 URP 에셋이 여전히 YAML** · **P-4 Unity 6.6에서 샘플 씬의 조명이 검게 나온다** · **G4-3 코드로 만든 URP Lit 머티리얼은
  키워드를 수동으로 켜야 한다** · **G4-2 스카이박스 앰비언트는 라이팅 베이크가 필요해서 Trilight로 우회 중** (2026-09-30, W4)
  - 현상(전): `Assets/Settings/PC_RPAsset`·`PC_Renderer`·`Mobile_*`는 템플릿 YAML 그대로라 그림자·SSAO·렌더링 경로를 바꾸려면 GUI나 일회성 eval이 필요했고,
    다른 버전으로 열면 Unity가 다시 써서 `git status`가 더러워졌다. 6.6 새 클론은 첫 플레이만 밝고 이후 URP Lit 표면이 검었다(selftest 1번 빨강).
    머티리얼은 `ctx.Material` + 속성 이름 문자열이라 오타·옛 이름이 조용히 무시됐고, 앰비언트는 베이크를 피해 Trilight 색 세 개였다.
    `build.fingerprint`는 GameObject만 훑어 RenderSettings(안개·앰비언트·스카이박스·반사)가 바뀌어도 그대로였다.
  - 방법(G1-1, `Editor/Build/ISettingsStep.cs`·`SettingsContext.cs`, `HarnessBuild`·`HarnessSetup`):
    - 모듈 `Builders/`의 `ISettingsStep.Apply(SettingsContext)`를 `harness_build`가 빌드 스텝 **전에**(빌드 스텝의 렌더가 그 파이프라인을 쓰게), `harness_setup`도
      실행한다(새 클론이 첫 루프 전에 파이프라인을 가짐). `setup: harness`에서만; `attach`면 건너뛰고 `build.warnings`에 스텝 수.
    - `ctx.UniversalPipeline(name, rp => …, renderer => …)` = URP 메뉴가 새 에셋을 만드는 것과 같은 기본값(`UniversalRenderPipelineAsset.Create`, 기본
      `PostProcessData`)에서 시작해 코드를 적용 → `<name>_RPAsset.asset` + `<name>_Renderer.asset`. `SettingsContext.AddRendererFeature<T>`(하위 에셋),
      `SettingsContext.Set(obj, "직렬화 경로", 값)`(internal setter 필드 — 이 버전에 없거나 타입이 틀리면 비슷한 이름과 함께 예외), `ctx.UsePipeline(rp[, 품질 레벨…])`
      (Graphics 기본 / 레벨별; 레벨의 `customRenderPipeline`은 SerializedObject로).
    - 에셋은 생성물(`<generatedRoot>/<모듈>/`, gitignore). **GUID = 경로의 MD5**: `AssetDatabase.CreateAsset`은 GUID를 고를 수 없어서(미리 쓴 `.meta`도 무시)
      임시 폴더에 `CreateAsset`으로 Unity가 쓴 파일을 제자리로 옮기고 `.meta`를 쓴 뒤 임포트한다(`SaveToSerializedFileAndForget`은 메인 fileID가 1이라
      로드 안 됨). 그래서 지워도 같은 GUID로 돌아오고 그것을 참조하는 `GraphicsSettings.asset`·`QualitySettings.asset`은 바뀌지 않는다(이번 커밋에서 한 번
      새 GUID로 바뀜, QualitySettings는 6.3 형식으로 다시 저장됨).
    - 새 인스턴스와 디스크 에셋을 파일 ID에 무관한 덤프(`SettingsContext.Dump`, 하위 오브젝트는 제자리 덤프, `m_RendererFeatureMap` 제외)로 비교해 다를 때만
      제자리 덮어쓰기(URP 파이프라인 재생성·재임포트를 매 루프 하지 않음). URP 17.6은 Renderer Feature에 `HideInHierarchy`를 켜서 6.6에서만 매번 다르게
      보였다 → 모든 버전에서 켠다.
    - 새 클론은 이제 Built-in으로 열리고 첫 `harness_setup`이 세션 도중 URP로 바꾼다. 그랬더니 **6.6 새 클론의 selftest 1번 카메라 픽스처가 2회 연속 빨갰다**:
      첫 캡처에서 스택 Overlay 카메라의 새 Unlit 쿼드가 빠짐(두 번째 캡처엔 있음, 비동기 셰이더 컴파일 꺼짐). 같은 클론을 처음부터 URP로 열면 셰이더 캐시를 지워도
      녹색, Built-in 시작 → setup으로 되돌리면 다시 빨강, 전환 뒤 도메인 리로드를 한 번 넣으면 녹색(6.0·6.3은 전환해도 녹색). → 설정 스텝이 활성 파이프라인을
      바꾸면 `EditorUtility.RequestScriptReload()`하고 결과에 `settings.switched`·`reloadRequested`·`domainReloads`; `uc.ps1`(`Wait-HarnessReload`)과 루프
      (`timings.reloadSec`)가 리로드가 끝난 뒤 계속한다. RP 에셋을 지운 뒤의 루프도 같은 경로(리로드 2.25 s).
    - fingerprint: 설정 에셋 덤프 + Graphics·품질 레벨별 파이프라인(`--settings--`), 그리고 씬의 RenderSettings·라이팅 데이터 참조(`RenderSettings` 절).
      GPU로 구운 큐브맵과 그 SH는 참조만(GPU마다 끝자리가 다를 수 있다; 기준 이미지가 본다).
    - 샘플: `Assets/Game/Stage/Builders/StageRenderSettingsStep.cs`가 옛 PC·Mobile 에셋의 값(URP 기본값과 다른 것: 깊이/불투명 텍스처, 추가 광원 그림자,
      캐스케이드 4개·분할·바이어스, 소프트 그림자 High, 반사 프로브 블렌딩·박스, 라이트 레이어, Forward+, SSAO 0.4/0.3, 네이티브 렌더 패스, copy depth,
      intermediate Auto; Mobile은 템플릿 값)을 옮겼다. 셰이더 프리필터 값(`m_Prefilter*`)은 플레이어 빌드가 계산하므로 옮기지 않았다.
      `Assets/Settings/`에는 URP가 관리하는 `UniversalRenderPipelineGlobalSettings`·`DefaultVolumeProfile`만 남았다.
  - 원인과 방법(P-4): 그림자가 아니었다. 6.6에서 설정을 하나씩 꺼 보니(태양·그림자·SSAO·안개·후처리 모두 무관) **반사**만 원인이었고(강도 0·큐브맵 없음·
    스카이박스 반사면 57–61), `ctx.BakeSkyReflection`의 큐브맵이 모든 mip 평균 −23.203 = half `0xCDCD`(초기화 안 된 메모리)였다.
    **`Camera.RenderToCubemap(Cubemap)`은 6.6에서 성공을 돌려주지만 CPU 픽셀을 채우지 않는다**(같은 세션에서 0이기도 함 → 비결정적). GPU 쪽은 맞게
    그려져서 에셋을 처음 만든 루프의 플레이만 밝았고, 저장된 에셋(쓰레기)을 다시 읽으면 음수 반사로 Lit 표면이 검었다. 6.3에서는 같은 호출이 CPU 픽셀을
    **sRGB로 인코딩해서** 넣었다(선형 0.071 → 0.298): 반사가 실제보다 밝고 태양 HDR(116)이 눌려 있었다.
    → 큐브 `RenderTexture`에 렌더하고 `AsyncGPUReadback`으로 면마다 읽어 `SetPixelData`(선형 HDR 그대로, 6.0/6.3/6.6 같은 경로). 면 방향은 6.3의 옛 결과와
    같은 배치(D3D 큐브맵 규약)이고 가장 밝은 텍셀이 태양 방향과 0.32° 차이. 옛 메모의 "하드 그림자·그림자 끄면 밝아진다"는 재현되지 않았다(검은 상태에서
    소프트 끄기·품질 Low/Medium/High·캐스케이드 1/2/4·하드·그림자 없음 모두 overview 1.0) — 저장된 쓰레기가 실행마다 달라(0이면 반사만 빠져 밝게 보임)
    설정 탓으로 보였던 것으로 본다. "빈 씬에서도 6.6만 소프트 그림자가 어둡다(114.7)"는 6.6이 새 씬의 **첫 렌더**에 기본 환경광이 아직 없어서였다
    (같은 순서로 다시 재면 Soft 114.7 → Hard 197.4 → None 197.6 → Soft 197.2).
  - 방법(G4-3, `Editor/Build/BuildContext.Materials.cs`): `ctx.LitMaterial(name, m => …)` + `LitSettings`(BaseColor/BaseMap/Tiling/Offset, Metallic/Smoothness/
    MetallicGlossMap, NormalMap/NormalScale, OcclusionMap, Emission/EmissionMap, Transparent, AlphaClip, Cull, ReceiveShadows). 조사해 보니 텍스처 키워드
    (`_NORMALMAP`·`_OCCLUSIONMAP`·`_METALLICSPECGLOSSMAP`·`_PARALLAXMAP`·`_DETAIL_MULX2`)·투명·알파 클립·큐는 이미 `ValidateMaterial`이 맞추고 있었고, 조용히
    틀리는 건 **이미션**이었다: `_EmissionColor` + `EnableKeyword("_EMISSION")`을 검증이 끈다(인스펙터의 Emission 체크 = GI 플래그, 기본 `EmissiveIsBlack`) →
    `LitMaterial`은 `Emission`이 검정이 아니면 `RealtimeEmissive`. `ctx.Material`은 설정 전·후·검증 후 값을 `GetPropertyNames`로 비교해(선언 안 된 이름은
    직렬화 목록엔 없고 여기엔 있다) 셰이더에 없는 이름(비슷한 이름 제시), URP가 읽지 않는 옛 이름(`_MainTex`·`_Color`·`_Glossiness`·`_GlossMapScale`·
    `_GlossyReflections`), 검증이 덮어쓴 값, 토글 없는 이미션 색을 `build.warnings`로. `LitMaterial`은 노멀맵이 노멀맵으로 임포트되지 않았거나 마스크가 sRGB면 경고.
    샘플 지형은 `LitMaterial`로 바꿨다(`EnableKeyword` 줄 삭제) — 머티리얼이 바이트까지 같아 fingerprint 그대로.
  - 방법(G4-2, `Runtime/Procedural/AmbientProbe.cs`, `ctx.SkyAmbient`): 큐브맵 텍셀마다 radiance × 입체각으로 L2 SH에 투영하고 코사인 로브로 컨볼루션(밴드별
    1, 2/3, 1/4)해 Unity의 `SphericalHarmonicsL2` 형식(`Evaluate` 기저를 재서 확인: 정규화 없는 1, y, z, x, xy, yz, 3z²−1, xz, x²−y²; 균일 radiance c → 계수0 = c
    = Flat 앰비언트 c)으로. 코사인 가중 적분(brute force)과 6방향에서 0.01 안. 유한·비음수가 아닌 텍셀이 있으면 예외(P-4 같은 쓰레기가 검은 씬 대신 빌드 에러).
    씬의 RenderSettings에는 앰비언트 프로브가 저장되지 않아서 Unity 6.0+의 공개 API `new LightingDataAsset(scene)` + `SetAmbientProbe` +
    `Lightmapping.SetLightingDataAssetForScene`으로 생성 라이팅 데이터에 넣고 ambient mode Skybox — 베이크한 것과 같은 자리. 저장 안 된 씬으로 만들면
    씬을 열 때마다 "incompatible … scene was not serialized" 경고가 나서(첫 매트릭스에서 루프마다 `warningCount` 1로 드러남), 씬을 저장한 뒤 만들고 씬을
    한 번 더 저장한다. Trilight 우회 삭제.
  - 검증(이 머신, 에디터를 하나씩):
    - G1-1: 옛 에셋과 새 에셋의 직렬화 차이는 프리필터 값·쓰이지 않는 기본 스텐실 값·캐스케이드 경계 소수점뿐이고 기준 이미지 **픽셀까지 같음**(`meanDiff` 0, 반사
      수정 전). 설정 스텝 첫 생성 530 ms, 이후 무변경 10 ms. RP·Renderer 에셋 4개를 지우면 그동안 Built-in → 루프 한 번으로 같은 GUID로 다시 생기고 fingerprint·
      기준 이미지·`git status` 같음(selftest 1번에 넣음). 6.6 새 클론의 `harness_setup`이 4개를 만들고 `git status` 깨끗.
    - P-4: 6.6 새 클론 루프 3회 61.0/56.4/47.3(전: 60.9/56.4/48.0 → 25.5/15.3/1.0 `dark`). 반사가 선형이 되면서 6.3도 60.9/56.4/47.3으로 6.6과 같아졌다
      (옛 6.3 기준 64.8/60.1/50.0과의 차이 = sRGB로 부풀었던 반사, 받침대 윗면이 회색 → 하늘을 비추는 남색).
    - G4-2: 앰비언트 계수0 (0.214, 0.289, 0.407)(Trilight 0.141/0.158/0.216), 위를 향한 면 (0.086, 0.153, 0.375)·아래 (0.294, 0.343, 0.327) — 이 하늘은 천정이 짙은
      파랑이고 지평선 아래 색(안개와 맞춘 `_GroundColor`)이 밝다. 샷 60.3/55.6/45.7, 그늘이 조금 더 푸르고 어둡다. 의도한 변경이라 6.3 기준 이미지를 갱신했다.
    - 루프 3.67–3.96 s(W3 3.48–3.52: 빌드 0.74 s 중 설정 스텝 10 ms, 늘어난 건 fingerprint의 설정 덤프와 라이팅 데이터 뒤 씬 재저장), 빌드 스텝 합 ~87 ms.
    - 매트릭스: 샘플 selftest 1–8 녹색 248.2 s(`4dc9c80b…`, 줄 61/68/87; 1번 51.1 s에 W4 검사 7개).
      9: 새 클론(최종 커밋) 6000.3.11f1 녹색 347.4 s(`4dc9c80b…` = 메인 트리, 새 Library의 첫 루프부터 기준 이미지 `same=3`, `harness_setup` 4.6 s),
      6000.0.84f1 녹색 307.5 s(`088e7345…`, 샷 60.3/55.6/45.7 = 6.3), **6000.6.3f1 녹색 333.4 s**(`d6ca82e3…`, 60.4/55.7/45.7, setup 12.5 s = 전환 뒤 리로드 포함) —
      세 버전 모두 루프 콘솔 경고 0, `git status`는 버전 전환 파일뿐(6.6은 `PhysicsCoreProjectSettings2D.asset`·`ProjectAuditorSettings.asset`을 새로 만든다).
      도중에: 첫 6.6 실행 2회가 카메라 픽스처로 빨강 → 위의 전환 뒤 리로드. 6.3·6.6 한 번씩 실제 입력 검사가 빨강(그 루프만 `editorFocused=false`) → selftest가
      포커스 없는 시도를 다시 하게 하고 G3-9로 남김(최종 실행은 세 버전 모두 첫 시도에 포커스 있음). 메인 트리 selftest 1–8은 리로드 수정 뒤 248.2 s, 재시도 수정 뒤 1번 55.4 s.
      10: BagelGame 녹색 57.6 s(`619be553…`, 65.5, 출시 빌드 `Managed/` 132개·`Harness.*` 0개), Fluid-Sim(Built-in) 녹색 31.2 s(`54880f05…`,
      23.0/14.9/22.2 = W3, 103개·0개), 사내 프로젝트 A 녹색 129.9 s(`6664b723…`, `brd-lobby-w3.json`: 37.7/91.2/120.3/145.7, 끝난 뒤 서버 선택 PlayerPrefs 1 → 0).
      세 프로젝트 모두 설정 스텝이 없어 RP 에셋을 건드리지 않았고(제거 뒤 `git status` 비어 있음) fingerprint는 W3와 같다.
  - 남은 것: RP 밖의 프로젝트 설정 → G1-5(W10). 반사·앰비언트는 하늘만(씬 오브젝트가 비치지 않음, 반사 프로브는 W6c). 기준 이미지는 여전히 6.3만(6.0·6.6은
    이제 6.3과 같은 밝기라 만들 수 있다). Unity 신고(RenderToCubemap)는 "상시".

- [x] **G3-7 캡처는 카메라 하나 + 스크린 공간 UI다** · **G3-4 시각 회귀 검사가 없다** · **G3-3 정지 이미지만 나온다** (2026-09-30, W3)
  - 현상(전): 캡처는 템플릿 카메라 하나를 렌더하고 UI만 합성해서 URP 스택의 Overlay 카메라(무기, UI 카메라)와 다른 Base 카메라(미니맵)가 빠졌다.
    실제 Game 뷰와 나란히 찍어 보니 반대 경우도 있었다: depth가 높은 전체 화면 Depth-only **Base** 카메라는 Game 뷰에서 앞 카메라의 씬을 지우는데
    (URP는 Base 카메라를 겹쳐 그리지 않는다 — 겹치려면 스택), 캡처는 그 카메라의 캔버스를 씬 위에 합성해 게임에 없는 화면을 찍었다(W2 uGUI 픽스처가 그 구성).
    기준 이미지가 없어서 렌더가 바뀌어도(셰이더 한 줄) 루프 결과는 같았고, 움직임은 여러 컷과 이벤트 수로만 추론했다.
  - 방법(G3-7, `Runtime/CaptureCameras.cs`):
    - 템플릿이 메인 카메라이고 화면에 그리면: 화면에 그리는 Base 카메라(활성·Game·타깃 텍스처 없음·디스플레이 1·하네스의 숨은 카메라 아님·URP Overlay 아님)를
      depth 순(같으면 경로 순 — URP의 정렬은 같은 depth끼리 순서가 없다)으로 같은 RT에 렌더하고, 템플릿 자리에 캡처 카메라를 넣는다. 캡처 카메라는 템플릿의
      스택(활성 Overlay), 렌더러(`GetRenderer(i)`로 인덱스를 찾아 `SetRenderer` — URP는 같은 렌더러 종류끼리만 쌓는다), `volumeTrigger`를 복사한다.
      `StandardRequest` 렌더 요청은 URP 17.0·17.3·17.6 모두 `Render(context, [camera])`로 스택까지 그린다(소스 확인). Built-in은 depth 순 `Camera.Render`.
    - 포즈가 템플릿과 다르면 템플릿을 그 포즈로 잠깐 옮겼다가 로컬 위치·회전을 되돌린다 → 자식(무기와 그것을 그리는 Overlay 카메라)이 같은 화면 위치로 따라온다.
      Overlay 카메라만 옮기면 무기 오브젝트가 남아서 안 보이고, 안 옮기면 월드를 그리는 Overlay(외곽선 등)가 어긋난다. 렌더는 여전히 복사한 캡처 카메라가 한다
      (템플릿의 TAA 히스토리·aspect를 건드리지 않음). 캡처 카메라의 aspect는 자기 viewport 모양(분할 화면).
    - 다른 Base 카메라는 캡처 동안 `targetTexture`를 캡처 RT로 바꿔(그 카메라의 캔버스가 캡처 크기로 배치된다) 렌더하고 되돌린 뒤 `ForceUpdateCanvases`.
      `"ui": false`면 그 카메라가 그리는 캔버스의 레이어를 culling mask에서 잠깐 뺀다.
    - `"camera": X`(메인 카메라가 아님)이거나 메인 카메라가 텍스처에 그리면: 그 카메라와 스택만, viewport 전체로.
    - 스택 Overlay 카메라의 Screen Space - Camera 캔버스는 **렌더 요청에서 그려지지 않았다**(요청한 베이스 카메라와 다른 Base 카메라의 캔버스는 그려짐 —
      Unity가 UI를 요청한 카메라에 대해서만 준비하는 것으로 보인다. Overlay 카메라에 타깃을 줘도 같음). → `CaptureUi`가 그 캔버스를 카메라들 위에 합성한다
      (그 Overlay의 그리기 순서로; 한계는 G3-8 (3)).
    - 보고: `shotStats[].cameras`(그린 순서, Overlay는 `" (overlay)"`), `ui`는 카메라가 그린 캔버스(카메라 순서) 다음 합성 레이어. 빈 샷의 `hint`는 화면에 그리는데
      캡처가 그리지 않은 카메라만 짚는다.
  - 방법(G3-3, `Runtime/ScenarioRunner.cs`, `Runtime/ContactSheet.cs`): 캡처에 `"frames": N`(+ `"every": k`). 포즈는 첫 프레임에 정하고(`"main"`·`"camera"`는
    카메라를 따라감) k프레임마다 `HarnessCapture.Render`(파일 없이 픽셀)로 찍어 박스 필터로 줄여 한 장의 시트에 넣는다(열 = ⌈√N⌉, 폭 ≤ 1920, 칸 위 캡션 띠에
    3x5 비트맵 숫자로 t — 처음엔 칸 안에 써서 게임 HUD를 가렸다). `motion` = 이웃 프레임의 평균 |Δ밝기|(0..255). 통계는 프레임 평균, blank·dark·magenta는
    하나라도. 시나리오 끝은 시퀀스를 기다리고, 실패·중단이면 찍은 만큼의 시트 + `error`. GIF는 만들지 않았다 — 에이전트는 Read로 한 장을 보고, GIF의 256색
    팔레트는 기준 이미지 비교에도 못 쓴다.
  - 방법(G3-4, `Editor/HarnessGolden.cs` `harness_golden`, `Tools~/Harness.psm1` `Invoke-HarnessGolden`, `loop.ps1 -UpdateGolden`/`-Golden`):
    - 위치: `golden/<Unity 버전>/<시나리오 name>/<샷 파일>.png`(설정 `goldenRoot`, worktree면 그 worktree의 것). 버전마다 렌더가 달라서(P-4, fingerprint도 버전별)
      버전별로 두고, 그 버전 폴더가 없으면 같은 major.minor의 가장 가까운 패치(`golden.from`, 아래 버전 우선).
    - 점수(Unity `LoadImage`로 읽어 C#에서): `meanDiff`(채널 평균 |차이|), `maxDiff`, `changedRatio`(채널 차이 > 24인 픽셀), SSIM(8x8 블록 루마), `rect`(바뀐 범위,
      왼쪽 위 기준), diff PNG(샷을 어둡게, 바뀐 픽셀 빨강, 뺀 영역 파랑, 범위 노란 테두리). `same` = 비율 ≤ 0.01% 이고 평균 ≤ 0.5. 크기가 다르면 `size`.
    - 결정성 기준(P-3과 같이 정함): 같은 머신·같은 버전이면 픽셀까지 같다(고정 시간 간격; 아래 측정 diff 0). 허용치는 다른 GPU·드라이버용이고 아직 재지 않았다(P-3).
    - 실패로 치지 않는다: 의도한 변경 중에도 루프는 녹색이고, 에이전트가 `golden.changed`와 diff PNG를 보고 판단한다. 갱신은 `-UpdateGolden`(녹색일 때만, 그
      폴더의 PNG를 이번 샷으로 교체). 빼는 것: 캡처의 `"golden": false`, `"ignore": [{x,y,w,h}]`(이미지 비율, 왼쪽 위 기준 — Read로 PNG를 보고 쓰기 쉽게),
      `"screen"` 샷(Game 뷰 크기). `-NoPlay`는 키 `capture`.
    - 샘플은 6000.3.11f1의 `default` 시나리오 3장(2.9 MB)을 커밋했다. 다른 버전은 `missing`(6.6은 P-4 때문에 만들 수 없다).
    - 기준 이미지가 드러낸 비결정성: 새 클론(새 Library)의 **첫 루프** closeup(t=0.5)에 지형·하늘·후처리가 없었다(`meanDiff` 67.6, `changedRatio` 75%; 2·3번 루프와
      horizon·overview는 `same`). 에디터가 처음 만난 셰이더 변형을 백그라운드에서 컴파일하며 그동안 그 오브젝트를 빼기 때문이다(Editor 설정 Asynchronous Shader
      Compilation; 편집 모드 픽스처의 첫 캡처에서도 새 Unlit 쿼드가 빠짐). 메인 트리에서 재현: 에디터를 닫고 `Library/ShaderCache*`·`LastSceneManagerSetup.txt`를 지우고
      열어 루프 → 같은 `meanDiff` 67.649. 캡처만 동기로 바꾸는 네 가지는 모두 안 됐다(`ShaderUtil.allowAsyncCompilation`, 명령 버퍼 `SetAsyncCompilation`, 캡처 동안만
      설정 끄기 — 앞 프레임의 Game·Scene 뷰가 비동기로 요청해 둔 변형이 그대로 빠짐, `anythingCompiling` 대기 — 메인 스레드를 막으면 20 s 동안 끝나지 않음).
      설정을 루프 전부터 끄면 같다 → `harness_setup`에 `syncShaders`(`EditorSettings.asyncShaderCompilation = false`): `setup: harness`면 적용(샘플의
      `ProjectSettings/EditorSettings.asset` 한 줄을 커밋), `attach`면 `recommendations`만. 켜진 프로젝트에서는 캡처 순간 컴파일 중이면 `shotStats[].shadersCompiling` + `hint`.
      적용 뒤 캐시를 지운 첫 루프 `same` 3/3(플레이 2.46 s, 히치 0), 캐시를 지운 selftest 1번 녹색(픽스처 첫 캡처 209 ms에 쿼드 있음).
  - 검증(이 머신, 에디터를 하나씩):
    - 실제 Game 뷰와 비교(플레이 중 eval로 만든 픽스처, `"screen"` 366x305와 `"main"`·다른 포즈 1280x720): 메인 카메라 자식 스택 Overlay의 빨간 쿼드, 미니맵
      Base 카메라(오른쪽 위 1/4, 파랑)와 그 캔버스의 노란 점, 스택 UI 카메라의 초록 캔버스, HUD — 네 가지가 세 샷 모두에 같은 레이어로 나옴(전에는 쿼드·미니맵이
      없었다). 다른 포즈(overview)에서도 쿼드가 같은 화면 위치. 전체 화면 Depth-only Base 카메라 구성은 Game 뷰가 씬을 지우고 이제 캡처도 같다.
    - 샘플 샷은 그대로(밝기 64.8/60.1/50.0, fingerprint `977545a7…`), 루프 3.49 s(`goldenSec` 0.2 — 샷 3장 PNG 디코드 + 비교). 캡처 비용: 카메라 픽스처(Base 3개 +
      Overlay 1개) 126 ms, uGUI 픽스처 132 ms(W2 127–159).
    - 같은 코드 3회: 기준 이미지와 `maxDiff` 0(세 샷 모두). 셰이더 한 줄(스펙큘러 절반): closeup `meanDiff` 0.591·`changedRatio` 1.12%·SSIM 0.9955, horizon
      0.039·0.083%, overview 0.008·0.015%(하이라이트 138픽셀) — 처음 정한 허용치(비율 0.05%)는 overview를 `same`으로 봐서 0.01% + 평균 0.5로 좁혔다. diff PNG가
      하이라이트 두 곳을 짚음. 받침대 `Standard`(마젠타): 12.2 / 3.5 / 0.24, `rect`가 받침대.
    - 연속 캡처: closeup 8프레임 6간격 → 3x3 시트, Space 뒤 CW→CCW가 칸마다 보임, `motion` 17.3–19.3. 메인 카메라 4프레임 5간격 → 2x2, `motion` 2.2–2.3,
      플레이 중 만든 오버레이 캔버스·스택 카메라가 모든 프레임에(selftest 1번).
    - 매트릭스(동기 셰이더 컴파일 수정 뒤): 샘플 selftest 1–8 녹색 219.6 s(`977545a7…`, 줄 61/68/87; 1번 36.1 s에 기준 이미지·카메라·연속 캡처 검사, 커밋된 기준
      이미지와 `same` 3/3, 루프 3.48–3.52 s; 4번 33.6 s; `syncShaders` 뒤 다시 219.0 s).
      9: 새 클론 6000.3.11f1 녹색 314.6 s(`977545a7…` = 메인 트리, **새 Library의 첫 루프부터 커밋된 기준 이미지와 `same=3`**), 6000.0.84f1 녹색 291.1 s(`d9a6d092…`,
      기준 이미지 `missing`), 6000.6.3f1 2–8 녹색·1 빨강 352.2 s(P-4: selftest 루프 2는 `same`, 3은 `changed` maxDiff 225 + `dark` — 이제 기준 이미지 비교도 잡는다).
      6.6 fingerprint가 `c24b65e7…`로 바뀌었다(W2까지 `0ba32228…`): W2 커밋(`a9133c4`)으로 뜬 6.6 새 클론도 `c24b65e7…`이고 fingerprint 덤프가 W3와 바이트까지
      같다 → W3 때문이 아니라 이 머신의 6.6 쪽 변화(원인 미상, P-4와 같은 버전). 6.6에서 8번 "land 도중 submit이 락을 기다림"이 두 번 빨갰다 — 기다려야 할
      submit의 compile-check 게이트가 4.88 s로 land 전체(4.89 s)만큼 걸려 줄을 서기 전에 land가 끝남 → 그 submit을 `-SkipCheck`로(worktree는 깨끗) 바꾼 뒤 6.6 녹색
      (6.6 재실행 352.2 s: 2–8 녹색, 1은 P-4만), 메인 트리 7·8 녹색(대기 3.4 s).
      10: BagelGame 녹색 56.1 s(`619be553…`, `cameras` = `Core/MainCamera`, 출시 빌드 `Managed/` 132개·`Harness.*` 0개), Fluid-Sim(Built-in) 녹색 32.1 s(`54880f05…`,
      `cameras` = `Shadow Camera, Main Camera` — 명령 버퍼로 그림자 맵을 그리는 depth 낮은 카메라를 메인 카메라가 덮어 밝기 23.0/14.9/22.2, W2 23.0/15.0/22.1과 이
      시뮬레이션의 흔들림 ±0.1 안; 103개·0개), 사내 프로젝트 A 녹색 127.1 s(`6664b723…`, 로비 시나리오에 연속 캡처를 더한 변형 `brd-lobby-w3.json`: `"auto"`
      37.7/92.0/120.4/145.7 ≈ W2, 카메라는 `Main Camera` 하나, 로비 첫 구매 팝업 6프레임 3x2 시트 `motion` 0.9–1.3; 끝난 뒤 서버 선택 PlayerPrefs를 1 → 0으로
      되돌림). 세 프로젝트 모두 `harness_setup` 권장 사항에 `syncShaders`가 나오고(attach는 바꾸지 않음), 제거 뒤 `git status` 비어 있음.
  - 남은 것: 다른 머신의 허용치(P-3), Overlay 카메라 캔버스의 합성(G3-8 (3)), 기준 이미지는 샘플 버전(6.3)만.

- [x] **G3-1 오프스크린 캡처에 스크린 공간 UI가 안 찍힌다** · **G3-5 마젠타 머티리얼을 따로 잡지 못한다** (2026-09-29, W2)
  - 현상(전): 프리셋 캡처는 카메라 오프스크린 렌더라 UI Toolkit 패널·오버레이 캔버스가 빠졌다. `"screen"`은 Game 뷰 탭이 보일 때만 되고 크기가 사용자
    레이아웃(1차 568x562, P-5 때 366x415)을 따랐다. 기존 프로젝트는 UI가 화면의 전부인 경우가 많다(사내 프로젝트 A의 부트·로그인·타이틀·로비).
    셰이더를 못 쓰는 머티리얼(URP 프로젝트에서 `Shader.Find("Standard")`)은 에러 없이 마젠타로 그려져 루프가 그대로 녹색이었다.
  - 방법(G3-1, `Runtime/CaptureUi.cs`): `"screen"` 말고 모든 캡처가 스크린 공간 UI를 **캡처 크기로 다시 배치해** 카메라 렌더 위에 합성한다.
    - uGUI: 템플릿 카메라의 Screen Space - Camera 캔버스는 캡처 카메라로 옮겨 씬과 함께 그린다(게임처럼 후처리 포함). 화면에 그리는 다른 카메라의
      캔버스와 Screen Space - Overlay 캔버스는 잠깐 Screen Space - Camera(숨은 직교 UI 카메라, 씬에서 먼 y=-100000, 캔버스 레이어만)로 바꿔 투명 RT에
      그린다. 카메라의 `targetTexture`를 먼저 정하고 `Canvas.ForceUpdateCanvases()` → `CanvasScaler`(`Canvas.renderingDisplaySize`)와 텍스트 메시가
      캡처 크기로 다시 계산된다(순서를 거꾸로 하면 글자가 Game 뷰 배율로 래스터돼 흐렸다). 같은 밴드·카메라·정렬 레이어의 연속 캔버스는 한 번에 그린다.
    - UI Toolkit: 패널마다 `PanelSettings.targetTexture`를 투명 RT로 바꾸고(색·깊이 지움) 내부 API로 즉시 그린다 — `RuntimePanel.Update()`(타깃 크기로
      패널 크기·배율·레이아웃; 이것 없이 `RepaintPanel`만 부르면 Game 뷰 레이아웃 그대로), `UIElementsRuntimeUtility.RepaintPanel` + `RenderPanel(panel, true)`
      (`RepaintPanel`만으로는 아무것도 그려지지 않았다). 공개 API로는 패널을 지금 그릴 방법이 없다. 6.0/6.3/6.6 모두 같은 이름이고, 없으면 `uiError`.
    - 합성: 두 UI 모두 투명 RT에 **선형 공간 프리멀티플라이드 색 + 커버리지 알파**를 남긴다(측정: UI Toolkit `rgba(6,9,20,0.62)` → (4,6,14,158),
      uGUI 50% 빨강 → (188,0,0,128), 그 위 50% 파랑 → (137,0,188,192)). CPU에서 `dst = src + dst·(1-a)`를 선형 프로젝트는 선형 공간(sRGB LUT)으로,
      감마 프로젝트는 저장값으로 계산한다. 순서: 씬과 함께 그린 캔버스 → 다른 카메라의 캔버스(카메라 depth) → 오버레이 캔버스·UI Toolkit(sortingOrder,
      같으면 UI Toolkit이 위). 바꾼 것(캔버스 모드·카메라·plane distance, 패널 타깃·지우기)은 같은 프레임에 되돌리고 레이아웃도 다시 계산한다.
    - 보고: `shotStats[].width/height`, `ui`(그린 순서), `uiError`. `"ui": false`(캡처)·`harness_capture {"ui":false}`로 끈다. 빈 샷의 `hint`는 이제
      "화면에 그리는 다른 카메라"를 알려 준다(UI는 이미 합성되므로 `"screen"` 권유는 `ui:false`일 때만).
    - **캡처 크기**: 시나리오 `width`/`height` → 설정 `captureSize` → 프로젝트 방향(Player Settings 기본 방향이 세로, 또는 세로만 허용한 자동 회전이면
      720x1280) → 1280x720. 사내 프로젝트 A가 세로 게임이라 1280x720 가로로는 게임에 없는 레이아웃이 찍혔다(로비가 가로로 펼쳐짐). Game 뷰 크기는 쓰지 않는다.
  - 방법(G3-5): `shotStats[].magentaRatio`·`magenta`(≥ 0.05%). 판정 = 밝고(r·b ≥ 128) r ≈ b(15% 안) g가 그 10% 이하 — 에러 셰이더 (1,0,1)은 샘플의 ACES +
    색 보정 뒤 (253,0,238); 깨끗한 샘플 샷 3장(블룸 받은 분홍 테두리 매듭 포함)은 0픽셀(느슨한 기준 g ≤ 15%는 closeup에서 313픽셀 오탐). `magenta`면
    `hint`에 파이프라인이 못 그리는 머티리얼을 쓴 렌더러(없는 머티리얼·셰이더, `!isSupported`, URP에서 Built-in LightMode 패스만 있는 셰이더)를 최대 5개.
    `dark`처럼 실패로 치지는 않는다(분홍 아트·기존 프로젝트의 오래된 머티리얼이 루프를 막지 않게) — CLAUDE.md "매 루프 후 반드시"에 적었다.
  - 검증(이 머신, 에디터를 하나씩):
    - 샘플: 기본 루프 3샷 모두 1280x720 + `uitk:SmokeHudPanel`, HUD가 게임 상태(LAPS 1, CCW)를 보여 줌. fingerprint `977545a7…` 그대로, 샷 밝기
      64.8/60.1/50.0(전 65.1/60.5/50.3 — HUD 배경만큼). 포즈 캡처(`top`)에도 HUD. 캡처 비용: 편집 모드 샷당 75–80 → 93–100 ms(UI Toolkit 패널 1개),
      uGUI 3개 + HUD 픽스처 127–159 ms. 루프 시간은 그대로(play 2.1–2.7 s).
    - uGUI 픽스처(편집 모드, selftest 1번): 오버레이 50% 빨강이 합성 전 픽셀로 계산한 기대값과 채널 차 1, 메인 카메라 캔버스 초록이 후처리를 받음
      (17,210,0), 다른 카메라 캔버스 파랑 (0,0,255), 캔버스·패널 설정과 크기 원상, 씬 dirty 아님. 같은 픽스처를 붙인 프로젝트에서도: BagelGame(URP, **감마**)
      채널 차 1, Fluid-Sim(**Built-in**, 선형) 0 — 둘 다 제거 뒤 `git status` 비어 있음.
    - 마젠타: 받침대 머티리얼을 `Standard`로 → 세 샷 모두 `magenta`(0.085/0.0224/0.0014), `hint` = `Smoke/Pedestal (shader 'Standard' is a Built-in render
      pipeline shader)`, 루프 녹색. 되돌리면 0. selftest 4번에 넣었다.
    - **사내 프로젝트 A**(세로, uGUI 로비 + UI Toolkit 개발용 대화상자): 부트 → 로그인 대화상자 → 타이틀 → 로비 흐름에서 같은 순간을 `"auto"`와 `"screen"`으로
      나란히 찍음(`../ah-p2/brd-lobby-auto.json`). `"auto"` = 720x1280에 uGUI 캔버스 7–8개 + UI Toolkit 패널 1개, 밝기 37.7/91.6/120.3/145.7
      (`"screen"` 1440x3040은 37.3/90.0/118.0) — 로그인 대화상자·타이틀·로비 위 첫 구매 팝업(반투명 딤 포함)이 PNG로 확인됨. 3회 모두 같은 값.
      서버 선택 PlayerPrefs(`dev.force_login.server_environment`)는 끝난 뒤 0으로 되돌렸다.
    - 남은 것(항목으로 옮김): 카메라 스택의 다른 카메라가 그리는 3D → G3-7(W3). 캡처 동안의 UI 재배치와 `Screen.width` 기준 UI → G3-8(W8, 플레이어
      캡처). 플레이 모드 uGUI는 기존 프로젝트(사내 프로젝트 A)로만 확인 → W3 추가 검증(selftest). UI Toolkit 내부 API 의존 → O-9(상시).
    - 매트릭스: 샘플 selftest 1–8 녹색 206.7 s(`977545a7…`, 줄 61/68/87). 9: 새 클론 6000.3.11f1 녹색 304.8 s(`977545a7…` = 메인 트리), 6000.0.84f1 녹색
      286.4 s(`d9a6d092…`), 6000.6.3f1 2–8 녹색·1 빨강 307.6 s(`0ba32228…`; P-4의 `dark` 25.5/15.3/1.0 — HUD를 합성해도 `dark`가 잡힘, HUD 검사는 통과)
      — 세 버전 모두 UI Toolkit 내부 API 동작. 10: BagelGame 녹색 47.8 s(`619be553…`, `ui` = 스크린 공간 패널 1개, `Managed/` 132개·`Harness.*` 0개),
      Fluid-Sim 녹색 26.3 s(`54880f05…`, 23.0/15.0/22.1, 103개·0개), 사내 프로젝트 A 녹색 117.1 s(`6664b723…`, 위 흐름 시나리오, `-NoBuild`).

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
