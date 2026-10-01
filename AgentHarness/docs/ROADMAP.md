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
- 공통 마무리: 매트릭스 1–10 녹색 + 샷 PNG 확인 → 항목을 "해결됨"으로 옮기고 측정값 기록 → 이 표의 상태·워크플로우 절 갱신 → 새로 드러난 항목 추가 →
  **저장소 루트 `README.md`와 `AgentHarness/CLAUDE.md`(필요하면 `Tools~/templates/AgentHarness.md`)에 바뀐 기능·측정값 반영** → 커밋(메시지에 항목 ID).
  README·ROADMAP 갱신은 워크플로우마다 빠뜨리지 않는다(W2 커밋은 README를 건드리지 않았고, W4 뒤에도 README "요구 사항"에 6.6의 옛 상태가 남아 있었다).
- 하네스 변경은 에디터 트리에서 selftest로 검증하므로 워크플로우는 한 번에 하나씩 진행한다. W1–W16은 모두 끝났다(2026-10-01).
  남은 것은 상시(업스트림 — G2-4의 남은 Unity 쪽 비용, W13의 URP 데칼·W16의 Pipeline 빌드 메시지 신고 포함)와 마지막(macOS)이다.
- 크기: S = 파일 1–2개 · M = 여러 파일 또는 새 커맨드 · L = 조사가 필요하거나 새 하위 시스템.

| 순서 | 워크플로우 | 항목 | 크기 | 선행 | 상태 |
|---|---|---|---|---|---|
| W1 | 시나리오 입력 격리 | G3-6 | S | — | 완료 (2026-09-29) |
| W2 | 캡처가 화면 전체를 본다 | G3-1, G3-5 | M | W1 | 완료 (2026-09-29) |
| W3 | 시각 회귀와 움직임 | G3-4, G3-3, G3-7 | L | W1, W2 | 완료 (2026-09-30) |
| W4 | 렌더 설정을 코드로 | G1-1, P-4, G4-3, G4-2 | L | W3 | 완료 (2026-09-30) |
| W5 | 루프 속도 | G2-3, G2-1 | L | — | 완료 (2026-09-30) |
| W6 | 콘텐츠 헬퍼(a/b/c로 나눠 진행) | G1-3, G1-4, G4-1, G4-4 (+G3-10, G3-11) | L | W3, W4 (W6b는 W2) | 완료 (2026-09-30; 남은 하늘·반사는 G4-5) |
| W7 | 에디터 밖·여러 에디터 | G2-2, G2-4, G1-2, G5-1 | L | — | 완료 (2026-09-30; G2-4는 Unity 쪽 리로드만 남음) |
| W8 | 플레이어에서 돌리기(성능·실제 화면) | G3-2, G3-8 (+G3-12, G3-13) | L | W1 | 완료 (2026-09-30; G3-8의 사내 프로젝트 A 확인은 W16) |
| W9 | 병렬 작업의 공유 지점 | G5-4, G5-3 | M | — | 완료 (2026-09-30) |
| W10 | 렌더 밖의 프로젝트 설정도 코드로 | G1-5 | M | W4 | 완료 (2026-09-30; 여러 worktree가 같은 ProjectSettings 파일을 바꾸는 경우는 G5-6 → W14) |
| W11 | 백그라운드 에디터의 실제 입력 격리 | G3-9 (+G3-14, G1-6) | S | — | 완료 (2026-10-01) |
| W12 | 핫 루프 넓히기 | G2-5 | M | W5 | 완료 (2026-10-01; `try/catch`·예외 줄·새 필드는 Pipeline — 상시) |
| W13 | 같은 코드면 픽셀까지 같은 샷 | G3-15 | M | — | 완료 (2026-10-01; URP DBuffer 데칼의 흔들림·ScreenSpace 데칼의 NRE는 신고 — 상시) |
| W14 | 여러 worktree의 ProjectSettings | G5-6 | M | — | 완료 (2026-10-01) |
| W15 | 하늘과 씬 반사 | G4-5 | L | W13 | 완료 (2026-10-01) |
| W16 | 사내 프로젝트 A의 플레이어 화면 | G3-8 (+G3-16) | S | — | 완료 (2026-10-01) |
| 상시 | 업스트림·외부 의존 | O-1, O-5, O-6, O-9, O-10, O-11, O-12, O-13, O-14, P-4·G2-5·W13(URP 데칼)·W15(카메라 상태) 신고, G2-4 재측정 | S | 새 버전이 나올 때 | — |
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

### W5 루프 속도 (G2-3 → G2-1) — 완료 (2026-09-30, 아래 "해결됨")
- 결과: 핫 루프 `loop.ps1 -Hot`(`[CodeReload]` 본문만 → 인터프리터로 교체, 컴파일·리로드·빌드 없음) ~3.0 s(첫 캡처 1.27 s), 빌드 730 → 390–470 ms.
  "플레이 상태 유지"는 하지 않았다: 핫 루프도 시나리오를 처음부터 돌아 `play.events`·기준 이미지가 전체 루프와 그대로 비교된다.
  재측정 결과 리로드 직후 ~2 s의 대부분은 JIT가 아니라 에디터의 리로드 뒤 네이티브 작업(~0.9 s)이었다 → G2-4(W7).
- 먼저: G2-1 메모의 재측정. 에디터 1개일 때 컴파일 / 도메인 리로드 / Pipeline 재응답 구간을 나눠 잰다(다른 에디터가 떠 있으면 4.7–19s로 흔들렸다).
- 순서: G2-3(리로드 직후 빌더·fingerprint 워밍업, 작음) → G2-1(`loop.ps1 -Hot`: Pipeline `[CodeReload]`/`reload_file`로 Tick 본문 핫패치, 플레이 상태 유지).
- 고치는 곳: `Tools~/loop.ps1`, `Tools~/Harness.psm1`, `Editor/Build/HarnessBuild.cs`, 필요하면 `Runtime/GameRoot.cs`.
- 주의: 핫패치 API는 실험판 Pipeline(O-1)에 있다. 패키지를 올리면 깨질 수 있으니 매트릭스에 `-Hot` 루프를 넣는다. W3과 `loop.ps1`을 같이 고치므로 둘은 이어서 한다.
- 추가 검증: 모듈 Tick 본문 수정 → 2초 안에 반영된 캡처. "1차 버전 기준선" 표를 다시 재서 갱신.

### W6 콘텐츠 헬퍼 (W6a G1-3 · W6b G1-4 · W6c G4-1 → G4-4)
- W6a 파티클·애니메이션 — 완료 (2026-09-30, 아래 "해결됨"): `ctx.Particles`(설정 한 벌 → 고정 시드 ParticleSystem), `ctx.ParticleMaterial`,
  `ctx.AnimationClip`(키를 코드로 → .anim), `ctx.Animate` + 런타임 `ClipPlayer`(Playables, AnimatorController 에셋 없음, 크로스페이드, 이벤트 →
  `play.events`의 `ClipEvent:<이름>`). 빌드 뒤 클립의 경로·컴포넌트·속성을 대상과 대조해 `build.warnings`. 스모크 씬에 매듭 주위를 도는 링 2개와
  받침대에서 피어오르는 불씨를 넣었고 연속 캡처로 움직임(motion 25–28)을 확인했다. 타임라인 헬퍼는 넣지 않았다(해결됨의 "남은 것").
- W6b UI 킷 — 완료 (2026-09-30, 아래 "해결됨"): 패키지 테마가 가져오는 `HarnessKit.uss`(디자인 변수 + 판·글자·버튼·게이지·토스트 클래스), 런타임 컨트롤
  `Harness.UI.Gauge`·`ToastStack`(`[UxmlElement]`), Unity 6 데이터 바인딩 예제(스모크 HUD가 `SmokeHudData`에 바인딩). 하다가 UI Toolkit의 transition·타이머가
  실시간이라 캡처가 흔들리는 것을 찾아(G3-10) 시나리오 동안 패널 시간을 프레임 시계로 바꿨다. 폰트: 한중일은 에디터가 OS 폰트로 그려 따로 넣지 않았다.
- W6c 절차적 생성 — 완료 (2026-09-30, 아래 "해결됨"): GPU 베이크(`ctx.BakeTexture`: 베이크 셰이더 → RT → PNG, C# `Noise`와 같은 HLSL 노이즈,
  fingerprint는 PNG가 아니라 입력 해시), 그 위에 SDF + 서피스 네트, 스플라인 튜브, 포아송 스캐터와 바위, URP 데칼, Lit 디테일 맵. 샘플에 GPU로 구운 지형
  (1024² + 타일링 디테일), 선돌·바위·아치·룬 데칼. 하다가 **빌더가 메시를 바꾼 첫 루프가 옛 메시를 그리는** 오래된 문제를 찾아 고쳤다(G3-11). 절차적 하늘(구름)과
  씬 반사 프로브는 넣지 않았다 → G4-5.
- 공통: 셋 다 `Editor/Build/BuildContext.cs`에 헬퍼를 더한다. 따로 진행하려면 헬퍼별 파일(`partial class`)로 나눈다.
  새 콘텐츠는 W4의 `ctx.LitMaterial`을 쓰고, 기존 샷 회귀가 없는지 W3 기준 이미지로 본다.

### W7 에디터 밖·여러 에디터 (G2-2 → G1-2 → G5-1) — 완료 (2026-09-30, 아래 "해결됨")
- 조사 결과: 상주 batchmode 에디터(`-batchmode`, `-quit`·`-nographics` 없음)가 D3D11로 렌더하고 Pipeline 서버·플레이 모드·오프스크린 캡처·UI Toolkit 합성이
  모두 된다(fingerprint·events 같음) → GUI 에디터 N개가 아니라 **창 없는 에디터**로 갔다. 창 에디터는 `-automated`(대화상자가 기본값으로 바로 닫힘)로 띄운다.
- 결과: `open.ps1`이 항상 `-automated -debugCodeOptimization`(`-Interactive`로 뺌), `-Headless`(창 없음), `-Own`(worktree에 에디터 트리 `Library/` 사본 +
  그 worktree만의 창 없는 에디터 → 루프가 나란히, submit/land는 여전히 에디터 트리). 창 없는 에디터에서 루프 ~2.4 s(창 ~3.9 s), C# 1줄 ~7.0 s(창 ~9.5 s:
  리로드 뒤 ~0.9 s가 사라지고 리로드도 2.7 → 2.1 s). 하다가 창 없는 에디터가 세션에서 처음 그리는 메시를 쓰레기 값으로 그리는 것을 찾아 캡처가 한 번 버리고 그린다.
- 매트릭스 6이 "대기"와 "나란히(대기 0)"를 둘 다 본다(worktree 전용 에디터, 시작 컴파일 에러 보고, 첫 샷 기준 이미지 포함).

### W8 플레이어에서 돌리기 (G3-2, G3-8) — 완료 (2026-09-30, 아래 "해결됨")
- 결과: `tools/player.ps1` = 에디터 루프 → 개발 빌드 플레이어(Pipeline `build`, 증분) → 캡처 크기의 창에서 플레이어가 **혼자** 같은 시나리오를 돌고 종료 →
  플레이어 샷·실제 화면을 에디터 샷과 비교. Pipeline 런타임 서버는 쓰지 않았다(명령줄 인자 + result.json: 설정 파일·HTTP 서버를 플레이어에 넣지 않고 프레임 시간에도
  섞이지 않는다). 샘플 플레이어 ~430 fps(p95 ~4 ms) vs 창 에디터 ~114 fps, 같은 `play.events`.
- 하다가 찾은 것: **캡처가 HDR을 잘랐다**(G3-12 — URP는 대상 텍스처 형식으로 카메라를 렌더한다; 실제 화면과 같은 프레임 비교로 드러남, 고쳐서 기준 이미지 갱신).
  에디터 플레이 모드와 플레이어의 첫 프레임 차이(맞춤), 플레이어 첫 씬의 파티클 한 스텝(엔진 동작, 보고만).
- 매트릭스 1이 플레이어 실행을 본다(selftest 1번 끝), 10은 `attach-test.ps1 -Player`로 기존 프로젝트에서.

### W9 병렬 작업의 공유 지점 (G5-4 → G5-3) — 완료 (2026-09-30, 아래 "해결됨")
- 결과: 계약 폴더의 규칙을 기계가 지킨다. **파일은 발행 모듈별**(`<Module>Events.cs`에 그 모듈이 발행하는 이벤트 — lint가 모듈의 IL에서 `EventBus.Publish<T>`를
  찾아 대조), **이름은 한 번**(play.events가 타입 이름으로 센다), **올라간 타입은 바뀌지 않는다**(add-only를 파일이 아니라 타입 단위로: Roslyn 토큰 해시, 새 타입은
  모듈 파일에 덧붙인다). submit·land가 복사·병합 전에 거부하고(submit 2.1–2.4 s, land 1.5–1.7 s), 아직 land하지 않은 계약 파일은 올린 worktree 것이다
  (`Library/Harness/submit/contracts.json`, land가 해제). G5-3은 실제 문제였다: 계약에 다른 모듈이 쓰는 이름(`Light`)을 더하면 그 모듈이 CS0104로 깨지는데
  게이트는 그 모듈을 컴파일하지 않았다 → `compile-check -Dependents`(submit 게이트).
- 하다가 찾은 것: 예전 submit의 add-only는 파일 단위라 문서("새 struct 추가")보다 엄격했고, 모듈 파일 규칙과 합치면 올라간 `<Module>Events.cs`에 이벤트를 더할 수
  없었다. land는 계약을 아예 검사하지 않았다(같은 이름은 병합 뒤 CS0101로 전체 루프를 돌고서야 되돌렸다).
- 매트릭스 5(lint 규칙 4종), 7(같은 이름·남의 미병합 계약·올라간 타입 변경 거부, 역의존 게이트, 자기 계약 수정), 8(land 쪽 같은 이름·타입 변경 거부, 덧붙인
  이벤트의 submit → land → 소유 해제).

### W10 렌더 밖의 프로젝트 설정도 코드로 (G1-5) — 완료 (2026-09-30, 아래 "해결됨")
- 결과: `SettingsContext`에 `QualityLevels`(레벨 목록·레벨별 값·플랫폼별 기본 레벨, 에디터가 쓰는 레벨 = 활성 플랫폼의 기본), `Player`, `Time`, `Physics`(충돌 매트릭스
  포함), `Layer`, `Tag`, `ProjectSetting`(그 밖의 파일·경로). ProjectSettings는 생성물로 둘 수 없어서 **코드가 이름 붙인 값만 소유**한다: 매 빌드에 다르면 쓰고
  (`build.settings.project.changed`), 찾은 값이 마지막 빌드가 남긴 값(`Library/Harness/project-settings.json`)과도 다르면 코드 밖에서 바뀐 것 → `drift` + 경고.
  레이어·태그·품질 레벨 목록은 선언한 것이 전부. 샘플: 품질 레벨 2개와 색 공간(렌더 스텝), 레이어 Ground·Props(지형·소품이 씀)·창 크기·Time·중력(새 `StageProjectSettingsStep`).
- 하다가 찾은 것: 에디터에서 품질 레벨을 클릭하면 그 뒤 루프가 모두 그 레벨의 파이프라인으로 돌았다(보고 없이) → 이제 드리프트. 병렬 흐름의 구멍 — 모듈의 설정 스텝이
  바꾼 ProjectSettings는 에디터 트리에만 생겨 커밋되지 않았고, 빨간 submit이 되돌려도 남았다 → submit이 ProjectSettings를 백업(되돌리면 복원)하고 녹색이면 `.meta`처럼
  worktree로 되복사(`settingsWrittenBack`). 여러 worktree가 같은 파일을 바꾸는 경우는 G5-6으로 남겼다.
- 매트릭스 1(설정 스텝이 소유한 값, 손으로 고친 YAML·창에서 바꾼 값의 되돌림과 드리프트 6건), 7(새 모듈 레이어의 되복사, 빨간 submit의 복원), 8(되복사한 TagManager의 land).

### W11 백그라운드 에디터의 실제 입력 격리 (G3-9) — 완료 (2026-10-01, 아래 "해결됨")
- 결과: 재현부터 했다(다른 프로세스의 작은 창으로 OS 포커스를 에디터에서 빼고 selftest 1번의 스페이스 주입). 에디터가 백그라운드인 채 플레이 모드에 들어가면 Unity가
  그때 `Application.focusChanged(false)`를 보내고, 게임의 Input System 설정(기본 `ResetAndDisableNonBackgroundDevices`)이 실제 장치를 모두 "백그라운드라 끔"
  (`disabledWhileInBackground`)으로 둔다. 그 장치의 이벤트는 `InputSystem.onEvent` 전에 버려져 게임에는 닿지 않았지만, 격리는 켜진 장치만 가져가서 세지 못했고,
  시나리오 동안은 `IgnoreFocus`라 도중에 포커스가 돌아와도 장치가 켜지지 않아 끝까지 보고되지 않았다. selftest의 eval이 본 "켜져 있음"은 `InputDevice.enabled`가
  에디터 입력 업데이트 동안 그런 장치를 켜진 것으로 읽는 탓이었다. → 격리가 그런 장치도 가져가 끄고 센다(`isolatedDevices[].background`).
- 하다가 찾은 것: **UI Toolkit이 앱 포커스가 없으면 입력을 통째로 버린다**(G3-14) — 시나리오의 UI Toolkit 클릭이 에디터가 백그라운드면 아무 일도 하지 않았다.
  selftest가 끝난 뒤 사람이 쓰던 창으로 포커스를 돌려주자 UI 킷 검사가 빨갛게 되어 드러났다 → 시나리오 동안 그 판정을 끈다(`PanelFocus`). 도메인 리로드 뒤 에디터가
  다른 창이 앞에 있어도 포커스가 있다고 보고한다(O-13, 보고만). 6.6 새 클론의 6번이 빨갰던 것은 W11과 무관한 **G1-6** — `ctx.UIDocument`가 만든 PanelSettings의
  `referenceDpi`가 6.6에서는 에디터가 있는 화면의 DPI라 창 에디터(144)와 창 없는 에디터(96)의 fingerprint가 달랐다 → 96으로 고정.
- selftest: 재시도(`realInputTries`) 대신 포커스 잃음·복귀를 Unity 안에서 만든다(`Application.InvokeFocusChanged` — Input System이 포커스를 아는 유일한 경로). 처음에는
  OS 포커스를 실제로 옮겼는데(도우미 창 ↔ 에디터) 사람이 다른 창을 쓰는 동안에는 전환이 거부되거나 도중에 뺏겨 두 번 빨갰고, 사람의 작업도 방해했다.
- 매트릭스 1이 포커스 있음·없음으로 시작하는 플레이(없음은 도중에 복귀)와 UI Toolkit의 포커스 판정을 본다.

### W12 핫 루프 넓히기 (G2-5) — 완료 (2026-10-01, 아래 "해결됨")
- 결과: `[CodeReload]` 본문이 부르는 **새 메서드**(파일의 `[CodeReload]` 클래스에 더한, 컴파일된 타입에 없는 이름의 비제네릭 메서드 — 인스턴스·static, private 필드 읽기,
  새 메서드끼리의 호출)도 핫이다: 판정이 그 선언을 비교에서 빼고 Pipeline이 받는 조건을 따로 본다(`report.hot.reloaded[].newMethods`). Tick 본문 1줄 `-Hot` 3.41–3.46 s,
  같은 변경을 새 헬퍼 2개로 3.44–3.51 s. 안 되는 편집(제네릭 새 메서드, 오버로드·시그니처 변경, 표식 없는 메서드 본문, 필드·속성, 새 타입)은 전처럼 전체 루프인데 사유에
  그 줄과 **무엇인지**(`field m_X`, `method Reverse(int n) (an overload or a new signature of a compiled method)`)를 적는다.
- 인터프리터 비용: 교체된 메서드의 디스패치를 감싸 호출 수·프레임 수·시간을 세고 `report.hot.interpreted`(`methods[]`, `msPerFrame`, `frameShare`)에 싣는다. 샘플 Tick
  0.056–0.063 ms/프레임 = 프레임의 ~0.8%(도메인 리로드 뒤 첫 플레이 ~0.14 ms) — 핫 루프 fps는 전체 루프와 구분되지 않는다(124–145 vs 101–136).
- 하지 않은 것: Assembly.Load 백엔드(`reload_file`) 선택 — 위 비용이 작고 모듈 본문은 private 필드를 써서 그 백엔드가 거부한다. 인터프리터 `try/catch`·예외 줄·새 필드는
  Pipeline 쪽(상시).
- 하다가 찾은 것: **G3-15** — 같은 코드의 루프끼리 closeup 샷의 받침대·룬 데칼 모서리 픽셀 6–7개가 두 값 중 하나로 찍힌다(허용치 안이라 `same`, W11 실행에도 있었음).
- 매트릭스 3이 새 단계를 본다: 인터프리터 호출 통계, 새 헬퍼 경유 변경 = 인라인 변경의 샷, `harness_hot check`로 제네릭·오버로드 거부와 안 쓰는 새 메서드 수용.

### W13 같은 코드면 픽셀까지 같은 샷 (G3-15) — 완료 (2026-10-01, 아래 "해결됨")
- 결과: 원인은 URP의 **DBuffer 데칼**(데스크톱의 Automatic 기본값)이다. 플레이 중 마지막 카메라 렌더와 캡처 사이에 에디터 GUI가 그리면(창 다시 그리기) 룬 데칼
  가장자리 2픽셀이 2–3단계 달라지고, SMAA의 에지 판정이 그것을 6–7픽셀·채널 차이 47로 키웠다. 매 에디터 업데이트마다 모든 창을 다시 그리게 하면 10번 중 10번,
  그냥 두면 15번 중 1번. ScreenSpace 데칼은 그냥 3번·GUI 부하 8번 모두 세 샷이 픽셀까지 같았다 → 샘플을 ScreenSpace로(`m_Settings.technique`), 기준 이미지 갱신.
- 하다가 찾은 것: **URP 17.3의 ScreenSpace 데칼 패스는 중간 텍스처 없이 타깃에 바로 그리는 카메라에서 NullReferenceException**을 낸다
  (`RenderingUtils.SetScaleBiasRt` — `resourceData.cameraColor`가 비어 있음). 하네스가 uGUI 캔버스를 그리는 숨은 UI 카메라가 그런 카메라라 selftest의 uGUI 합성·연속
  캡처가 빨갰다 → 그 카메라는 렌더러의 renderer feature를 끄고 그린다(게임도 오버레이 캔버스를 카메라 뒤에 feature 없이 그린다). 기존 프로젝트가 ScreenSpace 데칼을
  쓰면 같은 일이 났을 것이다.
- 해 보고 하지 않은 것: 캡처 직전에 같은 카메라를 한 번 더 그려 버리기(+ `GL.Flush`) — 편집 모드에서는 맞았지만 플레이 중 GUI 부하에서는 여전히 갈렸고,
  렌더 횟수가 바뀌어 디더링 순번이 밀려 모든 샷이 ±4 바뀌었다. 렌더 그래프 풀·`UNITY_HDR_ON`·`GL.sRGBWrite`·SSAO·`copyDepthMode`·`intermediateTextureMode`는 원인이 아니었다.
- 매트릭스 1이 루프 2·3의 maxDiff 0과, 모든 창을 매 업데이트마다 다시 그리는 루프의 maxDiff 0을 본다(수정 전 DBuffer에서는 그 루프가 3번 모두 47).

### W14 여러 worktree의 ProjectSettings (G5-6) — 완료 (2026-10-01, 아래 "해결됨")
- 결과: land 쪽 방향. "하네스가 쓴 파일"을 "마지막 빌드가 남긴 내용과 같은 파일"로 정하면 그 빌드 전에 손으로 고친 값(이름 붙이지 않은 값)도 산출물이 되므로,
  매 설정 실행이 ProjectSettings 파일마다 **빌드만 거쳐 지금 내용에 이른 내용들**(git blob id)을 기록한다(`project-settings.json`의 `files`). 커밋된 내용이 그 안에
  있는 미커밋 파일 = 커밋된 파일 + 설정 스텝이 쓴 것 → land가 `foreign` 대신 산출물로 본다(병합이 바꾸면 stash로 비키고 병합 뒤 루프가 다시 씀,
  `land.settings.regenerated`). 녹색 land 뒤 모듈·계약 폴더에 다른 미병합 submit이 없고 에디터 트리에서 고친 코드도 없으면 그 파일들을 land 위에 커밋한다
  (`land.settings.commit`), 있으면 그 submit까지 land한 마지막 land가 커밋한다(`waitingFor`) → 순서 무관하게 에디터 트리 깨끗·모든 값 커밋. 빨간 land는 루프가 쓴
  ProjectSettings와 그 기록도 되돌린다(병합이 건드리지 않은 파일 포함 — W10에서 남긴 구멍).
- 줄 단위 YAML 병합은 git에 맡기지 않았다: 병합 커밋에는 브랜치의 파일이 들어가고(충돌 없는 경우), 그 위의 값은 루프가 쓴다. 두 브랜치가 같은 파일을 각자
  커밋해 충돌하면(예: `-Own` 에디터의 루프가 쓴 파일) 여전히 `land.conflicts` — 메시지가 master 쪽을 받으라고 알린다(자동으로 풀지 않음: 브랜치의 파일에 사람의
  편집이 있을 수 있다).
- 하다가 찾은 것: 손 편집을 가리는 데 "파일 내용 = 마지막 빌드가 남긴 것"으로는 모자랐다 — 손으로 고친 뒤 루프가 한 번 돌면 그 내용이 빌드의 출력이 된다. 그래서 기록은
  빌드 바깥의 변경(디스크의 다른 내용, 에디터가 저장하지 않은 변경)에서 다시 시작하고, 빌드가 그 변경을 통째로 되돌렸으면(소유한 값만 고친 드리프트) 이어진다.
  selftest의 진단 문자열 두 곳이 JSON 객체를 `['키']`로 읽어 늘 비어 있었다(판정은 맞았음) → 고침.
- 매트릭스 7(submit의 진단·되복사), 8(두 순서의 land, 손 편집 거부, 빨간 land의 TagManager 복원).

### W15 하늘과 씬 반사 (G4-5) — 완료 (2026-10-01, 아래 "해결됨")
- 결과: 하네스 하늘 `ctx.Sky`(패키지 셰이더 `Harness/Sky`: 그라디언트 + 태양 원반·빛무리 + `Noise_Perlin` fBm 구름, 구름은 `SkyClock`이 미는 게임 시간 `_HarnessSkyTime`으로 흐름 —
  플레이가 끝나면 0)과 `ctx.ReflectionProbe(path, size, resolution)`(모든 빌드 스텝이 끝나고 씬이 라이팅 데이터와 저장된 뒤 그 자리에서 Reflection Probe Static 렌더러 + 하늘을
  큐브로 그려 Custom 프로브로). 하늘·프로브 큐브맵 모두 mip을 GGX로 거른다(`HarnessCubeFilter.shader`, 고정 표본) — W4의 `BakeSkyReflection`은 박스 mip이었다. 큐브맵은
  매 빌드 다시 그리되 픽셀이 바뀐 때만 쓰고(`build.cubemapsWritten`), fingerprint에는 모양만(`cubemap 256 RGBAHalf 9`). 샘플: 구름 하늘, 강철 받침대(프로브 없이는 하늘만 비춰
  하얗게 뜬다)·매듭(손으로 쓴 셰이더에 Forward+ 클러스터 루프의 프로브 반사를 더함)·링이 선돌·아치·지면·룬 원을 비춘다. 기준 이미지·fingerprint 갱신, 셰이더 표식 87 → 99행.
- 하다가 찾은 것: **프로브 렌더가 같은 에디터 프레임에서 끈 프로브를 계속 썼다** — 컬링이 프레임 시작 때의 프로브를 봐서, 빌드(한 프레임)의 프로브 렌더가 앞 빌드 씬의 프로브
  (같은 큐브맵 에셋)를 비췄다. 받침대 재질을 바꾸면 큐브맵이 2–3 빌드에 걸쳐 수렴했다(바꾼 뒤 첫 루프의 샷이 다음 루프와 다름) → 끈 뒤 `ReflectionProbe.UpdateCachedState()`.
  **카메라의 첫 렌더가 앞 카메라의 상태를 이어받았다** — near/far가 다른 카메라 뒤의 첫 하늘 렌더는 태양 원반 가장자리 텍셀이 half 몇 단계 달라, 컴파일 뒤 빌드마다 하늘
  큐브맵을 다시 썼다 → 같은 카메라로 두 번 그린다(O-11 우회와 같은 자리, 모든 에디터 모드). 둘째는 W15 전 `BakeSkyReflection`에도 있었다(그때는 매 빌드 큐브맵을 썼다).
- 매트릭스 1이 새 검사를 본다: `Harness/Sky`·플레이 뒤 시계 0, 하늘만 보는 연속 캡처에서 구름이 흐름(`motion` 4.4), 프로브(Custom 256, 박스 투영)의 아치 방향 텍셀이 돌,
  fingerprint의 큐브맵 줄이 모양, 프로브를 켜고 끈 closeup 렌더에서 받침대(평균 차 0.27, 잡음 0.00002)·매듭(하늘이 +0.15 밝음)이 그것을 비춤, 루프 2·3의 `cubemapsWritten` 없음.

### W16 사내 프로젝트 A의 플레이어 화면 (G3-8 + G3-16) — 완료 (2026-10-01, 아래 "해결됨")
- 사람의 결정(2026-10-01): 클론(`../../ah-p2/brd`, 사용자 본인의 프로젝트 폴더가 아님)을 Standalone으로 바꿔도 된다, 로비 시나리오(개발 서버 로그인, PlayerPrefs)를
  돌려도 된다, Windows 빌드에 필요한 게임 코드 패치는 클론의 로컬 커밋으로(푸시 안 함, 끝나면 원래 HEAD로).
- 결과: 클론을 배치 모드 `-buildTarget Win64 -quit`로 전환(101 s) → 첫 플레이어 빌드가 **게임 코드의 플레이어 전용 컴파일 에러**로 실패
  (`Handheld.Vibrate()`가 `Application.isMobilePlatform` 런타임 검사만 받음 — 에디터는 컴파일된다) → report가 그 줄을 못 짚어서 고침(`compileErrors`, kind `player`) →
  패치 뒤 개발 빌드 211 s·426 MB, 부트 대화상자 2장과 로그인부터 **로비까지 4장 모두 플레이어 720x1280 창의 실제 화면 = 같은 프레임 캡처**(바뀐 픽셀 0) — 그 전에 캡처가
  오버레이 캔버스의 TMP 글자를 더 날카롭게 그리던 것(G3-16)을 고쳤다. 에디터 샷과의 차이(7–15%)는 게임 쪽(에디터 전용 UI, 실시간 연출)이고 report가 `cause`로 가른다.
- 하다가 찾은 것: 클론의 Standalone 스크립팅 define에는 `DEV`가 없어(Android에는 있음) Windows 플레이어는 라이브 서버 환경이었다(로그인 전에 확인, 로그인 없음) →
  테스트 커밋에 `DEV`를 더해 개발 서버로. 개발 서버 로비 한 번에 게임의 차팅 동기화가 에디터 PlayerPrefs의 `charting.hash.*` 138개와 캐시 파일(`LocalLow/<회사>/<제품>/Charting`)을
  지웠다(서버에 없는 테이블의 캐시 삭제 — 원본 에디터와 공유, 다음 부팅에 다시 받음; 되돌리면 해시와 파일이 어긋나므로 두었다). 플레이어는 작업 폴더에
  Facebook SDK 로그(`fbg.log`)를 썼다 → 플레이어를 출력 폴더에서 띄운다.
- 끝: 공식 `attach-test.ps1 -Player`(출시 빌드 포함) 녹색 202 s → 클론을 Android로 되돌리고(배치 모드 73 s) 테스트 커밋을 버려 원래 HEAD, 매트릭스 10은 원래 절차
  (`brd-attach.json`, `-NoBuild`).

### 상시: 업스트림·외부 의존 (O-1, O-5, O-6, O-9, O-10, O-11, O-12, O-13, O-14, P-4·G2-5·W13·W15 신고, G2-4 재측정)
- 코드보다 신고와 재검증: Pipeline에 2건(`RuntimeInputCommand.cs`의 `ENABLE_INPUT_SYSTEM` 조건, 출시 빌드 의존)과 G2-5의 인터프리터 2건(`try/catch` 미지원,
  교체 본문이 던진 예외를 줄 없이 로그하고 원래 본문으로 이어 돌림), Unity에 P-4의 원인
  (`Camera.RenderToCubemap(Cubemap)`: 6.6은 CPU 픽셀을 안 채우고 6.3은 sRGB로 인코딩 — 빈 씬 + 스카이박스 + half 큐브맵 한 개로 재현), O-6 Unity Search 예외,
  O-11(batchmode의 첫 메시 그리기 — 빈 프로젝트 재현부터), O-12(도메인 리로드 뒤 새 토큰이 디스크립터에 늦게 적힘 — 먼저 원인 조사),
  O-13(도메인 리로드 뒤 다른 창이 앞에 있어도 `Application.isFocused` true). W11: UI Toolkit 내부 `DefaultEventSystem.IsEditorRemoteConnected`(`PanelFocus`)와
  `InputDevice.disabledWhileInBackground`(격리)에 기댄다 — 새 버전에서 selftest 1번의 포커스 검사로 확인. W12: 핫 루프의 새 메서드 판정은 Pipeline의 규칙
  (파일당 첫 `[CodeReload]` 클래스, 비제네릭, 컴파일된 이름이 아님)을 따라 하고, 인터프리터 호출 수는 내부 `CodeReloadRegistry.m_MethodOverrides`·`MethodOverride.InterpreterInvoke`를
  감싸 센다 — Pipeline을 올리면 selftest 3번의 새 헬퍼·호출 통계 단계로 확인(없어지면 `hot.interpreted.error`만). O-10: Unity 6.7이 나오면 `EditorDialogEvents`로 자동으로 닫힌 대화상자를 report에 싣는다.
  G2-4: 남은 도메인 리로드 ~2.1 s와 리로드 직후 빌드 +0.8 s는 Unity 쪽이라 새 Unity(CoreCLR 에디터)가 나오면 "기준선"의 C# 1줄 루프를 다시 잰다.
  W13: URP에 2건 — (1) DBuffer 데칼: 플레이 중 `SubmitRenderRequest`로 그린 카메라의 데칼 가장자리 픽셀이 그 앞에 에디터 GUI가 그렸는지에 따라 2–3단계
  다르다(샘플 closeup의 룬 원·받침대 모서리; `InternalEditorUtility.RepaintAllViews()`를 매 에디터 업데이트마다 부르면 10/10, ScreenSpace 데칼은 0/8; 편집 모드에서는 에디터
  프레임마다 첫 캡처가 다르고, 같은 프레임에서 앞서 그린 카메라 렌더가 GPU로 넘어간 뒤(`ReadPixels`·`GL.Flush`)에는 같다). (2) ScreenSpace 데칼:
  `DecalScreenSpaceRenderPass`가 `resourceData.cameraColor`로 `SetScaleBiasRt`를 불러, 중간 텍스처 없는 카메라(후처리·HDR·MSAA·깊이/불투명 텍스처 없음, 타깃 텍스처)에서
  NullReferenceException → Render Graph Execution error. 고쳐지면 CaptureUi의 feature 끄기는 그대로 두고(게임과 같은 그리기), 샘플을 DBuffer로 되돌려 selftest 1번의
  다시 그리기 루프로 확인할 수 있다.
  W15: URP에 1건 — 카메라의 첫 렌더가 앞서 그린 다른 카메라의 상태를 이어받는다(near/far가 다른 카메라 뒤 첫 하늘 렌더의 태양 원반 가장자리 텍셀이 half 1–6단계 다름,
  같은 near/far 카메라를 먼저 한 번 그리면 같음; 재현: 하늘만 있는 씬에서 near/far가 다른 카메라 둘로 `RenderToCubemap`). 고쳐지면 `RenderEnvironment`의 두 번 그리기는
  창 없는 에디터(O-11)에서만 남긴다. 반사 프로브를 같은 프레임에서 끄면 컬링이 다음 프레임에야 아는 것은 문서화된 동작으로 보여 신고하지 않는다(`UpdateCachedState`).
  W8: Unity에 증분 플레이어 빌드가 앞선 빌드의 `ScriptingAssemblies.json`을 쓰는 것(출시 빌드 → 다른 폴더로 개발 빌드, define 제약으로 어셈블리 집합이
  달라짐; 6.0 Fluid-Sim에서 재현 — 고쳐지면 `player.ps1`의 `CleanBuildCache` 재빌드를 걷어낸다), 플레이어 첫 씬 파티클의 로드 시점 한 스텝(의도인지 문의).
  W16: Pipeline에 O-14(`build_status`의 에러가 줄을 파싱하고도 버림). 고쳐지면 `player.ps1`은 그대로 둬도 된다(빌드 단계 메시지를 먼저 읽음).
- 계기: Pipeline 새 버전이나 Unity 6000.x 새 패치 → 매트릭스(9는 그 버전으로) 재검증 → 우회 코드(`Invoke-HarnessRecompile` 세대 번호,
  install의 Input System 추가, `HarnessReleaseBuild`)를 걷어낼 수 있는지 본다. O-9: 새 버전에서 selftest 1번의 HUD 검사(`uiError` 없음)를 보고,
  UI Toolkit에 패널을 지금 그리는 공개 API가 생기면 리플렉션을 걷어낸다.

### 마지막: macOS (P-3)
- 실제 Apple Silicon Mac이 있을 때 한다. 그 전까지 모든 워크플로우에서 새 코드에 백슬래시 경로·`powershell.exe`·`C:\` 경로를 늘리지 않는다.
- 결정성 기준("같은 머신 안에서 결정적")은 W3의 기준 이미지 정책을 정할 때 같이 정한다.

## 기준선 (비교용)

1차 버전(2026-09-28), W5·W6a·W6c·W7·W8 뒤(2026-09-30)와 W12·W15 뒤(2026-10-01; 새로 연 에디터에서 각 3회, 이 머신; W8 측정 때는 다른 앱의 백그라운드 부하가 있었다). 워크플로우가 루프 시간을 바꾸면 열을 더한다(W6a: 파티클·링 애니메이션,
W6c: GPU 베이크 지형·소품, W7: `open.ps1`의 `-automated` 창 에디터 / 창 없는 에디터 `-Headless`, W8: 캡처가 카메라의 HDR 형식으로 렌더 + 플레이어 실행, W12(2026-10-01): 핫 루프의 새 메서드·인터프리터 계측, W15(2026-10-01): 구름 하늘·GGX 큐브맵·빌드 뒤 반사 프로브 256²; 잰 것만).

| 항목 | 1차 버전 | W5 | W6a | W6c | W7 창(`-automated`) / 창 없음(`-Headless`) | W8 (창) | W12 (창) | W15 (창) |
|---|---|---|---|---|---|---|---|---|
| 루프: 코드 변경 없음 | 3.5–3.8s (build 0.7s 캐시 적중, play 2.6s) | 3.47–3.68s (build 0.47s, play 2.4–2.6s, 첫 캡처 1.75s) | 3.52–3.64s (build 0.51–0.53s, play 2.39–2.51s) | 3.73–3.91s (build 0.61–0.64s, play 2.49–2.66s) | 3.82–3.90s (build 0.59–0.61s, play 2.51–2.62s) / **2.36–2.50s** (build 0.52–0.54s, play 1.18–1.30s) | 4.08–4.37s (build 0.64–0.71s, play 2.71–2.95s; 샷 한 장 ~95 ms, 대부분 PNG 인코딩) | 3.82–4.72s (build 0.64–1.01s, play 2.52–3.00s; fps 101–130) | 4.03–4.28s (build 0.67–0.76s, play 2.64–2.80s, fps 113–120); 새로 연 에디터의 첫 루프 5.03s (build 1.29s — 프로브 0.22s 콜드) |
| 루프: 셰이더만 수정 | ~4s (도메인 리로드 없음) | 3.79–3.83s | — | 4.06–4.50s | 3.94–4.27s / **2.47–2.68s** | — | — | — |
| 루프: 모듈 C# 1줄 수정 | ~9.2s (compile+reload 4.1s, build 1.9s, play 2.8s) | 8.84–9.08s (compile 4.7–4.9s = Tundra 0.35s + 리로드 ~2.5s + 리로드 뒤 에디터 ~0.9s, build 0.93–1.0s, play 2.55s) | 8.88–9.39s (compile 4.64–5.09s, build 0.97–1.03s, play 2.57–2.63s) | 9.53–9.69s (compile 4.73–4.92s, build 1.14–1.23s, play 2.86–2.97s) | 9.27–9.67s (compile 4.61–4.99s, 리로드 2.73–2.77s, build 1.14–1.23s, play 2.73–2.87s) / **6.75–7.10s** (compile 3.23–3.42s, 리로드 2.07–2.10s, 리로드 뒤 에디터 작업 없음, build 1.33–1.35s, play 1.59–1.69s) | — | — | — |
| 루프: `-Hot`(Tick 본문 1줄) | — | 3.00–3.06s (판정+교체 0.14s, 첫 캡처 1.27s); 도메인 리로드 뒤 첫 번째 3.81–3.89s (교체 0.9s) | 3.21–3.29s (교체 0.15s, play 2.42–2.50s); 리로드 뒤 첫 번째 3.97s (교체 0.94s) | 3.35–3.45s (교체 0.15s, play 2.51–2.62s); 리로드 뒤 첫 번째 4.11s | 3.14–3.42s; 리로드 뒤 첫 번째 4.34s / **1.96–2.18s**; 리로드 뒤 첫 번째 3.01s | — | 3.41–3.46s (교체 0.13s, play 2.49–2.55s, fps 124–131); 리로드 뒤 첫 번째 4.27s (교체 0.86s) | — |
| 루프: `-Hot`(같은 변경을 Tick이 부르는 새 헬퍼 2개로, G2-5) | — | — | — | — | — | — | 3.44–3.51s (교체 0.16–0.17s, play 2.49–2.56s, fps 128–136) | — |
| 핫 루프의 인터프리터 비용(Tick, `hot.interpreted`) | — | — | — | — | — | — | 0.056–0.063 ms/프레임 = 프레임의 0.7–0.8% (리로드 뒤 첫 플레이 0.13–0.14 ms); 플레이의 Tick 182회 | — |
| 루프: C# 컴파일 에러 보고 | ~1s | 0.94–1.14s | — | — | — | — | — | — |
| 빌드 단계(lint + `harness_build` + 셰이더; 웜 / 리로드 직후) | ~0.9s / 1.9s | 0.47s / 0.93–1.0s (리로드 뒤 에디터 ~0.9s는 이제 compile 쪽에서 기다림) | 0.51–0.53s / 0.97–1.03s (FX 스텝 7.5–8 ms) | 0.61–0.64s / 1.14–1.23s (소품 스텝 ~60 ms, 큰 메시 fingerprint ~60 ms) | 0.59–0.65s / 1.14–1.23s — 창 없음 0.52–0.60s / 1.33–1.35s | — | — | 0.67–0.76s (`harness_build` 577–632 ms: 환경 스텝 36–40 ms — 하늘 128² 큐브 두 번 + GGX mip + 비교 ~13–16 ms; 프로브 단계 36–56 ms — 256² 두 번 + GGX + 4 MB 비교, 바꾼 뒤 첫 빌드는 쓰기 포함 0.1–0.23s) |
| compile-check csc / msbuild | 어셈블리당 ~0.1s / 웜 0.5–2s, 콜드 10–75s | 어셈블리당 0.13–0.16s / 웜 0.45–0.63s, 콜드 4–13s | — | — | — | — | — | — |
| 스모크 씬 렌더 | batches ~46, SetPass ~43, tris ~60만 | 같음 (45.8 / 42.8 / 59만) | 50.9 / 47.8 / 61만 (링 2개 + 파티클) | 67.1 / 51.9 / 124만 (선돌·바위·아치·데칼, 그림자 캐스케이드 포함) | 같음 / 없음(Game 뷰가 그리지 않음, `render` null) | 같음(에디터 65 / 50 / 122만); 개발 빌드 플레이어 69 / 54 / 126만(D3D12) | — | 61 / 51 / 122만 (하늘이 `Skybox/Procedural`에서 `Harness/Sky`로) |
| 에디터 열기(재시작, `open.ps1`이 준비될 때까지) | ~30s (첫 응답 뒤 Debug 재컴파일 + 리로드 ~10s 포함) | — | — | — | 14.3s (`-debugCodeOptimization`: 재컴파일 없음) / 12.1s | 17.3s | 13.5s | 15.9s |
| 창 없는 에디터 유휴 CPU | — | — | — | — | 쉬지 않는 루프 1코어의 120% → `HarnessHeadless` 1코어의 ~8%, ping 17–22 → ~8 ms | — | — | — |
| 루프 2개 동시 | 두 번째가 3.55s 대기 | — | — | — | 같은 에디터: 두 번째가 4.59s 대기 / worktree 전용 에디터(`-Own`): 둘 다 대기 0 (창 4.4–4.9s, 창 없음 2.7–2.9s) | — | — | — |
| worktree 전용 에디터 준비(`open.ps1 -Own`) | — | — | — | — | 28.7–29.3s (`Library/` 사본 1.9 GB·2.7만 파일 11.4s + 스크립트 전체 재컴파일 ~17s), 첫 루프 9.8s (빌드 캐시 없음) | — | — | — |
| 플레이어(개발 빌드, 1280x720 창, vSync 끔) 시나리오 fps / p95 | — | — | — | — | — | **452–516 / 2.8–3.6 ms** (같은 때 창 에디터 105–120 / ~11 ms, ×3.8–4.9) | — | 493 / 3.1 ms (같은 때 창 에디터 124, ×3.97; selftest 1번) |
| `player.ps1` 한 바퀴 (에디터 루프 + 증분 빌드 + 플레이어 + 비교) | — | — | — | — | — | 15.4–17.4s (4.1–4.5 + 4.9–6.8 + 5.4–6.0 + 0.4s), `-NoBuild` 11.4s | — | — |
| 플레이어 개발 빌드 처음 / 증분 | — | — | — | — | — | 116s(셰이더 81s) / 3–12s, 189 MB | — | — |

---

## 성질 1 — 모든 게 텍스트

- **G1-1 프로젝트 설정과 URP 에셋이 여전히 YAML** → 2026-09-30 해결(W4, 아래 "해결됨"). RP 밖의 프로젝트 설정은 G1-5.

- **G1-2 빌더가 에디터 안에서만 실행된다** → 2026-09-30 해결(W7, 아래 "해결됨"): 창 없는 에디터(`open.ps1 -Headless`)와 worktree 사본마다의 에디터
  (`open.ps1 -Own`). 여전히 에디터 프로세스는 필요하다(빌더는 에디터 API).

- **G1-3 파티클·애니메이션·타임라인용 코드 헬퍼가 없다** → 2026-09-30 해결(W6a, 아래 "해결됨"). 타임라인은 넣지 않았다(클립 + `ClipEvent` + 모듈 코드로 대신).

- **G1-4 UI Toolkit 경로는 있지만 얇다** → 2026-09-30 해결(W6b, 아래 "해결됨").

- **G1-5 렌더 파이프라인 밖의 프로젝트 설정은 여전히 YAML** → 2026-09-30 해결(W10, 아래 "해결됨"): 설정 스텝이 품질 레벨·Player·Time·Physics·레이어·태그 값을
  소유하고, 창·YAML로 바꾼 값을 다음 루프가 되돌리며 `drift`로 보고한다. 코드가 이름 붙이지 않은 값과 URP 전역 설정 에셋은 여전히 커밋된 YAML.

- **G1-6 6.6에서 빌더가 만든 PanelSettings가 에디터가 있는 화면의 DPI를 따라 fingerprint가 흔들린다** (2026-10-01, W11 매트릭스 9에서 발견) → 같은 날 해결
  (W11, 아래 "해결됨"). 같은 코드인데 에디터 창을 둔 화면에 따라 6.6의 build.fingerprint가 달랐다.

## 성질 2 — 루프가 초 단위

- **G2-1 C# 1줄 수정에 ~9초 (컴파일 + 도메인 리로드 ~4초가 고정비)** → 2026-09-30 해결(W5, 아래 "해결됨"): `[CodeReload]` 본문만 바꿨으면 `loop.ps1 -Hot` ~3.0 s.
  그 밖의 C# 변경은 여전히 ~9 s → G2-4(고정비), G2-5(핫 범위).

- **G2-2 GUI 에디터가 떠 있어야 하고, 모달 다이얼로그가 뜨면 멈춘다** → 2026-09-30 해결(W7, 아래 "해결됨"): `open.ps1`이 `-automated`로 연다
  (`DisplayDialog`가 기본값으로 바로 닫힘), `-Headless`면 창 없이. 에디터가 뜨기 전의 창(새 버전의 이용 약관)은 여전히 사람 몫, 6.0–6.6에서는 자동으로 닫힌
  대화상자가 보이지 않는다(O-10).

- **G2-3 도메인 리로드 직후 첫 `harness_build`가 ~2초 (JIT 워밍업)** → 2026-09-30 해결(W5, 아래 "해결됨"). 대부분이 JIT가 아니라 에디터의 리로드 뒤 작업이었다(G2-4).

- [~] **G2-4 전체 루프의 고정비는 Unity 쪽이다** (2026-09-30, W5에서 드러남)
  - 현상: C# 1줄 루프 ~8.9 s 중 도메인 리로드 ~2.5 s(`Domain Reload Profiling`: `CreateAndSetChildDomain` ~0.5 s, `[InitializeOnLoad]` ~0.4 s,
    `AwakeInstancesAfterBackupRestoration` ~0.46 s, …)와 리로드 뒤 첫 두 에디터 틱 사이의 네이티브 작업 ~0.9 s(관리 코드 `update`·`delayCall` 콜백은 모두
    20 ms 미만 — 창 다시 그리기로 보인다), 리로드 직후 빌드가 웜보다 ~0.5 s 더 든다(Unity·패키지 쪽 JIT; 하네스 코드만 미리 JIT하면 ~75 ms). 리로드는 에디터를
    오래 띄워 둘수록 늘었다(2.5 → 3.5 s, 재시작하면 돌아옴).
  - 2026-09-30 W7 측정(새로 연 에디터 각 3회, 위 "기준선"): 창 없는 에디터에서는 리로드 뒤 ~0.9 s가 **없고**(창 다시 그리기였다) 리로드도 2.73–2.77 → 2.07–2.10 s,
    플레이 2.8 → 1.6 s → C# 1줄 9.27–9.67 s → **6.75–7.10 s**. 창 에디터(`-automated`)는 전과 같다.
  - 남은 것: 도메인 리로드 ~2.1 s와 리로드 직후 빌드 +0.8 s는 Unity 쪽이다(CoreCLR 에디터가 나오면 다시 잰다 — 상시). 에디터 세션의 나이를 보고 재시작을 권하는 것은
    하지 않았다.

- **G2-5 핫 루프는 `[CodeReload]` 메서드 본문만 받는다** → 2026-10-01 해결(W12, 아래 "해결됨"): 본문이 부르는 새 메서드도 핫, 인터프리터 비용을
  `hot.interpreted`로 보고. 인터프리터 `try/catch`·예외 줄·새 필드·속성은 Pipeline(상시).


## 성질 3 — 에이전트가 화면을 본다

- **G3-1 오프스크린 캡처에 스크린 공간 UI가 안 찍힌다** → 2026-09-29 해결(W2, 아래 "해결됨").

- **G3-2 에디터 플레이 모드 FPS는 실제 성능을 대표하지 못한다** → 2026-09-30 해결(W8, 아래 "해결됨"): `tools/player.ps1`이 같은 시나리오를 개발 빌드
  플레이어에서 돌려 에디터와 나란히 보고한다(샘플 ~430 fps vs 에디터 ~114 fps). 출시(비개발) 빌드의 성능은 재지 않는다.

- **G3-3 정지 이미지만 나온다** → 2026-09-30 해결(W3, 아래 "해결됨"). 연속 캡처는 한 장의 시트(GIF는 만들지 않음).

- **G3-4 시각 회귀 검사가 없다** → 2026-09-30 해결(W3, 아래 "해결됨"). 다른 머신의 허용치는 P-3.

- **G3-5 이미지 판정이 휴리스틱이다(마젠타 머티리얼)** → 2026-09-29 해결(W2, 아래 "해결됨"). `blank`·`dark`는 여전히 휴리스틱이다
  (기준 이미지가 있으면 G3-4의 비교가 바뀐 화면을 잡는다).

- **G3-6 실제 키보드·게임패드 입력이 시나리오 재생에 섞인다** → 2026-09-29 해결(W1, 아래 "해결됨").

- **G3-7 캡처는 카메라 하나 + 스크린 공간 UI다** → 2026-09-30 해결(W3, 아래 "해결됨"). 스택 Overlay 카메라의 캔버스는 렌더 요청이 그리지 않아
  합성한다(아래 G3-8의 플레이어 캡처와 비교할 것).

- **G3-8 에디터 캡처의 UI가 게임의 화면 크기 코드와 어긋날 수 있다** → 경로는 2026-09-30(W8), 사내 프로젝트 A 확인은 2026-10-01(W16) 해결(아래 "해결됨").
  플레이어 720x1280 창의 실제 화면이 부트·타이틀·로비 4장 모두 같은 프레임의 캡처와 픽셀까지 같고, 에디터 샷과의 차이는 `vsEditor.cause`(`game`/`capture`)와
  `compare.note`가 가른다. 에디터의 `Screen.width`가 Game 뷰라 생기는 차이는 여전히 에디터 캡처로는 고칠 수 없다 — `player.ps1`로 본다.

- **G3-16 오버레이 캔버스의 TextMesh Pro 글자를 캡처가 더 날카롭게 그렸다** (2026-10-01, W16에서 발견) → 같은 날 해결(W16, 아래 "해결됨").
  캡처가 Screen Space - Camera로 바꾼 캔버스에서 TMP가 Overlay용 SDF 스케일을 그대로 썼다(scaleFactor 0.46에서 가장자리 ~2배 날카로움, "0%" 글자 96픽셀).

- **G3-11 빌더가 메시를 바꾼 첫 루프의 샷이 옛 메시를 그린다** (2026-09-30, W6c에서 발견) → 같은 날 해결(W6c, 아래 "해결됨").

- **G3-9 에디터가 백그라운드일 때 실제 입력 격리가 검증되지 않는다** → 2026-10-01 해결(W11, 아래 "해결됨"). 격리는 켜진 장치만 가져가서, 에디터가 백그라운드인 채
  플레이가 시작되면 Input System이 먼저 꺼 둔 장치를 세지 못했다(게임에는 닿지 않았다).

- **G3-14 에디터(플레이어)가 백그라운드면 UI Toolkit이 시나리오 입력을 버린다** (2026-09-30, W11 매트릭스에서 발견) → 같은 날 해결(W11, 아래 "해결됨").
  같은 시나리오의 UI Toolkit 클릭이 어느 창에 포커스가 있느냐에 따라 먹거나 안 먹었다.

- **G3-10 UI Toolkit의 transition·타이머가 실시간이라 UI가 움직이는 동안의 캡처가 매번 다르다** (2026-09-30, W6b에서 발견) → 같은 날 해결(W6b, 아래 "해결됨").

- **G3-13 Unity 6.6에서 `render.batches`·`drawCalls`가 0이었다** (2026-09-30, W8의 6.6 새 클론에서 발견) → 같은 날 해결(W8, 아래 "해결됨" 8번).
  6.6은 Render 분류의 `Batches Count`·`Draw Calls Count`를 없애고 종류별로 나눴다. 6.6 batches는 이제 null(대응 카운터 없음).

- **G3-12 캡처가 HDR 이미션·블룸을 잘랐다** (2026-09-30, W8에서 발견) → 같은 날 해결(W8, 아래 "해결됨"). URP는 대상 텍스처가 있는 카메라를 그 텍스처의
  형식으로 렌더해서 8비트 캡처 RT가 톤 매핑 전에 HDR을 1로 잘랐다. 에디터 캡처끼리는 매번 같아 기준 이미지로는 안 보였고 플레이어의 실제 화면과 비교해 드러났다.

- **G3-15 같은 코드의 루프끼리 closeup 샷의 픽셀 6–7개가 두 값 중 하나였다** (2026-10-01, W12에서 발견) → 같은 날 해결(W13, 아래 "해결됨").
  URP DBuffer 데칼의 가장자리 픽셀이 캡처 전에 에디터 GUI가 그렸는지에 따라 달랐고 SMAA가 그것을 키웠다. 샘플은 ScreenSpace 데칼로 바꿨고, DBuffer 쪽은 상시(신고).

## 성질 4 — 에셋 없이도 완성도

- **G4-1 CPU(C#) 텍스처 베이크가 느리다** → 2026-09-30 해결(W6c, 아래 "해결됨").

- **G4-2 스카이박스 앰비언트는 라이팅 베이크가 필요해서 Trilight로 우회 중** → 2026-09-30 해결(W4, 아래 "해결됨").

- **G4-3 코드로 만든 URP Lit 머티리얼은 키워드를 수동으로 켜야 한다** → 2026-09-30 해결(W4, 아래 "해결됨").

- **G4-4 절차적 라이브러리가 기본 수준이다** → 2026-09-30 해결(W6c, 아래 "해결됨"). 하늘·반사는 G4-5.

- **G4-5 하늘과 반사가 아직 기본이다** → 2026-10-01 해결(W15, 아래 "해결됨"): 하네스 하늘(`ctx.Sky`, 게임 시간으로 흐르는 구름)과 빌드 뒤 씬을 찍는 반사 프로브
  (`ctx.ReflectionProbe`), 두 큐브맵의 GGX mip. 프로브는 Reflection Probe Static만 그린다(움직이는 것은 비치지 않음) — 실시간 프로브·SSR은 없다.

## 성질 5 — 병렬 작업이 쉽다

- **G5-1 에디터 1개 → 루프가 직렬화된다** → 2026-09-30 해결(W7, 아래 "해결됨"): worktree마다 에디터(`open.ps1 -Own`), 루프 2개 동시에 대기 0.
  에디터 수 = worktree 수라 메모리(~2 GB)·디스크(`Library/`)가 그만큼 든다. 에디터 몇 개를 여러 worktree가 나눠 쓰는 풀은 만들지 않았다.

- **G5-3 compile-check는 다른 모듈의 최신 변경을 모른다** → 2026-09-30 해결(W9, 아래 "해결됨"): 검사 집합이 참조하는 쪽은 에디터 DLL이 맞았고(submit이 합쳐질
  에디터 트리가 컴파일하는 바로 그것), 문제는 집합을 참조하는 쪽이었다 → `-Dependents`(submit 게이트).

- **G5-4 `Assets/Game/Contracts`가 공유 지점이다** → 2026-09-30 해결(W9, 아래 "해결됨"): 발행 모듈별 파일·이름 한 번(lint), 타입 단위 add-only·이름 충돌·미병합
  계약 파일의 소유(submit·land).

- **G5-6 ProjectSettings 파일은 여러 모듈의 설정 스텝이 같이 쓴다** → 2026-10-01 해결(W14, 아래 "해결됨"): 빌드가 파일마다 "빌드만 거쳐 지금 내용에 이른
  내용들"을 기록하고, land가 커밋된 파일 + 설정 스텝이 쓴 것을 산출물로 다시 쓰고 마지막 land가 커밋한다(순서 무관). 같은 파일을 각자 커밋한 두 브랜치의 git 충돌은
  여전히 사람(또는 에이전트)이 master 쪽으로 푼다.

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
  - 2026-09-30(W5): 핫 루프가 Pipeline에 더 기댄다 — 번들 Roslyn(`UnityPipeline.Microsoft.CodeAnalysis*`, 토큰 비교·워밍업), `CommandRegistry.DiscoverCommands()`로
    찾은 `reload_file_editor_interpreter`의 인자 이름(`filename`)과 응답 속성(`Success`·`Items`·`Diagnostics`·`Error`·`ErrorDetails`), `CodeReloadRegistry.GetStats/
    ClearAllOverrides`, 교체 본문의 예외 메시지 접두어 `CodeReload:`(루프가 전체 루프로 다시 도는 기준). 올릴 때 selftest 3번의 핫 단계가 이것들을 확인한다.
  - 2026-10-01(W12): 새 메서드 판정이 Pipeline의 규칙을 따라 한다 — 파일당 첫 `[CodeReload]` 클래스만 교체(`InPlaceReloadProcessor`), 그 클래스에서 컴파일된 타입에 없는
    이름의 비제네릭 메서드만 교체 본문과 함께 컴파일(`SourceCodeTransformer.ComputeNewMethodNames`). 인터프리터 호출 수는 내부 `CodeReloadRegistry.m_MethodOverrides`
    (ConcurrentDictionary)의 `MethodOverride.InterpreterInvoke` 델리게이트를 감싸 센다. 규칙이 넓어지면 판정도 넓힐 수 있다(selftest 3번의 `check` 단계가 지금 규칙을 확인).
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
  - 2026-09-30(W6b): 시나리오 동안의 UI 시계(`Runtime/PanelClock.cs`, G3-10)도 내부 API(`BaseVisualElementPanel.TimeSinceStartupFunc`,
    `UIElementsRuntimeUtility.GetSortedPlayerPanels`)에 기댄다. 없으면 `play.uiClock`이 `real` + `error`이고 selftest 1번의 UI 시계 검사가 빨갛다.
  - 2026-09-30(W11): 시나리오 동안 앱 포커스 없이도 입력을 받게 하는 것(`Runtime/PanelFocus.cs`, G3-14)도 내부 필드 `DefaultEventSystem.IsEditorRemoteConnected`다
    (6.0.84f1·6.3.11f1·6.6.3f1에 있음). 없으면 `play.uiFocusError`이고 selftest 1번의 G3-14 검사가 빨갛다.
- [ ] **O-10 `-automated`가 닫은 대화상자가 6.0–6.6에서는 보이지 않는다** (2026-09-30, W7)
  - 현상: `-automated` 에디터의 `EditorUtility.DisplayDialog`는 곧바로 `false`, `DisplayDialogComplex`는 1(취소)을 돌려주고 로그에 아무것도 남기지 않는다.
    Pipeline이 대화상자를 보는 `EditorDialogEvents`는 6.7부터라(`EditorDialogStateMirror`가 `#if UNITY_6000_7_OR_NEWER`) 6.0–6.6에서는 에디터 작업이 "취소"로
    끝난 이유를 알 수 없다. 에디터가 뜨기 전의 창(새로 설치한 버전의 이용 약관)은 `-automated`와 무관하게 여전히 사람을 기다린다(`open.ps1`의 `dialog`).
  - 방향: 6.7 이상에서 Pipeline의 `dialogsDuringExecution`을 loop report에 옮긴다(W7 때는 설치된 6.7이 없어 확인하지 못함). 그 전에는 `DisplayDialog`를
    부르는 에디터 코드를 하네스 명령에서 부르지 않는다.
- [ ] **O-11 창 없는 에디터가 세션에서 처음 그리는 메시를 쓰레기 값으로 그린다** (2026-09-30, W7, 우회함)
  - 현상: `-batchmode` 에디터를 새로 열고 첫 캡처에서 매듭(커스텀 HLSL)이 노랑·흰색, 선돌이 검정이었다(매번 다른 색). 같은 머티리얼의 아치는 정상이라 메시의 첫
    그리기다. 두 번째 그리기는 같은 프레임 안에서도 정상, 하늘만 보는 렌더로 미리 그려도 소용없음, `-force-gfx-mt`로도 같음(6.3 확인).
  - 지금: `HarnessCapture.Render`가 batchmode면 카메라들을 한 번 버리고 다시 그린다 → 매트릭스 6이 창 없는 에디터의 첫 샷들을 기준 이미지와 비교한다.
  - 할 일(상시): 빈 프로젝트로 재현해 Unity에 신고하고, 고쳐진 버전에서 두 번 그리기를 걷어낸다.
- [ ] **O-12 도메인 리로드 뒤 Pipeline의 새 토큰이 ~14 s 늦게 보일 때가 있다** (2026-09-30, W10 측정 중에 드러남)
  - 현상: 전체 루프(빌드·플레이) 사이에 모듈 C# 1줄을 고친 루프가 `compileSec` 18.6–19.6 s다(W7 기준선 ~4.7 s). Tundra 0.35 s·도메인 리로드 ~2.8 s는 그대로이고,
    리로드 뒤 ~14–17 s 동안 모든 요청이 401("Editor token rotated by a domain reload")이다 — 디스크립터(`Library/Pipeline/.unity-pipeline-port`)의 토큰이 새 서버의
    토큰보다 늦게 바뀐다(`WriteToProjectRoot failed` 로그는 없음). `/api/status`도 인증이 필요해서 그것으로 디스크립터를 다시 쓰게 할 수 없다.
  - W10 탓이 아니다(같은 에디터에서 A/B): W9 코드(`cc2eeee`로 되돌림)의 전체 루프 사이 C# 수정 6/6회 18.8–19.6 s, W10 코드 6회 중 2회 18.6–19.0·2회 10.2·2회 5.0 s.
    플레이 없이 `recompile`만 반복하면 W9·W10 코드 모두 12/12회 4.0–4.8 s. 그래서 selftest가 W9 때보다 길다(에디터 트리 520 → 743 s, 새 클론 6.6의 3번 78 → 148 s,
    모든 버전에서). 새로 연 에디터에서도 재현됐고 W9 측정 때는 없었다 — 이 머신의 상태(다른 앱의 부하 등)와 어떻게 맞물리는지는 모른다.
  - 할 일: Pipeline의 토큰(`SecurityTokenManager.GetOrCreateToken`)과 디스크립터 쓰기(`CreateInstanceDescriptor`, 요청마다의 `UpdateHeartBeat`) 시점을 조사해 신고.
    하네스 쪽 우회는 토큰 없이 디스크립터를 다시 쓰게 하는 경로가 있어야 한다(없으면 신고만). 기준선의 "C# 1줄" 행은 이 현상이 없을 때의 값이다.
  - 2026-10-01(W11): 여전하다 — 샘플 selftest 1–8 677–850 s(3번 88–153 s).
  - 2026-10-01(W12): 들쭉날쭉하다 — 에디터 트리 selftest 3번 148.8–150.3 s(같은 날 전체 루프 하나가 24 s, `compileSec` 18.8), 새 클론 6.3·6.0·6.6의 3번은 77–84 s(지연 없음).
- [ ] **O-13 도메인 리로드 뒤 에디터가 다른 창이 앞에 있어도 포커스가 있다고 보고한다** (2026-09-30, W11에서 발견)
  - 현상: 다른 프로세스의 창이 포그라운드인 채 스크립트를 고쳐 도메인 리로드가 일어나면, 그 뒤 `Application.isFocused`(루프의 `fps.editorFocused`)와
    `InternalEditorUtility.isApplicationActive`가 true였다(6.3, 2회: 도우미 창·다른 앱이 앞). 에디터를 한 번 앞으로 가져왔다가 다른 창으로 옮기면 `isFocused`만 false로
    돌아오고 `isApplicationActive`는 true로 남았다. 그동안 Input System도 포커스가 있다고 보고 플레이 진입 때 장치를 끄지 않는다.
  - 영향: `fps.editorFocused`는 프레임 시간을 읽을 때의 참고값이다. 격리(G3-9)와 UI Toolkit 입력(G3-14)은 이제 포커스와 무관하게 같게 돌아 루프 결과는 같다.
  - 할 일(상시): 빈 프로젝트로 재현해 Unity에 신고. 그 전에는 `fps.editorFocused`·`isApplicationActive`를 포커스 판정에 쓰지 않는다(selftest는 포커스를 Unity 안에서 만든다).
- [ ] **O-14 Pipeline `build_status`의 에러에 줄이 없다** (2026-10-01, W16에서 발견, 우회함)
  - 현상: `com.unity.pipeline` 0.8.0-exp.1의 `BuildIssue.From`이 `BuildMessage.Parse`로 `File.cs(line,col): ...`의 파일과 줄을 읽고도 `file`만 남긴다(`errors[]`에
    `line` 없음, `message`는 접두사를 뗀 나머지). 플레이어 빌드에서만 나는 컴파일 에러(사내 프로젝트 A의 `Handheld`)를 줄 없이 보고했다.
  - 우회: `player.ps1`이 같은 응답의 `buildSteps[].messages[]`(원문)를 하네스의 컴파일 메시지 파서로 읽는다(`compileErrors`·`player.build.errors`의 file·line·module).
  - 할 일(상시): Pipeline에 신고(`BuildIssue`에 `line`).

### 검증 매트릭스 (하네스를 고친 뒤 매번)

**1–8은 `tools/selftest.ps1` 한 번**(에디터 트리, 하네스 변경은 임시 커밋 후; O-12가 겹쳐 ~9–14분), **9는 `tools/fresh-clone-test.ps1 -SelfTest`**
(새 클론에서 루프 3회 + 1–8, 지원 버전마다 `-UnityVersion`; 버전당 ~12–17분(O-12 포함)), **10은 `tools/attach-test.ps1`**(기존 프로젝트 클론마다; 0.5–1분).
아래는 각 항목이 검사하는 것이다. 샷 PNG는 여전히 Read로 확인한다.

1. `loop.ps1` 3회 연속 녹색, `build.fingerprint`·`play.events` 동일, PNG를 Read로 확인(selftest: blank·dark·magenta 샷 없음, 모든 샷 1280x720에
   HUD 합성(`ui`) + `compile-check -IncludeHarness`
   + 기준 이미지(G3-4): 루프 1이 임시 폴더에 쓰고(`-UpdateGolden`) 2·3이 픽셀까지 같음(maxDiff 0 — 허용치 안의 `same`이 아니라), 모든 에디터 창을 매 업데이트마다
   다시 그리는 플레이의 루프도 픽셀까지 같음(G3-15: 그 앞에 에디터 GUI가 그리면 DBuffer 데칼 가장자리가 달라졌다), 커밋된 이 버전의 기준 이미지와 같음(있을 때),
   `harness_golden`의 `ignore`(왼쪽 위 기준)와 같은 major.minor의 다른 패치 폴더 대체
   + 시나리오 도구 루프 한 번: `waitScene`·`waitTarget`·UI Toolkit `click`·KeyCode 키 이름·포즈/카메라 캡처
   + uGUI 합성(G3-1, 편집 모드 픽스처: 오버레이·메인 카메라의 Screen Space - Camera·스택 UI 카메라의 캔버스 → 순서, 색 공간 블렌드 오차 ≤ 2, 되돌림)
   + 카메라(G3-7, 편집 모드 픽스처: 메인 카메라 자식인 스택 Overlay 카메라의 쿼드가 메인·다른 포즈 모두 화면 중앙, 미니맵 Base 카메라가 viewport에,
   앞 depth 카메라는 덮임, `"camera"`로 미니맵만 전체 화면, 메인 카메라 위치·다른 카메라 타깃·스택 되돌림, 씬 dirty 아님)
   + 연속 캡처(G3-3, 플레이 중 eval로 만든 오버레이 캔버스·스택 카메라가 2x2 시트의 모든 프레임에, `motion` > 0; G4-5: 하늘만 보는 1초 간격 두 프레임의 `motion` > 0.5 —
   구름이 게임 시간으로 흐름)
   + 실제 입력 격리(G3-6): 플레이 동안 실제 키보드 장치에 스페이스를 넣어도 `play.events` 그대로·`isolatedDevices` 누름 > 0 — 플레이가 포커스 있음으로 시작할 때와
   없음으로 시작할 때(G3-9: Input System이 먼저 꺼 둔 장치를 가져가 `background`로 세고, 20번째 주입 뒤 포커스가 돌아와도 끝까지 셈; 포커스 변화는 Unity 안에서
   `Application.InvokeFocusChanged`로 만들어 OS 포커스와 무관), UI Toolkit의 "포커스 없으면 입력 무시"가 시나리오 동안 꺼지고 뒤에 돌아옴(G3-14), 실패한 플레이·중간에
   멈춘 플레이 뒤에도 실제 장치가 다시 켜짐)
   + 렌더 설정(W4): RP·Renderer 에셋이 생성물이고 루프 2·3은 다시 쓰지 않음, 지우면(그동안 Built-in) 루프 한 번으로 다시 생기고 fingerprint·픽셀·
   `git status`가 같음(G1-1); 프로젝트 설정(G1-5): 설정 스텝이 소유한 값을 루프 2·3은 쓰지 않음, YAML을 손으로 고치고(Mobile 레벨 이름, PC `lodBias`, 레이어 9 이름·레이어 10
   추가) 메모리에서 바꾼 값(PC vSync, 에디터의 레벨 → 파이프라인 전환)을 루프 한 번이 되돌리며 6개 모두 `drift`·경고, 파일과 에디터에 코드 값, fingerprint·픽셀·`git status`
   같음(샘플 버전은 바이트까지, 다른 버전은 그 버전 형식으로 다시 쓴 두 파일을 커밋된 것으로 되돌림); 반사 큐브맵에 잘못된 텍셀이 없고 가장 밝은 텍셀이 태양 방향 2° 안(P-4); 앰비언트 = 생성된 라이팅 데이터의 큐브맵 SH,
   `AmbientProbe`가 균일 환경을 Flat 앰비언트와 같게·쓰레기 텍셀은 거부(G4-2); 하늘·반사(G4-5): 스카이박스가 `Harness/Sky`·플레이 뒤 `_HarnessSkyTime` 0, 반사 프로브가
   Custom 256·박스 투영이고 큐브맵에 잘못된 텍셀이 없으며 아치 꼭대기 방향이 돌(하늘 큐브맵은 하늘), `fingerprint.txt`의 그 큐브맵 줄이 모양(`cubemap 256 RGBAHalf 9`),
   프로브를 켜고 끈 closeup 렌더(편집 모드)에서 받침대 영역 평균 차 > 0.05·잡음의 20배, 매듭 영역은 프로브 없이 하늘을 비춰 더 밝음(> 0.03·잡음의 20배), 루프 2·3이 하늘·
   프로브 큐브맵을 다시 쓰지 않음(`build.cubemapsWritten`)과 매 빌드 프로브 1개(`build.reflectionProbes`); `ctx.Material`이 오타·옛 URP 이름·토글 없는 이미션을 경고,
   `ctx.LitMaterial`이 이미션·알파 클립을 켬(G4-3)
   + 콘텐츠 헬퍼(G1-3): 빌드된 불씨가 고정 시드·`AlwaysSimulate`, Halo가 컨트롤러 없는 `ClipPlayer`로 재생; 편집 모드 픽스처(따로 연 씬, 지움)에서
   경로·컴포넌트·머티리얼 속성·Transform 속성 오타가 각각 경고 한 줄(비슷한 이름 포함), 선형 회전이 1 s에 90°, 파티클이 같은 시드로 두 번 같은 입자,
   가산 `ParticleMaterial`; 플레이 중 런타임 클립 두 개를 받은 `ClipPlayer`가 끝까지 재생·유지(`IsDone`)·이벤트(`ClipEvent:RiseEnd=1`)·크로스페이드(중간 x=2),
   기본 루프의 `ClipEvent:HaloHalfTurn=1`
   + UI 킷(G1-4)·UI 시계(G3-10): 루프 3회의 `play.uiClock`이 `frames`(패널 ≥ 1); 편집 모드에서 빌드된 HUD의 `Gauge`·`ToastStack`·킷 버튼, 테마 변수(`--ah-bg`,
   `--ah-accent`) 해석, 토스트 클래스; 플레이 중 LAPS·SPIN 라벨과 게이지가 HUD의 `dataSource`(`SmokeHudData`) 값과 같음(바인딩), REVERSE 버튼을 이름으로
   클릭(`uitk`) → 방향 전환, 토스트가 페이드 인·아웃하는 중의 캡처 2장이 두 번 돌려도 픽셀까지 같음
   + 플레이어 실행(W8: G3-2·G3-8): `player.ps1`이 기본 시나리오 + 게임 카메라 캡처를 개발 빌드 플레이어에서 — 녹색, 1280x720 창·vSync 0·종료코드 0, 에디터와 같은
   `play.events`, 플레이어의 fps·batches·UI 시계 `frames`, 샷이 에디터 것과 파티클 차이 안(바뀐 픽셀 ≤ 1%, 평균 ≤ 2), 그 프레임의 화면 = 캡처(`screen.vsShot` same,
   워터마크 제외), 빌드 뒤 작업 트리 그대로
   + GPU 베이크(G4-1): 임시 베이크 셰이더로 PNG 첫 줄 = uv.y 0, HLSL `Noise_Fbm`·`Noise_Ridged` = C# `Noise`(8비트 반올림 안), 같은 입력이면 건너뛰고 속성을 바꾸면
   다시 구움, 임포터의 fingerprint 키 + 절차적 라이브러리(G4-4): SDF 구가 닫히고(열린 모서리 0) 정점이 반지름 1.000 위, 부드러운 합집합도 닫힘, 바위·아이코스피어·
   닫힌/열린 튜브의 면 방향 100%, 스플라인 끝점·호 길이 간격 3% 안, 포아송 최소 거리·같은 시드 같은 점; 빌드된 선돌·바위·아치, 룬 데칼과 `DecalRendererFeature`,
   지형 디테일 맵(`_DETAIL_MULX2`, 타일링 80)
2. C# 컴파일 에러 주입 → `stage=compile`, file/line/module 정확 → 원복 후 녹색
3. 런타임 예외 주입 → `stage=runtime`, 정확한 줄 → 원복 후 녹색. 핫 루프(G2-1): `[CodeReload] Tick` 본문 수정 → `loop.ps1 -Hot`이 컴파일·빌드·도메인 리로드
   없이 반영(events 같음, golden `changed`, G2-5: `hot.interpreted`에 Tick의 호출 수 ≥ `play.frames`·시간·프레임 비율) → 같은 변경을 새 헬퍼 2개(인스턴스·static)로
   → 여전히 핫(`newMethods` 둘), 샷이 인라인 변경의 샷과 `same`·events 같음 → `harness_hot check`: 제네릭 새 메서드·오버로드는 핫 아님(그 줄), 아무도 안 부르는 새 메서드는 핫
   → 되돌리면 교체 해제·`same`·`interpreted` 없음 → 필드 추가는 전체 루프(fallback에 그 줄과 `field m_SelftestField`) → 핫 본문의 예외는 전체 루프가 주입한 줄로 보고
4. HLSL 에러 주입 → `stage=shader`, 재임포트 없는 다음 루프에서도 검출 → 원복 후 녹색(그 샷을 이 항목의 기준 이미지로). 셰이더 한 줄(스펙큘러 절반, G3-4)
   → 루프 녹색, golden `changed` + `rect` + diff PNG. 파이프라인이 못 그리는 머티리얼(받침대 → `Standard`, G3-5)
   → 샷 `magenta` + `hint`에 `Smoke/Pedestal` + golden `changed`, 루프는 녹색 → 원복 후 마젠타 없음·golden `same`. 빌더 메시 변경(아치 두께 2배, G3-11)
   → 첫 루프가 이미 새 메시(golden `changed`, 다음 루프와 같은 값) → 원복 후 `same`
5. 리셋 없는 static 추가 → `stage=lint` → 원복. 계약(G5-4, 같은 루프): 모듈 이름이 아닌 계약 파일(`SelftestEvents.cs`), 다른 네임스페이스의 같은 이벤트 이름
   (`SpinnerLap`), Stage가 발행하는데 `StageEvents.cs`가 아닌 곳에 있는 이벤트, Stage도 발행하는 Smoke의 `SpinnerLap` → `contract-file`·`contract-name` 5건이
   각각 맞는 파일·모듈로(그 밖의 계약 이슈 없음)
6. 루프 2개 동시 실행 → 두 번째가 대기 후 성공. 그리고 worktree 전용 에디터(W7): 커밋된 코드의 detached worktree(`<저장소>-st-o`)에 컴파일 에러를 넣고
   `open.ps1 -Own` → `Library/` 사본 + 창 없는·`-automated` 에디터가 그래도 뜸(`compileFailed`) → 그 루프가 주입한 줄로 `stage=compile` → 되돌리면 에디터 트리
   루프와 동시에 둘 다 녹색·대기 0·fingerprint·events 같음·`render` 없음(`fps.note`)·첫 샷들이 그 에디터 트리 루프의 샷과 허용치 안에서 같음(O-11 우회 확인,
   기준 이미지가 없는 버전에서도; 커밋된 기준 이미지도 `changed` 0) → `quit.ps1`로 닫힘
7. worktree 격리(G5-2): 에이전트 worktree 2개. A가 깨진 코드를 `submit.ps1 -SkipCheck` → `stage=compile` + `reverted` + `restore.ok`,
   그 사이 B의 `submit.ps1`은 락 대기 후 녹색. 게이트(`-SkipCheck` 없이)는 에디터 트리를 건드리지 않고 거부. submit 도중 kill →
   다음 `loop.ps1`에 `recoveredSubmit`, 녹색. 끝나면 메인 트리 `git status`로 테스트 사본이 남지 않았는지 확인.
   계약(W9): B의 새 계약 파일은 B 소유(`contracts.json`)이고 게이트가 계약을 쓰는 Smoke·Stage도 컴파일(G5-3); 타입 해시가 주석·줄바꿈에는 같고 필드 타입에는
   다름; A가 올라간 `SpinnerLap`을 바꿈 → `stage=submit` + `contractChanged`; A가 B의 미병합 `ProbeEcho`와 같은 이름을 Smoke 파일에 덧붙임 →
   `contractConflicts`(상대가 미병합임을 보고), 에디터 트리 무변경; A가 B의 미병합 `ProbeEvents.cs`를 자기 내용으로 → `contractOwner` B;
   A가 `Light`를 덧붙임 → 게이트가 `StageModule.cs`의 CS0104(모듈 Stage)로 `stage=compile`; B가 자기 미병합 계약에 필드를 더해 다시 submit → 녹색·`contractsUpdated`.
   프로젝트 설정(W10): B의 새 모듈 설정 스텝이 레이어 20을 선언 → 에디터 트리 `TagManager.asset`이 바뀌고 `settingsWrittenBack`으로 B에 같은 파일; A의 런타임 에러 submit에
   넣은 설정 스텝(레이어 21) → 되돌리며 TagManager 바이트 그대로(설정 기록 `project-settings.json`도), 에디터에서도 레이어 21 없음
8. land(G5-5): 새 모듈을 submit → 커밋 → `land.ps1` 녹색(에디터 트리 `git status` 깨끗, stash 버림, 소유 해제), 그 사이 다른 worktree의
   submit은 락 대기 후 녹색. 컴파일 에러 커밋 land → `stage=compile` + `land.reverted` + `restore.ok`, HEAD·`git status` 동일.
   land를 병합 직후 kill → 다음 `loop.ps1`에 `recoveredLand`, 녹색, HEAD·`git status` 동일. `.meta` 미커밋·충돌 → `stage=land` 거부, 무변경.
   (selftest는 이미 병합됨·미커밋·에디터 트리 직접 수정 거부까지 보고, 끝나면 worktree·`selftest/*` 브랜치·테스트 커밋을 스스로 걷어낸다)
   계약(W9): 첫 land가 모듈과 함께 계약 파일 소유도 해제(`releasedContracts`); A의 브랜치가 올라간 `ProbeEcho`와 같은 이름을 선언 → `stage=land` +
   `contractConflicts`(landed), 올라간 타입을 바꿈 → `contractChanged`, 둘 다 HEAD·`git status` 무변경; A가 Smoke 파일에 새 이벤트를 덧붙여 submit(A 소유) →
   커밋 → land(병합 커밋) 녹색, 소유 해제, 에디터 트리 깨끗. 프로젝트 설정(W10): 되복사한 `TagManager.asset`이 모듈과 함께 land(에디터 트리의 미커밋 사본은 stash로 버려짐).
   여러 worktree의 ProjectSettings(W14, G5-6): A·B가 각자 레이어를 더한 모듈을 submit(A만 되복사, B는 `settingsNotWrittenBack`) → 커밋 → A가 먼저 land(`regenerated`,
   커밋 안 함 — `waitingFor`에 B의 파일, 에디터 트리에 세 레이어·HEAD에 두 레이어) → B land(설정 커밋이 HEAD, 세 레이어, 에디터 트리 깨끗); 레이어를 하나씩 더 해 반대
   순서(B 먼저: 커밋 안 함 → A: 다섯 레이어 커밋); 그 사이 손으로 고친 TagManager(정렬 레이어 `locked`, 루프가 저장한 뒤) → `foreign`(메시지에 `git checkout`), 무변경 →
   `git checkout` + 루프 → A land 녹색·손 편집 없음; 레이어 25를 더한 런타임 에러 브랜치의 land → 루프가 쓴 레이어, `stage=runtime`, `land.undo`에 TagManager·기록 복원,
   TagManager 바이트·에디터의 레이어 20–25·HEAD·`git status` 그대로
9. 새 클론(O-8): `tools/`·`ProjectSettings/`·`Packages/`·`.gitignore`·에디터 시작 코드를 바꿨으면 임시 커밋 후
   `tools/fresh-clone-test.ps1 -SelfTest -ExpectFingerprint <1의 fingerprint>` 녹색(클론이 메인 트리와 같은 fingerprint), `shots/`를 Read로 확인.
   루프 요약의 `golden`: 샘플 버전(6000.3.11f1)은 커밋된 기준 이미지와 `same=3`(새 Library의 첫 임포트도 같은 픽셀), 다른 버전은 `missing`.
   지원 버전(CLAUDE.md "Unity 버전")마다 `-UnityVersion <v>`로도 돌린다(fingerprint는 그 버전의 값)
10. 기존 프로젝트(P-2): 하네스 패키지·설치/제거 스크립트·런타임을 바꿨으면, 기준선 커밋이 있는 테스트 클론마다
   `tools/attach-test.ps1 -Project <클론> [-Scene ...] [-Module ...]` 녹색 — install → 설치분만 바뀜 → 기존 씬으로 루프 3회 녹색(fingerprint·events 동일)
   → (`-Player`: `player.ps1`이 그 시나리오를 개발 빌드 플레이어에서 녹색, W8) → 출시 빌드에 `Harness.*` 없음 → uninstall 뒤 `git status` 비어 있음.
   `shots/`(`-Player`면 `shots-player/`)를 Read로 확인. 지금 쓰는 클론(`../ah-p2`, 기준선 커밋 포함):
   BagelGame(`-Module Game=Assets/Game,UI=Assets/UI`), Fluid-Sim(`-Scene "Assets/Scenes/Fluid Particles.unity"`), 사내 프로젝트 A(비공개 클론, 이 머신에만;
   `-Scenario ../../ah-p2/brd-attach.json`(부트 대화상자까지; 경로는 `AgentHarness/` 기준) 또는 `brd-lobby-auto.json`(테스트 서버로 로비까지, `"auto"`·`"screen"` 나란히; `brd-lobby-w3.json`은
   여기에 로비 연속 캡처를 더한 것 — 끝나면 에디터
   PlayerPrefs `dev.force_login.server_environment`를 0으로), `-KnownErrors '^\[Firebase\] Dependency'`, `-NoBuild`; 클론의 실제 타깃 Android 그대로).
   사내 프로젝트 A의 `-Player`·출시 빌드(W16)는 클론을 Standalone으로 바꾸고 Windows 빌드용 테스트 커밋(`Handheld`의 `#if`, Standalone define `DEV`)을 얹어서만 된다 —
   `brd-attach-player.json`(부트 대화상자), 로비는 `brd-lobby-player.json`(에디터 서버 선택을 Dev로 둔 뒤, 플레이어에는 서버 버튼이 없다). 배포 경로를 바꿨으면
   `-Source git+file:///<저장소>?path=/AgentHarness/Packages/com.geuneda.agentharness#<브랜치>`(커밋된 것, 부트스트랩 포함)로도.

---

## 해결됨

(해결한 항목을 여기로 옮기고 날짜, 방법, 검증 결과, 측정값을 적는다.)

- [x] **G3-8 에디터 캡처의 UI가 게임의 화면 크기 코드와 어긋날 수 있다**(사내 프로젝트 A 확인) · **G3-16 오버레이 캔버스의 TextMesh Pro 글자를 캡처가 더 날카롭게
  그렸다** (2026-10-01, W16; 경로는 W8)
  - 현상(전): W8의 플레이어 경로(실제 화면 = 같은 프레임 캡처 비교)는 샘플·BagelGame에서만 확인됐다. 완료 기준인 사내 프로젝트 A(uGUI·TMP·Addressables·Firebase,
    세로 720x1280)는 활성 타깃이 Android라 `player.ps1`이 빌드 전에 거부했다.
  - 절차(사람의 결정, 위 W16): 클론을 배치 모드 `Unity -batchmode -quit -projectPath <클론> -buildTarget Win64`로 전환(101 s — 텍스처 ~3,300개 재임포트, Library 5.3 GB;
    추적 파일 변경 없음) → `attach-test.ps1 -Player`·`player.ps1` → 끝나면 `-buildTarget Android`로 되돌림.
  - 찾은 것과 고친 것:
    1. **에디터는 컴파일되는데 Windows 플레이어는 컴파일되지 않았다**: `VibrationService.cs:35`의 `Handheld.Vibrate()`가 `Application.isMobilePlatform` 런타임 검사만
       받았다(`Handheld`는 모바일 플레이어에만 있다). report는 `stage=playerBuild`에 `error CS0103`과 파일만 줬다 — Pipeline `build_status`의 에러가 줄을 버린다(O-14).
       → `player.ps1`이 같은 응답의 빌드 단계 메시지(원문)를 하네스의 컴파일 메시지 파서로 읽어 `player.build.errors[]`·`compileErrors[]`(kind `player`)에 file·line·module,
       `error`에 "에디터는 컴파일했지만 플레이어 타깃에 없는 API(`#if UNITY_EDITOR` 없는 UnityEditor, `#if UNITY_ANDROID || UNITY_IOS` 없는 Handheld — 런타임 검사로는 안 됨)".
       `attach-test.ps1`의 `player.compileErrors`에도. 실패한 빌드까지 35 s(빌드 38.9 s). 클론에는 테스트 커밋으로 `#if`를 넣었다.
    2. **G3-16**: 패치 뒤 첫 개발 빌드 211 s·426 MB, 부트 대화상자 샷의 실제 화면과 캡처가 "0%" 로딩 글자에서만 달랐다(96픽셀 = 0.0104%, 채널 차이 최대 69, 평균 0.09 —
       기준 이미지 규칙으로 `changed`). 정수 이동은 없고 캡처의 글자 가장자리가 ~2배 날카로웠다(화면 81·119·39 → 캡처 62·129·0). TMP는 글자마다 SDF 스케일(uv0.w)을
       캔버스 렌더 모드별로 넣는다(`TextMeshProUGUI.GenerateTextMesh`: Overlay `lossyScale / scaleFactor`, Camera `lossyScale`)는데, 캡처가 오버레이 캔버스를 Screen Space -
       Camera(1단위 = 1픽셀인 UI 카메라)로 바꿔도 lossyScale이 그대로라(그리고 TMP는 20% 넘는 변화만 따른다) 메시를 다시 만들지 않았다 → 기준 해상도 1440x3040, match 0.5,
       720x1280에서 scaleFactor 0.46 → 1/0.46배 날카로움. 에디터 캡처도 같았다(그래서 에디터·플레이어 캡처끼리는 같았다). → `CaptureUi`가 오버레이였던 캔버스 중
       scaleFactor가 1이 아닌 것의 `TextMeshProUGUI`를 Camera로 바꾼 뒤와 되돌린 뒤 `ForceMeshUpdate`(이름으로 찾음, TMP 의존 없음) → 실제 화면 = 캡처(바뀐 픽셀 0, maxDiff 10,
       평균 0.084). 정점을 매 프레임 직접 움직이는 TMP 연출(`OnPreRenderText`를 쓰지 않는 것)은 그 프레임에 풀린다(다음 프레임에 돌아옴).
    3. **에디터 샷과의 차이는 게임 쪽이었다**: 플레이어 샷 vs 에디터 샷 14–15% — 강제 로그인 대화상자의 서버 선택 줄이 게임 코드의 `#if UNITY_EDITOR` 안이라 개발 빌드에서는
       감춰진다. report의 `compare.note`는 "첫 씬 파티클"만 말했다. → 실제 화면 비교로 원인을 가른다: `shotStats[].vsEditor.cause` = `game`(그 프레임의 플레이어 화면이 캡처와
       같음 → 에디터·플레이어에서 게임이 다르게 그림: `#if UNITY_EDITOR`·`Application.isEditor`, 플랫폼 `#if`, `OnValidate` 데이터, `Screen.width` 기반 배치(에디터의 Screen은 Game 뷰),
       실시간 연출, 첫 씬 파티클) 또는 `capture`(캡처가 그 프레임의 화면과 다름 → 하네스 캡처 경로, `.screen.png`가 게임이 보인 것), `compare.note`에 샷 이름과 함께.
    4. 게임이 첫 씬 뒤에 `Application.targetFrameRate = 60`을 다시 걸어 플레이어 fps가 60(×1.0)이었다 → `fps.note`("게임이 직접 상한을 걸었다: fps는 그 상한").
    5. 플레이어가 작업 폴더(프로젝트 루트)에 Facebook SDK 로그 `fbg.log`를 썼다 → 플레이어를 출력 폴더(`<Out>/player`)에서 띄운다.
    6. 빌드가 Addressables의 `Assets/AddressableAssetsData/Windows/`(gitignore된 content state)와 그 `.meta`를 만들었다. attach-test는 `.meta`만 지우고 폴더를 남겨 다음 에디터
       시작이 `.meta`를 다시 썼다(`stage=status`) → 추적 파일이 없는 그 폴더도 지운다.
    7. 클론의 Standalone 스크립팅 define에 `DEV`가 없어(Android에는 있음) 플레이어는 라이브 서버 환경이었다(로그인 전, 대화상자에서 멈춤 — 통신 없음). 테스트 커밋에 `DEV`를
       더했다. 활성 타깃이 바뀌면 에디터 루프의 fingerprint도 바뀐다(Android `6664b723` → Standalone `dad22744` — 에셋 의존 해시가 플랫폼별 임포트를 따른다; 루프끼리는 같음).
  - 로비(`brd-lobby-player.json` = `brd-lobby-auto.json`에서 플레이어에 없는 서버 버튼 클릭을 뺀 것 + 에디터 PlayerPrefs 서버 선택을 Dev로 둔 뒤, 끝나고 0으로): 4장
    (로그인 t=0.05, 타이틀 0.55, 로비 1.8, 로비 3.3) **모두 실제 화면 = 캡처**(바뀐 픽셀 0, maxDiff 10–25), `eventsMatch` 참, 대기 3개(스킵 버튼 2.2 s, 타이틀 3.2 s, 로비 씬 0.35 s).
    에디터 샷과는 14.7%(서버 선택 줄)·8.3%(타이틀 캐릭터·로고 연출)·7.7%(로비 좌우 아이콘 열 ~140 px — 게임의 실시간(unscaled) 등장 트윈이 에디터와 다른 지점)·0.88%
    (회전 빛줄기·남은 시간 글자) — 모두 `cause` `game`. 한 바퀴 123 s(에디터 21.8 + 증분 빌드 83.4(define 변경으로 스크립트 전체) + 플레이어 15.2 + 비교 1.1), 플레이어 fps 55.1
    (상한 60, 로딩 히치 4). 하네스 런타임만 바뀐 증분 빌드 38.8 s, `-NoBuild` 부트 한 바퀴 ~35 s.
  - 부수 효과(보고): 개발 서버 로비 한 번에 게임의 차팅 동기화가 서버에 없는 테이블의 캐시를 지웠다 — 에디터 PlayerPrefs `charting.hash.*` 138개와
    `%USERPROFILE%/AppData/LocalLow/TeamSparta/BunkerTapTapDefense/Charting`의 파일(원본 에디터와 공유; 다음 부팅에 다시 받는다 — 해시만 되살리면 파일과 어긋나서 두었다).
    플레이어 PlayerPrefs `HKCU\Software\TeamSparta\BunkerTapTapDefense`(23개)가 새로 생겼다.
  - 검증: 사내 프로젝트 A(Standalone + 테스트 커밋) `attach-test.ps1 -Player` 녹색 201.5 s — 루프 3회 `dad22744`, 플레이어 부트 2장 실제 화면 = 캡처, 출시 빌드 46.5 s·DLL 225개에
    `Harness.*` 없음(`Unity.Pipeline.Attributes`는 남음 — 그 게임이 설치 전부터 Pipeline 0.6을 쓴다), 제거 뒤 `git status` 깨끗(빌드가 만든 `AddressableAssetsData/Windows/` 포함).
    매트릭스 1–8(에디터 트리) 녹색 813.8 s(`609b54d2…`, 줄 64/71/77/99; 1번 236.4 s — 플레이어 465.5 fps vs 에디터 115.5(×4.03), 하네스 런타임이 바뀐 첫 빌드 130.6 s,
    `"main"` 샷 `vsEditor.cause` game(파티클), 실제 화면 = 캡처 평균 0.49). 9: 새 클론 6.3 968.4 s(루프 3회 `609b54d2`·기준 이미지 same=3, selftest 1–8), 6.0 783.5 s(`a0df2fa8`),
    6.6 1070.3 s(`cadaeca6`) 녹색. 10: BagelGame `-Player` 100.8 s(플레이어 361.6 fps vs 115.0 ×3.14, 실제 화면 = 캡처 3/3, 에디터 차이 최대 0.056% → `cause` game, 출시 빌드에 `Harness.*` 없음),
    Fluid-Sim 30.4 s, 사내 프로젝트 A(Android로 되돌린 원래 HEAD, `brd-attach.json` `-NoBuild`) 98.6 s(`6664b723` — W14·W15와 같음) 녹색. 매트릭스 뒤 `compare.note`의 샷 표기만
    "이름 (t=…)"로 바꿨다(BagelGame의 같은 이름 샷 세 장이 "main, main, main") — 샘플 `player.ps1 -NoBuild`로 확인. selftest에 TMP 검사는 없다(샘플에 TMP 폰트 에셋이 없음 —
    사내 프로젝트 A의 실제 화면 비교로 확인).
  - 남은 것: 정점 연출 TMP의 그 프레임(위 2), 원근 UI 카메라로 기울여 그린 캔버스(G3-8 (3), 해당 게임 없음), Windows만(P-3).

- [x] **G4-5 하늘과 반사가 아직 기본이다** (2026-10-01, W15)
  - 현상(전): 하늘은 Unity 내장 `Skybox/Procedural`을 코드로 설정한 것(구름 없음)이고, 반사·앰비언트는 하늘 큐브맵 하나라 씬 오브젝트(선돌·아치)가 금속 매듭·링·받침대에
    비치지 않았다. 하늘 큐브맵의 mip은 박스 필터(거친 면에서 면 경계가 보일 수 있음)였다.
  - 방법:
    - 하늘(`Shaders/HarnessSky.shader` "Harness/Sky", `ctx.Sky(sun, s => …)`, `SkySettings`): 지평선 안개가 높이에 따라 지수로 옅어지는 그라디언트, 지평선 아래 땅색, 태양 원반
      (각반지름, HDR 세기)·빛무리, `HarnessNoise.hlsl`의 `Noise_Perlin`으로 만든 fBm 구름 — 하늘 위 평면(`d.xz / (d.y + 0.12)`)에 투영, 태양 쪽 밀도로 그늘·밝은 가장자리,
      태양 근처 은빛 테두리, 먼 구름은 지평선 색으로 흐려짐. 픽셀보다 작아진 옥타브는 `fwidth`로 지운다. CGPROGRAM + UnityCG라 파이프라인 무관.
    - 구름 시계: 전역 `_HarnessSkyTime` — `Runtime/SkyClock.cs`(`ctx.Sky`가 `<Module>/Sky`에 둠)가 매 `Update`에 `Time.timeSinceLevelLoad`, `OnDisable`에 0. URP의 `_Time`은
      편집 모드에서 실시간이라 빌드가 찍는 하늘 큐브맵이 매번 달라질 것이어서 쓰지 않았다.
    - 큐브 렌더 공통(`BuildContext.Environment.cs`의 `RenderEnvironment`): 큐브 RenderTexture에 `RenderToCubemap` 두 번(아래 두 번째 발견, O-11) → `GenerateMips` →
      mip 1+를 `Hidden/Harness/CubeFilter`로 거름(mip m의 거칠기 = URP `PerceptualRoughnessToMipmapLevel`의 역, GGX 중요도 표본 256개 고정 Hammersley + 원본 mip에서 거른
      표본, 면·mip마다 큐브 RT의 그 면에 그리기) → 모든 면·mip을 `AsyncGPUReadback`(한꺼번에 요청) → `WriteCubemap`: 에셋의 픽셀과 같으면 쓰지 않음(`SequenceEqual`).
      `BakeSkyReflection`도 이 경로로 바뀌었다(전: mip 0만 읽고 `Apply(true)` 박스 mip, 매 빌드 씀).
    - 반사 프로브(`ctx.ReflectionProbe(path, size, resolution = 128)`): `<Module>/<path>`에 Custom 프로브(HDR, 박스 투영)와 `Reflection_<path>.asset` 자리(없으면 검은 큐브로
      만들어 씬이 참조) → `HarnessBuild`가 씬 저장·라이팅 데이터 뒤 `RenderReflectionProbes`(새 빌드 단계 `phases.probes`): Reflection Probe Static이 아닌 렌더러는
      `forceRenderingOff`, 모든 프로브 `enabled = false` + `ReflectionProbe.UpdateCachedState()`(아래 첫 발견), 프로브 자리·near/far·컬링 마스크·클리어로 렌더, 되돌림.
      report: `build.reflectionProbes`, `build.cubemapsWritten`.
    - fingerprint(`SceneFingerprint.HashAsset`): Cubemap 에셋은 `cubemap <크기> <형식> <mip 수>`만(GPU 결과; 하늘 큐브맵은 원래 RenderSettings 참조만이었고, 프로브 컴포넌트의
      참조를 따라가면 픽셀 바이트를 직렬화 속성으로 훑을 뻔했다).
    - 샘플: `StageEnvironmentStep`이 `ctx.Sky`(구름 0.45, 바람 (0.02, 0.008)); `SmokeBuildStep`의 받침대를 강철(BaseColor 0.78, Metallic 0.92, Smoothness 0.88)·`isStatic`,
      매듭 중심에 256² 프로브(박스 19×12.4×19, 지면에 맞춘 `center`); 매듭 셰이더(`SmokeIridescent.shader`)에 `_Smoothness`·`_Reflectivity`와
      `GlossyEnvironmentReflection` + 슐릭 프레넬, Forward+ 키워드(`#if UNITY_VERSION >= 60010000` `_CLUSTER_LIGHT_LOOP`, 아니면 `_FORWARD_PLUS`)와 프로브 블렌딩·박스 투영.
  - 하다가 찾은 것:
    - **같은 에디터 프레임 안에서 끈 프로브를 렌더가 계속 쓴다**: 프로브를 바꾼 뒤 루프마다 큐브맵이 다시 쓰였다(받침대 재질 하나 → 2–3 빌드에 걸쳐 수렴, 측면 면들까지).
      eval로 가름: 큐브의 픽셀을 그 자리에서 바꾸면(같은 프레임) 결과가 바뀌고, 프로브 `enabled = false`·GameObject 끄기·텍스처 null·크기 0·멀리 옮기기는 같은 프레임에서
      아무 효과가 없었고, 앞 프레임부터 꺼 둔 프로브는 빠졌다 → 컬링이 프레임 시작 때의 프로브를 본다. 빌드는 한 프레임이라 앞 빌드 씬의 프로브(같은 큐브맵 에셋)가 비쳤다.
      `ReflectionProbe.UpdateCachedState()`로 같은 프레임에서 반영(끈 뒤·되돌린 뒤) → 바꾼 뒤 첫 빌드만 쓰고 다음 루프는 그대로.
    - **카메라의 첫 렌더가 앞 카메라의 상태를 이어받는다**: 컴파일 뒤 빌드에서 하늘 큐브맵이 다시 쓰였고, eval의 같은 프레임 첫 하늘 렌더와 둘째가 태양 원반 가장자리
      텍셀 8개·구름 가장자리 2개에서 달랐다(half 1–6단계). 같은 near/far(1000/0.3)의 카메라를 먼저 그리면 같았고, 다른 near/far(600/0.1, 50/0.3)면 달랐다 → 같은 카메라로
      두 번 그려 둘째를 쓴다(창 없는 에디터의 첫 메시 그리기 O-11 우회를 모든 모드로). URP 쪽 신고(상시).
    - 큐브 비교를 `NativeArray` 인덱서로 하면 256² 프로브(4 MB)에서 프로브 단계가 ~110 ms였다 → `SequenceEqual`로 ~40 ms.
    - 손으로 쓴 셰이더(매듭)는 Forward+에서 키워드 없이는 하늘만 비춘다(URP는 블렌딩 켠 Forward+에서 오브젝트별 프로브를 주지 않음 — `GetPerObjectLightFlags`).
    - **Unity 6의 `UNITY_VERSION`은 6000.3.11f1 = 60030011, 6000.0.84f1 = 60000084**(문서의 2020.3.0 = 202030 형식이 아님; `ShaderUtil.CreateShaderAsset`으로 만든 셰이더가
      값을 렌더해 읽음). 처음 쓴 `#if UNITY_VERSION >= 600010`이 6.0에서도 참이라 6.0 새 클론의 매듭이 `_CLUSTER_LIGHT_LOOP`(URP 17.0에 없음)로 프로브를 받지 못했다 —
      selftest 1번의 매듭 검사가 잡음(+0.0004, 받침대는 0.275로 정상) → `>= 60010000`.
  - 하지 않은 것: 실시간 프로브·SSR(움직이는 매듭은 프로브에 없다 — 비치려면 매 프레임 렌더가 필요), 구름 그림자(지면), 태양을 런타임에 돌릴 때 하늘이 따라가기(빌드 때
    방향 — 머티리얼 값을 게임이 바꾼다), 프로브를 빌드 캐시로 건너뛰기(키가 씬 전체라 렌더 ~40 ms를 매번 함; 쓰기만 건너뜀).
  - 검증(이 머신, 6.3 창 에디터):
    - 샷: 구름 하늘(horizon), 강철 받침대에 지면·선돌·룬 원의 빛, 매듭에 구름·지면(프로브 유무 비교: 없으면 받침대가 하늘만 비춰 하얗다). 프로브 큐브맵을 펼쳐 보면 아치·선돌·
      지형·산·구름 하늘·받침대 윗면과 룬 원(매듭·링 없음).
    - 결정성: 루프 3회 maxDiff 0(창을 매 업데이트 다시 그리는 루프도), `cubemapsWritten` 루프 2·3 없음; 컴파일(주석 한 줄)·리로드 뒤에도 하늘·프로브 큐브맵 그대로;
      fingerprint `609b54d2`(루프·selftest·새 클론 같음). 새 클론(6.3)은 루프 1이 셰이더 변형을 처음 컴파일하며(프로브 단계 6.1 s) 하늘·프로브 큐브맵이 다음 빌드와
      조금 달라(샷이 커밋된 기준 이미지와 maxDiff 2–3, 평균 0) 루프 2가 한 번 더 썼고, 루프 2·3은 에디터 트리의 기준 이미지와 픽셀까지 같았다(maxDiff 0). 에디터 트리에서
      `Library/ShaderCache*`만 지우거나 큐브맵·RP 에셋을 지워서는 재현되지 않았다(원인 미상, 결과에는 영향 없음 — 다음 빌드가 맞춘다).
    - 매트릭스: 1–8 녹색 668.1 s(1번 112.4 s, 셰이더 표식 99행, 플레이어 493 fps vs 에디터 124 ×3.97, `vsEditor` 0.6%/1.39, 화면 = 캡처). 9: 새 클론 6000.3.11f1 녹색 869.4 s(selftest 767.8 s, `609b54d2` = 에디터 트리, 커밋된 기준 이미지와 루프 2·3 maxDiff 0;
      임계값 수정 전 커밋 — 6.3에서는 같은 분기), 6000.0.84f1 처음엔 1번 빨강(매듭 +0.0004 — `UNITY_VERSION`, 위) → 고친 뒤 녹색 675.1 s(`a0df2fa8`, 매듭 `_FORWARD_PLUS` +0.152,
      플레이어는 전처럼 건너뜀), 6000.6.3f1 녹색 924.3 s(`cadaeca6`, 플레이어 408 fps, 루프 1만 큐브맵을 씀); 세 버전 샷 81.8/66.1/50.6. 10: BagelGame 녹색 49.0 s(출시 빌드
      `Managed/` 132개·`Harness.*` 0개), Fluid-Sim 녹색 28.2 s(103개·0개), 사내 프로젝트 A 녹색 82.1 s(`brd-attach.json`, `-NoBuild`).
    - 비용: "기준선"의 W15 열(빌드 0.67–0.76 s, 프로브 단계 36–56 ms, 하늘 큐브 ~13–16 ms; 프로브 큐브맵 에셋 8.4 MB — 생성물, gitignore).

- [x] **G5-6 ProjectSettings 파일은 여러 모듈의 설정 스텝이 같이 쓴다** (2026-10-01, W14)
  - 현상(전): 설정 스텝의 값은 모듈 코드에 있지만 그 값이 적히는 YAML(`TagManager.asset` 등)은 파일 하나다. submit은 에디터 트리 사본과 worktree 사본이 둘 다
    올라간 내용일 때만 되복사하므로, 두 worktree의 미병합 모듈이 같은 파일을 바꾸면(둘 다 레이어 추가) 둘째는 되복사되지 않고(`settingsNotWrittenBack`) 에디터 트리에
    미커밋 변경이 남았다 → 그 파일을 바꾼 브랜치의 land는 `foreign`으로 거부, 아니면 마지막 land 뒤에도 미커밋으로 남았다(안내는 "merge 뒤 다시 submit하거나 `-Own`").
    빨간 land의 되돌림은 루프가 쓴 ProjectSettings를 되돌리지 않았다.
  - 방향 정하기: ROADMAP의 첫 안("마지막 빌드가 남긴 내용과 같은 파일 = 하네스가 쓴 것")은 손 편집을 가리지 못한다 — 이름 붙이지 않은 값을 손으로 고친 뒤 루프가 한 번
    돌면 그 내용이 곧 빌드의 출력이다. 그래서 내용 하나가 아니라 **빌드만 거쳐 지금 내용에 이른 내용들**을 기록하고, 커밋된 내용이 그 안에 있는지로 판정했다.
    submit 쪽 안(이 worktree의 코드만으로 만든 파일을 되복사)은 `-Own` 에디터가 있어야 하고 submit 순서에 기대서 하지 않았다.
  - 방법:
    - 기록(`SettingsContext.SaveSnapshot`, `Library/Harness/project-settings.json`의 `files`): 설정 실행(빌드·`harness_setup`)마다 ProjectSettings/*.asset 각각의 git
      blob id 목록(텍스트는 LF로 — `git hash-object`와 같은 값, 끝이 지금 내용, 최대 64개). 실행이 찾은 내용이 앞 실행이 남긴 것이면 이어 붙이고, 아니면(YAML 손 편집,
      다른 도구) 찾은 내용부터 다시 시작한다 — 단 이번 실행이 앞 실행이 남긴 내용으로 되돌렸으면(소유한 값만 고친 드리프트) 이어진다. 에디터가 저장하지 않은 변경을
      가진 파일(`EditorUtility.IsDirty`)은 찾은 내용을 모르는 것으로 친다(이번 실행의 저장이 그 변경을 같이 쓰므로). id는 파일 길이·수정 시각이 같으면 다시 읽지 않는다
      (25개 파일을 두 번 읽는 데 ~5 ms였다).
    - land(`Get-HarnessDerivedSettings`, `Get-HarnessModifiedSettings`): 미커밋 ProjectSettings 파일의 지금 내용이 기록의 끝이고 병합 전 커밋의 내용이 기록 안에 있으면
      = 커밋된 파일 + 설정 스텝이 쓴 것 → `foreign`이 아니다. 병합이 그 파일을 바꾸면 stash에 넣고(`land.settings.regenerated`) 병합 뒤 루프가 병합된 코드와 아직
      미병합인 submit으로 다시 쓴다(stash 버리기의 "예상 밖 변경"에서도 뺀다).
    - 녹색 land 뒤(`Save-LandedSettings`): HEAD 기준으로 다시 판정한 산출물 파일(`land.settings.derived`)을, 모듈·계약 폴더에 다른 미커밋 파일이 없고 그 밖의 코드
      (`.cs`·asmdef·asmref·rsp·dll·`Packages/manifest.json`)도 미커밋이 아니면 land 위에 커밋한다("Project settings the landed code's settings steps write (after landing
      <branch>)", `land.settings.commit`, `land.head.after`). 아니면 미커밋으로 두고 `waitingFor`(최대 10개)·`note` — 그 submit까지 land한 마지막 land가 커밋한다.
      다른 Unity 버전의 `ProjectVersion.txt`·`packages-lock.json`이나 그 버전이 다시 쓴 ProjectSettings(6.6: ProjectSettings 4개 + 새 파일 2개)는 막지 않는다(그 파일들은 산출물로도 안 잡힘).
    - 빨간 land: 저널에 ProjectSettings/*.asset과 기록의 사본(`Library/Harness/land/<runId>.settings`) → 되돌림이 stash 복원 뒤 바뀐 파일을 돌려놓는다(`land.undo`의
      `restored …`, 죽은 land의 복구도 같은 경로). submit의 백업에도 기록 파일을 더했다(빨간 submit이 파일과 기록을 같이 되돌림 — 기록이 앞서 가 있으면 다음 land에서
      산출물을 손 편집으로 오판했을 것).
    - 안내: submit `settingsNote`("Nothing to do: commit the code; land.ps1 …"), land의 `foreign`·`conflicts` 메시지에 ProjectSettings 파일이면 할 일(`git checkout` + 루프,
      master 쪽 받기).
  - 하지 않은 것: 두 브랜치가 같은 ProjectSettings 파일을 각자 커밋한 git 충돌(예: `-Own` 에디터의 루프가 쓴 파일을 커밋)은 자동으로 풀지 않는다 — 브랜치 파일에 사람의
    편집이 섞였는지 에디터 트리의 기록으로는 모른다. "그 파일에 값을 둔 모듈만 미병합이면 기다림"까지 좁히지 않았다(미병합 버전이 선언을 지운 경우 커밋된 코드의 값과
    달라진다) → 모듈·코드에 미병합이 하나라도 있으면 기다린다.
  - 검증(이 머신, 6.3 창 에디터):
    - 기록의 id = `git ls-tree`의 blob id(TagManager·QualitySettings·ProjectSettings 확인). 루프의 `settings` 단계 20.5–20.7 ms(id 캐시 전 26.7 ms, W10 16–17.5 ms),
      코드 변경 없는 루프 3.68–3.89 s(기준선과 같음), fingerprint 그대로(`896e67fc`).
    - selftest 7·8만(첫 시도 녹색, 312.5 s): 8m–8o의 submit 11.8–13.6 s(A `settingsWrittenBack`, B `settingsNotWrittenBack`), land 5.7–6.4 s(검사 0.7–1.3 s, 병합 0.4–0.6 s),
      손 편집 거부 1.4 s, 빨간 land 30.8 s(복원 18.2 s — 컴파일 에러 land 8e의 18.9 s와 같은 재컴파일).
    - 매트릭스(커밋 `W14 wip`): 샘플 selftest 1–8 녹색 694.6 s(`896e67fc`, 줄 64/71/77/87; 1번 127.5 s — `goldenMaxDiff` 0·다시 그리기 루프 181회 maxDiff 0·커밋된 기준 이미지
      3/3 같음, 7번 75.6 s, 8번 178.3 s).
      9: 새 클론 6.3 녹색 804.5 s(selftest 711.1 s, 8번 172.2 s; 루프 3회 `896e67fc`·기준 이미지 same=3, `git status` 깨끗), 6.0 녹색 631.9 s(selftest 544.3 s, 8번 164.3 s;
      `8acf308c`), 6.6 녹색 840.9 s(selftest 732.1 s, 8번 164.4 s; `6b943b54`) — 두 버전 모두 `git status`는 버전 전환 파일뿐이고 그 미커밋 파일들이 있는 채로 8번의 설정
      커밋이 녹색이었고, land 보고에서 산출물로 잡힌 파일은 `TagManager.asset` 하나뿐이었다(그 뒤 selftest가 이것을 "정확히 하나"로 검사 — 그 검사로 에디터 트리에서 7·8 다시 녹색 234.5 s, 8번 158.3 s).
      10: BagelGame 녹색 46.5 s(루프 3회 `619be553`, 출시 빌드 `Managed/` 132개·`Harness.*` 0개), Fluid-Sim 녹색 26.4 s(`54880f05`, 103개·0개), 사내 프로젝트 A 녹색
      75.9 s(`brd-attach.json`, `6664b723`) — 셋 다 W13과 같은 fingerprint(attach 프로젝트는 설정 스텝이 돌지 않아 기록도 없음), 제거 뒤 `git status` 비어 있음.
  - 남은 것: 미병합 submit을 land하지 않고 버리면 그 submit의 코드처럼 그동안 다시 쓴 설정도 에디터 트리에 미커밋으로 남는다(그 submit을 걷어낼 때 같이 정리).
    같은 파일을 각자 커밋한 두 브랜치의 충돌은 사람(에이전트)이 master 쪽으로 푼다.

- [x] **G3-15 같은 코드의 루프끼리 closeup 샷의 픽셀 6–7개가 두 값 중 하나였다** (2026-10-01, W13 — 원인은 URP DBuffer 데칼, 신고는 상시)
  - 현상(W12): 기본 시나리오 `shot0_closeup`의 받침대 오른쪽 아래 — 룬 데칼 원이 받침대 모서리 너머 지면에 닿는 곳, x 907–950·y 666–676 — 의 픽셀 6–7개가 실행마다
    (10,99,127) ↔ (23,69,80) 중 하나(채널 차이 47, ~0.0008%라 판정은 `same`). 6.3·6.0 새 클론에도.
  - 재현율(이 머신, 6.3 창 에디터, 수정 전): 그냥 루프 1/15. 편집 모드에서 한 eval 안에 closeup을 15번 그리면 첫 렌더만 B(23,69,80 쪽), 나머지는 A(기준 이미지 쪽)
    — 에디터 프레임마다 그랬다.
  - 좁히기(편집 모드 → 플레이 모드): 데칼을 끄면 그 자리에 차이 없음, SMAA를 끄면 같은 픽셀이 2–3단계만 다름 → 흔들림은 데칼, SMAA는 키울 뿐. 앞선 렌더로
    "예열"되는 조건: 같은 프레임에 캡처 렌더(64x36·컬링 마스크 0·데칼·후처리 없이도)가 있었으면 A, 메인 카메라·Scene 뷰 카메라·빈 카메라의 렌더 요청은 예열하지 않음,
    수동 렌더도 `ReadPixels`·`AsyncGPUReadback`·`GL.Flush`가 뒤따르면 예열, `GL.Flush`나 대기만으로는 아님. 플레이 중 캡처 시점의 렌더 그래프 텍스처 풀에는
    매번 1280x720 텍스처가 없었다(B가 나온 실행도) → 풀이 아니다. **모든 에디터 창을 매 에디터 업데이트마다 다시 그리게 하면 10/10 B**(GPU 부하만 주면 1/4 —
    평소 수준) → 계기는 에디터 GUI 그리기. 그 부하에서 설정별로 3–4회: `copyDepthMode=ForcePrepass`·`intermediateTextureMode=Always`·SSAO 끔 → 여전히 B,
    **ScreenSpace 데칼 → 0회**(그 자리 값이 늘 같음). `UNITY_HDR_ON`·`GL.sRGBWrite`도 아니었다(에디터 GUI 뒤에도 같은 값).
  - 방법: 샘플 렌더 설정 스텝이 `DecalRendererFeature`를 ScreenSpace로(`SettingsContext.Set(decals, "m_Settings.technique", 2)` — 그 enum은 internal). 룬 원의 모양은
    같고 가장자리 0.009–0.013% 픽셀만 바뀌어 6.3 기준 이미지 3장을 갱신했다. 6.3 fingerprint `1d7568ed` → `896e67fc`(렌더러 설정값이 fingerprint에 들어간다).
  - 하다가 찾은 것: URP 17.3의 ScreenSpace 데칼 패스는 중간 텍스처 없는 카메라에서 `RenderingUtils.SetScaleBiasRt` NullReferenceException(“Render Graph Execution
    error”)을 낸다 — 캡처의 uGUI 레이어를 그리는 숨은 UI 카메라(후처리·HDR·MSAA·깊이/불투명 텍스처 없음)가 그랬고, selftest 1번의 uGUI 합성(빨강·파랑 캔버스가 빠짐)과
    연속 캡처(`stage=runtime`)가 빨갰다. → `CaptureUi.RenderWithoutFeatures`: 그 카메라의 렌더러 feature를 그 렌더 동안 `SetActive(false)`(필드 하나 — 에셋을 dirty로
    만들지 않음), 끝나면 켠다. 게임도 오버레이 캔버스를 카메라 뒤에 feature 없이 그리므로 더 게임과 같다(전체 화면 feature가 UI 레이어를 덮지 않음).
  - 해 보고 하지 않은 것: 캡처 직전에 같은 카메라를 한 번 그려 버리고 `GL.Flush`(창 없는 에디터의 O-11 우회를 모든 모드로) — 편집 모드 실험으로는 맞았지만 플레이 중
    GUI 부하에서 10번 중 6번 B였고, 렌더 횟수가 바뀌어 디더링(URP 후처리의 블루 노이즈 순번)이 밀려 모든 샷이 ±4(픽셀 63만 개) 바뀌었다 → 되돌림.
  - 검증(6.3 창 에디터): ScreenSpace에서 그냥 3회 + GUI 부하 8회 = 세 샷 모두 픽셀까지 같음(maxDiff 0). selftest 1번에 두 검사: 루프 2·3이 루프 1과 **maxDiff 0**
    (예전엔 `same`이면 통과), 모든 창을 매 업데이트마다 다시 그리는 플레이의 루프도 maxDiff 0(arm은 플레이 모드를 나가면 스스로 꺼짐; 다시 그리기 181회).
    그 검사는 메모리에서 DBuffer로 되돌린 상태에서 3/3 B를 잡았다.
    - 매트릭스(커밋 `W13 wip`): 샘플 selftest 1–8 녹색 538.6 s(`896e67fc`, 줄 64/71/77/87; 1번 94.7 s — `goldenMaxDiff` 0, 다시 그리기 루프 181회·maxDiff 0,
      커밋된 기준 이미지 3/3 같음).
      9: 새 클론 6.3 녹색 703.8 s(selftest 609.8 s — 1번 213.9 s(첫 플레이어 빌드 121 s 포함), 3번 80.0 s; 루프 3회 `896e67fc` = 메인 트리, `goldenMaxDiff` 0·
      다시 그리기 루프 181회 maxDiff 0·커밋된 기준 이미지 3/3 같음, 플레이어 447 fps, `git status` 깨끗), 6.0 녹색 533.6 s(selftest 450.1 s; `8acf308c`,
      `goldenMaxDiff` 0 — W12에서는 47, 다시 그리기 maxDiff 0, 플레이어 단계는 전처럼 건너뜀), 6.6 녹색 755.5 s(selftest 646.1 s; `6b943b54`, `goldenMaxDiff` 0,
      다시 그리기 maxDiff 0, 플레이어 429 fps) — 세 버전 샷 72.6/61.3/48.2, `git status`는 버전 전환 파일뿐.
      10: BagelGame 녹색 44.4 s(루프 3회 `619be553`, 출시 빌드 `Managed/` 132개·`Harness.*` 0개), Fluid-Sim 녹색 27.9 s(`54880f05`, 103개·0개), 사내 프로젝트 A 녹색
      76.0 s(`brd-attach.json`, `6664b723`; uGUI 위주 — feature를 끈 UI 카메라로 합성한 로그인 대화상자가 W12 샷과 같은 배치, 다른 픽셀은 실행마다 바뀌는 배경 연출로
      W6a·W6b 샷끼리도 그만큼 다름) — 셋 다 W12와 같은 fingerprint, 제거 뒤 `git status` 비어 있음.
  - 남은 것: URP 쪽 두 건(DBuffer 데칼의 흔들림, ScreenSpace 데칼 패스의 NRE)은 신고(상시). 게임에서 DBuffer 데칼을 쓰면 그 가장자리 몇 픽셀은 여전히 실행마다 갈릴 수
    있다(허용치 안 — CLAUDE.md "기준 이미지").

- [x] **G2-5 핫 루프는 `[CodeReload]` 메서드 본문만 받는다** (2026-10-01, W12 — 하네스 쪽; `try/catch`·예외 줄은 Pipeline, 상시)
  - 조사: Pipeline 0.8.0-exp.1의 교체는 파일당 **첫 번째 `[CodeReload]` 클래스 하나**만 다룬다(`InPlaceReloadProcessor.ExtractCodeReloadableMethods`; 중첩 타입·두 번째
    클래스의 표식은 건너뛰고 진단에 적는다). 그 클래스에서 컴파일된 타입에 **없는 이름**의 메서드(제네릭 제외, 표현식 본문 포함)는 교체 본문과 함께 오버라이드
    클래스에 넣어 컴파일하고, 교체 본문 속 호출을 그 정적 사본으로 바꾼다(`SourceCodeTransformer.ComputeNewMethodNames`·`ImplicitScopeQualifier`; 인스턴스 메서드는
    수신자를 첫 인자로, 새 메서드끼리의 호출도). 이름이 컴파일된 멤버와 같으면(오버로드) 호출이 컴파일된 쪽에 묶인다. 컴파일된 메서드(표식 없음)의 본문을 바꿔도
    교체 본문은 컴파일된 것을 부른다. 새 `[CodeReload]` 메서드는 진입점으로 등록되지 않을 뿐 같은 식으로 불린다. 교체된 메서드는 `CodeReloadRegistry`의
    `MethodOverride.InterpreterInvoke` 델리게이트로 호출된다(호출 수는 세지 않음, 전체 호출 수만 오버레이용 `CodeReloadActivity`에).
  - 방법(판정, `Editor/HarnessHot.cs` `Diff`): 컴파일된 텍스트의 그 타입에 같은 이름의 메서드가 없는 메서드 선언을 "새 메서드"로 보고 비교에서 통째로 빼서(표식 메서드
    본문처럼) 나머지 토큰이 같으면, 새 메서드마다 Pipeline이 받는지 본다: 파일의 첫 `[CodeReload]` 클래스(최상위)에 직접 선언, 제네릭 아님, 본문 있음, 컴파일된 그 타입
    (로드된 어셈블리의 타입 — partial의 다른 파일 포함, 못 찾으면 컴파일된 텍스트)에 같은 이름의 멤버 없음. 아니면 그 줄과 사유로 전체 루프. 오버로드·시그니처 변경·표식
    없는 메서드의 본문은 비교에 남아 전과 같이 "컨텍스트 변경"인데, 사유에 무엇인지를 붙인다(`field m_SelftestField`, `method UpdateHud (no [CodeReload]: its compiled
    body runs)`, `method Reverse(int n) (an overload or a new signature of a compiled method)`, `type SmokeExtra`, `a using directive`). 새 메서드만 더하고 아무 교체 본문도
    부르지 않으면 핫이고 교체할 것이 없다. `harness_hot` 응답·`report.hot.reloaded[]`·`hot.changes[]`에 `newMethods`.
  - 방법(인터프리터 비용): 교체가 끝나면 활성 오버라이드마다 `InterpreterInvoke`를 감싸(리플렉션: `CodeReloadRegistry.m_MethodOverrides`) 호출 수·호출된 프레임 수·그 안의
    시간(Stopwatch, 안에서 부른 새 메서드 포함)을 센다. 플레이 모드에 들어갈 때(`ExitingEditMode`) 0으로, 앞선 교체를 지울 때(`prepare`·`apply`) 버린다. 루프가 플레이 뒤
    `harness_hot {"mode":"calls"}`로 읽어 `report.hot.interpreted = {methods:[{method, calls, frames, ms, msPerCall}], msPerFrame, frameShare}`(메서드마다 ms/프레임의 합,
    그 루프 fps의 한 프레임에 대한 비율). 내부 필드가 없는 Pipeline이면 `interpreted.error`만 남고 나머지는 그대로.
  - 해 보고 하지 않은 것: `reload_file`(Assembly.Load) 백엔드로 인터프리터 비용을 없애는 선택지 — 잰 비용이 Tick 하나에 0.06 ms/프레임(프레임의 ~0.8%)이고 샘플 모듈의
    교체 본문은 private 필드를 써서 그 백엔드가 거부한다(W5). 새 필드·속성은 Pipeline이 받지 않는다(상시).
  - 검증(이 머신, 6.3, 새로 연 창 에디터에서 각 3회): 전체 루프(변경 없음) 3.82–4.72 s(fps 101–130), `-Hot` Tick 본문 1줄 3.41–3.46 s(교체 0.13 s, 리로드 뒤 첫 번째
    4.27 s — 교체 0.86 s), **Tick이 새 헬퍼 2개(인스턴스·static)를 부르게 한 `-Hot` 3.44–3.51 s**(교체 0.16–0.17 s). 인터프리터로 돈 Tick: 182회(플레이의 모든 프레임;
    `play.frames` 163은 워밍업·캡처 프레임을 뺀 수) 0.056–0.063 ms/프레임 = 프레임의 0.7–0.8%(리로드 뒤 첫 플레이 0.13–0.15 ms), 핫 루프 fps 124–136으로 전체 루프와
    구분되지 않는다. 같은 변경을 인라인으로 쓴 핫 루프와 새 헬퍼로 쓴 핫 루프의 샷 3장이 픽셀까지 같았다(maxDiff 0). `harness_hot check`로 8가지 편집: 제네릭 새 메서드·
    오버로드·시그니처 변경·표식 없는 메서드 본문·필드·새 타입 → 핫 아님(그 줄과 사유), 아무도 안 부르는 새 메서드·새 `[CodeReload]` 메서드(값을 돌려주는 것도 불림) → 핫.
    - selftest 3번(새 단계): 본문 수정 단계의 `hot.interpreted`(Tick 호출 ≥ `play.frames`, 시간·비율 > 0), 같은 변경을 새 헬퍼 `SelftestBob`(인스턴스, private 필드 읽음)·
      `SelftestWave`(static)로 → 핫(`newMethods` 둘, 컴파일·빌드·도메인 리로드 없음), 인라인 단계의 샷과 `same` 3/3·같은 events, `check`로 제네릭 새 메서드·오버로드 거부
      (그 줄)·안 쓰는 새 메서드 수용, 되돌림 뒤 `interpreted` 없음, 필드 추가 사유에 `field m_SelftestField`. 3번 148.8–150.3 s(W11 88–153 s — O-12 편차;
      루프 1개와 `check` 3개를 더함). 헬퍼 경유 샷 대 인라인 샷: maxDiff 0–1(`same` 3/3), 그 실행의 핫 루프 fps 144.7·같은 때 전체 루프 136.1.
    - 매트릭스(커밋 `W12 wip`): 샘플 selftest 1–8 녹색 702.9 s(`1d7568ed…` 그대로, 줄 64/71/77/87; 1번 122.3 s — 그 루프 3의 closeup `maxDiff` 47은 G3-15).
      9: 새 클론 6.3 녹색 694.3 s(selftest 595.1 s — 3번 82.3 s, 이번엔 O-12 지연 없음; 루프 3회 `1d7568ed` = 메인 트리·기준 이미지 same=3, `git status` 깨끗;
      헬퍼 경유 샷 = 인라인 샷 maxDiff 0–1; 1번 `goldenMaxDiff` 47 = G3-15). 첫 시도는 6번(전용 에디터 = 에디터 2개)에서 시스템 메모리 부족으로 Claude Code가 중단했다
      (1–5번 녹색, 여유 6.9 GB; 사용자의 다른 에디터를 닫고 다시 — 15.9 GB).
      6.0 녹색 549.9 s(selftest 462.5 s, 3번 77.3 s; `330ebb7a` 그대로, 헬퍼 경유 샷 = 인라인 maxDiff 0, 1번 `goldenMaxDiff` 47 — G3-15는 6.0에도),
      6.6 녹색 764.5 s(selftest 646.4 s, 3번 83.5 s; `995ce417` 그대로, 헬퍼 경유 샷 = 인라인 maxDiff 0, 플레이어 317 fps) — 세 버전 모두 새 핫 단계 녹색,
      `git status`는 버전 전환 파일뿐.
      10: BagelGame 녹색 47.5 s(루프 3회 `619be553`, 출시 빌드 `Managed/` 132개·`Harness.*` 0개), Fluid-Sim 녹색 27.9 s(`54880f05`, 103개·0개), 사내 프로젝트 A 녹색
      79.4 s(`brd-attach.json`, `6664b723`) — 셋 다 W11과 같은 fingerprint, 제거 뒤 `git status` 비어 있음.
  - 남은 것: 인터프리터의 `try/catch`·예외 줄, 새 필드·속성은 Pipeline(상시). 호출 수 세기는 Pipeline 내부 필드에 기댄다(상시 — 올릴 때 selftest 3번).

- [x] **G3-9 에디터가 백그라운드일 때 실제 입력 격리가 검증되지 않는다** · **G3-14 에디터(플레이어)가 백그라운드면 UI Toolkit이 시나리오 입력을 버린다** (2026-10-01, W11)
  - 재현(G3-9): 다른 프로세스의 작은 창으로 OS 포커스를 에디터에서 빼고 selftest 1번의 주입(실제 키보드 장치에 스페이스 누름·뗌 60개)을 건 기본 루프 → 녹색,
    `SpinDirectionChanged=1`(게임에 닿지 않음)인데 `isolatedDevices`가 비었다. 플레이 동안 장치 플래그를 기록해 보니, 플레이 모드 진입 때(에디터 업데이트) Unity가
    `Application.focusChanged(false)`를 보내고 게임의 Input System 설정(기본 `ResetAndDisableNonBackgroundDevices`·`PointersAndKeyboardsRespectGameViewFocus`)이
    Keyboard·Mouse·Touchscreen·Pen을 `DisabledWhileInBackground`로 끈다 → 러너의 격리가 다음 프레임에 `enabled == false`를 보고 모두 건너뛰었다. 그런 장치의 이벤트는
    `InputManager.OnUpdate`가 `InputSystem.onEvent` 리스너보다 먼저 버려서 게임에는 안 닿지만 누름을 셀 수 없다. 시나리오 동안은 `ScriptedInput`이 `IgnoreFocus`로 바꾸고
    러너가 `runInBackground`를 켜서, 도중에 포커스가 돌아와도(`focusChanged(true)`) Input System이 장치를 켜지 않는다(4 s 시나리오 도중 에디터를 앞으로 → 장치가
    `LeavePlayMode`까지 그대로). selftest가 본 "중단한 플레이 동안 켜져 있음(`disabled []`)"은 `InputDevice.enabled`가 **에디터 입력 업데이트 동안에는**
    `DisabledWhileInBackground` 장치를 켜진 것으로 읽기 때문이었다(eval은 에디터 문맥; 같은 순간 플레이어 업데이트에서는 꺼짐).
  - 방법(G3-9, `Runtime/ScriptedInput.cs` `RealInputIsolation`): 켜진 장치뿐 아니라 "백그라운드라 꺼진" 장치(내부 `InputDevice.disabledWhileInBackground`, 리플렉션 —
    게임이 끈 장치와 구분할 공개 API가 없다)도 가져간다. `DisableDevice(keepSendingEvents: true)`가 그 상태를 지우고(런타임에서 꺼져 있었으면 켜서) 이벤트가 오게 한 뒤
    전처럼 handled 표시·누름 세기. 게임·TouchSimulation이 끈 장치는 전처럼 두고, 속성이 없는 버전이면 전처럼 켜진 장치만. 보고: `isolatedDevices[].background = true`.
    끝나면 다른 장치처럼 켠다 — 시나리오 직후 플레이 모드(플레이어)가 끝나고 `LeavePlayMode`도 그 장치를 켜므로 "백그라운드라 끔"으로 되돌리지 않는다(다음 포커스 변화에
    Input System이 다시 판단).
  - G3-14(W11 매트릭스에서 발견): 처음 만든 selftest가 끝에 사람이 쓰던 창으로 포커스를 돌려주자 그 뒤의 UI 킷 검사가 빨갰다 — REVERSE 버튼 클릭(`via uitk`)이 방향을
    바꾸지 않았다. UI Toolkit의 `DefaultEventSystem.Update`는 `!Application.isFocused && ShouldIgnoreEventsOnAppNotFocused()`(데스크톱 OS이고
    `IsEditorRemoteConnected()`가 거짓 — Unity Remote가 없으면)이면 입력을 하나도 처리하지 않는다(IL로 확인, 격리와 무관). uGUI의 `InputSystemUIInputModule`은
    `runInBackground`면 포커스를 무시해서(러너가 켬) 괜찮다. → `Runtime/PanelFocus.cs`: 시나리오 동안 내부 필드 `DefaultEventSystem.IsEditorRemoteConnected`를
    `() => true`로, 끝나면 원래 것으로. UI Toolkit 안에서 이 필드를 읽는 곳은 그 판정 하나다(UIElements 어셈블리 1.5만 메서드의 IL을 훑어 확인). 6.0.84f1·6.3.11f1·
    6.6.3f1 모두 같은 필드. 없으면 `play.uiFocusError`.
  - selftest(매트릭스 1): 처음에는 OS 포커스를 실제로 옮겼다(다른 프로세스의 작은 창 ↔ 에디터, `AttachThreadInput` + `SetForegroundWindow`, 끝나면 원래 앞 창으로).
    사람이 다른 앱을 쓰는 동안 돌리자 두 번 빨갰다 — 포그라운드로 시작한 플레이 도중 포커스를 뺏겼고(`editorFocused=False`), 도중에 에디터를 앞으로 가져오는 전환이
    거부됐다(`back=False`). 사람의 작업도 방해한다. → 주입 스크립트가 `Application.InvokeFocusChanged`(네이티브가 포커스 이벤트를 올리는 내부 함수; Input System이
    포커스를 아는 경로는 `Application.focusChanged` 하나)를 플레이 진입 직후(Unity 자신의 포커스 이벤트 뒤, 러너 시작 전)와 20번째 주입 뒤에 부른다 — OS 포커스와
    무관하게 같고, 창 없는 에디터·macOS에서도 돈다. G3-14는 OS 포커스 자체(`Application.isFocused`)를 읽으므로 판정 함수의 값을 본다(플레이 중 0, 뒤 1).
    재시도(`realInputTries`, `realInputStopTries`)는 없앴다.
  - 검증(이 머신, 6.3):
    - 실제 OS 포커스(수동): 도우미 창을 앞에 두고 주입한 기본 루프 → `Keyboard=30(background)`, Mouse·Touchscreen·Pen 0(background), events 그대로. 4 s 시나리오 도중
      에디터를 앞으로 → 주입 80·누름 40(복귀 뒤까지 모두), 장치가 끝까지 격리. UI 킷 시나리오를 백그라운드에서: `PanelFocus` 없이 클릭은 기록되지만 `SpinDirectionChanged`
      없음 → 있으면 1. OS 포커스를 옮기던 selftest 실행에서 UI 킷 루프 1회차 포그라운드·2회차 백그라운드(장치 전부 `background`)의 토스트 캡처가 픽셀까지 같음(`same` 2/2).
    - selftest 1번의 새 검사: 포커스 있음으로 시작 → `Keyboard=30`(background 아님); 없음으로 시작 → `Keyboard=30(background)`, 20번째 주입 뒤 복귀해도 누름 30(= 주입 60의
      절반, 복귀 뒤까지 모두), events 그대로; UI Toolkit 판정 플레이 중 0/0, 뒤 1. W10 `ScriptedInput.cs`로 되돌린 "없음으로 시작" 루프는 `isolatedDevices` 비어 있음
      → 새 검사가 G3-9를 잡는다. OS 포커스를 건드리지 않으므로 그동안 사람이 다른 앱을 써도 같다. 1번 99.8–141.5 s(W10 104.4 s; 루프 1회·검사 5개를 더하고 재시도를 뺌).
    - 매트릭스(최종 커밋, G1-6 포함): 샘플 selftest 1–8 녹색 677.4 s(`1d7568ed…` 그대로, 줄 64/71/77/87; 1번 99.8 s; 그 전 커밋으로 849.5 s — O-12 편차).
      9: 새 클론 6.3 녹색(G1-6 전 커밋으로 1022.6 s — selftest 905.7 s, 루프 3회 `1d7568ed` = 메인 트리·기준 이미지 same=3, `git status` 깨끗; 최종 커밋 루프 3회 100.5 s
      같은 값·same=3), 6.0 녹색(G1-6 전 커밋 733.7 s — selftest 633.1 s, `330ebb7a`; 최종 커밋 루프 3회 92.9 s 같은 값), 6.6 녹색(최종 커밋 878.8 s — selftest 746.3 s,
      `995ce417`; G1-6 전에는 6번만 빨강) — 세 버전 모두 새 검사(포커스 있음·없음 시작, UI Toolkit 판정) 녹색, `git status`는 버전 전환 파일뿐.
      10: BagelGame 녹색 48.3 s(루프 3회 `619be553`, 출시 빌드 `Managed/` 132개·`Harness.*` 0개), Fluid-Sim 녹색 31.4 s(`54880f05`, 103개·0개), 사내 프로젝트 A 녹색
      90.2 s(`brd-attach.json`, `6664b723`) — 셋 다 W10과 같은 fingerprint, 제거 뒤 `git status` 비어 있음.
  - 남은 것: `fps.editorFocused`는 도메인 리로드 뒤 틀릴 수 있다(O-13). 격리·`PanelFocus`는 내부 API에 기댄다(상시, O-9).

- [x] **G1-6 6.6에서 빌더가 만든 PanelSettings가 에디터가 있는 화면의 DPI를 따라 fingerprint가 흔들린다** (2026-10-01, W11 매트릭스 9에서 발견)
  - 현상: 6.6 새 클론의 selftest 6번이 빨갰다 — 클론 에디터 트리(창)의 fingerprint `9950ea8a`, 같은 커밋의 worktree 전용 에디터(창 없음) `995ce417`. W10 커밋의 새 클론도
    오늘은 `9950ea8a`였고(W10 때는 둘 다 `995ce417`), 6.3·6.0은 그대로였다. 두 에디터의 fingerprint 덤프(`Library/Harness/fingerprint.txt`) 차이는 한 줄:
    HUD의 `SmokeHudPanel.asset`(`ctx.UIDocument`가 만든 PanelSettings) `m_ReferenceDpi` 144 대 96. 6.6은 `CreateInstance<PanelSettings>()`에 에디터가 있는 화면의 DPI를
    넣는다(150% 화면 = 144, 창 없는 에디터 = 96; 6.3·6.0은 늘 96). 어제는 에디터 창이 100% 화면에 있었던 것으로 보인다. `ScaleWithScreenSize`라 화면에는 영향이 없다.
  - 방법: `Editor/Build/BuildContext.cs` `UIDocument`가 `referenceDpi`·`fallbackDpi`를 96으로 정한다(6.3·6.0의 기본값 → 두 버전의 fingerprint 그대로).
  - 검증: 같은 6.6 클론에서 창 에디터·창 없는 전용 에디터 모두 `995ce417`(W10 기록값), 덤프 `m_ReferenceDpi=96`. 매트릭스는 위 W11 항목.

- [x] **G1-5 렌더 파이프라인 밖의 프로젝트 설정은 여전히 YAML** (2026-09-30, W10)
  - 현상(전): W4로 URP·Renderer 에셋과 품질 레벨별 파이프라인 배정은 코드가 됐지만 품질 레벨 목록·레벨별 값, Player(색 공간·창), Time, Physics, Tags/Layers는 커밋된
    YAML이었고, 하네스가 코드로 만지는 것은 `harness_setup`의 몇 가지뿐이었다. Project Settings 창에서 바꾼 값은 조용히 남았다 — 예: 에디터의 품질 레벨을 Mobile로 누르면
    (`QualitySettings.SetQualityLevel`, `m_CurrentQuality`가 저장됨) 그 뒤 루프가 모두 Mobile 파이프라인(렌더 스케일 0.8, SSAO 없음)으로 돌았고 아무 보고도 없었다.
  - 조사: ProjectSettings는 에디터 시작에 필요해 W4처럼 생성물(gitignore)로 둘 수 없다. 6.3은 밖에서 고친 ProjectSettings YAML을 `AssetDatabase.Refresh`(루프의
    recompile)에서 다시 읽는다(레이어·품질 값을 파일에서 바꾸고 Refresh → 메모리 값이 바뀜; 6.0·6.6도 — 매트릭스 9의 드리프트 검사) → 손으로 고친 YAML도 루프가 메모리에서 본다. `Time.fixedDeltaTime` 세터는
    TimeManager를 dirty로 만들지 않는다(`Physics.gravity`는 만든다) → 하네스가 쓴 설정 오브젝트를 직접 dirty + `SaveAssets`. 6.3은 fixed timestep을 분수
    (2822399/141120000 → 0.0199999921)로 들고 있어 0.02와 1e-6 허용치로 비교한다. 설정 오브젝트를 한 번 저장하면 6.3 형식으로 다시 쓰인다(TagManager
    serializedVersion 2 → 3과 빈 렌더링 레이어 24줄 삭제, 옛 형식의 TimeManager·DynamicsManager도) — 값이 다를 때만 쓰므로 이번 커밋에서 바뀐 YAML은 레이어를 더한
    TagManager와 창 설정을 바꾼 ProjectSettings(3줄)뿐이다. `UnityEngine.QualityLevel`(옛 enum)과 이름이 겹쳐 품질 레벨 값 묶음은 `QualityLevelValues`.
  - 방법(`Editor/Build/SettingsContext.Project.cs`, `ProjectValues.cs`, `SettingsContext.Run`·`Fingerprint`, `HarnessBuild.SettingsSummary`, `HarnessSetup`):
    - 설정 스텝이 이름 붙인 값만 소유: `ctx.Player(p => …)`·`ctx.Time`·`ctx.Physics`(값 묶음의 null = 그대로, 그 밖은 `Set("직렬화 이름", 값)`),
      `ctx.QualityLevels(new QualityLevelValues("PC") { Pipeline, DefaultFor, ExcludedPlatforms, VSyncCount, LodBias, AnisotropicTextures, SkinWeights, … })`,
      `ctx.Layer(i, "이름")`(인덱스 반환), `ctx.Tag`, `ctx.ProjectSetting(파일, 경로, 값)`. 공개 API가 있으면 API(색 공간 전환의 재임포트 같은 부수 효과), 없으면
      SerializedObject. `Physics`는 `AGENTHARNESS_PHYSICS`(물리 모듈이 없는 프로젝트도 컴파일).
    - 키마다 읽고 → 다르면 쓰고 → 다시 읽어 `changed`("키: 전 -> 후", 쓴 파일은 저장). 비교는 텍스트, 숫자는 1e-6 상대 허용치. 두 스텝이 같은 키를 다른 값으로 → 예외.
      모든 스텝이 끝나면 각 키의 값을 `Library/Harness/project-settings.json`에 남기고, 다음 빌드가 찾은 값이 코드와도 그것과도 다르면 코드 밖의 변경 → `drift` +
      `build.warnings`(어떻게 고칠지 포함). 스텝이 실패한 빌드는 남기지도 정리하지도 않는다(일부 선언만으로 레이어를 비우지 않게).
    - 목록은 선언한 것이 전부: 레이어를 하나라도 선언하면 나머지 사용자 레이어를 비우고(스텝이 모두 끝난 뒤), 태그는 선언한 목록 그대로, 품질 레벨은 한 스텝이 목록 전체를
      선언한다(이름으로 맞춰 순서 바꾸기, 없는 레벨은 앞 레벨의 복사로 추가 — Unity의 "Add Quality Level"도 복사한다, 나머지 삭제; 에디터의 레벨·플랫폼별 기본 레벨은 이름을
      따라감). 에디터가 쓰는 레벨 = 활성 플랫폼의 기본 레벨(플랫폼을 바꿀 때 Unity가 하는 것). 활성 파이프라인이 바뀌면 W4의 리로드 경로(`settings.switched`).
    - 오타는 스텝의 줄로 예외: 품질 레벨 필드(`quality level 'Mobile' has no field 'lodBiass' … (similar: lodBias, …)`), 알 수 없는 플랫폼(목록), 내장 레이어 번호,
      이미 쓰인 레이어 이름·번호, 선언 안 된 레이어(`IgnoreCollision`), 없는 직렬화 경로(비슷한 이름).
    - 보고: `build.settings.project` = `{owned, changed, drift}`, `harness_setup`의 `changed`("settings: project …")·`warnings`. fingerprint에 소유한 값(`--project--`).
      attach 프로젝트는 W4처럼 설정 스텝을 돌리지 않는다.
    - submit(`Tools~/submit.ps1`, `Start-HarnessSubmit -Guard`): harness 프로젝트면 `ProjectSettings/*.asset`을 저널에 함께 백업 → 빨간 submit·도중에 죽은 submit은
      내용이 바뀐 파일만 복원(같은 파일은 건드리지 않음 — Unity가 바뀐 설정 파일을 다시 읽는다). 녹색이면 루프가 바꾼 파일을 worktree로 되복사(`submit.settingsWrittenBack`) —
      에디터 트리 사본과 worktree 사본이 둘 다 올라간 내용이었을 때만(아니면 `settingsNotWrittenBack` + `settingsNote`, G5-6). 그래서 모듈 코드와 그 YAML이 같이
      커밋되고, land 때 에디터 트리의 미커밋 사본과 같아 stash가 버려진다.
    - 샘플: `StageRenderSettingsStep`이 `UsePipeline(rp, 레벨)` 두 줄 대신 품질 레벨 Mobile·PC(옛 YAML 값: 파이프라인, 기본 플랫폼 Android·iPhone·WebGL / Standalone,
      제외 플랫폼, vSync 0, LOD 바이어스 1/2, 이방성 Enable/ForceEnable, 스킨 웨이트 2/4, 실시간 반사 프로브 끔)와 색 공간 Linear. 새 `StageProjectSettingsStep`:
      레이어 Ground(8, 지형 — MeshCollider)·Props(9, 선돌·바위·아치; 빌드 스텝이 상수로 씀), 창 1280x720 창 모드(1024x768 전체 화면 창에서 — 플레이어를 손으로 띄워도 캡처
      크기), Time(0.02, 1/3), 중력. 소유한 값 54개(빈 사용자 레이어 27개 포함). 레이어가 바뀌어 fingerprint가 새 값(`1d7568ed…`), 픽셀은 같음(기준 이미지 `same`).
  - 검증(이 머신):
    - 루프: 설정 스텝 첫 적용 49 ms(레이어 2개·창 설정 3개 씀), 이후 무변경 11.5–13 ms(렌더 스텝; W4 ~10 ms) + 0.6 ms(프로젝트 스텝), 빌드의 `settings` 단계
      16–17.5 ms(스냅샷 쓰기 포함). 새로 연 창 에디터에서 코드 변경 없는 루프 3.78–4.13 s(빌드 0.60–0.68 s) — W8 기준선(4.08–4.37 s)과 같아 기준선 표에 열을 더하지 않았다. 손으로 PC `lodBias` 2 → 3, 레이어 9 → Foo·
      10 → Extra로 고친 YAML → 루프 한 번이 셋 다 되돌리고 `drift` 3건·경고 3줄, `QualitySettings.asset`은 바이트까지 원래대로. 메모리에서 PC vSync 1·에디터 레벨 Mobile →
      `drift` 2건, 파이프라인 Mobile → PC 전환으로 리로드 2.27 s 뒤 녹색. 오타·잘못된 플랫폼·내장 레이어 예외 메시지와 `IgnoreCollision`의 매트릭스(선언한 쌍만)를 eval로 확인.
    - 매트릭스(W9보다 긴 시간은 W10과 무관한 O-12 — 같은 에디터에서 W9 코드로 되돌려 A/B): 샘플 selftest 1–8 녹색 742.8 s(`1d7568ed…`, 줄 64/71/77/87; 1번 104.4 s에 새 검사 4개, 7번 121.5 s·8번 95.9 s에 3개 — `settingsWrittenBack`
      `[ProjectSettings/TagManager.asset]`, 빨간 submit 뒤 TagManager 바이트 그대로·에디터에서 레이어 21 없음, land stash에 TagManager → 버려지고 에디터 트리 깨끗).
      9: 새 클론 6.3 녹색(888 s: `harness_setup`이 ProjectSettings를 하나도 쓰지 않음 — 커밋된 YAML = 코드, 루프 3회 `1d7568ed` = 메인 트리·기준 이미지 same=3,
      클론 selftest 1–8 782 s, 플레이어 410 fps, 드리프트 되돌림 뒤 두 파일 바이트까지 같음, `git status` 깨끗), 6.0 녹색(757 s, `330ebb7a`, selftest 655 s; 드리프트 되돌림이
      두 파일을 6.0 형식으로 다시 써서 커밋본으로 되돌림 — 설계대로, 플레이어 단계는 W8처럼 건너뜀), 6.6 녹색(923 s, `995ce417`, selftest 792 s, 플레이어 312 fps; 6.0과 같이
      다시 씀, 빌드가 쓴 6.6 직렬화는 보고만). 세 버전 모두 샷 72.6/61.3/48.2–48.3(레이어가 바뀌어도 픽셀 같음), 새 모듈 레이어의 되복사·land 녹색.
      10: BagelGame 녹색(49 s, 루프 3회 `619be553`, 출시 빌드 `Managed/` 132개·`Harness.*` 0개), Fluid-Sim 녹색(31 s, `54880f05`, 103개·0개), 사내 프로젝트 A 녹색
      (`brd-attach.json`, 86 s, `6664b723`) — 셋 다 W9와 같은 fingerprint(설정 스텝이 없고 attach라 돌지도 않음), 제거 뒤 `git status` 비어 있음.
  - 남은 것: 코드가 이름 붙이지 않은 값은 여전히 커밋된 YAML(ProjectSettings 전체를 코드로 두지는 않았다 — 버전마다 필드가 다르고, 새 필드는 그 버전의 기본값이 맞다).
    정렬 레이어·렌더링 레이어 이름은 `ProjectSetting` 경로로만. URP 전역 설정(`UniversalRenderPipelineGlobalSettings`)은 여전히 URP가 관리하는 커밋 에셋이다.
    여러 worktree가 같은 ProjectSettings 파일을 바꾸는 경우 → G5-6(W14에서 해결). `harness_setup`이 적용하는 값(runInBackground 등)을 설정 스텝도 정하면 둘이 번갈아 쓴다(샘플은 안 씀).

- [x] **G5-4 `Assets/Game/Contracts`가 공유 지점이다** · **G5-3 compile-check는 다른 모듈의 최신 변경을 모른다** (2026-09-30, W9)
  - 현상(전): 계약 폴더는 "추가만" 규칙과 주석("One file per publishing module")으로 버텼다. submit은 에디터 트리에 있는 계약 **파일**이 달라지면 거부했고(자기가 막
    올린 미병합 계약도 못 고침), 같은 이벤트 이름을 두 에이전트가 다른 파일에 만들면 에디터 트리 루프가 CS0101(같은 네임스페이스)로 빨개지거나, 다른 네임스페이스면
    컴파일은 되는데 `play.events`가 둘을 한 이름으로 셌다(`EventBus`가 `typeof(T).Name`으로 센다). land는 계약을 검사하지 않았다. compile-check의 검사 집합 밖은
    에디터 DLL 기준이라 "다른 모듈의 최신 변경을 모른다"(G5-3 `[~]`).
  - 조사(G5-3): 집합이 **참조하는** 쪽(Harness, 다른 패키지)은 에디터 DLL이 정확히 맞다 — submit이 코드를 넣을 에디터 트리가 컴파일하는 그 DLL이다. 모듈끼리는
    `module-boundary`로 서로 참조하지 않는다. 문제는 집합을 **참조하는** 쪽이었다: worktree에서 Smoke 파일에 `public readonly struct Light { }`를 더하면 게이트
    (`-Module Smoke` = Smoke + Contracts)는 녹색인데 에디터 트리에서는 `StageModule.cs:18`이 CS0104(`Game.Contracts.Light` vs `UnityEngine.Light`)로 깨진다 →
    남의 모듈 에러로 submit이 되돌려지고 그동안 에디터 트리 락을 잡는다. 추가만 하는 계약이 다른 모듈을 깨뜨리는 유일한 길이 이 이름 충돌이다.
    기존 프로젝트의 `modules[]` 배치는 모듈끼리 직접 참조할 수 있어 더 흔하다: BagelGame(`-Module Game=Assets/Game,UI=Assets/UI`)에서 Game의 공개 속성
    `BagelTracker.bagelTrackerData`의 이름을 바꾸면 `-Module Game` 게이트는 녹색(0.47 s), `-Dependents`는 UI 모듈의 `BagelTrackerDriver.cs:22` CS1061로 빨강(0.70 s;
    BagelUI·BagelUIEditor·BagelTests·BagelTestsEditor까지 검사).
  - 방법:
    - lint(`Editor/HarnessContracts.cs`, `harness_lint`): `contract-file` — 계약 폴더의 .cs는 `<Module>Events.cs`(모듈 = 모듈 루트의 폴더·`modules[]`), 모듈 M이
      발행하는 계약 타입은 `MEvents.cs`에(발행 = M의 런타임 어셈블리 IL에서 `call EventBus.Publish<T>`를 찾음: `MethodBody.GetILAsByteArray` + `Module.ResolveMethod`,
      람다·상태 기계 포함, 제네릭 T는 건너뜀), 두 모듈이 발행하면 각 모듈에 한 줄. `contract-name` — 계약의 최상위 타입 이름(+arity)이 두 번(네임스페이스가 달라도).
      선언은 Pipeline이 번들한 Roslyn(3.11) 구문 트리로.
    - `harness_contracts`: 소스(경로 또는 텍스트)가 선언하는 최상위 타입 `{name, ns, full, kind, line, hash}` — hash는 그 타입의 토큰(속성 포함, 공백·주석 제외).
    - submit(`Get-ContractPlan`, 락 안, 복사 전): 계약 파일마다 — 두 트리가 같거나 이 worktree가 바꾸지 않은 파일(에디터 트리 브랜치와 만나는 merge-base와 같음:
      뒤처졌을 뿐, `contractsBehind`)은 그대로; 다른 살아 있는 worktree가 올리고 아직 land하지 않은 파일은 거부(`contractOwner`, `-Takeover`); 올라간 .cs는
      **타입 단위 add-only**(올라간 타입이 모두 같은 hash로 남아야 함 — 새 타입은 덧붙여도 됨, `contractChanged`), 올라간 .meta는 불변; 뒤의 계약 전체에서 새 타입
      이름이 한 번(`contractConflicts`, 상대가 landed/미병합인지). 이 worktree의 미병합 계약은 고치거나 지울 수 있다(`contractsUpdated`/`contractsDeleted`).
      유지되면 쓴 파일을 `Library/Harness/submit/contracts.json`에 이 worktree 소유로(올라간 내용으로 되돌린 파일은 해제).
    - land(병합 전): 병합이 바꾸는 계약 파일에 다른 worktree의 미병합 submit이 있으면 거부(`contractOwner`), 병합 결과로 올라간 타입이 바뀌면(`contractChanged`),
      병합 결과 + 에디터 트리의 나머지 계약(미병합 포함)에서 이름이 겹치면(`contractConflicts`). 이 브랜치 소유의 미병합 계약 사본은 `foreign`이 아니다.
      녹색이면 커밋된 것과 같아진 계약 파일의 소유를 해제(`releasedContracts`; 아직 다르면 `stillPending`).
    - compile-check `-Dependents`(submit 게이트가 씀): 모듈의 어셈블리와, 에디터 트리와 소스가 다른 참조 어셈블리(계약 추가)를 참조하는 프로젝트 어셈블리를 이
      worktree 소스로 함께 컴파일(`dependentOf`). 에디터가 컴파일하지 않는(응답 파일 없는) 어셈블리는 건너뜀(`dependentsSkipped`).
  - 조사하며 찾은 것:
    1. 예전 add-only는 파일 단위였다 — 문서(`SmokeEvents.cs` 주석 "add new files/structs")보다 엄격했고, "발행 모듈별 파일"과 합치면 올라간 `<Module>Events.cs`에
       그 모듈의 새 이벤트를 영영 더할 수 없었다 → 타입 단위. 올라간 파일이 자라기 시작하면 그 파일을 안 건드린 worktree도 뒤처지므로 merge-base와 비교해 건너뛴다.
    2. land는 계약을 검사하지 않았다(브랜치가 올라간 계약을 바꿔도 병합했고, 같은 이름은 병합 뒤 CS0101 → 전체 루프 → 되돌림).
    3. lint의 첫 판은 도메인 리로드 직후 83–158 ms였다 — `CompilationPipeline.GetAssemblies(Editor)`(캐시 전 ~60 ms)와 Builders 어셈블리까지 훑은 탓 → static-reset이
       이미 받은 Player 목록(런타임 어셈블리)으로 21–30 ms(Roslyn 파싱 12–19 ms, IL 4–6 ms), 웜 2.5 ms.
    4. PowerShell 모듈(`Set-StrictMode -Version Latest`)에서 해시테이블의 없는 키를 속성 문법(`$h.text`)으로 읽으면 예외다 → 인덱서(`$h['text']`).
  - 검증: 매트릭스 1–8 녹색(에디터 트리 520 s, fingerprint `345ba0d7`, 표식 줄 64/71/77/87 그대로; 5번 lint 5건 25 s, 7번 26개 검사 79 s, 8번 24개 75 s).
    9: 새 클론 6.3 녹색(701 s: 루프 3회 `345ba0d7`·기준 이미지 same=3, 클론 selftest 1–8 598 s, 플레이어 422 fps), 6.0 녹색(528 s, `c8561a2d`, selftest 437 s;
    플레이어 단계는 W8처럼 URP 다운그레이드로 빌드 전에 건너뜀), 6.6 녹색(742 s, `7cda8899`, selftest 618 s, 플레이어 330 fps; 빌드가 쓴 6.6 직렬화는 보고만).
    10: BagelGame 녹색(50 s, 루프 3회 `619be553`, 출시 빌드에 `Harness.*` 없음, 제거 뒤 깨끗), Fluid-Sim 녹색(29 s, `54880f05`), 사내 프로젝트 A 녹색
    (`brd-attach.json`, 81 s, `6664b723`). 기존 프로젝트는 `contracts`가 비어 있어 계약 규칙이 돌지 않는다(루프·lint 그대로); `-Dependents`는 BagelGame에
    설치해 따로 확인했다(위 조사, 제거 뒤 `git status` 깨끗).
  - 측정값(이 머신): submit 게이트 `compile-check -Module Smoke` 1.15 s → 계약이 바뀐 `-Dependents` 1.42 s(Game.Stage 컴파일 +0.13 s; 계약이 같으면 추가 없음),
    worktree 게이트 1.27–1.39 s. 계약 거부는 게이트 뒤 락 안에서 ~1 s(submit 한 번 2.1–2.4 s), land 거부 1.5–1.7 s. `harness_contracts` 소스 2개 17 ms.
    lint 전체는 리로드 직후 ~100–125 ms(계약 21–30 ms), 루프 시간은 그대로.
  - 남은 것: 발행 모듈은 모듈 루트의 asmdef 모듈만 본다(`modules[]`의 `Assembly-CSharp` 코드가 발행하는 계약은 파일 규칙에서 빠짐). 파일 사이로 옮긴 타입은
    "지움 + 새 타입"이라 올라간 뒤엔 거부된다. 역의존은 이 worktree의 소스로 컴파일한다(worktree가 뒤처졌으면 그 스냅샷 기준 — 최종 판정은 에디터 트리 루프).

- [x] **G3-2 에디터 플레이 모드 FPS는 실제 성능을 대표하지 못한다** · **G3-8 에디터 캡처의 UI가 게임의 화면 크기 코드와 어긋날 수 있다**(경로; 사내 프로젝트 A 확인은 W16)
  (+ **G3-12 캡처가 HDR 이미션·블룸을 잘랐다**, **G3-13 6.6의 render 카운터**, 2026-09-30, W8)
  - 현상(전): 성능은 에디터 플레이 모드 fps(에디터 오버헤드·autotick·Game 뷰)뿐이라 변경 전후 비교에만 쓸 수 있었다. 캡처는 UI를 캡처 크기로 다시 배치해
    합성하므로 `Screen.width`를 읽는 UI·크기 콜백·Overlay 카메라 캔버스가 게임과 다를 수 있는데, 게임이 실제로 그린 화면과 비교할 길이 없었다.
  - 결과: `tools/player.ps1` = 에디터 루프(`<Out>/editor`) → 개발 빌드 플레이어(Pipeline `build` + `build_status`, `HarnessOut/player-build/<타깃>/`, 증분) →
    캡처 크기의 창(`-screen-fullscreen 0 -screen-width/-height`)으로 띄운 플레이어가 **혼자** 시나리오를 돌고 종료 → `harness_compare`로 비교 → report.
    - 플레이어(`Runtime/PlayerRun.cs`): 명령줄 `-harness-scenario/-out/-config/-size/-screen/-id/-paced` → BeforeSceneLoad에 설정 파일(`HarnessConfig.UseFile`)·
      시나리오·vSync 0·프레임 상한 없음·개발자 콘솔 끔·창 크기 → 첫 프레임 끝에 에디터와 같은 `ScenarioRunner` → 끝나면 `Application.Quit(0/1)`.
      `[AgentHarnessInput]` 훅은 리플렉션(`InputHooks.FindMarked`, 에디터는 TypeCache로 같은 `InputHooks.Combine`). **Pipeline 런타임 서버는 쓰지 않았다** —
      `enableInBuilds`를 `ProjectSettings/Packages/com.unity.pipeline/`에 써야 하고(기존 프로젝트 설정 변경), 플레이어에 HTTP 서버가 돌며 프레임 시간에 섞인다.
    - 러너: splash가 끝날 때까지 시계를 세우고(`player.splashSec`), 고정 간격을 Start가 아니라 첫 Update에서 켜고(에디터에선 같은 프레임), 게임 카메라 캡처의 화면 쌍
      (`ScreenTwins` → `<샷>.screen.png`), 결과에 `player`(플랫폼·개발 빌드·창·vSync·그래픽 장치·시작 시간).
    - 에디터: `harness_player_plan`(씬 = 플레이 씬 + Build Settings, 타깃·출력·캡처 크기·설정 파일; 데스크톱이 아닌 활성 타깃은 전환하지 않고 거부),
      `harness_player_built`(빌드 뒤 `SaveAssets`), `harness_compare`(쌍 비교, `same_mean`). 스택의 절대 경로도 모듈로(`HarnessLogParse`).
    - report: `fps`·`render`(플레이어), `fpsVsEditor`, `eventsMatch`, `runtimeErrors`/`knownErrors`, `shotStats[].vsEditor`, `shotStats[].screen.vsShot/vsEditor`,
      `compare`(same/changed, 가장 큰 차이, 워터마크 영역, 파티클 안내), `player.build`/`buildRewrote`. 스위치 `-NoEditor`·`-NoBuild`·`-Paced`·`-Debugging`·`-PlayerArgs`.
    - selftest 1번 끝에 플레이어 실행, `attach-test.ps1 -Player`, install/uninstall 진입점에 `player.ps1`.
  - 조사하며 찾은 것:
    1. **첫 프레임이 달랐다**: 에디터는 게임을 한 프레임(dt 0.02) 돌린 뒤 러너를 띄우고 둘째 프레임도 0.02 s, `time` 0.02에서 시계가 시작한다. 플레이어에서 러너를
       AfterSceneLoad에 띄우자 한 프레임 어긋나 매듭·링이 프레임당 회전만큼 달랐다(closeup 바뀐 픽셀 14%) → 첫 프레임 끝에 러너 + 둘째 프레임 0.02 s → 매듭·링 같음.
    2. **플레이어는 첫 씬의 파티클을 한 스텝 앞서 시작한다**: AfterSceneLoad에 이미 `time=0.02`(에디터 0). 로드 시점 `Time.deltaTime`이라 BeforeSplashScreen부터
       `timeScale=0`도, `Simulate(0, restart)`(프리웜을 다시 돌려 더 달라짐)도 안 됐다 → 비교에 안내만(`compare.note`). 샘플은 파티클 둘레만 다르다.
    3. **G3-12**: 같은 프레임의 실제 화면과 하네스 캡처가 매듭 테두리(화면 흰·청록, 캡처 분홍)와 불씨 밝기에서 달랐다(바뀐 픽셀 0.99%, 평균 1.33).
       URP `CreateRenderTextureDescriptor`: 대상 텍스처가 있으면 그 형식이 중간 색 버퍼를 대신한다 → 8비트 sRGB 캡처 RT가 톤 매핑·블룸 전에 HDR을 잘랐다.
       캡처 RT = 그 카메라의 URP 색 형식(B10G11R11/RGBA half, `MakeRenderTextureGraphicsFormat`과 같은 규칙) → 8비트 sRGB로 `Blit`해 읽기 → 0.06%(워터마크뿐),
       워터마크를 빼면 `same`(평균 0.49). 샘플 기준 이미지 3장 갱신(closeup 밝기 67.7 → 72.6). `_Time`은 원인이 아니었다(LateUpdate의 전역 값은 이전 프레임이지만
       URP 요청이 카메라마다 현재 시간을 넣음, 바꿔도 결과 같음).
    4. 플레이어 기본 D3D12(에디터 D3D11): `-force-d3d11`로도 에디터 비교 수치가 같았다(API 차이 아님). 플레이어끼리 몇 픽셀(최대 97, 비율 ≤ 1e-5) — `same`.
    5. 개발 빌드는 화면 오른쪽 아래에 "Development Build"를 그린다(back buffer에 들어감) → 화면 비교에서 170x28 px 제외. 두 렌더(다른 카메라·프로세스)는 URP 디더링이
       달라 평균 차이 ~0.5 → 이 비교들은 평균 1까지 `same`.
    6. 개발 빌드 플레이어는 최적화 코드라 런타임 에러 줄이 어긋났다(주입 71행 → 78행, 파일·모듈·메서드는 맞음) → `-Debugging`(Script Debugging 빌드)이면 71행
       (fps ~350 vs ~430). 스택이 절대 경로(`(at C:/.../Assets/X.cs:78)`)라 파서를 넓혔다.
    7. 빌드가 설정 파일을 다시 쓴다(하네스 없이도): URP 전역 설정의 런타임 목록, 기본 Volume 프로필(새 필드, 스크립트 없는 컴포넌트 제거), PlayerSettings의 Standalone
       배칭 — 한 번 쓰면 안정 → 샘플은 빌드 뒤 상태로 커밋. Input System은 설정 에셋을 Preloaded Assets에 넣었다 메모리에서만 빼서 디스크는 에디터가 저장할 때까지
       바뀐 채 → 빌드 뒤 `SaveAssets`(`SaveAssetIfDirty(PlayerSettings)`·`SaveToSerializedFileAndForget`은 파일을 쓰지 않았다). Unity는 `Library/` 안으로 빌드를 거부한다.
    8. **G3-13**: 6.6 새 클론의 플레이어·에디터 모두 `render.batches`·`drawCalls`가 0이었다 — 6.6에는 Render 분류의 `Batches Count`·`Draw Calls Count`가 없고
       (종류별 `… Draw Calls Count`로 나뉨) 그 이름의 `StartNew(ProfilerCategory.Render, …)`가 UI Toolkit의 같은 이름 카운터에 붙었다(W7까지 6.6 루프도 0) →
       Render 분류에서 이름으로 찾고 없으면 draw call = 종류별 합, batches = null.
    9. 6.0 새 클론(6.3 샘플을 6.0으로)은 URP가 플레이어 빌드를 거부했다("UniversalRenderPipelineGlobalSettings ... is not at last version": 커밋된 전역 설정 에셋 버전 10,
       URP 17.0의 마지막 8 — 내려 쓰지 않음). 실패한 빌드가 `Assets/Resources/PerformanceTestRun*.json`을 남겨서 → `harness_player_plan`이 URP의 같은 검사
       (`IsAtLastVersion`, 리플렉션)를 빌드 전에 하고 `urpStale`로 알린다. selftest는 에디터가 프로젝트의 커밋된 버전보다 오래되고 이 이유일 때만 건너뛴다(6.0 프로젝트
       자체는 Fluid-Sim으로 확인).
    10. **증분 빌드가 앞선 출시 빌드의 플레이어 데이터를 썼다**(Fluid-Sim, 6.0): 개발 빌드가 "player data was not rebuilt"로 `ScriptingAssemblies.json`을 출시 빌드
        것 그대로 두어 `Harness.Runtime.dll`이 로드되지 않았다 → 플레이어가 시나리오 없이 게임만 돌다 시간 초과. 빌드 뒤 그 목록을 확인하고 없으면 `CleanBuildCache`로
        다시(`player.build.cleanRebuild`). 그 시간 초과 보고에서 `Get-Content` 줄의 PS 속성을 `ConvertTo-Json -Depth 20`이 펼치느라 `player.ps1`이 몇 분씩 멈췄다
        → `Read-HarnessLogTail`. 플레이어 로그에 하네스 진행 줄, stderr에 단계.
    11. G3-12의 짝: URP는 대상 텍스처가 있는 카메라의 MSAA도 그 텍스처의 샘플 수로 정한다 → 1샘플 캡처 RT가 MSAA 게임(BagelGame 2x)의 가장자리를 계단으로 찍었다
        (같은 프레임 화면 비교에서 가장자리만 0.42%) → 캡처 RT = 카메라의 MSAA, 읽기 전에 해제.
    12. 개발 빌드는 PlayerConnection이 네트워크에서 기다려 새 exe 경로마다 Windows 방화벽이 허용을 묻는다(실행에는 상관없음, 프로젝트당 한 번).
    13. **찾아 준 게임 버그**: Fluid-Sim은 입자 색 그라디언트를 에디터 전용 `OnValidate`에서만 만들어 플레이어의 입자가 회색이다 — 에디터 루프는 녹색, `player.ps1`은
        `vsEditor` 14%·2색 `blank`(`stage=shots`), 플레이어 fps 204 vs 에디터 213(GPU 계산 셰이더라 비슷).
  - 검증: 매트릭스 1–8 녹색(에디터 트리, 최종 코드 7.0분; 1번의 플레이어 단계: 녹색, 같은 events, 플레이어 468.2 fps vs 에디터 129.7, 샷 vsEditor 가장 큰 차이
    0.65%/평균 1.42, 화면 vsShot `same` 평균 0.49, 작업 트리 그대로). 9: 새 클론 6.3 녹색(루프 3회 `345ba0d7`·기준 이미지 same=3 — 새 Library에서도 새 기준 이미지와 픽셀까지, selftest 1–8,
    클론의 첫 플레이어 빌드 104.7 s·415 fps), 6.0 녹색(`c8561a2d`; 플레이어 단계는 URP 다운그레이드로 빌드 전에 거부돼 건너뜀 — 9번), 6.6 녹색(`7cda8899`; 플레이어 414.3 fps vs 에디터 98.7, 에디터 비교 수치가 6.3과 같음, 빌드가 쓴
    6.6 직렬화(`GraphicsSettings`·`ProjectSettings`)는 보고만 — selftest `final.versionRewrites`).
    10: BagelGame `-Player` 녹색(플레이어 727.9 fps vs 에디터 134.5 ×5.4, 같은 프레임 화면 = 캡처 3/3, 출시 빌드에 `Harness.*` 없음, 제거 뒤 깨끗), Fluid-Sim 녹색
    (기존 절차; `-Player`는 13번의 게임 버그로 `stage=shots`가 맞다), 사내 프로젝트 A 녹색(`brd-attach.json`, `6664b723`; `-Player`는 활성 타깃 Android로 거부).
    샘플 수동: 세로 720x1280 창(2560x1440 모니터)도 창 = 캡처 크기, HUD·토스트·버튼 배치가 에디터 합성과 같음(차이는 파티클뿐). 주입한 런타임 예외가 플레이어에서
    `stage=runtime`, `Assets/Game/Smoke/SmokeModule.cs`, 모듈 Smoke(최적화 78행 / `-Debugging` 71행).
  - 측정값(이 머신, RTX 4060 Ti, 1280x720 창): 첫 개발 빌드 116 s(에셋 쓰기 = 셰이더 81 s), 증분 3–12 s(스크립트만 바뀌면 ~7–12 s), 189 MB. 플레이어 시작 ~2.3–3.2 s
    (splash 포함, 시계는 splash 뒤), 시나리오 3 s → 플레이어 5.6–6.3 s. `player.ps1` 한 바퀴(새로 연 에디터, 3회) 15.4–17.4 s = 에디터 루프 4.1–4.5 s + 증분 빌드 4.9–6.8 s + 플레이어 5.4–6.0 s
    + 비교 0.4 s, `-NoBuild` 11.4 s. 그때 플레이어 452–516 fps(p95 2.8–3.6 ms) vs 창 에디터 105–120(p95 ~11 ms) → ×3.8–4.9(개발 중 여러 번 398–468 fps),
    batches 69 / SetPass 54(D3D12) vs 65 / 50. 루프(코드 변경 없음)는 4.08–4.37 s(W7 3.82–3.90 s; 샷 한 장 ~95 ms 중 HDR 변환은 수 ms, 나머지는 이날의 편차).
  - 남은 것: 출시(비개발) 빌드 성능(개발 빌드의 프로파일러 마커가 켜져 있음; `AGENTHARNESS_RUNTIME` 출시 빌드로 도는 옵션은 없다), 첫 씬 파티클 한 스텝(엔진),
    데스크톱 타깃·Windows만(P-3), IL2CPP 플레이어는 재지 않음, TAA처럼 앞 프레임을 쓰는 효과는 캡처 카메라에 이력이 없다. 사내 프로젝트 A(활성 타깃
    Android)는 `player.ps1`이 빌드 전에 거부해 G3-8의 완료 기준은 확인하지 못했다(→ W16, 위 G3-8·G3-16 해결).

- [x] **G2-2 GUI 에디터가 떠 있어야 하고, 모달 다이얼로그가 뜨면 멈춘다** · **G1-2 빌더가 에디터 안에서만 실행된다** · **G5-1 에디터 1개 → 루프가 직렬화된다**
  (+ **G2-4** 측정, 2026-09-30, W7)
  - 현상(전): 루프는 사람이 쓰는 GUI 에디터 하나에 붙었다. `open.ps1`이 `-automated` 없이 띄워서 에디터의 모달 대화상자가 메인 스레드를 막을 수 있었고
    (Pipeline 디스크립터의 `info` 경고), 에이전트가 여럿이면 뮤텍스로 줄을 섰다(두 번째 루프 3.55 s 대기). 새로 연 에디터는 첫 응답 뒤 Debug 코드 최적화로
    한 번 더 컴파일했다(재시작 ~30 s).
  - 조사: (1) `-automated`(Unity 명령줄 인자, `IsHumanControllingUs: 0`)면 `EditorUtility.DisplayDialog`가 1 ms 만에 `false`, `DisplayDialogComplex`가 1(취소)을
    돌려준다(eval로 확인, 로그 없음). Cecil로 에디터 DLL을 훑어 보니 `isHumanControllingUs`는 창 배치 저장·닫기 전 저장 확인·검색 모니터·라이선스 UI에서 쓰인다.
    (2) 상주 batchmode 에디터(`-batchmode`, `-quit`·`-nographics` 없음)가 D3D11(RTX 4060 Ti)로 렌더하고 Pipeline 서버(디스크립터 `mode: batchmode`)·플레이 모드·
    오프스크린 캡처·UI Toolkit 합성이 모두 된다 → GUI 에디터 N개가 아니라 창 없는 에디터로 갔다. (3) 같은 프로젝트는 에디터 하나만 열 수 있으니 복제 프로젝트가
    필요한데, `Library/`를 통째로 복사한 worktree(1.9 GB, robocopy 7–11 s)는 에셋을 다시 임포트하지 않고 스크립트만 다시 컴파일했다(경로가 바뀌어 ~17 s). Pipeline
    포트는 7800–7849에서 자동으로 골라 에디터 여럿이 충돌하지 않는다.
  - 발견: 창 없는 에디터의 **첫 캡처에서 매듭이 노랑·흰색, 선돌이 검정**(O-11) — 같은 머티리얼의 아치는 정상이라 메시의 첫 그리기이고, 같은 프레임의 두 번째
    그리기는 정상, 하늘만 보는 사전 렌더·`-force-gfx-mt`로는 안 고쳐짐. 유휴 창 없는 에디터가 초당 ~6만 틱(1.2코어). `-automated` 창 에디터는 시작 때 컴파일 에러가
    있으면 묻지 않고 Safe Mode(Pipeline 없음, 백그라운드에서 고쳐도 안 나옴), batchmode는 종료(코드 1), `-batchmode -ignoreCompilerErrors`는 마지막으로 성공한
    어셈블리로 뜬다. 그때의 컴파일 에러는 하네스가 로드되기 전이라 아무 데도 없고, 바뀐 게 없으면 `recompile`이 다시 컴파일하지 않는다.
  - 방법(G2-2, `Tools~/open.ps1`·`Harness.psm1` `Get-HarnessEditorArguments`): 에디터를 항상 `-automated -debugCodeOptimization`으로 띄운다(`-Interactive`면
    `-automated` 없이 — 사람이 쓰는 에디터). `-Headless` = `-batchmode -ignoreCompilerErrors`(창 없음). 창 에디터가 Safe Mode면(창 제목) `open.ps1`과 루프가
    `safeMode` + 에디터 로그에서 읽은 `compileErrors`(file·line·module)를 보고한다(`quit.ps1 -Force` → 고치고 `open.ps1`). 준비 대기 3 → 2 s(Debug 재컴파일이 없다).
    - `Editor/HarnessHeadless.cs`: 에디터 모드(`window`/`headless`, `automated`)를 `harness_ping`과 report `editor`에. 창 없는 에디터는 할 일 없는 틱(플레이·컴파일·
      임포트 아님)마다 1 ms 자고 이 프로세스의 Windows 타이머를 1 ms로(`timeBeginPeriod`; 15.6 ms 기본이면 명령이 틱을 기다려 ping 17 → 32 ms). `HarnessCodeOptimization`·
      핫 루프 워밍업이 batchmode라고 건너뛰던 것을 `-quit` 한 번짜리만 건너뛰게.
    - `Runtime/HarnessCapture.cs`: batchmode면 카메라들을 한 번 버리고 다시 그린다(O-11 우회). `ScenarioRunner`: 창 없는 에디터의 `"screen"` 샷은 즉시 에러.
      report: 창 없는 에디터면 `render` null, `fps.note`(Game 뷰가 그리지 않는 프레임).
    - `HarnessConsole`: 시작할 때 컴파일이 실패해 있고 이 프로세스가 본 에러가 없으면 세션에 한 번 `RequestScriptCompilation()` → 루프가 정확한 줄을 보고한다.
  - 방법(G5-1·G1-2, `open.ps1 -Own`): 에이전트 worktree에 에디터 트리 `Library/`의 사본을 두고(에디터 트리의 락 + 그 에디터 idle; 임시 폴더에 복사 후 이름 변경;
    Pipeline 디스크립터·`Library/Harness`·락·pid 파일은 빼고) 그 worktree만의 창 없는 에디터를 띄운다(`-Window`면 창). `Library/`가 있는 worktree는 이미 자기
    프로젝트로 풀리므로(`Resolve-HarnessEditorRoot`) `loop`·`uc`·`quit`·`compile-check`가 자기 에디터·자기 락을 쓰고, `submit.ps1`·`land.ps1`만
    `Use-HarnessIntegrationRoot`로 메인 worktree(에디터 트리)에 붙는다(루트에 딸린 경로를 `Set-HarnessEditorRoot` 한 곳에서). report `editor.own`.
    사본은 에디터 트리의 Unity 버전으로 연다 — 6.0 새 클론(`-UnityVersion`으로 연 에디터 트리)의 첫 매트릭스에서 worktree가 커밋된 `ProjectVersion.txt`(6.3)로
    6.0 `Library` 사본을 열어 업그레이드하고(열기 72 s) fingerprint가 6.3 값이 되어 6번이 빨갰다.
  - 검증(이 머신): 창 없는 에디터 루프 3회 녹색·fingerprint `345ba0d7…`·events 같음, 첫 루프부터 기준 이미지 `same=3`(`meanDiff` 0.45–0.49, 최대 2 — 디더링 순번,
    창 없는 에디터끼리는 픽셀까지 같음). 유휴 CPU 120 → ~8 %. 창 없는 에디터가 컴파일 에러로 시작 → `open.ps1` 녹색 + `compileFailed`, 루프 `stage=compile`
    `SmokeModule.cs:64 [Smoke]` → 고치면 녹색. 창 에디터가 컴파일 에러로 시작 → `open.ps1` 6.7 s 만에 `safeMode` + 같은 에러, `quit.ps1 -Force` → 녹색.
    `-Own` worktree(에디터 트리 창 에디터가 떠 있는 채로): 사본 11.4 s, 준비 28.7 s, 첫 루프 9.8 s(빌드 캐시 없음) `same=3`; 에디터 트리 루프와 동시에 두 번 →
    둘 다 락 대기 0.00–0.01 s, 창 4.4–4.9 s · 창 없음 2.7–2.9 s, fingerprint·events 같음; 그 worktree에서 `submit.ps1 -Module Smoke`가 에디터 트리(창 에디터)에서 녹색.
    측정(G2-4, 새로 연 에디터 각 3회, 위 "기준선"): 창 없는 에디터 변경 없음 2.36–2.50 s(창 3.82–3.90), C# 1줄 6.75–7.10 s(창 9.27–9.67: 리로드 2.73–2.77 → 2.07–2.10 s,
    리로드 뒤 ~0.9 s 없음), `-Hot` 1.96–2.18 s(창 3.14–3.42), 셰이더 2.47–2.68 s(창 3.94–4.27). 재시작 창 14.3 s · 창 없음 12.1 s(전 ~30 s).
    - selftest 1–8 녹색 424 s(`345ba0d7…`, 6번 62.6 s: 새 검사 12개 — 위 "검증 매트릭스" 6).
    - 9(커밋된 코드): 새 클론 6000.3.11f1 녹색 509 s(`345ba0d7…`, 기준 이미지 `same=3`), 6000.0.84f1 녹색 480 s(`c8561a2d…`, 67.8/60.8/48.5),
      6000.6.3f1 녹색 565 s(`7cda8899…`, 67.9/60.8/48.5) — 세 버전 모두 새 클론 옆의 전용 에디터 6번 녹색(첫 샷이 에디터 트리 샷과 `same`, `meanDiff` 0.45–0.49),
      루프 경고·에디터 에러 0. 창 에디터 열기(`-automated`, 새 Library 첫 임포트) 53–89 s.
    - 10(`-automated -debugCodeOptimization`으로 여는 `open.ps1`): BagelGame 녹색 49.0 s(`619be553…` = W5, 출시 빌드 `Managed/` 132개·`Harness.*` 0개),
      Fluid-Sim 녹색 29.7 s(`54880f05…`, 103개·0개), 사내 프로젝트 A 녹색 80.1 s(`6664b723…`, `brd-attach.json`, Domain Reload 켜짐) — 에디터 열기가 Debug 재컴파일이
      없어 W6c보다 빨라졌다(30.3 → 20.1 s, 16.5 → 13.1 s, 51.5 → 37.3 s). fingerprint·`git status` 그대로.
  - 남은 것: 에디터 몇 개를 여러 worktree가 나눠 쓰는 풀(지금은 worktree = 에디터), worktree 전용 에디터의 첫 열기 스크립트 재컴파일·캐시 없는 빌드(~30 s + 10 s),
    창 없는 에디터의 성능 수치(렌더 없음 → W8 플레이어 `harness_perf`), 자동으로 닫힌 대화상자 보고(O-10, 6.7+), batchmode 첫 그리기 신고(O-11).
    도메인 리로드 ~2.1 s는 Unity 쪽(G2-4 `[~]`).

- [x] **G4-1 CPU(C#) 텍스처 베이크가 느리다** · **G4-4 절차적 라이브러리가 기본 수준이다** · **G3-11 빌더가 메시를 바꾼 첫 루프의 샷이 옛 메시를 그린다**
  (2026-09-30, W6c)
  - 현상(전): 지형 알베도·노멀 512²(320 m → 텍셀 0.63 m, 가까이서 흐림)를 C#으로 굽는 데 952 ms(Debug, PNG 인코딩 제외; 빌드 캐시로 가림, 1024²면 4배).
    절차적 라이브러리는 격자·구·토러스·매듭·상자 메시와 2D 노이즈·텍스처 베이크뿐이었다.
  - 방법(G4-1, `Editor/Build/BuildContext.Bake.cs`, `Shaders/HarnessBake.hlsl`·`HarnessNoise.hlsl`): `ctx.BakeTexture(name, w, h, shader, setup, sRGB, normalMap, wrap,
    mipmaps, pass)` — 베이크 셰이더 한 패스를 `CommandBuffer.DrawProcedural`(정점 ID로 만든 전체 화면 삼각형)로 RT에 그리고 `AsyncGPUReadback` → PNG → `SaveTexture`.
    Direct3D·Metal·Vulkan은 리드백 첫 줄이 위라 뒤집어 `uv.y = 0` = Texture2D 0행(`TextureBaker`와 같은 (u, v)). 셰이더는 선형 색을 돌려주고 sRGB 베이크는 sRGB RT라
    PNG가 sRGB. `ctx.FloatTexture(grid)`는 CPU 값(높이장)을 RFloat 입력으로. `HarnessNoise.hlsl`은 C# `Noise`의 해시·32방향 기울기·옥타브 시드를 그대로 옮겨서
    **CPU로 만든 메시와 GPU로 구운 텍스처가 같은 무늬**다(`Noise_Fbm`·`Noise_Ridged` 차이 ≤ 0.0022 = 8비트 반올림), 타일링용 `…Tiled` 변형 포함.
    - 캐시·fingerprint: 입력(셰이더 소스와 include를 줄바꿈 정규화해 해시, 속성 값, 입력 텍스처 내용, 크기·패스)의 해시를 임포터 `userData`에 두고, 같으면 그리지 않는다
      (`build.bakes`/`bakesSkipped`). **fingerprint는 PNG 대신 이 해시** — GPU 결과의 끝 비트가 GPU·드라이버마다 다를 수 있어서(모양은 기준 이미지가 본다). 소스 해시는
      `GetAssetDependencyHash`가 아니라 파일 내용이라 클론·머신이 달라도 같다. `ctx.CacheHit`의 키에도 모듈 폴더의 셰이더 소스와 하네스 `Shaders/`를 넣었다(베이크 셰이더만
      고쳐도 다시 굽는다).
    - 비용(같은 에디터): 그리기 + 리드백 1024² 9 ms · 2048² 28 ms · 4096² 105 ms. 남은 비용은 PNG 인코딩(73 / 259 / 907 ms)과 임포트 압축이다 — 1024² 알베도 293 ms,
      노멀 331 ms, 512² 디테일 147·180 ms(인코딩·임포트 포함), CPU 높이장 257² 137 ms. 같은 입력이면 4 ms.
    - `LitSettings` 디테일 맵(`DetailAlbedoMap`(선형, 0.5 중립), `DetailNormalMap`, `DetailNormalScale`, `DetailTiling`, `DetailMask`; sRGB 디테일 알베도는 경고).
    - 샘플 지형: 메시는 그대로 CPU, 텍스처는 GPU(`Assets/Game/Stage/Shaders/TerrainBake.shader` 4패스) — 1024² 알베도·노멀(텍셀 0.31 m) + 512² 타일링 디테일(4 m마다,
      텍셀 0.8 cm). 알베도 색·섞기는 CPU 베이크와 같은 식(sRGB 수로 계산해 선형으로 반환).
  - 방법(G4-4, `Runtime/Procedural/Sdf.cs`·`Spline.cs`·`Scatter.cs`, `MeshBuilder`을 partial로): `Sdf`(구·상자·둥근 상자·캡슐·토러스·원기둥·평면, 합·차·교,
    부드러운 합·차·교, `Normal`), `MeshBuilder.FromSdf`(서피스 네트: 부호가 바뀌는 칸마다 모서리 교차점 평균 → 기울기로 면 위에 한 번 투영, 교차 모서리마다 쿼드,
    감김은 바깥(양수) 쪽으로; 박스 투영 UV), `Spline`(구심 Catmull-Rom, 호 길이 표로 `Evaluate`/`Tangent`), `MeshBuilder.Tube`(회전 최소 프레임, 닫힌 튜브의 남는
    비틀림 분배, 열린 끝 뚜껑), `Scatter.Poisson`(Bridson), `MeshBuilder.Rock`(노이즈로 민 아이코스피어, 평면 셰이딩)·`Icosphere`, URP 데칼(`ctx.Decal`,
    `ctx.DecalMaterial`; 빌드 뒤 렌더러에 `DecalRendererFeature`가 없으면 경고).
    - 샘플 소품(`Assets/Game/Stage/Builders/StagePropsStep.cs`): 선돌 9개(둥근 상자 − 구 홈 + 3D 노이즈, SDF 3종), 언덕의 바위(포아송 5.5 m, 플래토·가파른 곳 제외,
      가까운 것만 분할 2 → 한 메시 14만 정점), 받침대 뒤의 아치(스플라인 튜브, 발 두껍게), 받침대 둘레의 룬 원(GPU로 구운 텍스처의 URP 데칼). 모두 `CacheHit`·베이크
      캐시로 웜 빌드에서는 다시 만들지 않는다. 렌더 설정에 `DecalRendererFeature` 추가.
  - 발견(G3-11): 바위 모양을 바꾸자 **바꾼 뒤 첫 루프의 샷은 옛 바위, 둘째 루프부터 새 바위**였다(fingerprint는 둘 다 새 값). 제자리 덮어쓰기
    (`EditorUtility.CopySerialized`)가 직렬화 데이터만 바꾸고 엔진이 그리는 메시 데이터는 두어서, 플레이 모드가 에셋을 다시 읽기 전까지 옛 모양이었다(`UploadMeshData`로도
    안 됨). 빌더가 메시를 만드는 모든 프로젝트에 W1부터 있던 문제로, 변경 직후 에이전트가 보는 바로 그 루프가 틀렸다(기준 이미지는 루프 2·3을 비교해 놓쳤다) →
    `SaveAsset`이 메시는 Mesh API(`Clear` → `SetVertices`/`SetNormals`/`SetUVs`/`SetIndices`/`bounds`), 큐브맵은 면·밉별 `SetPixelData` + `Apply`로 덮어쓴다.
    같은 재현(바위 2배)에서 루프 1 = 루프 2(차이 없음). selftest 4번에 "아치 두께 2배 → 첫 루프가 golden `changed`이고 다음 루프와 같은 값" 단계를 넣었다.
  - 조사하며 확인한 것: 아이코스피어 표의 감김을 "반시계"로 짐작해 뒤집었더니 바위가 속 빈 껍데기로 보였다(면 방향 검사 0%) → 방향 검사를 selftest에 넣었다.
    ShaderLab `Int` 속성은 float라 `SetInteger`가 충돌한다(`Integer`로). `CacheHit`의 존재 확인과 `LoadAsset`이 매 빌드 큰 메시를 디스크에서 다시 읽는다(29만 정점
    ~90 ms → 먼 바위를 덜 쪼개 14만, 소품 스텝 ~60 ms).
  - 검증(이 머신): 생성물(`Assets/Generated/Stage`·`Smoke`)을 지운 뒤 루프 1–3: fingerprint 같음(`345ba0d7…`), 세 샷 모두 픽셀까지 같음(첫 빌드 5.97 s + 파이프라인 전환
    리로드 2.22 s). 기본 루프 3회 픽셀까지 같음. 샷 67.8/60.8/48.5(W6b 62.9/56.1/45.9 — 선돌·바위·아치·데칼, 선명한 지형). 6.3 기준 이미지를 갱신했다.
    루프(새로 연 에디터, 위 "기준선"): 변경 없음 3.73–3.91 s(빌드 0.61–0.64 s — W6a 0.51–0.53 s에 소품 스텝 ~60 ms·큰 메시 fingerprint), C# 1줄 9.53–9.69 s,
    `-Hot` 3.35–3.45 s. 렌더 batches 67 / SetPass 52 / tris 124만(그림자 캐스케이드 포함), fps ~131.
    - selftest 1–8 녹색 401.9 s(`345ba0d7…`, 1번 79.3 s에 GPU 베이크·절차적 검사 6개, 4번 73.3 s에 메시 변경 단계).
    - 9(커밋된 코드): 새 클론 6000.3.11f1 녹색 451.7 s(`345ba0d7…` = 메인 트리, 새 Library의 첫 루프부터 기준 이미지 `same=3` — GPU 베이크·SDF 메시를 처음 만든
      루프에서도), 6000.0.84f1 녹색 430.8 s(`c8561a2d…`, 샷 67.8/60.8/48.5 = 6.3), 6000.6.3f1 녹색 456.8 s(`7cda8899…`, 67.9/60.8/48.5) — 세 버전 모두 W6c 검사 8개
      녹색, 루프 경고·에디터 에러 0.
    - 10: BagelGame 녹색 56.8 s(`619be553…` = W5, 출시 빌드 `Managed/` 132개·`Harness.*` 0개), Fluid-Sim 녹색 31.8 s(`54880f05…`, 103개·0개), 사내 프로젝트 A 녹색
      98.7 s(`6664b723…`, `brd-attach.json`) — 붙인 프로젝트는 빌드 스텝이 없어 새 헬퍼·메시 덮어쓰기 경로를 타지 않는다(fingerprint 그대로).
  - 남은 것: 하늘(구름)과 씬 반사 프로브 → G4-5. 컴퓨트 셰이더 베이크(여러 출력, 반복 시뮬레이션)는 없다 — 한 패스 = 텍스처 하나. 식생(풀·나무)은 스캐터 + 메시로
    만들 수 있지만 인스턴싱 렌더러가 없어 수천 개면 한 메시가 커진다. SDF 메시는 빌드 때 칸 수만큼 SDF를 부른다(소품 스텝 첫 생성 ~0.5 s — 베이크 3장 포함, 이후 캐시).

- [x] **G1-4 UI Toolkit 경로는 있지만 얇다** · **G3-10 UI Toolkit의 transition·타이머가 실시간이라 UI가 움직이는 동안의 캡처가 매번 다르다** (2026-09-30, W6b)
  - 현상(전): UXML/USS + `ctx.UIDocument()` + 기본 테마만 있어서 HUD마다 색·크기·판 모양을 새로 썼고(스모크 HUD의 USS 44줄), 재사용 컨트롤·바인딩 예제가 없었다
    (모듈이 라벨을 찾아 `text`를 넣음). 폰트는 확인해 보니 문제가 아니었다: 한글·일본어·중국어 라벨이 기본 테마로 그대로 찍혔다(에디터가 OS 폰트로 대신 그림).
  - 방법(G1-4): 패키지 테마 `UI/DefaultRuntimeTheme.tss`가 `HarnessKit.uss`를 가져온다 → `ctx.UIDocument`의 모든 패널에서 쓸 수 있다.
    - 변수(`:root`): 배경·선·글자·강조 2종·good/warn/bad 색, 반경, 간격, 글자 크기 3단, 페이드 시간. 서브트리에서 다시 정의하면 그 아래만 바뀐다.
    - 클래스: `ah-panel`(`--accent`), `ah-row`, `ah-title`/`ah-key`/`ah-value`/`ah-hint`, `ah-button`(`--ghost`, hover·active·disabled), `ah-gauge`(`--good/--warn/--bad`),
      `ah-toast-stack`/`ah-toast`(`--shown`, `--good/--warn/--bad`; opacity·translate transition).
    - 컨트롤(`Runtime/UI/`, `Harness.UI`, Unity 6 `[UxmlElement]`): `Gauge`(`value`/`max`, UXML 속성·`[CreateProperty]`로 바인딩 가능; 채움과 나머지를 flex-grow로 나눔),
      `ToastStack.Show(text, seconds, kind)`(첫 레이아웃 뒤 `--shown`을 붙여 transition, 패널 타이머로 페이드 아웃·제거).
    - 샘플: 스모크 HUD를 킷으로 다시 씀 — 모양은 킷 클래스, `SmokeHud.uss`는 배치 3개만. 라벨·게이지는 `SmokeHudData`(`[CreateProperty]`)에 UXML
      `<Bindings><ui:DataBinding …/></Bindings>`로 묶이고 모듈은 값만 바꾼다(int → 라벨 text 기본 변환). 한 바퀴 진행 게이지, 방향 전환 때 토스트
      ("COUNTER-CLOCKWISE", 1.2 s), 클릭하면 방향을 바꾸는 REVERSE 버튼(`ah-button--ghost`).
  - 발견(G3-10): 토스트를 페이드 중에 찍으려다 **USS transition·`schedule` 타이머가 실시간**임을 확인했다 — 1초 opacity transition을 같은 게임 시간(0.903 s)에 재니
    실행마다 0.732/0.786/0.792(그 사이 실시간 0.136–0.166 s, fps 142–173). 고정 시간 간격은 게임 시간만 바꾸고, `Time.unscaledTime`도 캡처 간격과 상관없이
    실시간이다(그걸로 바꿔도 0.745/0.779/0.786). 게임의 UI Toolkit 애니메이션이 캡처·기준 이미지를 흔드는 구멍이다(붙인 프로젝트 포함).
  - 방법(G3-10, `Runtime/PanelClock.cs`): 패널마다 시간 함수(`BaseVisualElementPanel.TimeSinceStartupFunc`, 내부)가 있어서, `fixedDeltaTime`이 있는 시나리오
    동안 러너가 매 프레임 `fixedDeltaTime`씩 미는 시계로 바꾼다 — 그 패널의 원래 시간에서 이어지고(되돌아가지 않음), `timeScale`과 무관(멈춘 게임의 메뉴도
    움직임). 런타임 패널은 `UIElementsRuntimeUtility.GetSortedPlayerPanels()`로 매 프레임 찾아 새 패널도 걸고, 끝나면 원래 함수로 되돌린다. 결과
    `play.uiClock`(`mode` `frames`/`real`, `panels`, `scope`, `error` — API가 없는 버전이면 `real` + 이유, 실패 아님). 같은 측정이 0.450/0.450/0.450.
    6.0은 매트릭스 9에서 빨갰다: 패널별 `TimeSinceStartupFunc`가 6.1 이후 것이라(설치된 세 버전의 `UnityEngine.UIElementsModule.dll` 메타데이터로 확인) UI 시계가
    `real`이었고 토스트 캡처가 달랐다(`meanDiff` 0.21) → 6.0에서는 모든 패널이 공유하는 정적 `Panel.TimeSinceStartup`(ms)을 같은 방식으로 바꾼다(`scope` =
    `every panel`: 시나리오 동안 에디터 창 UI도 그 시계). 또 6.0은 편집 모드에서 런타임 패널의 바인딩을 갱신하지 않아 편집 모드 바인딩 검사가 0이었다(플레이
    중에는 됨, 샷의 LAPS·SPIN) → selftest의 바인딩 검사를 플레이 중 값으로 옮겼다.
  - 조사하며 확인한 것: 새 `[UxmlElement]` 컨트롤과 그 UXML을 한 번에 넣으면 루프의 `AssetDatabase.Refresh`가 컴파일 전에 UXML을 임포트해 `editorErrors`에
    "missing a UxmlElementAttribute"가 한 번 나오지만, 컴파일 뒤 다시 임포트돼 그 루프의 플레이부터 정상이었다. UI Toolkit 레이아웃은 패널의 물리 픽셀로
    반올림된다 — 이 머신의 Game 뷰(366x305, 배율 0.24, 1 px = 4.2 단위)에서는 25% 폭이 23.5%로 배치됐고(퍼센트·flex 모두), 캡처는 캡처 크기로 다시 배치해
    1280x720 격자를 따른다(게이지 값 0.749 → 캡처에서 0.762).
  - 검증(이 머신): 기본 루프 3회 픽셀까지 같음(루프 2·3 `same=3`), fingerprint 그대로 `1c6fa406…`(HUD는 생성 에셋이 아니라 fingerprint 밖, 기준 이미지가 본다),
    `play.uiClock` `frames/1`, 루프 3.53–3.54 s(같은 에디터). 버튼 클릭 → 토스트 페이드 인(1.15 s)·아웃(2.36 s) 캡처가 fps가 달라도(141–155) 두 번·세 번
    모두 maxDiff 0. 6.3 기준 이미지를 갱신했다(HUD에 게이지·버튼, horizon 샷에 토스트; 샷 62.9/56.1/45.9).
    - selftest 1–8 녹색 325.1 s(`1c6fa406…`, 주입 줄 64/71/87 — `SmokeModule`이 바뀌어 한 줄씩 밀림, 1번 54.4 s에 UI 킷·시계 검사 6개).
    - 9(최종 커밋): 새 클론 6000.3.11f1 녹색 407.3 s(`1c6fa406…` = 메인 트리, 첫 루프부터 기준 이미지 `same=3`, UI 시계 `runtime panels`), 6000.0.84f1 녹색
      381.8 s(`4ffb4440…`, 샷 62.9/56.1/45.9 = 6.3, UI 시계 `every panel`), 6000.6.3f1 녹색 402.8 s(`5ab10290…`, 63.0/56.1/45.9) — 세 버전 모두 UI 킷·시계 검사 6개 녹색.
      (첫 실행에서 6.0이 빨간 것은 위 "방법(G3-10)"의 6.0 항목.)
    - 10: BagelGame 녹색 60.1 s(`619be553…` = W5, `uiClock` `frames/2` — 이 게임의 월드 공간 UI Toolkit 패널 2개가 프레임 시계를 따름, 출시 빌드 `Managed/` 132개·
      `Harness.*` 0개), Fluid-Sim 녹색 31.3 s(`54880f05…`, 6.0이라 `every panel`, UI Toolkit 패널 0개, 103개·0개), 사내 프로젝트 A 녹색 98.1 s(`6664b723…`,
      `brd-attach.json`, `frames/1`; 6.0 대체 경로를 넣기 전 실행 — 패널별 경로는 그대로다).
  - 남은 것: 폰트 에셋 헬퍼는 없다 — 출시 플레이어·다른 OS의 한중일 글꼴은 그 OS에 달렸다. 킷은 HUD용 몇 가지뿐이다(슬라이더·토글·리스트·모달은 UI Toolkit
    기본 컨트롤에 킷 변수로 모양을 입힌다). `PanelClock`은 내부 API라 새 Unity 버전마다 selftest 1번이 확인한다(O-9와 같은 상시 항목).

- [x] **G1-3 파티클·애니메이션·타임라인용 코드 헬퍼가 없다** (2026-09-30, W6a)
  - 현상(전): `BuildContext`에는 메시·머티리얼·텍스처·Volume·UI·샷 헬퍼만 있었다. ParticleSystem은 모듈 구조체 수십 개를 하나씩 켜야 하고 기본값이
    결정적이지 않으며(자동 시드), 애니메이션은 AnimatorController(창에서 만드는 상태 기계 에셋)가 있어야 돌았다. 스모크 씬의 움직임은 모듈 코드(매듭 회전)뿐이었다.
  - 방법(파티클, `Editor/Build/BuildContext.Particles.cs`): `ctx.Particles(path, p => …)` + `ParticleSettings`(주 모듈·방출·버스트·형태·수명 동안의 색/크기/회전/
    속도·드래그·노이즈·렌더러; 범위·커브는 Unity 타입 `MinMaxCurve`/`MinMaxGradient`/`Gradient`/`AnimationCurve` 그대로). 만들 때 멈춘 상태에서
    `useAutoRandomSeed = false`, `randomSeed` = 모듈+경로의 해시(`ctx.Seed`), `cullingMode = AlwaysSimulate`(자동이면 화면 밖 루프 시스템이 멈춰 뒤 프레임이
    카메라가 본 것에 달라진다), `playOnAwake`. 원뿔은 기본으로 위(+Y)로(메뉴로 만든 파티클과 같게). `ctx.ParticleMaterial(name, m => …)` = URP Particles/Unlit,
    투명, `ParticleBlend`(Alpha/Premultiply/Additive/Multiply)·소프트 파티클·컬링을 설정하면 URP 검증이 블렌드·키워드·큐를 맞춘다. 텍스처가 없으면 생성한
    부드러운 점(`ParticleDot.png`, 64²)이라 네모가 아니다. 파티클 색은 8비트라(HDR 시작 색이 1로 잘림) 발광은 머티리얼 색(HDR)으로 준다.
  - 방법(애니메이션, `Editor/Build/BuildContext.Animation.cs`, `Runtime/ClipPlayer.cs`): `ctx.AnimationClip(name, c => …)` + `ClipBuilder` — `Position`/`Rotation`
    (Euler 도, `localEulerAnglesRaw`라 0 → 360이 한 바퀴)/`Scale`/`Float`(직렬화 이름, 예: Light `m_Intensity`)/`Color`(`material._EmissionColor` 등, 값은
    `SetColor`와 같은 의미)/`Active`/`Event`, 트랙마다 `.Linear()`·`.Constant()`(기본 Smooth = Clamped Auto), `Loop`. `AnimationUtility.SetEditorCurves`로 한 번에 넣고
    `.anim`으로 저장. `ctx.Animate(go, clips)` = 컨트롤러 없는 Animator(루트 모션 끔, `AlwaysAnimate`; 아바타는 둠 — 휴머노이드 클립용; 기존 컨트롤러는 지우고 경고)
    + `ClipPlayer`. `ClipPlayer`는 Animator에 출력하는 PlayableGraph(믹서 + 클립마다 입력, 게임 시간)로 `playOnEnable` 클립을 돌리고, `Play(name, fade)`(처음부터,
    앞 클립에서 선형 크로스페이드), `Stop()`, `Current`/`Time`/`IsDone`, `speed`. `AddComponent` 뒤에 `clips`를 넣어도 `Play`가 그래프를 다시 만든다.
    클립 이벤트는 `ClipPlayer.OnClipEvent` → `EventBus.Publish(new ClipEvent(name, go))`, 발행 수는 `play.events`에 `ClipEvent:<이름>`(EventBus에 이름을 따로 세는
    internal `Publish(evt, key)`).
  - 빌드 뒤 검사(`BuildContext.AfterSteps`, 빌드 스텝이 다 돈 뒤라 자식을 나중에 만들어도 된다): `Animate`한 클립의 바인딩마다 대상에서 경로·컴포넌트·속성을 찾는다.
    `AnimationUtility.GetEditorCurveValueType`은 없는 경로·컴포넌트·Transform 속성에 null을 주지만 **머티리얼 속성(`material._X`)은 아무 이름이나 풀린다**
    (`_BaseColr`도 Single) → 렌더러의 `GetAnimatableBindings`(셰이더가 가진 이름)와 대조. 트랙 단위로 경고 한 줄 + 공통 접두어가 긴 비슷한 이름
    (`material._BaseColr` → `material._BaseColor`). 그리고 씬을 첫 클립의 0초 포즈로 저장한다(`SampleAnimation`) → 편집 모드 캡처가 플레이 시작 모습.
  - 조사하며 확인한 것: HDR 머티리얼 색은 애니메이션 가능 목록에 `.x/.y/.z/.w`로, 보통 색은 `.r/.g/.b/.a`로 나온다. 둘 다 동작하지만 의미가 다르다 — `.r` 0.75는
    감마 → 선형(0.52, `SetColor`와 같음), `.x` 0.75는 그대로 → `ClipBuilder.Color`는 `.r`(빌더의 `LitSettings` 값과 같은 숫자), 검사는 둘 다 받는다.
    Playables(`AnimationClipPlayable`)도 애니메이션 이벤트를 부른다. `Playable.SetTime`을 한 번만 해서 0.78 s → 0으로 되감으면 그 사이 0.5 s 이벤트가 **다시 발행됐고**
    (1 → 2), 두 번 하면 발행되지 않았다(3 → 3) → `Play`는 두 번 한다.
  - fingerprint: `SerializedProperty`의 AnimationCurve는 키 개수만, Gradient는 아예 해시하지 않고 있었다 → 모든 키(시간·값·탄젠트·가중치)·wrap 모드, Gradient의 모드와
    키. 기존 씬에는 해당 값이 없어 W5 값(`78354e2e…`) 그대로였다. 그리고 **생성물 폴더를 지운 뒤 첫 빌드만 fingerprint가 달랐다**(`8e716e45` → 이후 `d994680b`):
    새로 만든 `.anim`은 파생 바인딩 캐시 `m_ClipBindingConstant`가 11개 채워져 있고 제자리 덮어쓰기 뒤에는 비어 있다 → 그 경로는 해시에서 뺐다(편집 커브는 해시함).
    새 클론의 첫 루프가 다음 루프와 같아야 하므로(매트릭스 9) 놓쳤으면 빨갰다.
  - 선택 모듈: `com.unity.modules.animation`·`particlesystem`을 끈 프로젝트에서도 컴파일되도록 `versionDefines`(`AGENTHARNESS_ANIMATION`·`AGENTHARNESS_PARTICLES`)로 가른다.
    `ClipPlayer`는 `Harness.Runtime`(개발 빌드 전용)이라 출시 빌드에는 `GameRoot`처럼 `AGENTHARNESS_RUNTIME`이 필요하다.
  - 샘플: `Assets/Game/Smoke/Builders/SmokeFxStep.cs` — 매듭 주위의 링 2개(`Halo/RingA`·`RingB`, 두께 0.03 토러스, 이미션 LitMaterial)를 `HaloOrbit` 클립
    (4 s 루프: 두 링이 반대로 한 바퀴씩 선형 회전, 루트 크기 1 → 1.06 → 1, 링 이미션 청록 ↔ 자홍, 2 s에 이벤트 `HaloHalfTurn`)으로, 받침대 위 원판에서 피어오르는
    불씨(`Embers`: 초당 40, 수명 2.5–4.5 s, 월드 공간, 노이즈, 수명 동안 노랑 → 빨강·페이드, 가산, Prewarm)를 모듈 코드 없이. 기본 루프의 `play.events`에
    `ClipEvent:HaloHalfTurn=1`이 더해졌다.
  - 검증(이 머신): 루프 3회 픽셀까지 같음(maxDiff 0)·fingerprint 같음. 연속 캡처(closeup 포즈, 15프레임마다 4장) motion 25.0/27.7/26.2 — 링 회전·색, 불씨 상승이 시트에 보임.
    편집 모드 픽스처: 오타 4종 → 경고 4줄, 선형 0 → 360(4 s)이 1 s에 90.0°, 곡선 16개, 파티클 1 s 시뮬레이션 두 번 49개 같은 위치, 가산 머티리얼 SrcAlpha/One·큐 3000.
    플레이 픽스처: `Rise`(0.5 s) 0.8 s에 `IsDone`·x=1.0, `Play("Hold", 0.4)` 0.2 s 뒤 x=2.00, 0.6 s 뒤 x=3.0, `ClipEvent:RiseEnd=1`, 그때 불씨 146개.
    빌드: FX 스텝 7.5–8 ms, 같은 에디터에서 FX 스텝을 빼고 0.47–0.50 s / 넣고 0.50–0.53 s. 렌더 batches 45.8 → 50.8, SetPass 42.8 → 47.8.
    샷 62.7/55.9/45.7(W5 60.3/55.6/45.7). 의도한 변경이라 6.3 기준 이미지를 갱신했다. 루프(새로 연 에디터, 위 "기준선"): 변경 없음 3.52–3.64 s,
    C# 1줄 8.88–9.39 s, `-Hot` 3.21–3.29 s(W5 3.00–3.06 s — 플레이 구간 2.42–2.50 s).
    - selftest 1–8 녹색 304.1 s(`1c6fa406…`, 1번 49.4 s에 G1-3 검사 8개).
    - 9: 새 클론(커밋된 코드) 6000.3.11f1 녹색 408.7 s(`1c6fa406…` = 메인 트리, 새 Library의 첫 루프부터 기준 이미지 `same=3`), 6000.0.84f1 녹색 384.6 s
      (`4ffb4440…`, 샷 62.7/55.9/45.7 = 6.3), 6000.6.3f1 녹색 444.6 s(`5ab10290…`, 62.8/56.0/45.8) — 세 버전 모두 selftest 1번의 G1-3 검사 8개 녹색(링·불씨가
      6.3과 같게 보임), 루프 경고 0, `git status`는 버전 전환 파일뿐.
    - 10(`-Source local`): BagelGame 녹색 60.5 s(`619be553…` = W5, 65.5, 출시 빌드 `Managed/` 132개·`Harness.*` 0개), Fluid-Sim 녹색 30.9 s(`54880f05…`,
      23.0/14.9/22.2, 103개·0개), 사내 프로젝트 A 녹색 98.7 s(`6664b723…`, `brd-attach.json` 부트 대화상자 37.3/37.4). 사내 프로젝트 A의 로비 시나리오
      (`brd-lobby-w3.json`)는 개발 서버 버튼을 눌러 원본과 공유하는 에디터 PlayerPrefs를 1로 바꾸는데, 이번 세션에서는 끝난 뒤 0으로 되돌리는 레지스트리 쓰기가
      허용되지 않아 아무것도 누르지 않는 부트 시나리오로 돌렸다. 세 프로젝트 모두 fingerprint가 W5와 같다(붙인 프로젝트는 에셋 임포트 해시로 fingerprint를 낸다).
  - 남은 것: 타임라인(TimelineAsset)을 코드로 만드는 헬퍼는 넣지 않았다 — Three.js의 `AnimationMixer`에 해당하는 것은 `ClipPlayer`이고, 여러 오브젝트의 순서는
    클립 + `ClipEvent` + 모듈 코드로 된다. 컷신 편집이 필요해지면 다시 본다. `ClipPlayer`는 한 번에 한 클립(+ 크로스페이드)이다 — 레이어·가산 블렌드·아바타
    마스크는 없고, 휴머노이드 클립은 아바타를 두기만 했지 검증하지 않았다. 편집 모드 캡처(`harness_capture`, `-NoPlay`)에는 파티클이 없다(시뮬레이션하지 않음).
    서브 이미터·트레일·라이트 모듈은 `ParticleSettings`에 없어 반환된 ParticleSystem을 직접 고친다.

- [x] **G2-3 도메인 리로드 직후 첫 `harness_build`가 ~2초 (JIT 워밍업)** · **G2-1 C# 1줄 수정에 ~9초** (2026-09-30, W5)
  - 재측정(G2-1 메모의 숙제, 에디터 1개): C# 1줄 루프 9.42 s = compile 4.18 s(Tundra 1.13 s + `Domain Reload Profiling` 2.53 s + 폴링) + build 2.12 s + play 2.55 s.
    "빌드 ~2 s"를 나눠 보니 **리로드 뒤 첫 메인 스레드 명령이 무엇이든 ~0.85–0.95 s**(`harness_ping`도 887 ms, 2 s 쉬고 보내면 15 ms)였고 빌드 자체의 콜드 비용은
    ~0.35 s(1.07 s vs 웜 0.73 s)였다. 임시 `[InitializeOnLoad]` 탐침으로 리로드 뒤 update 틱을 재니 첫 두 틱 사이가 916 ms이고 그동안 `update`·`delayCall` 콜백
    (Pipeline·Input System·URP 등 30여 개)은 모두 20 ms 미만 → Unity 네이티브 작업(창 다시 그리기로 보임)이라 하네스가 줄일 수 없다 → G2-4. 프로파일러
    (`ProfilerDriver.profileEditor`)로 보려던 시도는 autotick과 겹쳐 수만 프레임이 쌓이고 리로드 뒤 명령이 8 s 걸려 버렸다.
  - 방법(G2-3, 빌드 자체): `harness_build`에 단계별 시간 `build.phases`(check/settings/steps/cleanup/save/lighting/fingerprint)를 넣고 재서 둘을 고쳤다.
    - fingerprint 245 ms 중 ~225 ms가 메시 정점을 `ToString("R")`로 문자열화하는 데 들었다(지형 40,401 + 매듭 10,593 정점) → 정점·인덱스의 원시 바이트를 SHA-1
      (`SceneFingerprint.HashAsset`) → 28–40 ms. **fingerprint 값이 바뀌었다**(해시 입력이 바뀜; 6.3 `4dc9c80b…` → `78354e2e…`, 아래 매트릭스).
    - `CompilationPipeline.GetAssemblies`가 호출마다 ~60 ms인데 빌드가 두 번(`IBuildStep`·`ISettingsStep` 찾기), lint가 한 번 불렀다 → 다음 컴파일까지 캐시
      (`HarnessPaths.Assemblies`, `compilationStarted`에 비움): check 64 → 3.5 ms, settings 113 → 14 ms.
    - 웜 빌드 730 → 390–420 ms, 변경 없는 루프 3.8 → 3.5 s. 플레이 상태 폴링 200 → 100 ms(`harness_play_status`는 메인 스레드 밖이라 게임에 영향 없음).
  - 해 보고 넣지 않은 것: 하네스 어셈블리를 리로드 직후 백그라운드에서 미리 JIT. Mono의 `RuntimeHelpers.PrepareMethod`는 아무것도 하지 않았고(612개 5 ms),
    `RuntimeMethodHandle.GetFunctionPointer`는 JIT해서(55 ms) 콜드 빌드를 ~75 ms 줄였지만(check 150 → 76 ms), JIT가 `beforefieldinit` static 초기화를 그 스레드에서
    돌릴 수 있어 Unity API를 부르는 초기화가 백그라운드에서 실패하면 그 타입이 도메인 끝까지 망가진다 — 75 ms와 바꿀 위험이 아니다.
  - 방법(G2-1, 핫 루프 — `Editor/HarnessHot.cs` `harness_hot`, `Invoke-HarnessLoop -Hot`, `loop.ps1 -Hot`):
    - Pipeline의 `[CodeReload]`(컴파일 때 표식 메서드에 "교체본이 있으면 그것을 호출" 프롤로그를 짜 넣음)를 쓴다. 두 백엔드 중 `reload_file`(Assembly.Load)은
      `SmokeModule.Tick`을 "accessibility violation 9건"으로 거부했다(교체 본문은 public 멤버만) → `reload_file_editor_interpreter`(IlInterpreter, private 가능).
      편집 모드에서 적용한 교체가 Domain Reload 없는 플레이 모드까지 유지되는 것을 확인했고(0.8.0 변경 사항), 같은 로직을 인터프리터로 돌린 루프가 기준 이미지와
      **픽셀까지 같았다**(3장 meanDiff 0, maxDiff 0, events 같음).
    - "플레이 상태를 유지한 채 교체"(원래 방향)는 하지 않았다: 핫 루프도 교체 뒤 시나리오를 처음부터 돈다 → 결정성·`play.events`·기준 이미지 비교가 전체 루프와 같다.
      그래도 첫 캡처까지 1.27 s라 완료 기준(2 s)을 넘지 않는다.
    - 무엇이 컴파일돼 있나: 전체 루프가 컴파일 직전 `Assets/`(생성물·빌드 씬 제외)·`ProjectSettings/`·`Packages/`의 크기·시각과 `CodeReload`가 든 .cs 텍스트를 찍고
      (prepare, 이전 교체도 지움) 컴파일이 성공하면 확정한다(commit; 그 사이 import가 만든 `.meta`는 받아들이고 그 사이 바뀐 .cs는 옛 상태로 둬서 다음 핫 루프가
      바뀐 것으로 본다). 핫 판정은 번들 Roslyn(`UnityPipeline.Microsoft.CodeAnalysis`)으로 두 텍스트를 파싱해(그 어셈블리의 define으로) 표식 메서드 본문 밖의 토큰
      열이 같으면 본문이 다른 메서드만 교체한다. 스냅샷에 에디터 세션 id와 도메인 리로드 수를 넣어, 에디터 재시작·루프 밖 컴파일 뒤에는 전체 루프로.
    - 폴백: 컨텍스트 변경(첫 차이의 줄), 추가·삭제·표식 없던 파일·에셋, 문법 오류, 인터프리터 미지원(`try/catch` → "No Methods Applied" 진단을 그대로 사유로),
      Domain Reload 켜짐. 교체 본문이 플레이 중 던지면 Pipeline이 `CodeReload: Error invoking override`로 줄 없이(선언 55행으로) 로그하고 원래 본문을 이어 돌렸다
      (`SpinDirectionChanged` 1 → 2) → 루프가 그 접두어를 보면 전체 루프를 다시 돌아 정확한 줄(64·70행)로 보고한다.
    - 도메인 리로드 뒤 첫 교체가 1.46–1.49 s(Roslyn 적재·JIT)였다 → `[CodeReload]`가 있는 프로젝트는 리로드 직후 워커 스레드에서 번들 Roslyn으로 몇 줄을 컴파일
      (Unity API 없음) → 0.9 s(eval로 Pipeline 경로까지 데우면 0.6 s라 나머지는 Pipeline 내부).
    - 샘플: `SmokeModule.Tick`·`StageModule.Tick`에 표식, 두 모듈 asmdef에 `Unity.Pipeline`·`Unity.Pipeline.Attributes`(CLAUDE.md 템플릿도). `Harness.Editor`가
      `Unity.Pipeline`을 참조(`CodeReloadRegistry`, `CommandRegistry`). 주입 표식 줄이 61/68 → 63/70행으로.
  - 매트릭스가 드러낸 것: selftest 8번 "land kill 뒤 루프"가 `stage=play`로 빨갰다 — `PlayState.Save`가 `play_state.json`을 지우는 순간 `harness_play_status`(요청 스레드)가
    그 파일을 읽고 있어 IOException(원래 있던 경합, 폴링 간격을 줄여 잦아짐). `compile.json` 쓰기도 같은 경합을 조용히 삼키고 있었다 → `HarnessPaths.WriteStateFile`
    (임시 파일 → 제자리 이동, 공유 위반이면 2 ms씩 최대 50번 재시도).
  - 검증(이 머신, 에디터를 하나씩): 새로 연 에디터에서 각 3회 — 변경 없음 3.47–3.68 s, 셰이더 3.79–3.83 s, C# 1줄 8.84–9.08 s, **`-Hot` 3.00–3.06 s(교체 0.14 s,
    첫 캡처 1.27 s)**, 리로드 뒤 첫 `-Hot` 3.81–3.89 s, 컴파일 에러 0.94–1.14 s(위 "기준선"). 스냅샷 10–20 ms(샘플 170개 파일).
    - selftest 1–8 녹색 318.1 s(`78354e2e…`, 줄 63/70/87, 3번 77.7 s에 핫 단계 6개: 본문 교체·도메인 리로드 수 그대로·golden `changed`, 되돌림 `same`, 필드 → 전체 루프
      "(line 31)", 핫 본문 예외 → 전체 루프 70행).
    - 9: 새 클론(최종 코드) 6000.3.11f1 녹색 380.3 s(`78354e2e…` = 메인 트리, 첫 루프부터 기준 이미지 `same=3`), 6000.0.84f1 녹색 364.8 s(`d6e6d71b…`),
      6000.6.3f1 녹색 387.5 s(`df34f931…`, 샷 60.4/55.7/45.7 = W4, 플레이 진입 0.12 s로 6.3의 0.4 s보다 빠름) — 세 버전 모두 핫 단계 포함 녹색, `git status`는 버전 전환 파일뿐.
    - 10: BagelGame 녹색 56.5 s(`619be553…` = W4, 65.5, 스냅샷 24–81 ms, 출시 빌드 `Managed/` 132개·`Harness.*` 0개), Fluid-Sim 녹색 30.5 s(`54880f05…`,
      23.0/14.9/22.2, 103개·0개), 사내 프로젝트 A 녹색 134.7 s(`6664b723…`, `brd-lobby-w3.json` 37.7/91.2/120.3/145.7, Domain Reload가 켜져 있어 스냅샷을 건너뜀,
      서버 선택 PlayerPrefs 0 유지).
  - 남은 것: 전체 루프의 고정비(리로드 2.5 s + 리로드 뒤 에디터 0.9 s)는 Unity 쪽 → G2-4(W7). 핫 범위(새 헬퍼 메서드, `try/catch`, 예외 줄) → G2-5(W12·상시).

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
  - 남은 것: RP 밖의 프로젝트 설정 → G1-5(W10). 반사·앰비언트는 하늘만(씬 오브젝트가 비치지 않음, 반사 프로브는 W6c → G4-5). 기준 이미지는 여전히 6.3만(6.0·6.6은
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
