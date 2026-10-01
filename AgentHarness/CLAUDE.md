# AgentHarness — Unity 6(6.0 LTS 이상) + URP 에이전트 하네스

이 문서만 읽고 바로 루프를 돌릴 수 있어야 한다. 게임은 아직 없다 — `Assets/Game/Stage`, `Assets/Game/Smoke`는
하네스를 검증하는 스모크 씬이다(절차적 지형 + 커스텀 HLSL + 라이트 + Volume 후처리 + 회전 오브젝트 + 코드로 만든 파티클·애니메이션 + 구름이 흐르는 하늘 +
씬을 비추는 반사 프로브 + UI Toolkit HUD).
샘플 프로젝트는 Unity 6000.3.11f1로 고정돼 있고, 하네스는 UPM 패키지 `Packages/com.geuneda.agentharness/`(이 프로젝트에 임베드)로
Unity 6.0 LTS 이상에서 돈다(아래 "Unity 버전"). 같은 패키지를 **기존 Unity 프로젝트에 설치 스크립트로 붙일 수 있다**(아래 "기존 프로젝트에 붙이기").
`tools/*.ps1`은 패키지 `Tools~/`의 같은 이름 스크립트를 부르는 얇은 진입점이다(모두 같은 파일). 도구를 고칠 때는 `Tools~/`를 고친다.

## 왜 이 하네스가 있나

에이전트가 Three.js로 만든 웹 3D 게임의 완성도가 높은 건 모델이 3D를 잘해서가 아니라 작업 환경 덕분이다.

| # | Three.js 환경의 성질 | Unity 기본 상태 | 이 하네스가 복원하는 방법 |
|---|---|---|---|
| 1 | 모든 게 텍스트(JS 코드) | 씬/프리팹이 GUID로 얽힌 YAML, GUI 중심 도구 | 씬은 `IBuildStep` 코드가 생성, 렌더 파이프라인과 프로젝트 설정(품질 레벨·레이어·Player·Time·Physics)은 `ISettingsStep` 코드, HLSL·UXML/USS·코드로 만든 머티리얼/Volume/라이팅/파티클/애니메이션 클립 |
| 2 | 수정→새로고침이 초 단위 | 컴파일 + 도메인 리로드, GUI 에디터와 모달 대화상자 | Domain Reload 끔, 모듈별 asmdef, 빌드 캐시, 에디터 없는 컴파일 체크, `[CodeReload]` 본문(과 그 본문이 부르는 새 메서드)만 바꾸면 컴파일 없이 적용하는 핫 루프(`loop.ps1 -Hot`), 대화상자에 멈추지 않는 `-automated` 에디터·창 없는 에디터(`open.ps1 -Headless`) |
| 3 | 스크린샷·콘솔·FPS를 눈/기계로 확인 | 에이전트가 화면을 못 봄 | `harness_capture/play`가 PNG + 이미지 통계, `harness_console/stats`가 JSON, 같은 시나리오를 개발 빌드 플레이어에서(`player.ps1`: 에디터 없는 프레임 시간, 게임이 그린 실제 화면과 캡처·에디터 샷의 비교) |
| 4 | 에셋 없이 절차적 생성 + 셰이더 + 후처리 | 에셋 임포트 중심 | `Harness.Procedural`(Mesh/Noise/SDF/스플라인/스캐터), GPU 텍스처 베이크(`ctx.BakeTexture`, C#과 같은 HLSL 노이즈), URP Volume·데칼을 코드로, 파티클·키프레임 애니메이션을 코드로(`ctx.Particles`, `ctx.AnimationClip` + Playables `ClipPlayer`), 구름 하늘(`ctx.Sky`)과 빌드 뒤 씬을 찍는 반사 프로브(`ctx.ReflectionProbe`) |
| 5 | 레지스트리 구조라 병렬 작업이 쉬움 | 에디터 하나를 공유 | `GameRoot.Register` + `EventBus`, 모듈 폴더 격리, 에디터 조작 뮤텍스, 에이전트별 git worktree + `submit.ps1`/`land.ps1` 트랜잭션, worktree마다 따로 도는 에디터(`open.ps1 -Own`), 기계가 지키는 계약 폴더(발행 모듈별 파일·이름 한 번·타입 단위 추가만) |

모든 설계 결정의 기준: **"Three.js 환경의 어떤 성질을 복원하는가"**. URP·물리·엔진 기능을 쓰니 결과는 그 이상을 노린다.

**아직 해결 안 된 격차는 [`docs/ROADMAP.md`](docs/ROADMAP.md)에 성질 1~5와 이식성(Unity 버전·기존 프로젝트·macOS)별로 기록돼 있다.** 하네스를 개선할 때는 거기 "작업 순서"에서 다음 워크플로우(W1…)를 고르고,
해결하면 체크 + 검증 방법·측정값을 남긴다. 하네스를 고친 뒤에는 ROADMAP의 "검증 매트릭스"를 다시 돌린다(1–8 = `tools/selftest.ps1`, 아래 "하네스 자기 검증").
**워크플로우마다 빠짐없이 갱신하는 문서**(커밋 전 확인): `docs/ROADMAP.md`(작업 순서 표의 상태, 워크플로우 절, 성질별 항목 → "해결됨"과 측정값, 새로 드러난 항목,
검증 매트릭스, 기준선 표가 있으면 그 값), 저장소 루트 `README.md`(소개·표·측정값·요구 사항·구조 중 바뀐 것), 이 문서, 기존 프로젝트용 안내서
`Tools~/templates/AgentHarness.md`(에이전트가 쓰는 기능이 바뀌었으면).

## 빠른 시작 (새로고침 + 스크린샷 + 콘솔 = 한 방)

```powershell
powershell -ExecutionPolicy Bypass -File tools/open.ps1   # 에디터가 없으면 열고, 쓸 수 있을 때까지 기다린다(이미 열려 있으면 기다리기만)
powershell -ExecutionPolicy Bypass -File tools/loop.ps1
powershell -ExecutionPolicy Bypass -File tools/quit.ps1   # 끝낼 때: 락을 잡고 정상 종료, 프로세스가 끝날 때까지 대기
```

`open.ps1`은 에디터 로그를 `Logs/Editor.log`(직전 것은 `Editor-prev.log`)에 따로 쓰게 하고, 응답한 뒤 2초간 idle일 때 돌아온다
(재시작 ~14 s, 새 클론 첫 임포트는 수 분). `unity open`이나 Hub로 열면 `-logFile`이 없어 여러 에디터가 사용자 전역 `Editor.log` 하나를
서로 덮어쓴다(아래 "함정"). 에디터는 이렇게 뜬다(W7, G2-2 — 아래 "에디터 모드"):
- **`-automated`**(기본): `EditorUtility.DisplayDialog*`가 사람을 기다리지 않고 기본값(취소)을 바로 돌려준다 → 에디터의 모달 대화상자가 메인 스레드를
  막지 않는다. 창 배치도 종료 때 저장하지 않는다. 사람이 그 에디터에서 작업하면 `-Interactive`(대화상자가 사람을 기다림).
- **`-debugCodeOptimization`**(항상): 처음부터 Debug 코드 최적화 → 정확한 예외 줄과 결정적 fingerprint를 위한 재컴파일(~10 s)이 없다.
- **`-Headless`**: 창 없는 에디터(`-batchmode`, `-quit` 없음, GPU로 렌더). 한 바퀴가 ~1.3 s 빠르고, 스크립트가 컴파일되지 않아도 마지막으로 성공한
  어셈블리로 뜬다(`-ignoreCompilerErrors`) → 루프가 에러를 보고한다. Game 뷰가 없어서 `"screen"` 캡처는 에러, `fps`에 렌더가 없다.
- **`-Own`**(에이전트 worktree에서): 그 worktree만의 에디터(기본 창 없음, `-Window`면 창). 아래 "병렬 에이전트".

실패하면 JSON의 `error`, `dialog`(모달 다이얼로그 — 사람이 답해야 함), `safeMode`+`compileErrors`, `logTail`을 본다.
Pipeline 서버가 뜨기 전의 다이얼로그(새로 설치한 버전의 이용 약관, 패키지 에러, `-Interactive`의 "Enter Safe Mode?")는 로그가 60 s 멈추고
에디터 창 제목이 진행 창이 아니면 `dialog.title`로 보고한다. 에디터는 그대로 두니 사람이 답한 뒤 `open.ps1`을 다시 부르면 그 에디터를 기다린다
(`open.ps1`이 띄운 pid는 `Logs/harness-editor.json`에 남아서, 락 파일이 생기기 전에도 두 번째 에디터를 띄우지 않는다).
**시작할 때 스크립트가 컴파일되지 않으면** 창 있는 `-automated` 에디터는 묻지 않고 Safe Mode로 들어간다(Pipeline 서버 없음) → `open.ps1`과 루프가
`safeMode: true`와 로그에서 읽은 `compileErrors`(file·line·module)를 준다. 고친 뒤 `quit.ps1 -Force` → `open.ps1`(또는 `-Headless`로 연다).
다른 설치 버전으로 열기: `open.ps1 -UnityVersion <버전>`(아래 "Unity 버전").

`tools/loop.ps1` = recompile → (C# 컴파일 에러면 즉시 중단) → lint → `harness_build` → `harness_shaders` → `harness_play`(기본 3컷)
→ `harness_console` + `harness_stats` → `HarnessOut/latest/report.json` (stdout에도 같은 JSON). 종료코드 0 = 전부 녹색.
**모듈의 `[CodeReload] Tick` 본문(과 거기서 부르는 새 메서드)만 고쳤으면 `tools/loop.ps1 -Hot`**: 컴파일·도메인 리로드·빌드 없이 그 본문을 바꿔 넣고 같은 시나리오를 돈다
(~3.4 s, 전체 루프 ~9.5 s). 그 밖의 변경이면 알아서 전체 루프를 돌고 이유를 `hot.fallback`에 남긴다(아래 "핫 루프").
**실제 성능과 게임이 그린 실제 화면은 `tools/player.ps1`**: 같은 시나리오를 개발 빌드 플레이어(캡처 크기의 창)에서 돌려 에디터 루프와 나란히
프레임 시간·이벤트·샷을 비교한다(아래 "플레이어에서 돌리기"). 에디터 플레이 모드의 `fps`는 변경 전후 비교용이다.

**매 루프 후 반드시**: `report.json`의 `ok/stage`를 보고, `shots`의 PNG를 **Read 툴로 직접 열어** 눈으로 확인한다. `build.warnings`(머티리얼 설정 실수 등)도 읽는다.
`shotStats[].blank=true`(평평/검은 화면)면 렌더가 깨진 것이다. `dark=true`(픽셀 98% 이상이 거의 검정)는 실패로 치지 않지만
조명이 빠진 화면일 가능성이 크다 — PNG를 열어 본다. `magenta=true`(에러 셰이더의 마젠타가 0.05% 이상)도 실패로 치지 않지만 거의 항상
렌더 파이프라인이 못 그리는 머티리얼이다(URP 프로젝트에서 `Shader.Find("Standard")` 같은 Built-in 셰이더, 없는·깨진 셰이더) — `hint`가 그 렌더러를 짚는다.
샷에는 화면에 그리는 카메라들(`shotStats[].cameras`: 스택·미니맵 포함)과 스크린 공간 UI(uGUI 캔버스·UI Toolkit 패널, 캡처 크기로 다시 배치)가
들어 있다(`shotStats[].ui`, 아래 "시나리오"). **`golden.changed` > 0이면** 기준 이미지와 달라진 샷이다(실패 아님): 의도한 변경이 아니면
`shotStats[].golden.diff` PNG(바뀐 픽셀 빨강, 바뀐 곳 노란 테두리)를 연다. 의도한 변경이고 샷이 맞으면 `-UpdateGolden`으로 갱신해 함께 커밋한다(아래 "기준 이미지").

옵션: `-Scenario tools/scenarios/x.json`, `-Out HarnessOut/x`, `-NoPlay`(편집 모드 캡처만), `-NoCompile`, `-Hot`(핫 루프),
`-UpdateGolden`(녹색이면 이번 샷을 기준 이미지로), `-Golden <폴더>`(기준 이미지 루트, 기본 설정 `goldenRoot` = `golden`).
**여러 에이전트가 동시에 작업하면** 이 폴더를 직접 고치지 말고 각자 worktree에서 `tools/submit.ps1`을 쓰고, 끝나면 커밋해서
`tools/land.ps1`로 병합한다(아래 "병렬 에이전트").

### report.json

```jsonc
{
  "ok": true, "stage": "done",            // 실패 시 stage = editor|compile|build|shader|play|runtime|lint|shots|submit|land
  "compileErrors": [{"file","line","msg","module"}],       // C# 에러, 또는 kind:"shader" (HLSL 에러, 상태 기반)
  "runtimeErrors": [{"type","msg","file","line","module","count","stack"}],   // count = 같은 에러 폴딩 수
  "editorErrors": [{"type","msg","count","stack"}],   // Unity/패키지 내부 에러(Assets/ 흔적 없음). 실패 사유는 아니지만 읽어볼 것
  "knownErrors": [{"type","msg","file","line","count"}],   // 설정 knownErrors 정규식에 맞은 에러(프로젝트가 원래 내는 것). 실패 아님
  "teardownErrors": [{"type","msg","file","line","module","count"}],   // 시나리오가 끝난 뒤 플레이 모드를 나가며 난 에러. 실패 아님, 읽어볼 것
  "editor": {"pid","mode":"window"|"headless","automated","own"},   // 루프를 돌린 에디터(own = worktree 전용 에디터, open.ps1 -Own)
  "safeMode": true,                        // stage=editor: 창 있는 에디터가 Safe Mode(시작 때 컴파일 실패) — compileErrors는 그 로그에서
  "fps": {"avg","min","p95ms","p99ms","hitches","cpuMainAvgMs","samples","editorFocused","note"},   // note: headless면 렌더 없는 프레임(창 에디터와 비교 불가)
  "shots": ["C:/.../HarnessOut/latest/shot0_closeup.png", ...],
  "durationSec": 3.5, "unityVersion": "6000.3.11f1",   // 루프를 돌린 에디터 버전
  "timings": {"lockWaitSec","editorWaitSec","hotSec","compileSec","snapshotSec","buildSec","reloadSec","playSec","hotPlaySec","collectSec","goldenSec"},
      // editorWaitSec: 시작 시 리로드·busy 대기, reloadSec: 파이프라인 전환 뒤 리로드(있을 때만), hotSec: 핫 판정·적용(-Hot),
      // snapshotSec: 핫 루프의 기준이 될 소스 스냅샷(compileSec에 포함), hotPlaySec: 핫 본문이 예외를 던져 전체 루프로 다시 돌기 전의 플레이
  "hot": {"applied","reloaded":[{"file","methods","newMethods","ms"}],"overridesCleared","fallback","changes":[{"file","kind","line","methods","newMethods"}],   // -Hot만
          "interpreted":{"methods":[{"method","calls","frames","ms","msPerCall"}],"msPerFrame","frameShare","error"}},   // 인터프리터로 돈 메서드의 플레이 동안 비용(G2-5)
  "build": {"fingerprint","steps":[{"type","module","ms","error","file","line"}], "warnings":[],   // warnings: 머티리얼 설정 실수 등(실패 아님, 읽을 것)
            "phases": {"check","settings","steps","cleanup","save","lighting","probes","fingerprint"},   // 빌드 시간이 든 곳(ms)
            "reflectionProbes": 1, "cubemapsWritten": [],   // ctx.ReflectionProbe를 그린 수, 픽셀이 바뀌어 다시 쓴 하늘·프로브 큐브맵(같은 씬이면 비어 있음)
            "settings": {"assets","written","assigned","pipeline","switched","reloadRequested",   // ISettingsStep이 만든 RP 에셋·이번에 다시 쓴 것·활성 파이프라인·전환
                         "project": {"owned","changed":[],"drift":[]}}, ...},   // 설정 스텝이 소유한 ProjectSettings 값 수·이번에 쓴 것·코드 밖에서 바뀌었던 것(아래 "프로젝트 설정")
            // 핫 루프는 빌드하지 않는다: {"ok":true,"skipped":true,"note"}
  "play": {"success","probeReady","frames","gameSec","modules","failedModules","inputEventsApplied",
           "events":[{"name":"SpinnerLap","count":2}],     // EventBus 발행 횟수 → 게임플레이를 기계적으로 검증 (클립 이벤트는 "ClipEvent:<이름>")
           "inputBackends":["inputSystem"|"hook"], "inputHooks":["HarnessInput.OnScenarioInput"],   // 입력이 들어간 곳
           "isolatedDevices":[{"name":"Keyboard","presses":0,"background":true}],   // 시나리오 동안 끈 실제 장치와 막은 키·버튼 누름 수
               // background: 에디터·플레이어가 백그라운드라 Input System이 먼저 꺼 둔 장치를 가져감(G3-9) — 포커스가 어디 있든 결과는 같다
           "activeScene", "scenes":[{"name","mode","t","wallSec"}],      // 로드된 씬(Start = 처음부터 있던 씬, t = 시나리오 시계)
           "waits":[{"type","target","t","waitedSec","frames"}], "clicks":[{"target","t","x","y","via"}],
           "uiClock":{"mode":"frames"|"real","panels","scope","error"},   // UI Toolkit 패널이 프레임 시계로 돌았는지(아래 "시나리오")
           "uiFocusError"},   // 있을 때만: 이 Unity에서는 UI Toolkit이 백그라운드(포커스 없음)에서 시나리오 입력을 버린다(G3-14, 내부 API 없음)
  "render": {"batches","setPassCalls","drawCalls","triangles","vertices"},   // headless면 null(Game 뷰가 그리지 않음). 6.6은 batches null(카운터 없음), drawCalls = 종류별 합
  "shotStats": [{"name","preset","t","width","height","meanLuma","stdLuma","blank","dark","magenta","magentaRatio",
                 "cameras":["Stage/Main Camera","Stage/Main Camera/Weapon (overlay)","Minimap"],   // 그린 카메라(아래부터)
                 "ui":["ugui:<캔버스 경로>","uitk:<PanelSettings>"],   // 샷의 UI(아래부터), 합성 실패는 "uiError"
                 "shadersCompiling": true,   // 캡처 순간 셰이더가 백그라운드 컴파일 중(비동기 컴파일이 켜진 프로젝트만): 오브젝트가 빠졌을 수 있다
                 "frames","every","sheet":"3x3","times":[],"motion":[],   // 연속 캡처("frames" > 1)만: 시트 PNG, 프레임 사이 움직임
                 "golden":{"status":"same|changed|size|missing|error","meanDiff","changedRatio","ssim","maxDiff",
                           "rect":[x,y,w,h],"diff":"<바뀐 곳 PNG>"},   // 기준 이미지가 있을 때만
                 "error","hint"}],   // hint: blank·magenta의 이유(화면에 그리지 않은 다른 카메라, 마젠타로 그린 렌더러)
  "golden": {"root","key","version","from","dir","same","changed","missing","updated":[],"hint","error"},   // 기준 이미지(G3-4), 실패 아님
  "lint": [{"rule","module","file","message"}], "warningCount": 0,
  "submit": {"phase","synced","kept","reverted","written","deleted","contractsAdded","metaWrittenBack",   // submit.ps1만.
             "errorModules","restore","check","owner","takeover",   // timings에 checkSec/syncSec/restoreSec 추가
             "contractsUpdated","contractsDeleted","contractsBehind",   // 계약 폴더(아래 "계약 폴더"): 바꾼 것·지운 것·이 worktree가 안 바꿔 건너뛴 것
             "contractChanged":[{"path","type","change","line"}],"contractConflicts":[{"type","full","path","line","other","otherLine","otherOwner"}],
             "contractOwner","contractTakeover",   // 계약 거부 사유(stage=submit)
             "settingsWrittenBack","settingsNotWrittenBack","settingsNote"},   // 루프의 설정 스텝이 바꾼 ProjectSettings를 worktree로 되복사했는지(아래 "프로젝트 설정")
  "land": {"branch","into","phase","head":{"before","after"},"merged","fastForward","kept","reverted",  // land.ps1만.
           "modules","files","stash":{"sha","paths","dropped"},"releasedOwners","releasedContracts","errorModules","undo","restore",
           "conflicts","missingMeta","owner","foreign","uncommitted","contractOwner","contractChanged","contractConflicts",
           "settings":{"regenerated","derived","commit","waitingFor","note","error"},   // 설정 스텝이 쓴 ProjectSettings(아래 "프로젝트 설정", G5-6)
           "note","warning","stillPending"},   // 거부 사유는 해당 필드에. timings에 checkSec/mergeSec/restoreSec
  "recoveredSubmit": {"runId","workRoot","modules","files"},     // 도중에 죽은 submit을 이번 실행이 되돌렸을 때만
  "recoveredLand": {"runId","branch","steps"}                    // 도중에 죽은 land를 이번 실행이 되돌렸을 때만
}
```

측정된 한 바퀴 시간(이 머신, W7 — ROADMAP "기준선"), 창 에디터 / **창 없는 에디터(`-Headless`)**: 코드 변경 없음 ~3.9s / **~2.4s**,
셰이더만 수정 ~4.1s / **~2.6s**(도메인 리로드 없음), 모듈 C# 1줄 수정 ~9.5s / **~7.0s**(창: 컴파일 ~0.4s + 도메인 리로드 ~2.7s + 리로드 뒤 에디터 자체
작업 ~0.9s + 리로드 직후 빌드 ~1.2s + 플레이 ~2.8s; 창 없음: 리로드 ~2.1s, 리로드 뒤 작업 없음, 플레이 ~1.6s), **`-Hot`(본문만) ~3.3s / ~2.1s**
(W12 창 3.41–3.46s, 본문이 부르는 새 메서드를 더해도 3.44–3.51s; 도메인 리로드 뒤 첫 핫 루프는 +0.9s), 컴파일 에러 보고 **~1.1s**. 창 없는 에디터는 플레이 동안 Game 뷰가 그리지 않아 빠르다(캡처만 그림).
도메인 리로드는 에디터를 오래 띄워 둘수록 늘었다(2.5 → 3.5s, 아래 "함정"). 리로드 뒤 ~14 s 동안 요청이 401("Editor token rotated")이면
Pipeline의 새 토큰이 늦게 적힌 것이다(C# 1줄 루프가 ~24 s, 하네스 코드와 무관 — ROADMAP O-12). 루프는 기다렸다가 계속한다.

## 규칙 (반드시 지킬 것)

1. **`.unity` / `.prefab` / `.asset` YAML 직접 수정 금지.** 씬은 `Assets/Game/<Module>/Builders/`의 `IBuildStep`이 만든다.
   `Assets/Scenes/Main.unity`와 `Assets/Generated/`는 빌드 산출물이며 gitignore 되어 있다(고쳐도 다음 빌드에 덮어써진다).
   렌더 파이프라인(URP·Renderer 에셋, Renderer Feature, 품질 레벨별 파이프라인)은 같은 폴더의 `ISettingsStep` 코드가 만든다(아래 "렌더 설정").
   품질 레벨·레이어·태그·Player·Time·Physics 같은 `ProjectSettings/` 값도 같은 설정 스텝이 소유한다(아래 "프로젝트 설정" — Project Settings 창이나
   YAML로 바꾸면 다음 루프가 되돌리고 `drift`로 보고). 그 밖의 설정도 YAML이 아니라 에디터 API(`harness_setup` 등)로 바꾼다.
2. **텍스트로 쓸 수 있는 형태만.** 셰이더 = 손으로 쓴 HLSL `.shader`(Shader Graph 금지), UI = UI Toolkit UXML/USS(uGUI 프리팹 금지; 모양은 UI 킷 클래스·변수, 아래 "UI 킷"),
   머티리얼·파티클·Volume·라이팅·PanelSettings = 빌더 코드(`BuildContext`)로 생성. 애니메이션은 `ctx.AnimationClip`(키를 코드로) + `ctx.Animate`
   (Playables — AnimatorController 에셋 없음, 아래 "파티클·애니메이션"). 그 밖의 GUI 에셋(Timeline 등)이 필요하면 코드로 생성한다.
3. **자기 모듈 폴더 밖 수정 금지.** 작업 범위는 `Assets/Game/<Module>/` 하나. 모듈 간 공유 이벤트 타입만
   `Assets/Game/Contracts/<Module>Events.cs`(자기 모듈이 발행하는 이벤트)에 **추가**한다 — 올라간(land된) 타입은 바꾸지 않고, 이름은 계약 전체에서 한 번
   (아래 "계약 폴더"; lint·submit·land가 검사). 하네스 패키지(`Packages/com.geuneda.agentharness/`)와 `tools/`는 하네스 작업일 때만 고친다.
4. **에디터 하나에 대한 조작은 한 번에 하나씩.** `tools/loop.ps1`과 `tools/uc.ps1`은 프로젝트별 시스템 뮤텍스를 잡으므로
   같은 에디터를 쓰는 에이전트는 자동으로 줄을 선다(`timings.lockWaitSec`). recompile/build/play/capture를 `unity command`로 직접 호출해
   락을 우회하지 말 것. **병렬 에이전트는 각자 git worktree에서 코드를 쓰고 `tools/submit.ps1 -Module <M>`으로 에디터에 넣고,
   커밋한 뒤 `tools/land.ps1`로 병합한다** (아래 "병렬 에이전트"). 줄을 서지 않으려면 worktree에 에디터를 따로 띄운다(`tools/open.ps1 -Own`).
   여럿이 에디터 트리를 직접 고치면 한 명의 컴파일 에러가 모두의 루프를 막는다.
   에디터 트리에서 `git merge`/`git stash`를 직접 하지 말 것 — land가 락 안에서 한다.
5. **Domain Reload가 꺼져 있다.** 플레이 사이에 static이 유지된다. 가변 static(필드·자동 프로퍼티·이벤트, static readonly 컬렉션 포함)이 있는
   타입은 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics()`에서 초기화한다.
   `harness_lint`가 검사하며 위반 시 루프는 `stage=lint`로 실패한다.
6. **결정성.** 빌더는 `ctx.Seed(...)` / `Noise.Rng`만 쓴다(`UnityEngine.Random`·시드 없는 `System.Random` 금지). 같은 코드면 `build.fingerprint`가 같아야 한다.
   시나리오는 기본 `fixedDeltaTime`(Time.captureDeltaTime)으로 돌아서 `t=1.5`의 화면·이벤트 수가 매번 같다.
7. 런타임 오브젝트에 `HideFlags.DontSave` 금지 — 플레이 모드가 끝나도 살아남아 다음 플레이에서 중복 Tick 된다(실제로 겪은 버그).

## 폴더 구조

```
Packages/com.geuneda.agentharness/        하네스 UPM 패키지 (package.json: com.unity.pipeline 의존)
  Runtime/                     런타임 계약: GameRoot, IGameModule, EventBus, HarnessProbe, HarnessConfig, ShotPreset(+ShotPose), ScriptedInput,
                               ScenarioInput(KeyNames, InputHookReplay), ScenarioRunner, HarnessCapture(+CaptureCameras: 화면의 카메라들,
                               CaptureUi: 스크린 공간 UI 합성, ContactSheet: 연속 캡처 시트), ClipPlayer(Playables 클립 재생, ClipEvent),
                               PanelClock(시나리오 동안 UI Toolkit 시간 = 프레임), PlayerRun(플레이어에서 명령줄 시나리오, W8), UI/(Gauge, ToastStack: UI 킷 컨트롤),
                               SkyClock(하늘 구름의 게임 시간)
                               (asmdef Harness.Runtime: UNITY_EDITOR || DEVELOPMENT_BUILD || AGENTHARNESS_RUNTIME)
  Runtime/Procedural/          MeshBuilder(+Sdf: FromSdf, +Spline: Tube, +Scatter: Rock·Icosphere), Noise(Perlin/fBm/Ridged/Worley/Rng), Sdf, Spline,
                               Scatter(Poisson), TextureBaker, PMath, AmbientProbe(큐브맵 → 앰비언트 SH)
  Editor/                      [CliCommand] harness_* (HarnessGolden: 기준 이미지 비교, HarnessHot: 핫 루프, HarnessPlayer: 플레이어 빌드 계획, HarnessHeadless: 에디터 모드·창 없는
                               에디터의 유휴 CPU 억제, HarnessContracts: 계약 폴더의 lint 규칙과 소스 선언 읽기) 와 BuildContext(+.Materials: LitMaterial,
                               +.Particles: Particles·ParticleMaterial, +.Animation: AnimationClip·Animate, +.Bake: BakeTexture·FloatTexture,
                               +.Decals: Decal·DecalMaterial, +.Environment: Sky·BakeSkyReflection·ReflectionProbe)/IBuildStep,
                               SettingsContext(+.Project: ProjectSettings 값, ProjectValues: 값 묶음)/ISettingsStep(렌더·프로젝트 설정), HarnessReleaseBuild
                               (asmdef Harness.Editor, Editor 전용)
  UI/                          DefaultRuntimeTheme.tss (UI Toolkit 기본 테마) + HarnessKit.uss (UI 킷: 디자인 변수·컴포넌트 클래스, 텍스트)
  Shaders/                     HarnessBake.hlsl (GPU 베이크 셰이더의 정점·도우미) + HarnessNoise.hlsl (C# Noise와 같은 HLSL 노이즈) +
                               HarnessSky.shader (Harness/Sky: ctx.Sky) + HarnessCubeFilter.shader (반사 큐브맵 mip의 GGX 프리필터)
  Tools~/                      도구 본체(.ps1, Harness.psm1) + templates/ (진입점, 기본 시나리오, 기존 프로젝트용 안내서, HarnessInput.cs)
ProjectSettings/AgentHarness.json   하네스 설정: setup 모드, 모듈 루트/폴더, contracts, 생성물 경로, 빌드·플레이 씬
Assets/Game/Contracts/         모듈 간 이벤트 타입 (Game.Contracts): <Module>Events.cs, 타입 단위 추가만, 이름 한 번 ("계약 폴더")
Assets/Game/<Module>/          런타임 코드 (Game.<Module>.asmdef) + Shaders/*.shader + UI/*.uxml|uss
Assets/Game/<Module>/Builders/ IBuildStep·ISettingsStep 구현 (Game.<Module>.Builders.asmdef, Editor 전용)
Assets/Generated/, Assets/Scenes/Main.unity   빌드 산출물 (gitignore, 직접 수정 금지). RP·Renderer 에셋도 여기(경로에서 만든 고정 GUID)
Assets/Settings/               URP가 관리하는 전역 설정(UniversalRenderPipelineGlobalSettings, DefaultVolumeProfile)만 커밋
tools/loop.ps1                 원커맨드 루프          tools/uc.ps1          커맨드 1개 호출(JSON 인자)
tools/submit.ps1               worktree의 모듈 → 에디터 트리, 트랜잭션 루프(실패 시 되돌림)
tools/land.ps1                 worktree 브랜치 → 에디터 트리 브랜치로 병합, 트랜잭션 루프(실패 시 되돌림)
tools/compile-check.ps1        에디터 없는 컴파일 검사  tools/Harness.psm1    HTTP 클라이언트·락·루프·submit/land 저널·git
tools/player.ps1               같은 시나리오를 개발 빌드 플레이어에서(에디터 루프와 나란히: 프레임 시간·이벤트·샷·실제 화면)
tools/open.ps1 / quit.ps1      에디터 열기(프로젝트별 로그, -automated, 준비 대기; -Headless 창 없음, -Own worktree 전용) / 정상 종료(락)
tools/fresh-clone-test.ps1     새 클론 검증: 짧은 경로에 클론 → open → harness_setup → 루프 N회 → quit → 삭제
tools/attach-test.ps1          기존 프로젝트 붙이기 검증: install → open → 루프 N회 → 출시 빌드 → quit → uninstall → git status
tools/selftest.ps1             검증 매트릭스 1–8 자동 실행(에러 주입·동시 루프·worktree submit/land)
tools/scenarios/*.json         플레이 시나리오        HarnessOut/           캡처·result.json·report.json, player-build/(개발 빌드 플레이어) (gitignore)
golden/<Unity 버전>/<시나리오>/  기준 이미지(커밋): 루프 샷과 비교 (loop.ps1 -UpdateGolden이 씀)
AgentScripts/                  eval_file / run_script 용 임시 C# (gitignore)
```

## 새 모듈 만들기 (파일 충돌 없이 병렬 작업)

`Assets/Game/<Name>/` 아래에만 파일을 만든다.

`Game.<Name>.asmdef`:
```json
{ "name": "Game.<Name>", "rootNamespace": "Game.<Name>",
  "references": ["Harness.Runtime", "Game.Contracts", "Unity.InputSystem", "Unity.Pipeline", "Unity.Pipeline.Attributes"], "autoReferenced": false }
```
(`Unity.Pipeline*`은 `[CodeReload]`(핫 루프)용. 출시 빌드에서는 `Unity.Pipeline`이 define 제약으로 빠지고 표식은 아무것도 하지 않는다.)
`Builders/Game.<Name>.Builders.asmdef`:
```json
{ "name": "Game.<Name>.Builders", "references": ["Harness.Runtime", "Harness.Editor",
  "Unity.RenderPipelines.Core.Runtime", "Unity.RenderPipelines.Universal.Runtime"],
  "includePlatforms": ["Editor"], "autoReferenced": false }
```

런타임 모듈 (자기 등록 — 공유 파일을 고칠 필요가 없다):
```csharp
public sealed class FooModule : IGameModule
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register() => GameRoot.Register(new FooModule());

    public string Name => "Foo";          // = 폴더명 = 빌더가 만드는 씬 루트 이름
    public int Order => 0;                // 낮을수록 먼저 Init/Tick
    IDisposable m_Sub;
    public void Init(GameContext ctx) {   // 씬 로드 후. ctx.Find("Foo", "Child/Path")
        m_Sub = EventBus.Subscribe<SpinnerLap>(e => { /* ... */ });
    }
    [CodeReload]                          // using Unity.Pipeline.CodeReload; 본문(+ 거기서 부르는 새 메서드)만 고치면 loop.ps1 -Hot으로 컴파일 없이 반영
    public void Tick(float dt) { }        // 매 프레임 (예외는 GameRoot가 잡아 로그 → report.runtimeErrors)
    public void Dispose() { m_Sub?.Dispose(); }
}
```
모든 모듈 Init이 끝나면 `HarnessProbe.Ready = true` — 시나리오 시계는 이때 0이다. 모듈끼리는 **EventBus로만** 통신한다
(`EventBus.Publish(new X(...))`, 공유 이벤트 struct는 `Assets/Game/Contracts/<Name>Events.cs` — 아래 "계약 폴더").

빌드 스텝:
```csharp
public sealed class FooBuildStep : IBuildStep
{
    public int Order => 100;   // 0-99 환경(카메라/라이트/하늘/후처리) · 100-899 콘텐츠 · 900+ 마무리
    public void Build(BuildContext ctx)
    {
        var mesh = ctx.SaveMesh(MeshBuilder.Sphere(0.5f).ToMesh("Ball"), "Ball");       // Assets/Generated/Foo/Ball.asset
        var mat  = ctx.LitMaterial("BallMat", m => { m.BaseColor = Color.red; m.Emission = Color.red * 2f; });  // URP Lit, 키워드 자동
        var glow = ctx.Material("Glow", "Game/Foo/MyShader", m => m.SetFloat("_Glow", 2f)); // 직접 쓴 셰이더
        var go   = ctx.MeshObject("Props/Ball", mesh, mat);                              // 씬: Foo/Props/Ball
        var tex  = ctx.SaveTexture(TextureBaker.Bake(256, 256, (u, v) => Color.white), "BallTex"); // PNG (Read 가능)
        ctx.VolumeProfile("Post", p => p.Add<Bloom>(true).intensity.value = 1f);         // Volume 오버라이드
        ctx.UIDocument("HUD", "Assets/Game/Foo/UI/Hud.uxml");                            // UI Toolkit
        ctx.Shot("foo_close", new Vector3(0, 2, -4), Vector3.zero, 45f);                // 캡처 프리셋
        ctx.Particles("Sparks", p => { p.Rate = 30f; p.Material = ctx.ParticleMaterial("Spark", m => m.Blend = ParticleBlend.Additive); });  // 아래 "파티클·애니메이션"
        ctx.Animate(go, ctx.AnimationClip("Bob", c => c.Position("", (0f, Vector3.zero), (1f, Vector3.up), (2f, Vector3.zero))));      // Playables로 재생
        if (!ctx.CacheHit("bake", new[] { "Big.png" }, someParam)) { /* 무거운 베이크 후 저장 */ }
    }
}
```
`BuildContext`는 산출물을 제자리 덮어쓰기(GUID 유지)하고, 이번 빌드에서 아무도 만들지 않은 `Assets/Generated` 에셋은 지운다.
`ctx.CacheHit`: 스텝 어셈블리·Harness.Runtime 코드, 모듈 폴더의 셰이더 소스(include 포함)와 입력이 같으면 재생성을 건너뛴다(`harness_build {"no_cache":true}`로 무시).
생성 메시·큐브맵은 Mesh·Texture API로 제자리 덮어쓴다(GUID 유지, 바꾼 첫 루프부터 새 모양이 그려짐 — 아래 "함정").
환경 헬퍼: `ctx.Sky(sun, s => …)`(구름 하늘), `ctx.BakeSkyReflection()`(스카이박스→HDR 큐브맵, 기본 반사로), `ctx.SkyAmbient(cube)`(그 큐브맵의 SH를
씬 라이팅 데이터의 앰비언트로 — 스카이박스 앰비언트를 베이크 없이), `ctx.ReflectionProbe(path, size)`(빌드 뒤 씬을 찍는 반사 프로브) — 아래 "하늘·반사",
`ctx.Create(path, types)`, `ctx.Root()`, `ctx.Seed(salt)`.
- 머티리얼: URP Lit은 `ctx.LitMaterial(name, m => …)`(`LitSettings`: BaseColor/BaseMap/Tiling, Metallic/Smoothness/MetallicGlossMap,
  NormalMap/NormalScale, OcclusionMap, Emission/EmissionMap, Transparent, AlphaClip, Cull, ReceiveShadows). 키워드·큐·블렌드는 셰이더 검증이
  설정에서 만든다 — 이미션은 GI 플래그(인스펙터의 Emission 체크)로 켜지므로 `EnableKeyword("_EMISSION")`은 검증이 되돌린다.
  다른 셰이더는 `ctx.Material(name, shader, m => …)`: 셰이더에 없는 프로퍼티(오타, 다른 파이프라인 이름), URP가 읽지 않는 옛 이름
  (`_MainTex`·`_Color`·`_Glossiness`), 검증이 덮어쓴 값, 이미션 색만 넣고 꺼진 이미션은 `build.warnings`에 나온다.

### 계약 폴더 (Assets/Game/Contracts, G5-4)

모듈이 병렬 에이전트끼리 만나는 유일한 곳이라 규칙을 기계가 지킨다:
- **발행 모듈별 파일**: 모듈 M이 발행하는 이벤트는 `Contracts/MEvents.cs`에(샘플 `SmokeEvents.cs`). 한 이벤트는 한 모듈만 발행한다. `harness_lint`의
  `contract-file`이 모듈 코드의 IL에서 `EventBus.Publish<T>`를 찾아 대조하고, 모듈 이름이 아닌 파일 이름도 잡는다. 한 모듈 = 한 에이전트라 두 에이전트가 한 파일을 고칠 일이 없다.
- **이름은 한 번**: `play.events`가 타입 이름으로 세므로 계약 전체에서 같은 이름(네임스페이스가 달라도)은 `contract-name`. submit·land는 복사·병합 전에
  거부한다(`contractConflicts` — 다른 worktree가 올리고 아직 land하지 않은 것 포함).
- **타입 단위 추가만**: land된 타입은 바뀌지 않는다(주석·줄바꿈은 바뀐 것이 아님). 새 이벤트는 자기 파일에 덧붙인다. submit·land가 거부(`contractChanged`).
- **미병합 계약은 올린 worktree 것**: submit이 쓴 계약 파일은 land될 때까지 그 worktree 소유(`Library/Harness/submit/contracts.json`) — 그 worktree는 다시 고치거나
  지울 수 있고, 다른 worktree는 거부된다(`contractOwner`, 버려진 작업이면 `-Takeover`). land가 해제한다. 이 worktree가 바꾸지 않은 계약 파일(다른 모듈이 그새 덧붙여
  뒤처졌을 뿐)은 건너뛴다(`contractsBehind`).
- 계약에 다른 모듈이 쓰는 타입과 같은 이름(`Light`, `ClipEvent`)을 더하면 그 모듈이 CS0104로 깨진다 → submit의 사전 컴파일 검사가 계약을 쓰는 모듈도 컴파일해 잡는다
  (`compile-check -Dependents`).
- 소스가 선언하는 타입은 `tools/uc.ps1 harness_contracts '{"sources":"[{\"id\":\"a\",\"path\":\"Assets/Game/Contracts/SmokeEvents.cs\"}]"}'`(Roslyn, 컴파일 없음; `hash`가
  같으면 같은 타입).

### 파티클·애니메이션 (G1-3)

```csharp
// 파티클: ParticleSettings 한 벌 → ParticleSystem (샘플: Assets/Game/Smoke/Builders/SmokeFxStep.cs)
ctx.Particles("Embers", p =>
{
    p.Prewarm = true;                                            // 첫 프레임부터 공중에(루프일 때)
    p.Lifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);     // float = 상수, (min, max) = 무작위 범위, (배율, 커브)
    p.Speed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
    p.Size = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
    p.Color = new Color(1f, 0.45f, 0.12f);                       // 8비트(0..1): 발광은 머티리얼의 HDR 색으로
    p.Rate = 40f; p.Angle = 10f; p.Radius = 1.9f;                // 원뿔(기본)은 위(+Y)로: ShapeRotation 기본 (-90, 0, 0)
    p.Space = ParticleSystemSimulationSpace.World;               // 이미터가 움직여도 뿜은 입자는 제자리
    p.ColorOverLifetime = TextureBaker.Ramp((0f, new Color(1f, 0.9f, 0.5f, 0f)), (0.1f, Color.white), (1f, new Color(1f, 0.2f, 0f, 0f)));
    p.SizeOverLifetime = AnimationCurve.Linear(0f, 1f, 1f, 0.3f);
    p.NoiseStrength = 0.4f;                                      // 0 = 모듈 끔 (Velocity, Drag, RotationOverLifetime도 같은 식)
    p.Material = ctx.ParticleMaterial("Ember", m => { m.Blend = ParticleBlend.Additive; m.Color = Color.white * 2f; });
}).transform.localPosition = new Vector3(0f, 1.45f, 0f);

// 애니메이션: 키를 코드로 → <name>.anim, ClipPlayer가 Playables로 재생 (AnimatorController 에셋 없음)
var orbit = ctx.AnimationClip("HaloOrbit", c =>
{
    c.Loop = true;
    c.Rotation("RingA", (0f, new Vector3(68f, 0f, 0f)), (4f, new Vector3(68f, 360f, 0f))).Linear();   // Euler(도)를 숫자 그대로 보간: 한 바퀴
    c.Scale("", (0f, Vector3.one), (2f, Vector3.one * 1.06f), (4f, Vector3.one));                     // "" = 재생하는 오브젝트 자신
    c.Color("RingA", typeof(MeshRenderer), "material._EmissionColor", (0f, cyan), (2f, magenta), (4f, cyan));
    c.Float("Lamp", typeof(Light), "m_Intensity", (0f, 1f), (0.5f, 4f)).Constant();                   // 직렬화 이름(YAML의 m_…)
    c.Active("Spark", (0f, false), (1f, true));
    c.Event(2f, "HaloHalfTurn");                                                                        // → ClipEvent, play.events
});
var player = ctx.Animate(halo, orbit);   // Animator(컨트롤러 없음, 루트 모션 끔) + ClipPlayer; 첫 클립이 씬 시작 때 재생
```
- 파티클은 **모듈+경로에서 만든 고정 시드**(`useAutoRandomSeed` 끔), 화면 밖에서도 시뮬레이션(`AlwaysSimulate` — 자동이면 화면 밖 루프 시스템이 멈춰
  뒤 프레임이 카메라가 본 것에 달라진다), 씬 시작 때 재생. 고정 시간 간격 시나리오라 캡처가 매번 픽셀까지 같다(기준 이미지 비교 가능).
  `ParticleSettings`: Duration/Loop/Prewarm, Lifetime/Speed/Size/Rotation(도)/Color/Gravity/Space/MaxParticles, Rate/`Burst(t, n)`, Shape/Angle/Radius/
  RadiusThickness/Arc/BoxSize/ShapeRotation/ShapePosition, ColorOverLifetime/SizeOverLifetime/RotationOverLifetime/Velocity/Drag/Noise*, Material/RenderMode/
  LengthScale/VelocityScale/Mesh/CastShadows/ReceiveShadows/SortMode. 그 밖의 모듈(서브 이미터·트레일·라이트)은 반환된 ParticleSystem을 고친다.
  `ctx.ParticleMaterial`: URP Particles/Unlit(투명), `Blend`(Alpha/Premultiply/Additive/Multiply), `Color`(HDR 가능), `SoftParticles`, `Cull`, `Texture`
  (없으면 생성한 부드러운 점 `ParticleDot.png`). 모듈 코드에서 터뜨리기: `ctx.Find("Foo", "Sparks").GetComponent<ParticleSystem>().Emit(30)`.
- `ClipBuilder`: `Position`/`Rotation`/`Scale`(경로, (초, Vector3)…), `Float`/`Color`(경로, 컴포넌트 타입, 직렬화 이름, 키…), `Active`, `Event`, `Loop`,
  `FrameRate`. 키는 시간 순. 트랙마다 `.Linear()`(등속)·`.Constant()`(계단), 기본은 Smooth(애니메이션 창의 Clamped Auto). 경로는 `Animate`한 오브젝트 기준.
  `Color` 값은 `Material.SetColor`/인스펙터와 같은 의미(`LitSettings.Emission`과 같은 숫자, HDR 가능).
- **빌드 뒤 검사**: 모든 스텝이 끝난 뒤 `Animate`한 클립의 커브마다 대상에서 경로·컴포넌트·속성을 찾는다 — 없으면 `build.warnings`에 트랙마다 한 줄
  (`no child 'Ring'`, `has no Light`, `material._BaseColr is not a property of the materials of 'Cube' (similar: material._BaseColor, …)`). 틀린 이름의
  커브는 에러 없이 아무것도 안 한다. 그리고 씬은 첫 클립의 0초 포즈로 저장된다(클립이 움직이는 값은 빌더가 준 값을 덮는다) → 편집 모드 캡처가 플레이 시작 모습.
- 런타임 `ClipPlayer`(모듈 코드): `Play(name, fade)`(처음부터, 앞 클립에서 `fade`초 크로스페이드), `Stop()`, `Current`, `Time`, `IsDone`(루프 아닌 클립이 끝나
  마지막 프레임을 유지), `speed`. `AddComponent<ClipPlayer>()` 뒤에 `clips`를 넣어도 `Play`가 반영한다. 게임 시간(`Time.deltaTime`)으로 돈다.
  클립 이벤트는 `EventBus.Subscribe<ClipEvent>(e => …)`(`e.Name`, `e.Source`)로 받고 `play.events`에 **`ClipEvent:<이름>`**으로 세진다 → "애니메이션이 그 순간에
  닿았다"를 기계적으로 검증(샘플 기본 루프: `ClipEvent:HaloHalfTurn=1`).
- 편집 모드 캡처(`harness_capture`, `-NoPlay`)에는 파티클이 없다(시뮬레이션하지 않음). 움직임은 플레이 캡처·연속 캡처(`"frames"`)로 본다.
- 출시 빌드: `ClipPlayer`는 `Harness.Runtime`에 있어 `GameRoot`처럼 `AGENTHARNESS_RUNTIME`이 필요하다. 타임라인(TimelineAsset) 헬퍼는 없다 — 여러 오브젝트의
  순서는 클립 + `ClipEvent` + 모듈 코드로.

### GPU 베이크 · 절차적 라이브러리 (G4-1, G4-4)

```csharp
// GPU 베이크: 베이크 셰이더(HLSL)의 한 패스가 텍스처의 모든 텍셀을 그린다 → <name>.png (샘플: Assets/Game/Stage/Shaders/TerrainBake.shader)
var heights = BuildContext.FloatTexture(TextureBaker.SampleGrid(257, 257, (u, v) => Height(u, v, seed)), 257, 257);   // CPU 값을 GPU 입력으로
var albedo = ctx.BakeTexture("TerrainAlbedo", 1024, 1024, "Game/Stage/TerrainBake", m => { m.SetTexture("_HeightMap", heights); m.SetInteger("_Seed", seed); },
                             wrap: TextureWrapMode.Clamp, pass: 0);                       // sRGB 색, normalMap: true / sRGB: false도
Object.DestroyImmediate(heights);
// 절차적 메시
var stone = MeshBuilder.FromSdf(p => Sdf.SmoothSubtract(Sdf.RoundBox(p, half, 0.16f), Sdf.Sphere(p - top, 0.42f), 0.12f), bounds, cellSize: 0.07f);
var arch  = MeshBuilder.Tube(new Spline(points), t => Mathf.Lerp(0.55f, 0.32f, Mathf.Sin(t * Mathf.PI)), segments: 96, radialSegments: 16);
var rock  = MeshBuilder.Rock(seed, radius: 1f, subdivisions: 2, roughness: 0.4f);
foreach (var p in Scatter.Poisson(new Rect(-150, -150, 300, 300), minDistance: 5.5f, seed)) { /* 높이·경사로 거르고 */ all.Append(rock, Matrix4x4.TRS(...)); }
// 데칼(URP): 렌더러에 DecalRendererFeature가 있어야 그린다(ISettingsStep에서 SettingsContext.AddRendererFeature<DecalRendererFeature>).
//   샘플은 ScreenSpace: decals => SettingsContext.Set(decals, "m_Settings.technique", 2) — DBuffer(데스크톱 기본)는 가장자리 픽셀이 실행마다 갈릴 수 있다(G3-15)
ctx.Decal("Props/Runes", ctx.DecalMaterial("Runes", runeTexture), new Vector3(8f, 8f, 3f)).transform.localPosition = new Vector3(0, 1.5f, 0);
// 가까이서 선명하게: URP Lit 디테일 맵(타일링)
ctx.LitMaterial("Terrain", m => { m.BaseMap = albedo; m.DetailAlbedoMap = detail; m.DetailNormalMap = detailNormal; m.DetailTiling = new Vector2(80, 80); });
```
- 베이크 셰이더: `#include "Packages/com.geuneda.agentharness/Shaders/HarnessBake.hlsl"` → 정점 셰이더 `BakeVert`(전체 화면 삼각형, `i.uv` (0,0) = 텍스처 첫 텍셀 =
  아래 왼쪽, `TextureBaker`의 (u, v)와 같음), `_BakeTexelSize`(1/w, 1/h, w, h), `SrgbToLinear`/`LinearToSrgb`, `EncodeNormal(dhdx, dhdy, strength)`. 보통 셰이더처럼
  **선형 색**을 돌려준다(sRGB 베이크는 PNG에 sRGB로 저장). `ZTest Always ZWrite Off Cull Off`. 속성의 정수는 ShaderLab `Integer`로 선언한다(`Int`는 float라 `SetInteger`와 충돌).
- `HarnessNoise.hlsl`(HarnessBake가 가져옴): `Noise_Perlin/Fbm/Ridged/Worley/Value01/Hash` — **C# `Harness.Procedural.Noise`와 같은 해시·기울기·옥타브 시드**라
  CPU로 만든 지형 메시와 GPU로 구운 텍스처가 같은 무늬다(8비트 반올림 안에서 같음, selftest가 확인). 타일링 텍스처는 `Noise_PerlinTiled/FbmTiled/WorleyTiled`.
- 비용: 1024² 그리기+리드백 ~9 ms, 2048² ~28 ms. 대부분은 PNG 인코딩(1024² ~70 ms)과 임포트(압축)다. **입력(셰이더 소스와 include, 속성 값, 입력 텍스처, 크기)이 디스크의 PNG와
  같으면 그리지 않는다**(`build.bakesSkipped`). 그 입력 해시가 임포터 `userData`에 남고 **fingerprint는 PNG 대신 그 해시**를 쓴다(GPU·드라이버마다 끝 비트가 다를 수 있어;
  모양은 기준 이미지가 본다).
- `ctx.CacheHit`의 키에는 모듈 폴더의 셰이더 소스(.shader/.hlsl/.cginc/.compute, include 포함, 줄바꿈 정규화)와 하네스 `Shaders/`도 들어간다 → 베이크 셰이더만 고쳐도 다시 굽는다.
- `Sdf`: `Sphere/Box/RoundBox/Capsule/Torus/Cylinder/Plane`, `Union/Subtract/Intersect`, `SmoothUnion/SmoothSubtract/SmoothIntersect(k)`, `Normal`. 음수가 안쪽, 원점 중심 —
  옮기려면 `p - center`, 돌리려면 `Quaternion.Inverse(rot) * p`. `MeshBuilder.FromSdf(sdf, bounds, cellSize, uvScale)`: 서피스 네트(칸마다 정점 하나를 면 위로 옮김) →
  닫힌 매끈한 메시, 노멀은 SDF에서, UV는 박스 투영. 두 칸보다 작은 모양은 사라진다. 셀 1,600만 개가 한도.
- `Spline(points, closed)`: 구심 Catmull-Rom(점이 몰려도 고리·뾰족점 없음), `Evaluate(t)`·`Tangent(t)`는 **호 길이** 기준(같은 t 간격 = 같은 거리), `Length`.
  `MeshBuilder.Tube(spline, radius(t) 또는 float, segments, radialSegments, caps)`: 회전 최소 프레임(갑작스런 비틀림 없음), 닫힌 튜브는 남는 비틀림을 전체에 나눈다.
- `Scatter.Poisson(rect, minDistance, seed)`: 포아송 디스크(겹치지도 뭉치지도 않음, 같은 시드 = 같은 점·순서). 높이·경사·거리로 거른 뒤 `MeshBuilder.Append`로 한 메시에
  모으거나(수백 개도 드로콜 하나) 오브젝트를 둔다. `MeshBuilder.Rock(seed, radius, subdivisions 0-4, roughness, scale)`: 노이즈로 울퉁불퉁한 아이코스피어, 면마다 평평한 노멀.
  `MeshBuilder.Icosphere(n)`, `MeshBuilder.BoxUv(p, n)`.
- `ctx.DecalMaterial(name, baseMap, normalMap, normalBlend)`(URP Decal 셰이더 그래프: 색 + 알파 = 덮는 정도), `ctx.Decal(path, material, size, pointDown)`
  (기본으로 아래를 향함, 상자 깊이는 투영 방향). 빌드 뒤 렌더러에 `DecalRendererFeature`가 없으면 `build.warnings`.
- `LitSettings`에 디테일 맵: `DetailAlbedoMap`(선형, 0.5 = 변화 없음 — URP가 알베도에 2 × 이 값을 곱한다; sRGB로 임포트돼 있으면 경고), `DetailNormalMap`, `DetailNormalScale`,
  `DetailTiling`, `DetailMask`.
- 빌드가 만드는 큰 메시(샘플 바위 14만 정점)는 매 빌드 디스크에서 다시 읽고 fingerprint가 해시한다(각 수십 ms) — 멀리 보이는 것은 덜 쪼갠다.

### 하늘·반사 (G4-5)

```csharp
// 하늘: 그라디언트 + 태양 + fBm 구름(게임 시간으로 흐름) — 샘플: Assets/Game/Stage/Builders/StageEnvironmentStep.cs
ctx.Sky(sun, s => { s.CloudCoverage = 0.45f; s.Wind = new Vector2(0.02f, 0.008f); });   // RenderSettings.skybox·sun + <Module>/Sky의 SkyClock
ctx.SkyAmbient(ctx.BakeSkyReflection());   // 하늘 → HDR 큐브맵(mip은 GGX로 거름) = 기본 반사, 그 SH = 앰비언트
// 반사 프로브: 빌드가 끝난 뒤 그 자리에서 씬을 찍은 큐브맵(Custom) — 샘플: Assets/Game/Smoke/Builders/SmokeBuildStep.cs
pedestal.isStatic = true;   // 프로브에 그려지는 것 = Reflection Probe Static 렌더러(Unity의 베이크 프로브 규칙) + 하늘·라이트·데칼·안개
var probe = ctx.ReflectionProbe("Probe", new Vector3(19f, 12.4f, 19f), 256);   // 박스(이 안의 오브젝트가 씀, 박스 투영 켬), 해상도(기본 128)
probe.transform.localPosition = new Vector3(0f, 3.4f, 0f);   // 찍는 자리 — 프로브는 빌드 스텝이 다 돈 뒤에 그려지므로 호출 뒤에 옮겨도 된다
probe.center = new Vector3(0f, 2.4f, 0f);                    // 박스를 땅에 맞춤(박스 투영이 그 면 위에 비춘다)
```
- `ctx.Sky(sun, setup)`: 패키지 셰이더 `Harness/Sky`(`Shaders/HarnessSky.shader`, 파이프라인 무관 스카이박스)의 `Sky.mat`. `SkySettings`: `Zenith`/`Horizon`/`Ground`
  (색은 `Material.SetColor`·인스펙터 값), `HorizonFalloff`, `SunColor`(기본 = 태양 라이트 색)/`SunSize`(각반지름, 도)/`SunIntensity`/`SunGlow`, `CloudCoverage`(0 맑음 – 1 흐림)/
  `CloudSharpness`/`CloudScale`/`CloudOpacity`/`CloudColor`/`CloudShadow`/`Wind`(초당 하늘 단위)/`CloudOctaves`/`Seed`(기본 `ctx.Seed("sky")`), `Exposure`. 태양 원반은
  빌드 때 라이트가 비추는 방향(`_SunDirection`) — 게임이 태양을 돌리면 그 머티리얼 값도 바꾼다. 원반과 태양 쪽 구름은 HDR(1 넘음)이라 블룸이 걸린다.
- 구름 시계: 전역 셰이더 값 `_HarnessSkyTime`을 `SkyClock`(Harness.Runtime, `ctx.Sky`가 둠)이 매 프레임 `Time.timeSinceLevelLoad`(게임 시간)로 → 고정 시간 간격
  시나리오에서 같은 t면 같은 구름(실시간이면 G3-10처럼 캡처가 흔들린다). 플레이가 끝나면 0 → 편집 모드 캡처와 빌드가 찍는 큐브맵은 시간 0의 구름. 픽셀보다 작아진
  노이즈 옥타브는 지운다(지평선·작은 큐브맵 면에서 반짝이지 않음). 출시 빌드에서 구름이 흐르려면 `GameRoot`처럼 `AGENTHARNESS_RUNTIME`.
- `ctx.ReflectionProbe(path, size, resolution)`: Custom 모드 프로브(HDR, 박스 투영). **모든 빌드 스텝이 끝나고 씬이 라이팅 데이터와 함께 저장된 뒤**
  (`build.phases.probes`, `build.reflectionProbes`) 그 자리에서 씬을 큐브로 그려 mip을 GGX로 거르고 `Reflection_<path>.asset`에 둔다(씬은 그 에셋을 참조).
  Reflection Probe Static이 아닌 렌더러(돌아가는 매듭·링·파티클)는 그리지 않는다 — 움직이는 것은 빌드 때 자리로 박혀 틀리게 비친다. 그리는 동안 다른 프로브는 끈다
  (한 번 튐: 프로브 안의 금속은 하늘을 비춘다). 그 자리에서 씬을 본 것이라 박스 안 오브젝트의 반사가 하늘이 아니라 주변이 된다(샘플: 프로브 없이는 강철 받침대가 하늘만
  비춰 하얗게 뜬다).
- 큐브맵(하늘·프로브)은 매 빌드 다시 그리고 **픽셀이 바뀐 때만 쓴다**(`build.cubemapsWritten` — 같은 머신·같은 씬이면 바꾼 뒤 첫 빌드에만; 새 클론(새 `Library/`)은
  첫 빌드가 셰이더 변형을 처음 컴파일하며 다음 빌드와 조금 달라 둘째 빌드도 쓴다 — 샷 차이 2–3단계, 그 뒤로 픽셀까지 같음). fingerprint에는 큐브맵의
  모양만 들어간다(`fingerprint.txt`에 `cubemap 256 RGBAHalf 9` — GPU 결과라 끝자리가 GPU마다 다를 수 있다, 보이는 모양은 기준 이미지가 본다). 비용(샘플, 창 에디터, 웜):
  하늘 128² ~13–16 ms, 프로브 256² ~20–55 ms(빌드가 쓰는 변경 뒤 첫 번째는 쓰기 포함 ~0.1–0.2 s).
- mip의 GGX 프리필터(`Shaders/HarnessCubeFilter.shader`): mip m = URP가 그 mip에서 읽는 거칠기(`PerceptualRoughnessToMipmapLevel`, 6단계)의 GGX 로브, 고정 Hammersley 256표본 +
  원본 박스 mip에서 거른 중요도 표본 → 매번 같은 값, 라이팅 베이크의 컨볼루션처럼 거친 면이 면 경계(seam) 없이 흐리다(W15 전 `BakeSkyReflection`은 박스 mip).
- **손으로 쓴 URP 셰이더가 프로브를 받으려면**: Forward+(샘플 PC 렌더러, 프로브 블렌딩 켬)는 프로브를 클러스터 루프로만 준다(오브젝트별 `unity_SpecCube0`는 채우지 않음) →
  `#pragma multi_compile _ _CLUSTER_LIGHT_LOOP`(6.0은 `_FORWARD_PLUS` — `#if UNITY_VERSION >= 60010000`로 가른다 — Unity 6의 `UNITY_VERSION`은 6000.3.11 = 60030011, 6.1+에서 `_FORWARD_PLUS`를 선언하면 폐기 경고),
  `_REFLECTION_PROBE_BLENDING`·`_REFLECTION_PROBE_BOX_PROJECTION`(fragment), `GlossyEnvironmentReflection(reflect(-v, n), positionWS, 1 - smoothness, 1,
  GetNormalizedScreenSpaceUV(positionCS))`. 샘플: `Assets/Game/Smoke/Shaders/SmokeIridescent.shader`. 키워드가 없으면 에러 없이 하늘만 비친다. URP Lit은 그대로 된다.

### UI 킷 (G1-4)

`ctx.UIDocument`가 만드는 패널의 테마(`Packages/com.geuneda.agentharness/UI/DefaultRuntimeTheme.tss`)가 `HarnessKit.uss`를 가져온다 → 모든 HUD에서
킷의 변수와 클래스를 쓸 수 있다. 샘플: `Assets/Game/Smoke/UI/SmokeHud.uxml`(+ `SmokeHudData.cs`, `SmokeModule.cs`).
```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:ah="Harness.UI">
    <ui:VisualElement class="ah-panel ah-panel--accent">                    <!-- 반투명 판 + 왼쪽 강조선 -->
        <ui:Label class="ah-title" text="SCORE" />
        <ui:VisualElement class="ah-row"><ui:Label class="ah-key" text="HP" /><ui:Label class="ah-value" text="0">
            <Bindings><ui:DataBinding property="text" data-source-path="Hp" binding-mode="ToTarget" /></Bindings></ui:Label></ui:VisualElement>
        <ah:Gauge class="ah-gauge--good">                                     <!-- value / max 만큼 찬 막대 -->
            <Bindings><ui:DataBinding property="value" data-source-path="HpRatio" binding-mode="ToTarget" /></Bindings></ah:Gauge>
    </ui:VisualElement>
    <ah:ToastStack name="toasts" />                                           <!-- 화면 위쪽 가운데 -->
    <ui:Button name="play" class="ah-button" text="PLAY" />                   <!-- ah-button--ghost: 테두리만 -->
</ui:UXML>
```
```csharp
public sealed class HudData { [CreateProperty] public int Hp { get; set; } [CreateProperty] public float HpRatio { get; set; } }   // using Unity.Properties;
root.dataSource = m_Hud;                                   // Init: 바인딩은 매 프레임 이 객체를 읽는다 → 모듈은 값만 바꾼다
root.Q<ToastStack>("toasts").Show("LEVEL UP", 1.5f, "good");   // using Harness.UI; 페이드·슬라이드 인 → 유지 → 페이드 아웃 → 제거
root.Q<Button>("play").clicked += OnPlay;                  // 시나리오: {"type": "click", "target": "play"}
```
- 변수(`:root`): `--ah-bg`, `--ah-bg-strong`, `--ah-line`, `--ah-fg`, `--ah-muted`, `--ah-accent`, `--ah-accent-soft`, `--ah-accent-2`, `--ah-good`/`--ah-warn`/`--ah-bad`,
  `--ah-radius`, `--ah-radius-pill`, `--ah-space-s`/`--ah-space`/`--ah-space-l`, `--ah-font-s`/`--ah-font`/`--ah-font-l`, `--ah-fade`. 한 서브트리만 바꾸려면
  그 요소에서 다시 정의한다(`.my-hud { --ah-accent: rgb(255, 160, 40); }`).
- 클래스: `ah-panel`(+`--accent`), `ah-row`, `ah-title`, `ah-key`, `ah-value`, `ah-hint`, `ah-button`(+`--ghost`, hover·active·disabled 상태 포함),
  `ah-gauge`(+`--good/--warn/--bad`), `ah-toast-stack`, `ah-toast`(+`--good/--warn/--bad`). 자기 USS에는 배치만 두고 모양은 킷 클래스로.
- 컨트롤(`Harness.UI`, `Harness.Runtime`): `Gauge`(`value`, `max`, 둘 다 UXML 속성·바인딩 가능), `ToastStack.Show(text, seconds, kind)`.
- 데이터 바인딩(Unity 6 런타임 바인딩): UXML의 `<Bindings><ui:DataBinding property="…" data-source-path="…" /></Bindings>` + 조상 요소의 `dataSource` +
  `[CreateProperty]` 속성. int → 라벨 text 같은 기본 변환은 된다. 모듈 코드가 라벨을 찾아 `text`를 넣을 필요가 없다.
- 움직임은 USS transition 그대로 쓴다 — 시나리오 동안 UI 시간이 프레임을 따르므로 transition 도중의 캡처도 매번 같다(위 "시나리오", G3-10).
- 한글·일본어·중국어는 기본 테마 폰트에 없지만 에디터가 OS 폰트로 대신 그린다(이 머신에서 확인). 플레이어·다른 OS에서는 그 OS에 있는 폰트에 달렸다 —
  출시할 게임은 폰트 에셋을 따로 정한다.
- 출시 빌드: 킷 컨트롤은 `Harness.Runtime`이라 `GameRoot`처럼 `AGENTHARNESS_RUNTIME`이 필요하다(USS 클래스만 쓰면 필요 없다 — 테마는 에셋).

### 렌더 설정 (ISettingsStep, G1-1)

```csharp
public sealed class FooRenderSettings : ISettingsStep     // Builders/ 폴더, 빌드 스텝보다 먼저 돈다
{
    public int Order => 0;
    public void Apply(SettingsContext ctx)
    {
        var pc = ctx.UniversalPipeline("PC", rp =>           // Assets/Generated/Foo/PC_RPAsset.asset + PC_Renderer.asset
        {
            rp.shadowDistance = 50f; rp.shadowCascadeCount = 4; rp.supportsHDR = true;
            SettingsContext.Set(rp, "m_SoftShadowsSupported", true);        // public setter가 없는 필드: 직렬화 경로(YAML 이름)
        }, renderer =>
        {
            renderer.renderingMode = RenderingMode.ForwardPlus;
            SettingsContext.AddRendererFeature<ScreenSpaceAmbientOcclusion>(renderer, f => SettingsContext.Set(f, "m_Settings.Intensity", 0.4f));
        });
        ctx.UsePipeline(pc);            // Graphics 설정의 기본 파이프라인
        ctx.UsePipeline(pc, "PC");      // 이 품질 레벨의 파이프라인(레벨은 이름으로, 있어야 함)
    }
}
```
- `harness_build`가 매번(빌드 스텝 전에), `harness_setup`도 돌린다(새 클론이 첫 루프 전에 파이프라인을 갖게). **`setup: harness`에서만** —
  `attach` 프로젝트의 RP 에셋은 건드리지 않는다(`build.warnings`에 건너뛴 스텝 수).
- 매번 URP 새 에셋 기본값에서 시작해 코드를 적용한다 → 코드가 정하지 않은 값은 그 Unity 버전의 기본값. 디스크의 에셋과 내용이 같으면 쓰지 않는다
  (`build.settings.written`이 비어 있음, ~10 ms), 다르면 제자리 덮어쓰기.
- 에셋은 생성물(`Assets/Generated/<모듈>/`, gitignore)이고 GUID는 경로에서 만든다(MD5) → 지워도 루프 한 번이면 같은 GUID로 다시 생기고, 그 GUID를
  참조하는 `ProjectSettings/GraphicsSettings.asset`·`QualitySettings.asset`은 바뀌지 않는다. 새 클론은 첫 `harness_setup`/루프까지 Built-in으로 열린다(씬도 아직 없다).
- 설정 스텝이 **활성 파이프라인을 바꾸면**(새 클론의 첫 `harness_setup`, RP 에셋을 지운 뒤의 루프) 도메인 리로드를 요청하고(`settings.switched`), `uc.ps1`과
  루프는 리로드가 끝난 뒤 계속한다(`timings.reloadSec`, ~2–10 s). Unity 6.6은 Built-in으로 시작한 세션에서 URP로 바뀐 뒤 새 셰이더 변형의 첫 그리기를 빼먹었다(아래 "함정").
- `SettingsContext.Set(obj, "경로", 값)`: 이 Unity 버전에 그 필드가 없거나 타입이 안 맞으면 예외(스텝의 줄로 `stage=build`) — 비슷한 필드 이름을 알려 준다.
  필드 이름은 생성된 에셋 YAML(`Assets/Generated/…_RPAsset.asset`)에서 읽는다.
- 설정값·파이프라인 배정은 `build.fingerprint`에 들어간다(`Library/Harness/fingerprint.txt`의 `--settings--`). 씬의 RenderSettings(안개·앰비언트·
  스카이박스·반사)와 라이팅 데이터 참조도 fingerprint에 들어간다(GPU로 구운 큐브맵·그 SH는 참조만 — GPU마다 끝자리가 다를 수 있어 기준 이미지가 본다).
- URP의 전역 설정(`Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`, `DefaultVolumeProfile.asset`)은 URP가 없으면 새 GUID로 만들어서 커밋된 채 둔다.

### 프로젝트 설정 (ISettingsStep, G1-5)

```csharp
public sealed class FooProjectSettings : ISettingsStep       // 렌더 설정과 같은 스텝이어도 된다
{
    public const int Enemy = 10;
    public int Order => 10;
    public void Apply(SettingsContext ctx)
    {
        ctx.Layer(Enemy, "Enemy");                             // 사용자 레이어(3, 6-31). 빌드 스텝은 Enemy 상수나 LayerMask.NameToLayer("Enemy")
        ctx.Tag("Pickup");
        ctx.Player(p => { p.ColorSpace = ColorSpace.Linear; p.DefaultScreenWidth = 1280; p.DefaultScreenHeight = 720; });
        ctx.Time(t => t.FixedTimestep = 0.02f);
        ctx.Physics(p => { p.Gravity = new Vector3(0, -20, 0); p.IgnoreCollision("Enemy", "Enemy"); });
        // 품질 레벨 목록 전체(이 순서), 한 스텝만. pc·mobile = 같은 스텝의 ctx.UniversalPipeline(...)이 돌려준 에셋
        ctx.QualityLevels(
            new QualityLevelValues("Mobile") { Pipeline = mobile, DefaultFor = new[] { "Android", "iPhone" }, ExcludedPlatforms = new[] { "Standalone" }, LodBias = 1f },
            new QualityLevelValues("PC") { Pipeline = pc, DefaultFor = new[] { "Standalone" }, VSyncCount = 0 }.Set("terrainPixelError", 1));
        ctx.ProjectSetting("TagManager", "m_RenderingLayers.Array.data[1]", "Glow");   // 헬퍼에 없는 값: 파일 + 직렬화 경로
    }
}
```
- `ProjectSettings/`는 에디터가 시작할 때 필요해서 RP 에셋처럼 생성물로 둘 수 없다. 그래서 **코드가 이름 붙인 값만 소유**한다: 매 빌드(`harness_build`·
  `harness_setup`)가 읽어 보고 다르면 쓰고(`build.settings.project.changed`: `"키: 전 -> 후"`), 설정 파일을 저장한다. 값 묶음(`PlayerValues` 등)에서 null인 것과
  이름 붙이지 않은 값은 커밋된 YAML 그대로다. 결과를 좌우하는 값은 코드에 적는다.
- **드리프트**: 마지막 빌드가 남긴 값을 `Library/Harness/project-settings.json`에 둔다. 찾은 값이 코드와도 그것과도 다르면 코드가 바뀐 게 아니라 Project Settings 창·YAML이
  바꾼 것 → 되돌리고 `build.settings.project.drift`(키) + `build.warnings`("changed outside the code"). 6.0·6.3·6.6 모두 밖에서 고친 ProjectSettings YAML을 루프의
  recompile(`AssetDatabase.Refresh`)에 다시 읽으므로 손으로 고친 YAML도 잡힌다. **바꾸려면 설정 스텝을 고친다.**
- 목록은 선언한 것이 전부: 레이어를 하나라도 선언하면 다른 사용자 레이어는 비운다(모든 설정 스텝이 끝난 뒤), `ctx.Tag`도 선언한 목록 그대로(내장 태그는 선언할 필요
  없음), `ctx.QualityLevels`는 레벨을 이름으로 맞춰 순서를 바꾸고·더하고(앞 레벨의 복사, Unity의 "Add Quality Level"처럼)·지운다(에디터의 레벨·플랫폼별 기본 레벨은 이름을
  따라감). 에디터가 쓰는 품질 레벨은 활성 플랫폼의 기본 레벨(`DefaultFor`)이다 — 에디터에서 다른 레벨을 클릭해도 다음 루프가 되돌린다(그 레벨로 보려면 코드의 기본 레벨을
  바꾼다). 활성 파이프라인이 바뀌면 렌더 설정의 리로드 경로(`settings.switched`)를 탄다.
- 두 스텝이 같은 키를 다른 값으로 정하면 예외. 오타도 스텝의 줄로 예외: 품질 레벨 필드(비슷한 이름), 알 수 없는 플랫폼 이름(목록), 내장 레이어 번호, 선언 안 된
  레이어 이름(`IgnoreCollision`), 없는 직렬화 경로. 숫자는 1e-6(상대) 안이면 같은 값이다 — 6.3은 fixed timestep을 분수로 저장해 0.02가 0.0199999921로 읽힌다.
- 공개 API가 있으면 API로 쓴다(색 공간 전환의 텍스처 재임포트 같은 부수 효과), 없으면 SerializedObject. 쓸 때 Unity가 그 파일 전체를 이 버전의 형식으로 다시 쓴다
  (6.3: TagManager가 serializedVersion 3, 옛 형식의 TimeManager·DynamicsManager도) — 값이 같으면 쓰지 않으니 새 클론의 `git status`는 깨끗하다.
- 소유한 값은 `build.fingerprint`에 들어간다(`fingerprint.txt`의 `--project--`). `setup: attach` 프로젝트에서는 설정 스텝이 돌지 않는다(렌더 설정과 같음).
- **worktree에서**: 설정 스텝을 고친 모듈을 submit하면 에디터 트리의 루프가 ProjectSettings 파일을 바꾼다. submit은 그 파일들을 먼저 백업하고(빨간·중단된 submit은
  바뀐 파일만 복원), 녹색이면 `.meta`처럼 worktree로 되복사한다(`submit.settingsWrittenBack` — **모듈과 함께 커밋할 것**, land 때 에디터 트리 사본과 같아서 깨끗하게
  병합된다). 에디터 트리 사본이나 worktree 사본이 이미 올라간 것과 달랐으면(다른 worktree의 미병합 설정, 이 worktree에 없는 병합된 설정) 되복사하지 않는다
  (`settingsNotWrittenBack`) — **할 일 없음**: 코드만 커밋하고 land한다.
- **여러 worktree가 같은 파일을 바꿀 때(G5-6)**: 레이어처럼 한 파일(`TagManager.asset`)을 여러 모듈의 설정 스텝이 같이 쓴다. 매 빌드가 ProjectSettings 파일마다
  "빌드만 거쳐 지금 내용에 이른 내용들"을 남기므로(`project-settings.json`의 `files`, git blob id), land는 **커밋된 파일 + 설정 스텝이 쓴 것**인 미커밋 파일을 남의
  편집이 아니라 산출물로 본다: 병합이 그 파일을 바꾸면 stash로 비켜 두고, 병합 뒤 루프가 병합된 코드(와 아직 미병합인 다른 submit)로 다시 쓴다(`land.settings.regenerated`).
  녹색 land 뒤 모듈·계약 폴더에 다른 미병합 submit이 없고 에디터 트리에서 고친 코드도 없으면 그 파일들을 land 위에 커밋한다(`land.settings.commit` —
  "Project settings the landed code's settings steps write"). 있으면 미커밋으로 두고(`land.settings.waitingFor`) 그것들까지 land한 마지막 land가 커밋한다 →
  **순서와 상관없이** 마지막 land 뒤 에디터 트리가 깨끗하고 모든 모듈의 값이 커밋돼 있다. 빨간 land는 루프가 쓴 ProjectSettings(병합이 건드리지 않은 파일 포함)와
  그 기록도 되돌린다(`land.undo`의 `restored ...`).
  - 손으로(또는 Project Settings 창에서) 고친 내용이 섞인 파일은 산출물이 아니다 — 그 뒤 루프가 저장했어도 기록이 그 내용부터 다시 시작한다. 병합이 그 파일을 바꾸면
    전처럼 `land.foreign`(메시지에 그 파일): 그 편집을 커밋하거나 `git checkout -- <파일>` 뒤 `loop.ps1`(설정 스텝이 값을 다시 쓴다)로 산출물로 되돌린다.
  - 두 브랜치가 같은 ProjectSettings 파일을 각자 커밋해 `land.conflicts`가 나면(예: `-Own` 에디터의 루프가 쓴 파일을 커밋) worktree에서 `git merge master` 때 그 파일은
    master 쪽(`git checkout --theirs -- <파일>`)을 받는다 — 브랜치의 값은 land 뒤 루프가 다시 쓴다.

## 에디터 커맨드 (모두 JSON 반환)

호출: `tools/uc.ps1 <command> '<JSON 인자>'` (권장: 빠르고 PowerShell 인용 문제 없음, 락 적용)
또는 `unity command <command> --arg value --format json`.

| 커맨드 | 하는 일 |
|---|---|
| `harness_build` | Builders의 ISettingsStep(렌더·프로젝트 설정, harness 프로젝트만) → IBuildStep을 Order 순으로 빈 씬에 실행 → `buildScene` 저장 → 반사 프로브(`ctx.ReflectionProbe`) 렌더. `{ok, fingerprint, steps[], settings, cacheHits, bakes, bakesSkipped, reflectionProbes, cubemapsWritten, deletedAssets, phases}`. `dry_run`, `no_cache`. 빌드 스텝이 없고 `playScene`이 build가 아니면(기존 프로젝트) 플레이 씬을 열고 에셋 기준 fingerprint만(`skipped`) |
| `harness_capture` | `{"preset":"all"\|"<name>"\|"main","out":"HarnessOut/capture","scene":"","ui":true}` 편집 모드 오프스크린 PNG(프로젝트 캡처 크기, 화면의 카메라들 + 스크린 공간 UI 합성) + `meanLuma/stdLuma/blank/dark/magenta/cameras/ui`. 샷 = 씬의 ShotPreset + 설정 `shots`. 플레이 씬을 먼저 연다(`"scene":"open"`이면 열린 씬 그대로) |
| `harness_golden` | `{"shots":"[{\"path\",\"name\",\"ignore\":[{x,y,w,h}]}]","golden":"","key":"default","out":"","update":false}` 샷을 `<golden>/<Unity 버전>/<key>/<파일>`과 비교(샷마다 status·meanDiff·changedRatio·ssim·rect, 바뀌었으면 `<out>/golden/<샷>.diff.png`) 또는 그 폴더에 씀(`update`). 루프가 매번 부른다 |
| `harness_play` | `{"scenario":"tools/scenarios/default.json"\|"{...inline}","out":"HarnessOut/play"}` 즉시 반환 → `harness_play_status` 폴링 |
| `harness_play_status` | `entering\|running\|exiting\|done\|failed` + 끝나면 `result`(result.json) |
| `harness_console` | `{"since":<mark>,"until":<seq>}` 최신 컴파일 에러(file,line,msg,module) + mark 이후 런타임 에러/경고 수. `until` 뒤의 에러는 `teardownErrors`, 설정 `knownErrors`에 맞으면 `knownErrors`. 응답의 `mark`를 다음에 넘긴다 |
| `harness_stats` | 플레이 중이면 live, 아니면 마지막 결과: fps avg/min/p95ms, batches, SetPass, tris |
| `harness_lint` | static-reset / module-asmdef / module-boundary / contract-file / contract-name 규칙 검사(`ms`, `contractsMs`) |
| `harness_contracts` | `{"sources":"[{id, path \| text}]"}` C# 소스가 선언하는 최상위 타입(`name, ns, full, kind, line, hash`; Roslyn, 컴파일 없음). submit·land의 계약 검사가 부른다 |
| `harness_shaders` | Assets/ 셰이더의 현재 컴파일 에러(file, line, msg, module). 셰이더 에러는 로그가 아니라 상태라 매 루프 조회 |
| `harness_ping` | domainReloads, isCompiling, isPlaying, compileFailed, mark, unityVersion |
| `harness_setup` | `setup: harness`면 프로젝트 설정 멱등 적용(Domain Reload off, runInBackground, 동기 셰이더 컴파일(`syncShaders`), 템플릿 샘플 삭제, ISettingsStep 실행 → `settings`·`warnings`). `attach`면 아무것도 안 바꾸고 `recommendations`만(`{"apply":"domainReload,syncShaders"}`로 명시 적용). Debug 코드 최적화(세션 한정)는 둘 다 |
| `harness_sync_csproj` | .sln/.csproj 생성(사용자 외부 에디터 설정은 복원) — compile-check msbuild 백엔드용 |
| `harness_hot` | `{"mode":"apply"\|"check"\|"calls"\|"prepare"\|"commit"}` 핫 루프(위 "핫 루프"). apply: 마지막 컴파일 스냅샷과 비교해 `[CodeReload]` 본문(과 새 메서드)만 바뀌었으면 앞선 교체를 지우고 인터프리터로 다시 넣음 → `{hot, applied, changes}`, 아니면 `{hot:false, reason, changes}`. check: 판정만. calls: 교체된 메서드의 플레이 동안 호출 수·시간(`hot.interpreted`). prepare/commit: 전체 루프가 컴파일 전후에 부른다 |
| `harness_quit` | 응답 ~0.3s 뒤 `EditorApplication.Exit(0)`(저장 확인 없음). 직접 부르지 말고 `tools/quit.ps1`(락 + 종료 대기) |
| `harness_player_plan` | `{"scenario":...}` 플레이어 실행 계획(읽기 전용): 빌드할 씬(플레이 씬 먼저 + Build Settings), 타깃·출력 경로, 캡처 크기(= 창), 설정 파일. 데스크톱이 아닌 활성 타깃이면 거부. `tools/player.ps1`이 부른다 |
| `harness_player_built` | 플레이어 빌드 뒤 `AssetDatabase.SaveAssets` — 빌드가 메모리에 남긴 설정 변경을 지금 디스크로(에디터 종료 때 쓰일 것) |
| `harness_compare` | `{"pairs":"[{name,path,against,diff,ignore}]","out":"...","same_mean":0.5}` 이미지 쌍을 기준 이미지 규칙으로 비교(status·meanDiff·changedRatio·rect·diff PNG). `player.ps1`이 플레이어 샷·화면을 비교한다 |

Pipeline 패키지 기본 커맨드도 쓸 수 있다: `recompile`/`recompile_status`, `eval_file {"file":"AgentScripts/x.cs"}`(C# 본문, using 불가 → 정규화된 이름 사용),
`run_script`, `get_scene_hierarchy`, `editor_status`(모달 다이얼로그 확인), `editor_stop`, `set_autotick`. 목록: `unity command --detail compact`.

## 시나리오 (tools/scenarios/*.json)

```jsonc
{ "name": "default", "durationSec": 3.0, "fixedDeltaTime": 0.0166667, "warmupSec": 0.25, "readyTimeoutSec": 10,
  "events": [ { "t": 1.0, "type": "keyTap", "key": "Space", "hold": 0.1 } ],
  "captures": [ { "t": 0.5, "preset": "auto" }, { "t": 1.5, "preset": "auto" }, { "t": 2.5, "preset": "auto" } ] }
```
- `"scene"`(선택): 이 시나리오가 플레이할 씬. 비우면 설정의 `playScene`.
- `t`는 HarnessProbe.Ready 이후 **게임 시간**(초). `fixedDeltaTime`이면 프레임마다 고정 간격으로 흘러서 `t=1.5`의 화면·이벤트 수가 매번 같다.
  로딩(네트워크, Addressables)은 벽시계로 걸리므로 "로딩이 끝났을 즈음"을 고정 `t`로 잡지 말고 아래 대기 이벤트를 쓴다.
- 입력 이벤트: `keyDown/keyUp/keyTap`(키 이름) · `mouseMove/mousePos/mouseDown/mouseUp/scroll`(`x`,`y`, `key`=Left/Right/Middle) ·
  `click`(`target` 또는 `x`,`y`: 이동 → 다음 프레임 누름 → `hold` 뒤 뗌) · `stick`(`key`=left/right, `x`,`y`) · `padDown/padUp`(GamepadButton) · `releaseAll`.
  - 키 이름은 Input System 이름(`Space`, `Digit1`, `Enter`, `LeftCtrl`, `Numpad0`)과 KeyCode 이름(`Alpha1`, `Return`, `LeftControl`, `Keypad0`) 둘 다 된다.
  - `x`,`y`는 Game 뷰 픽셀(원점 왼쪽 아래). Game 뷰 크기는 사용자 레이아웃이라 매번 다를 수 있다 → 같은 곳을 눌러야 하면
    `"mouseSpace": "normalized"`(0..1)나 `click`의 `target`을 쓴다.
  - `target`: GameObject 이름·경로(uGUI 요소면 그 사각형 중심, 아니면 렌더러/콜라이더 중심을 메인 카메라로 투영) 또는 UI Toolkit 요소 이름
    (스크린 공간 패널, 6.2+ 월드 공간 패널은 메인 카메라로 투영). 찾은 좌표와 방법(`ugui`/`world`/`uitk`/`uitk-world`)은 `play.clicks`에 남는다.
- 대기 이벤트(시나리오 시계를 멈춘다 → 뒤의 이벤트·캡처가 그 순간 기준): `waitScene`(`scene` = 이름·경로, 로드될 때까지) ·
  `waitTarget`(`target`이 활성·표시될 때까지). `timeoutSec`(기본 30, 벽시계) 안에 안 되면 시나리오 실패. 걸린 시간은 `play.waits`,
  기다리는 동안의 프레임은 FPS 통계에서 빠진다. 부트 → 메뉴 → 레벨: `waitTarget "PlayButton"` → `click` → `waitScene "Level1"` → 캡처.
- 입력은 **이벤트가 쓰는 장치만** Input System 가상 디바이스(`ScriptedInput`)로 넣는다(입력 이벤트가 없으면 장치를 안 만든다 — 가상 게임패드가
  생기면 게임패드 안내로 바뀌는 게임이 있다). InputAction·`Keyboard.current` 그대로 동작.
  **구 Input Manager(`UnityEngine.Input`)는 코드로 누를 수 없다**(에디터에서 OS 입력을 직접 읽는다, 아래 "함정") → 게임이 `[AgentHarnessInput]`
  정적 메서드로 받는다: `Tools~/templates/HarnessInput.cs`(install `-InputShim`)는 같은 멤버 이름의 드롭인 `Input`이다(`Input.` → `HarnessInput.`).
- **시나리오 동안 게임은 시나리오 입력만 받는다**(G3-6). 러너가 시작하면(첫 프레임) 실제 Input System 장치(백엔드가 보고한 키보드·마우스·게임패드)를
  끄고(`RealInputIsolation`: `DisableDevice(keepSendingEvents)` + 이벤트를 handled로 표시, 켤 때 하드 리셋 → 눌린 키 없음·포인터 0,0), 훅에는
  `"begin"`을 보낸다(`HarnessInput`은 그때부터 `UnityEngine.Input`을 읽지 않고, 마우스는 시나리오가 옮기기 전까지 화면 중앙). 끝나면(실패·중단 포함)
  켜고 `"end"`. 다른 창에서 누른 키가 `<Keyboard>/space` 같은 바인딩으로 들어와 `play.events`가 달라지던 문제다. 막은 실제 키·버튼 누름은
  `play.isolatedDevices[].presses`(마우스 이동, 플레이 진입·포커스 때 장치가 보내는 상태(sync)는 막되 세지 않는다). 원래 꺼져 있던 장치(TouchSimulation이
  끈 마우스 등)는 건드리지 않는다. 에디터(플레이어)가 백그라운드라 Input System이 먼저 꺼 둔 장치 — 포커스 없이 플레이 모드에 들어가면 전부 — 는 가져가서 끄고 센다
  (`background: true`, G3-9). **UI Toolkit은 앱 포커스가 없으면 입력을 통째로 버리므로 시나리오 동안 그 판정을 끈다**(G3-14, `Runtime/PanelFocus.cs`; uGUI는
  `runInBackground`로 이미 받는다) → 루프 중에 사람이 다른 창을 써도 UI 클릭까지 결과가 같다. 구 Input Manager를 `Input.`으로 직접 읽는 코드는 막을 수 없다 —
  `HarnessInput`을 거쳐야 한다.
- 캡처는 그 프레임의 모든 `LateUpdate` 뒤에 찍는다(LateUpdate에서 카메라를 움직이거나 `Graphics.DrawMesh*`로 그리는 게임도 그대로 찍힌다).
- **UI Toolkit의 시간도 프레임을 따른다**(G3-10): USS transition·`schedule.Execute` 타이머·캐럿은 원래 실시간으로 돌아서 고정 시간 간격이어도 transition 도중의
  캡처가 매번 달랐다. `fixedDeltaTime`이 있는 시나리오 동안 런타임 패널은 러너가 매 프레임 `fixedDeltaTime`씩 미는 시계로 시간을 잰다(`Runtime/PanelClock.cs`,
  그 패널의 원래 시간에서 이어짐, `timeScale`과 무관 — `Time.unscaledTime`은 캡처 간격이 있어도 실시간이라 못 쓴다). `play.uiClock`: `mode` `frames`, 따른 패널 수,
  `scope`. 6.1+는 런타임 패널마다(`BaseVisualElementPanel.TimeSinceStartupFunc`), 패널별 시계가 없는 6.0은 모든 패널이 공유하는 시계(`Panel.TimeSinceStartup`)를
  바꿔서 `scope` = `every panel`(시나리오 동안 에디터 창의 UI도 그 시계를 따른다). 둘 다 없는 버전이면 `real` + `error`(실패 아님).
- 캡처 `preset`: 샷 이름(씬의 ShotPreset 또는 설정 `shots`) · `"auto"`(이름순 다음 샷, 없으면 메인 카메라) · `"main"`(메인 카메라 그대로) ·
  `"screen"`(Game 뷰 그대로, 해상도는 Game 뷰 크기 = 사용자 레이아웃, Game 뷰 탭이 보여야 함; 창 없는 에디터에서는 그 샷의 `error`).
  `"camera": "<이름>"`이면 그 카메라로, `"pos": [x,y,z]` + `"lookAt": [x,y,z]`(또는 `"rot"` 오일러) + `"fov"`면 그 자리에서 찍는다(설정은 메인 카메라).
- `"screen"` 말고는 오프스크린으로 **Game 뷰가 합치는 카메라들을** 한 RT에 렌더한다(G3-7, `Runtime/CaptureCameras.cs`, `shotStats[].cameras` = 그린 순서):
  - 템플릿이 메인 카메라면(`"camera"` 없음) 화면에 그리는 Base 카메라 전부를 depth 순으로(viewport·clear 그대로 — 미니맵·분할 화면), 각 카메라의 URP 카메라
    스택(Overlay)까지 그리고, 메인 카메라 자리에 캡처 카메라(캡처 포즈, 메인 카메라 설정·렌더러·스택)가 들어간다. 게임처럼 뒤에 그린 전체 화면 카메라는
    앞의 것을 덮는다(URP는 Base 카메라를 겹쳐 그리지 않는다 — 겹치려면 스택. 실제 Game 뷰로 확인했다).
  - 포즈가 메인 카메라와 다르면(샷 프리셋, `pos`) **메인 카메라를 그 포즈로 잠깐 옮긴다** → 거기 달린 것(무기와 그것을 그리는 Overlay 카메라)이 같은 화면
    위치로 따라온다. 다른 Base 카메라(미니맵)는 제자리. 같은 프레임에 원래 로컬 위치·회전으로 되돌린다(편집 모드에서 씬을 dirty로 만들지 않음).
  - `"camera": "<이름>"`이 메인 카메라가 아니면 그 카메라와 그 스택만 전체 화면으로 그린다. 메인 카메라가 텍스처에 그리는 게임(화면에 안 나옴)도 그 카메라만.
  - 다른 카메라는 캡처 동안 `targetTexture`를 캡처 RT로 바꿨다가 되돌린다(그 카메라의 캔버스가 캡처 크기로 배치된다). Built-in은 depth 순 `Camera.Render`.
  - 캡처 RT는 URP가 그 카메라를 화면에 그릴 때의 색 형식이다(HDR이면 B10G11R11/RGBA half) — 다 그린 뒤 8비트 sRGB로 옮겨 읽는다. URP는 대상 텍스처가 있는
    카메라의 중간 색 버퍼를 **대상의 형식으로** 만들어서, W8 전의 8비트 캡처는 톤 매핑 전에 HDR 이미션·블룸을 잘랐다(아래 "함정", `player.ps1`의 실제 화면 비교로 찾음).
    MSAA도 대상 텍스처의 샘플 수를 따르므로 캡처 RT는 그 카메라의 MSAA(URP 에셋, Built-in은 품질 설정)로 만들고 읽기 전에 해제(resolve)한다(W8 전엔 MSAA 게임의
    가장자리가 계단으로 찍혔다 — BagelGame 2x). TAA처럼 앞 프레임을 쓰는 효과는 캡처 카메라에 이력이 없어 여전히 다를 수 있다.
- 그 위에 **스크린 공간 UI를 캡처 크기로 다시 배치해 합성**한다(G3-1, `Runtime/CaptureUi.cs`) → HUD·메뉴·팝업이 Game 뷰 크기와
  상관없이 같은 모양으로 찍힌다. 샷의 UI는 `shotStats[].ui`(아래부터 그린 순서):
  - 캡처가 그린 Base 카메라의 Screen Space - Camera 캔버스 → 그 카메라가 씬과 함께 그린다(게임처럼 후처리 포함, 메인 카메라 것은 캡처 카메라가).
  - 스택 Overlay 카메라의 캔버스(UI 카메라) → 카메라들 위에 합성. URP 렌더 요청은 요청한 카메라의 UI만 준비해서 Overlay 카메라의 캔버스를 그리지 않는다
    (Game 뷰 프레임은 모든 카메라를 준비). 캡처가 그리지 않은 카메라(`"camera"` 캡처일 때 화면의 다른 카메라)의 캔버스도 여기.
  - 그 위에 Screen Space - Overlay 캔버스와 UI Toolkit 패널(sortingOrder 순, 같으면 UI Toolkit이 위). 레이어마다 투명 RT에 그려 프리멀티플라이드 알파로
    합성(프로젝트 색 공간, 선형이면 선형 공간).
  - 캔버스의 렌더 모드·카메라·plane distance, PanelSettings의 타깃 텍스처, 카메라 타깃을 잠깐 바꿨다가 같은 프레임에 되돌린다(레이아웃 포함). 그 사이 UI 코드의
    `OnRectTransformDimensionsChange`·`GeometryChangedEvent`가 캡처 크기와 Game 뷰 크기로 한 번씩 더 불린다. 오버레이 캔버스(scaleFactor ≠ 1)의 TextMesh Pro 글자는
    바꾼 뒤와 되돌린 뒤 다시 생성한다(`ForceMeshUpdate`, W16 G3-16 — TMP의 SDF 스케일이 렌더 모드마다 달라서, 안 하면 글자 가장자리가 1/scaleFactor배 날카롭게 찍혔다).
    정점을 매 프레임 직접 움직이는 TMP 연출은 그 프레임에 풀린다. `Screen.width`를 직접 읽어 배치한
    UI는 Game 뷰 기준 그대로다. 게임이 그걸로 이상해지면 캡처에 `"ui": false`(`harness_capture {"ui":false}`; 다른 카메라의 캔버스 레이어도 뺀다). 게임이 실제로 그리는 화면은
  `tools/player.ps1`: 캡처 크기의 플레이어 창을 그대로 찍어 이 캡처와 비교한다(`screen.vsShot`/`screen.vsEditor`, 위 "플레이어에서 돌리기").
  - 빠지는 것: 타깃 텍스처가 있는 패널(게임의 render-to-texture UI), 다른 디스플레이 → `"screen"`. 캡처가 비었는데(`blank`) 화면에 그리는
    카메라 중 그리지 않은 것이 있으면 `hint`가 알려 준다.
  - uGUI 레이어(스택 Overlay 카메라·오버레이 캔버스)를 그리는 숨은 UI 카메라는 렌더러의 renderer feature를 그 렌더 동안 끄고 그린다(W13) — 게임도 오버레이
    캔버스를 카메라 뒤에 feature 없이 그리고, 전체 화면 feature가 UI 레이어를 덮지 않는다. URP 17.3의 ScreenSpace 데칼 패스는 그런 카메라(중간 텍스처 없음)에서
    예외를 던졌다(아래 "함정").
  - UI Toolkit 패널은 내부 API(`RuntimePanel.Update`, `UIElementsRuntimeUtility.RepaintPanel/RenderPanel`, 리플렉션)로 즉시 그린다. 없는 Unity 버전이면
    `uiError`로 보고한다(selftest 1번이 버전마다 HUD를 확인, ROADMAP O-9).
- **연속 캡처(G3-3)**: `{"t": 1.0, "preset": "main", "name": "spin", "frames": 8, "every": 4}` → t부터 4프레임마다 8장을 **한 장의 PNG(시트)**로
  (왼쪽→오른쪽, 위→아래, 칸 위에 그 프레임의 t; 시트 폭 최대 1920). `shotStats[]`에 `frames`, `every`, `sheet`(열x행), `times`, `motion`(이웃 프레임의
  평균 밝기 차이 0..255 — 0이면 아무것도 안 움직였다). blank·dark·magenta는 한 프레임이라도 그러면 참, 밝기는 평균. 포즈는 첫 프레임에 정해지고
  (`"auto"`·샷 이름·`pos`), `"main"`·`"camera"`는 카메라를 따라간다. 시나리오는 시퀀스가 끝날 때까지 기다린다. `"screen"`과는 못 쓴다.
  GIF는 만들지 않는다(에이전트는 Read로 시트를 본다).
- 캡처 크기: 시나리오 `"width"`·`"height"` → 설정 `captureSize` → 프로젝트 방향(Player Settings 기본 방향이 세로, 또는 세로만 허용한 자동 회전이면
  720x1280) → 1280x720. Game 뷰 크기는 쓰지 않는다.
- 새 게임플레이를 넣으면 `default.json`의 입력/캡처와 기대 이벤트 수를 같이 갱신한다.

### 기준 이미지 (golden/, G3-4)

```powershell
powershell -ExecutionPolicy Bypass -File tools/loop.ps1 -UpdateGolden   # 녹색이고 샷이 맞을 때: 이 샷들을 기준 이미지로 (커밋한다)
powershell -ExecutionPolicy Bypass -File tools/loop.ps1                 # 이후 매 루프: 기준 이미지와 비교 → report.golden, shotStats[].golden
```
- 위치: `golden/<Unity 버전>/<시나리오 "name">/<샷 파일>.png`(설정 `goldenRoot`, worktree면 그 worktree의 것). `-UpdateGolden`은 그 폴더의 PNG를 이번 샷으로
  바꾼다(없어진 샷의 PNG는 지움). 루프가 빨가면 쓰지 않는다(`golden.error`). `-NoPlay`의 샷은 `<버전>/capture/`.
- Unity 버전마다 따로 둔다(URP 버전마다 렌더가 다르다, P-4). 그 버전 폴더가 없으면 같은 major.minor의 가장 가까운 패치 것과 비교한다(`golden.from`).
  샘플은 6000.3.11f1의 `default` 시나리오 3장을 커밋해 두었다(다른 버전은 `missing`).
- 판정(`Editor/HarnessGolden.cs`): 채널 차이가 24 넘는 픽셀이 0.01% 넘거나 평균 차이(`meanDiff`, 0..255)가 0.5 넘으면 `changed`. 같은 머신·같은 버전은
  **픽셀까지 같다**(고정 시간 간격: `maxDiff` 0 — 플레이 동안 에디터 창이 계속 다시 그려져도, selftest 1번). 예외는 URP의 **DBuffer 데칼**(데스크톱 Automatic의
  기본값): 마지막 카메라 렌더와 캡처 사이에 에디터 GUI가 그렸는지에 따라 데칼 가장자리 픽셀 몇 개가 달라진다(SMAA가 키움 — 샘플에서 6–7픽셀, 채널 차이 47,
  허용치 안이라 `same`; 아래 "함정"). 픽셀까지 같아야 하면 ScreenSpace 데칼을 쓴다(샘플). 허용치는 다른 GPU·드라이버용인데 아직 재지 않았다(ROADMAP P-3). 크기가 다르면 `size`.
  `changed`면 `<Out>/golden/<샷>.diff.png`: 샷을 어둡게, 바뀐 픽셀 빨강(진할수록 많이), 뺀 영역 파랑, 바뀐 범위 노란 테두리(`rect` = `[x, y, w, h]`, 왼쪽 위 기준).
- **실패로 치지 않는다** — 루프는 의도한 변경 중에도 녹색이다. 의도하지 않은 `changed`(다른 모듈 작업, 렌더 설정 이전 W4)를 잡는 용도.
- 매번 다른 글자(시계·네트워크 값)가 있는 샷: 캡처에 `"ignore": [{"x": 0.8, "y": 0, "w": 0.2, "h": 0.1}]`(이미지 비율, 왼쪽 위 기준)로 그 영역을 빼거나
  `"golden": false`, 또는 `"ui": false` 샷을 따로 둔다. `"screen"` 샷(Game 뷰 크기)은 비교하지 않는다. 연속 캡처는 시트 이미지를 비교한다.
- 비용: 샷 3장 비교 ~0.2 s(`timings.goldenSec`). 샷 PNG 한 장 ~1 MB라 기준 이미지를 자주 갈면 저장소가 커진다 — 의도한 화면 변경일 때만 갱신한다.

## 핫 루프 (loop.ps1 -Hot, G2-1 · G2-5)

```powershell
powershell -ExecutionPolicy Bypass -File tools/loop.ps1 -Hot           # 본문(+ 새 메서드)만 고쳤으면 ~3.4s, 아니면 알아서 전체 루프
powershell -ExecutionPolicy Bypass -File tools/uc.ps1 harness_hot '{"mode":"check"}'   # 무엇이 바뀌었고 핫으로 되는지만
```
- **핫으로 되는 것**: 마지막 전체 루프가 컴파일한 뒤 바뀐 것이 `[CodeReload]` 메서드의 **본문**과, 그 본문이 부르는 **새 메서드**(W12)뿐일 때. 그 파일들을 Pipeline의
  인터프리터 백엔드(`reload_file_editor_interpreter`)로 다시 넣고(컴파일·도메인 리로드 없음), lint·빌드·셰이더 검사를 건너뛰고(`build.skipped`), 같은 시나리오를
  처음부터 돈다 → `play.events`·기준 이미지 비교가 전체 루프와 그대로 비교된다(같은 코드면 픽셀까지 같음을 확인). `report.hot.reloaded`에 파일과 메서드(`methods`),
  함께 들어간 새 메서드(`newMethods`).
- **새 메서드**: 컴파일된 타입에 없는 이름의 메서드를 그 파일의 `[CodeReload]` 클래스에 더하고 바꾼 본문에서 부르면 핫이다(인스턴스·static, 표현식 본문, private 필드
  읽기, 새 메서드끼리의 호출 모두 됨 — Pipeline이 교체 본문과 함께 컴파일한다). 안 되는 것(전체 루프, 사유에 그 줄): 제네릭 새 메서드, 컴파일된 이름의 오버로드,
  파일의 첫 `[CodeReload]` 클래스가 아닌 곳(다른·중첩 클래스)의 새 메서드. 새 메서드는 **다음 전체 루프까지 교체 본문에서만** 불린다 — 표식 없는 기존 메서드에서
  부르려면 그 메서드의 본문이 바뀌므로 전체 루프다. 새 메서드에 `[CodeReload]`를 달아도 다음 컴파일 전까지는 진입점이 아니다(본문에서 부르는 것은 됨).
- **전체 루프로 돌아가는 것**(`hot.applied=false`, `hot.fallback`에 이유, `hot.changes`에 파일별 `kind`): 필드·속성·시그니처·using·표식 없는 기존 메서드의 본문·속성 표식
  추가(`context`, 첫 차이의 `line`과 그것이 무엇인지 — `field m_X`, `method Foo (no [CodeReload]: its compiled body runs)`, `method Foo(int n) (an overload or a new
  signature of a compiled method)`, `type X`), 새·지운 파일(`added`/`deleted`), `[CodeReload]`가 없던 .cs나 셰이더·UXML·에셋·설정(`changed`), 문법 오류, 인터프리터가 못
  돌리는 본문(`try/catch`, `lock`, 제네릭 메서드 선언, `T?` 값 타입 nullable 등 — Pipeline 문서 "Interpreter constraints"), Domain Reload가 켜진 프로젝트, 에디터를 다시
  열었거나 루프 밖에서 컴파일된 뒤(스냅샷이 오래됨).
- **바꾼 본문이 플레이 중 예외를 던지면** 전체 루프를 다시 돈다(`hot.fallback`: "a reloaded method failed ..."): Pipeline은 인터프리터의 예외를 줄 없이
  로그하고(메서드 선언 줄로 보고됨) 원래 본문을 한 번 더 돌린다(이벤트가 두 번 나감). 컴파일된 전체 루프가 정확한 줄로 보고한다.
- **인터프리터 비용(`hot.interpreted`, W12)**: 교체된 메서드는 인터프리터로 돌아 컴파일된 것보다 느리다 → 플레이 동안의 호출 수·호출된 프레임 수·그 안의 시간
  (`methods[]`: `method`, `calls`, `frames`, `ms`, `msPerCall`; 안에서 부른 새 메서드 포함), `msPerFrame`(메서드마다 ms/프레임의 합), `frameShare`(그 루프 fps의 한 프레임에
  대한 비율). 핫 루프의 `fps`가 전체 루프보다 낮으면 이것으로 인터프리터 몫을 뺀다. 샘플 Tick: 0.06 ms/프레임(프레임의 ~0.8%, 도메인 리로드 뒤 첫 플레이는 ~0.14 ms) —
  fps는 전체 루프와 구분되지 않는다. 무거운 본문(큰 루프)이면 이 값이 커진다.
- 기준: 전체 루프마다 컴파일 직전에 `Assets/`(생성물·빌드 씬 제외)·`ProjectSettings/`·`Packages/`의 크기·시각과 `CodeReload`가 든 .cs의 텍스트를
  찍고(`harness_hot` prepare, 이때 이전 핫 루프의 교체도 지운다), 컴파일이 성공하면 확정한다(commit) → `Library/Harness/hot/compiled.json`.
  비교는 Roslyn 토큰 단위(공백·주석·비활성 `#if` 무시)라 서식만 바꾼 파일은 바뀐 게 아니다. `timings.snapshotSec` ~0.01s(샘플 170개 파일).
- 핫 루프마다 앞선 교체를 모두 지우고 지금 바뀐 파일만 다시 넣는다 → 컴파일된 텍스트로 되돌린 메서드는 다시 컴파일된 코드로 돈다(`hot.overridesCleared`).
- 속도: 판정 ~10–50 ms + 교체 ~0.13 s(새 메서드가 있으면 ~0.16 s; 도메인 리로드 뒤 첫 번째는 Roslyn 적재로 ~0.9 s — `[CodeReload]`가 있는 프로젝트는 리로드 직후
  워커 스레드에서 Roslyn을 한 번 돌려 1.5 s에서 줄였다).
- 조건: Domain Reload 끔(하네스 프로젝트 기본), 메서드가 public이고 void·`IEnumerator`, 어셈블리가 `Unity.Pipeline`·`Unity.Pipeline.Attributes` 참조
  (위 모듈 템플릿; `Assembly-CSharp`는 자동). 샘플의 `SmokeModule.Tick`·`StageModule.Tick`이 표식돼 있다.
- `reload_file`(Assembly.Load 백엔드)은 쓰지 않는다: 교체한 본문이 public 멤버만 쓸 수 있는데 모듈은 상태를 private 필드에 둔다(인터프리터 비용이 위처럼 작아 바꿀 이유도 작다).
- submit/land는 항상 전체 루프다(핫 교체는 에디터 트리의 메모리에만 있어 트랜잭션으로 되돌릴 수 없다).

## 플레이어에서 돌리기 (tools/player.ps1, W8: G3-2 · G3-8)

```powershell
powershell -ExecutionPolicy Bypass -File tools/player.ps1                                   # default.json을 에디터와 개발 빌드 플레이어에서
powershell -ExecutionPolicy Bypass -File tools/player.ps1 -Scenario tools/scenarios/x.json
powershell -ExecutionPolicy Bypass -File tools/player.ps1 -NoBuild                          # 시나리오만 바꿨을 때: 마지막 빌드를 그대로
```
`player.ps1` = 에디터 루프(컴파일 → 빌드 → 에디터 플레이, `<Out>/editor/report.json`) → **개발 빌드 플레이어**(`HarnessOut/player-build/<타깃>/`,
Pipeline `build` 명령, 증분) → 플레이어를 **캡처 크기의 창**(`-screen-width/-height`, 창 모드)으로 띄우면 플레이어가 혼자 시나리오를 돌고 샷·
result.json을 쓰고 종료 → 에디터 샷과 비교 → `HarnessOut/player/report.json`(stdout에도). 종료코드 0 = 플레이어가 빌드되고 시나리오를 에러 없이 돌았다.
에디터와의 차이(프레임 시간·이벤트·픽셀)는 보고이지 실패가 아니다. 락을 끝까지 잡는다(플레이어가 프레임을 재는 동안 이 에디터에서 다른 루프가 돌지 않음).

- **플레이어는 에디터 없이 혼자 돈다**: 런타임(`Runtime/PlayerRun.cs`)이 명령줄 `-harness-scenario <파일> -harness-out <폴더>`를 보면 에디터의
  플레이 모드와 같은 `ScenarioRunner`를 띄우고(입력 재생·대기·캡처·이벤트·실제 입력 격리 그대로), 끝나면 `Application.Quit`. Pipeline 런타임 서버는 쓰지 않는다
  (`enableInBuilds` 설정 파일과 HTTP 서버를 플레이어에 넣어야 하고, 프레임 시간에도 섞인다). 설정(`shots`·`captureSize`)은 `-harness-config`로 받은
  프로젝트의 `AgentHarness.json`, `[AgentHarnessInput]` 훅은 리플렉션으로 찾는다.
- **에디터와 같은 프레임을 돈다**: 에디터 플레이 모드는 게임을 한 번 돌린 뒤 러너를 띄우고 처음 두 프레임이 `Time.fixedDeltaTime`(0.02 s)이다 — 플레이어도
  똑같이 맞춘다(첫 프레임 끝에 러너, 둘째 프레임 0.02 s, 러너의 첫 Update부터 시나리오의 고정 간격). 그래서 `play.events`가 같고(`eventsMatch`), 같은 `t`의 샷이
  같은 게임 상태다. **예외: 첫 씬의 파티클은 플레이어에서 한 스텝(0.02 s) 앞선다** — Unity가 플레이어의 첫 씬을 불러오며 파티클을 한 번 진행해 둔다
  (스크립트가 바꿀 수 없는 로드 시점 dt). 샘플은 파티클 둘레만 달라 에디터 비교가 `changed`(바뀐 픽셀 0.02–0.65%)다(`compare.note`).
- **프레임을 묶지 않는다**: vSync 0, 프레임 상한 없음(`player.vSyncCount`, `targetFrameRate`) → `fps`·`p95ms`가 게임의 일이다. 프로젝트 설정 그대로 재려면 `-Paced`.
  게임이 첫 씬 뒤에 직접 상한을 다시 걸면(`Application.targetFrameRate = 60` — 사내 프로젝트 A) 그 값이 `player.targetFrameRate`에 남고 `fps.note`가 "fps는 그 상한"이라고 알린다.
  `fpsVsEditor` = 플레이어 fps / 에디터 fps. 샘플: 플레이어 ~450–515 fps(p95 ~3 ms) vs 창 에디터 ~105–120 fps(p95 ~11 ms), **×3.8–4.9**(BagelGame ×5.4). 개발 빌드라 프로파일러 마커가
  켜져 있다(출시 빌드보다 조금 느리다). `render`(batches·SetPass·tris)도 플레이어 값.
- **실제 화면(G3-8)**: 게임 자신의 카메라로 찍는 캡처(`"main"`, 샷 프리셋이 없어 메인 카메라로 떨어진 `"auto"` — 포즈 샷은 게임 카메라가 거기 없으니 제외)는
  그 프레임에 화면(back buffer)도 찍는다(`shotStats[].screen.path`, `<샷>.screen.png`). 창 = 캡처 크기라 UI를 다시 배치하지 않고 `Screen.width`가 캡처 크기이며
  카메라 스택 그대로다. `screen.vsShot` = 같은 프레임의 하네스 캡처와(캡처 경로가 실제 화면과 같은가), `screen.vsEditor` = 에디터 루프의 같은 캡처와.
  시나리오의 `"screen"` 캡처는 플레이어에서 창 그대로다. 개발 빌드가 오른쪽 아래에 그리는 "Development Build"는 화면 비교에서 뺀다(`compare.screenIgnore`).
- **비교**: `shotStats[].vsEditor`(플레이어 샷 vs 에디터 샷, 파일 이름이 같은 것), `screen.*` — 각각 `status`·`meanDiff`·`changedRatio`·`rect`·`diff` PNG
  (`<Out>/compare/`). 규칙은 기준 이미지와 같고 평균만 1까지 같다고 본다(카메라·프로세스가 다르면 URP 디더링이 달라 평균 ~0.5). `compare.vsEditor/screen`에
  `same`/`changed`와 가장 큰 `changedRatio`·`meanDiff`.
- **에디터와 다른 이유(`vsEditor.cause`, W16)**: 실제 화면이 있는 샷은 그 프레임의 화면으로 원인을 가른다 — `game`: 플레이어 화면 = 캡처(`screen.vsShot` same)라
  차이는 게임이 에디터와 플레이어에서 다르게 그린 것(`#if UNITY_EDITOR`·`Application.isEditor` 안의 UI, 플랫폼 `#if`, `OnValidate`가 만든 데이터, `Screen.width`로 배치한
  UI — 에디터의 Screen은 Game 뷰, 실시간(unscaled) 연출 — 프레임당 벽시계가 다르다, 첫 씬 파티클), `capture`: 캡처가 그 프레임의 화면과 다르다(하네스의 캡처 경로 — `.screen.png`가
  게임이 보인 것). `compare.note`가 샷 이름과 함께 설명한다(화면 쌍이 없는 포즈·카메라 샷은 `cause` 없이 "가를 화면 없음 + 첫 씬 파티클" 안내). 사내 프로젝트 A:
  강제 로그인 대화상자의 서버 선택 줄(`#if UNITY_EDITOR`) 14%, 로비의 실시간 등장 트윈 7.7% — `game`. 샘플은 `"main"` 샷이 파티클 차이로 `game`.
- **report.json**: `ok`, `stage`(editor|compile|build|shader|lint = 에디터 루프가 먼저 실패 · playerBuild · player(안 끝남·크래시, `logTail`) · play · runtime · shots),
  `editor`(ok·stage·fps·render·events·report 경로), `player`(`exe`, `target`, `build`{result, sec, sizeMB, code, cleanRebuild, errors[]{file, line, msg, module}}, `buildRewrote`,
  `development`, `graphicsDevice`, `screen`, `startupSec`, `exitCode`, `log`), `fps`(+`note`), `render`, `fpsVsEditor`, `play`(에디터와 같은 모양), `eventsMatch`,
  `runtimeErrors`(`knownErrors` 따로), `compileErrors`(플레이어 빌드만 실패한 컴파일 에러, kind `player`), `shots`, `shotStats`(`vsEditor.cause`), `compare`(+`note`),
  `timings`{lockWaitSec, editorSec, buildSec, playerSec, compareSec}, 빌드 전에 거부됐으면 `urpStale`.
- **플레이어 빌드만 컴파일되지 않으면**(에디터 루프는 녹색, W16): `stage=playerBuild` + `compileErrors[]`(file·line·module, kind `player`)와 `error`의 원인 —
  플레이어 타깃에 없는 API를 런타임 검사로만 막은 코드(`#if UNITY_EDITOR` 없는 `UnityEditor`, `#if UNITY_ANDROID || UNITY_IOS` 없는 `Handheld` — 사내 프로젝트 A의
  `Handheld.Vibrate()`가 `Application.isMobilePlatform` 안에만 있었다). Pipeline `build_status`의 `errors[]`는 줄을 버려서(ROADMAP O-14) 빌드 단계 메시지(원문)를 읽는다.
- **런타임 에러의 줄**: 개발 빌드 플레이어는 최적화 코드라 `runtimeErrors`의 줄이 몇 줄 어긋날 수 있다(파일·모듈·메서드는 맞다; 샘플 주입 71행 → 78행). 에디터
  루프(`editor/report.json`)가 정확한 줄이고, 플레이어에서만 나는 에러면 `-Debugging`(Script Debugging 빌드: 정확한 줄, 코드가 느림 — 샘플 ~350 fps).
- **빌드**: 첫 빌드는 셰이더를 컴파일해서 길고(샘플 ~2분), 그 뒤는 증분(스크립트만 바뀌면 ~10 s, 아무것도 안 바뀌면 ~3–5 s). 한 바퀴 ~16 s(에디터 루프 ~4.5 s +
  증분 빌드 ~5 s + 플레이어 ~6 s), `-NoBuild` ~11 s. 출력은 `HarnessOut/player-build/`
  (Unity는 `Library/` 안으로 빌드를 거부한다; HarnessOut은 git이 무시). 플레이할 씬이 첫 씬, 이어서 Build Settings의 나머지 활성 씬. 데스크톱(Standalone) 활성
  타깃만 — 안드로이드 같은 타깃이면 전환(프로젝트 전체 재임포트)하지 않고 `stage=playerBuild`로 알린다. 지금은 Windows만 검증(P-3).
  URP 에셋이 이 에디터의 URP보다 새 버전이면(더 새 Unity가 저장, URP는 내려 쓰지 않음) URP가 빌드를 거부하므로 빌드 전에 `urpStale`로 알린다 —
  6.3 샘플을 6.0으로 연 새 클론이 그 경우다(전역 설정 에셋 버전 10, URP 17.0의 마지막 8; selftest는 그때만 플레이어 단계를 건너뛴다).
- **빌드가 프로젝트 설정을 다시 쓴다**(하네스와 무관하게 Unity·URP·Input System이): 빌드 뒤 `AssetDatabase.SaveAssets`(`harness_player_built`)로 메모리의 상태를
  바로 쓰고, 작업 트리에서 바뀐 파일을 `player.buildRewrote`로 알린다. 샘플은 첫 빌드가 쓰는 세 파일(`DefaultVolumeProfile`, URP 전역 설정, `ProjectSettings.asset`의
  `m_BuildTargetBatching`)을 빌드 뒤 상태로 커밋해 두어 비어 있다. 기존 프로젝트면 되돌리거나 커밋한다.
- 빌드 뒤 플레이어의 `<Data>/ScriptingAssemblies.json`에 `Harness.Runtime`이 없으면 `CleanBuildCache`로 한 번 다시 빌드한다(`player.build.cleanRebuild`): Unity의
  증분 빌드가 앞선 출시 빌드(하네스 없음)의 플레이어 데이터를 그대로 써서, DLL은 있는데 로드 목록에 없어 플레이어가 시나리오를 돌지 않았다(Fluid-Sim, 6.0).
- 플레이어 로그(`<Out>/Player.log`)에 `[Harness] player run …`, `scenario runner started`, `scenario clock started`, `scenario finished`가 남는다 — 플레이어가
  끝나지 않으면(`stage=player`, `logTail`) 어디까지 왔는지 본다. 진행은 stderr에 `player.ps1: <단계> (s)`.
- 개발 빌드는 프로파일러 연결(PlayerConnection)을 네트워크에서 기다려서, **새 exe 경로마다 Windows 방화벽이 한 번 허용을 묻는다**. 허용하든 취소하든 실행에는
  상관없다(창이 떠 있어도 플레이어는 돈다). 빌드 경로가 프로젝트마다 고정이라 프로젝트당 한 번이다.
- 플레이어 창이 몇 초 동안 포커스를 가져간다. 기존 프로젝트의 플레이어는 에디터와 다른 PlayerPrefs(`HKCU\Software\<회사>\<제품>`)를 쓴다. `persistentDataPath`
  (`LocalLow\<회사>\<제품>`)는 에디터·플레이어·같은 회사·제품 이름의 원본 프로젝트가 함께 쓴다. 플레이어의 작업 폴더는 `<Out>/player`다(게임이 상대 경로로 쓰는 파일 —
  사내 프로젝트 A의 Facebook SDK `fbg.log` — 이 프로젝트 루트에 떨어지지 않게).
- **모바일 타깃 프로젝트**(W16, 사내 프로젝트 A): 활성 타깃이 Android면 `player.ps1`은 거부한다. 클론에서 `Unity -batchmode -quit -projectPath <클론> -buildTarget Win64`로
  바꿔(그 프로젝트 101 s, 추적 파일 변경 없음; 되돌릴 때 `-buildTarget Android`) 돌린다. 모바일 전용 코드는 Windows에서 컴파일되지 않을 수 있고(위), 스크립팅 define이
  타깃마다 달라 서버·기능이 바뀔 수 있다(그 프로젝트는 Standalone에 `DEV`가 없어 라이브 서버 환경이었다) — 플레이어가 어디에 접속하는지 먼저 본다. 에디터 루프의
  fingerprint도 활성 타깃마다 다르다(플랫폼별 임포트).
- **찾아 주는 것의 예**: Fluid-Sim은 입자 색 그라디언트 텍스처를 에디터 전용 `OnValidate`에서만 만들어서 빌드한 플레이어의 입자가 회색이었다 — 에디터 루프는
  녹색인데 `player.ps1`은 `vsEditor` 14% 변경 + 2색이라 `blank`(`stage=shots`), 실제 화면도 캡처와 같게 회색.
- 출시 빌드와는 별개다: 하네스 런타임은 개발 빌드(`DEVELOPMENT_BUILD`)라 들어가고, 출시 빌드에는 여전히 없다(매트릭스 10).

## 병렬 에이전트: worktree + submit + land (남의 컴파일 에러에 막히지 않기)

에디터는 이 폴더(에디터 트리) 하나에 묶여 있다. 여러 에이전트가 여기서 직접 코드를 고치면 한 명의 쓰다 만 코드가
Unity 도메인 리로드를 막아 **모두의 루프가 `stage=compile`로 멈춘다.** 그래서 병렬 작업은 에이전트별 worktree에서 한다:

```powershell
# 1회: 에디터가 연 체크아웃에서 에이전트별 worktree를 만든다 (Library/가 없으니 에디터도 임포트도 필요 없다)
git worktree add ..\wt-foo -b agent/foo
cd ..\wt-foo\AgentHarness                                                        # 이후 편집·명령은 모두 여기서
powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Foo    # 에디터 없이 ~0.5s, 동시 실행 OK
powershell -ExecutionPolicy Bypass -File tools/submit.ps1 -Module Foo          # 에디터 트리에서 루프(트랜잭션)
git add -A; git commit -m "Foo: ..."                                             # submit이 되복사한 .meta·ProjectSettings까지
powershell -ExecutionPolicy Bypass -File tools/land.ps1                         # 이 브랜치를 에디터 트리 브랜치에 병합(트랜잭션)
```

`submit.ps1` = ① worktree 소스로 compile-check(락 없음, `-Dependents`: 바뀐 계약을 쓰는 다른 모듈도; 실패면 아무것도 복사하지 않고 `stage=compile`,
`submit.phase=check`) → ② 에디터 락 → 계약 검사(위 "계약 폴더": 올라간 타입 변경·이름 중복·남의 미병합 계약 파일이면 `stage=submit`으로 거부) → 덮어쓰거나
지울 파일을 백업하고 저널을 남긴 뒤 `Assets/Game/<Module>/`를 에디터 트리로 미러링, `Assets/Game/Contracts/`는 이 worktree가 바꾼 파일만
→ ③ 에디터 트리에서 평소 루프 → ④ 녹색이면 유지하고 Unity가 만든 `.meta`와 설정 스텝이 바꾼 ProjectSettings 파일을
worktree로 되복사(**커밋할 것**; 위 "프로젝트 설정"), 아니면 **백업으로 되돌리고(ProjectSettings 포함) 재컴파일**해 에디터 트리를 submit 전 상태로 돌려놓는다.
결과는 loop와 같은 report.json + `submit` 필드(worktree의 `HarnessOut/submit/`). 종료코드 0 = 녹색이고 반영됨.

- 컴파일 실패는 항상 되돌린다. 런타임/린트/셰이더/샷 실패도 기본은 되돌린다. `-KeepOnFail`은 유지 — `submit.errorModules`가
  남의 모듈일 때(에디터 트리가 이미 빨간 상태)만 쓴다.
- submit이 도중에 죽어도(타임아웃·kill) 다음에 락을 잡는 loop/uc/submit이 저널(`Library/Harness/submit/pending.json`)로
  되돌린다 → 그 report에 `recoveredSubmit`.
- worktree에서 `loop.ps1`은 거부된다(`stage=submit`): 에디터가 컴파일하는 건 worktree가 아니라 에디터 트리다. 그 worktree에 에디터를
  따로 띄웠으면(`open.ps1 -Own`, 아래) 거기서 돈다.
  `uc.ps1`은 worktree에서도 에디터 트리의 에디터에 붙는다(JSON 안의 상대 경로는 에디터 트리 기준).
- 시나리오는 worktree의 파일을 절대 경로로 넘기므로 worktree에서 고친 `tools/scenarios/*.json`이 그대로 쓰인다.
- **한 모듈 = 한 에이전트**(강제): submit은 모듈별로 마지막에 반영한 worktree를 `Library/Harness/submit/owners.json`에 기록한다.
  다른 살아 있는 worktree가 올린 미병합 변경이 에디터 트리에 남아 있는 모듈은 `stage=submit`으로 거부된다(`submit.owner`).
  그 작업이 버려졌을 때만 `-Takeover`. land가 그 브랜치의 모듈 소유를 해제한다. 계약 파일도 같다(`Library/Harness/submit/contracts.json`, `submit.contractOwner`).
- 에디터 트리 브랜치에 그 모듈을 건드린 커밋이 있는데 worktree에 없으면 거부된다(미러링하면 병합된 작업을 되돌리게 된다) → `git merge master`.
- 모듈 삭제·하네스 패키지·`tools/`는 submit 대상이 아니다(하네스 작업은 에디터 트리에서 직접). land로는 병합된다.
- 모듈은 `ProjectSettings/AgentHarness.json`에서 온다: `moduleRoots`의 하위 폴더(`Assets/Game/<Module>`)와 `modules[]`의 폴더(기존 코드).
  submit/land/compile-check/에러의 `module`이 모두 이것을 쓴다.
- 에디터 트리 찾기: `Library/`가 없는 체크아웃이면 `git worktree list`의 메인 worktree에서 같은 하위 경로.
  git worktree가 아닌 복사본이면 `$env:AGENTHARNESS_EDITOR_ROOT`에 에디터 트리 경로를 준다.

### worktree 전용 에디터 (open.ps1 -Own, W7: G5-1·G1-2)

에디터가 하나면 루프가 줄을 선다(에이전트 N명이면 대기가 선형으로 는다). Unity는 한 프로젝트 폴더를 에디터 하나만 열 수 있으므로, 루프를 나란히
돌리려면 에이전트 worktree가 프로젝트 사본이 되어 자기 에디터를 가진다:

```powershell
cd ..\wt-foo\AgentHarness
powershell -ExecutionPolicy Bypass -File tools/open.ps1 -Own      # 에디터 트리 Library/의 사본 + 창 없는 에디터 (~30 s)
powershell -ExecutionPolicy Bypass -File tools/loop.ps1           # 이 worktree의 전체 상태로, 에디터 트리의 루프와 동시에 (~2.4 s)
powershell -ExecutionPolicy Bypass -File tools/submit.ps1 -Module Foo   # 여전히 에디터 트리로(트랜잭션). land.ps1도 같다
powershell -ExecutionPolicy Bypass -File tools/quit.ps1           # 이 worktree의 에디터를 닫는다 (worktree를 지우기 전에)
```
- `Library/`가 없는 worktree면 에디터 트리의 `Library/`를 복사해 둔다(에디터 트리의 락을 잡고 그 에디터가 idle일 때; 샘플 1.9 GB·2.7만 파일 ~11 s).
  복사하지 않는 것: Pipeline 디스크립터(복사하면 이 worktree의 도구가 에디터 트리의 에디터에 붙는다), `Library/Harness`(submit/land 저널·소유 기록),
  락·pid 파일. 임시 폴더에 복사한 뒤 이름을 바꾸므로 반쯤 된 `Library/`는 생기지 않는다.
- 그 뒤로 이 worktree는 `Library/`가 있는 프로젝트라 `loop.ps1`·`uc.ps1`·`quit.ps1`·`compile-check.ps1`이 자기 에디터를 쓰고(락도 따로),
  `submit.ps1`·`land.ps1`만 에디터 트리로 간다(메인 worktree). report의 `editor.own`이 참이다.
- 처음 열 때 스크립트를 전부 다시 컴파일한다(경로가 바뀌어서 ~17 s; 에셋은 다시 임포트하지 않는다), 첫 루프는 빌드 캐시 없이 전체 빌드(~10 s).
- 사본 `Library/`는 에디터 트리의 Unity 버전 것이라 그 버전으로 연다: 에디터 트리를 `-UnityVersion`으로 열어 `ProjectVersion.txt`가 커밋된 것과 다르면
  worktree의 `ProjectVersion.txt`도 그 버전으로 바꾼다(다른 버전으로 열면 Unity가 사본을 업그레이드하고 fingerprint가 달라졌다 — 6.0 새 클론에서 겪음).
- 비용: 에디터 하나당 메모리 ~2 GB(+ 임포트 워커), `Library/` 크기만큼 디스크. 같은 머신의 에디터 여럿이 한 라이선스(같은 사용자 로그인)로 떴다.
  창 없는 에디터는 유휴일 때 CPU를 거의 쓰지 않는다(`HarnessHeadless`: 1코어의 ~8%).
- 창이 필요하면 `-Own -Window`. worktree를 지우기 전에 `quit.ps1`(에디터가 파일을 잡고 있으면 `git worktree remove`가 실패한다).

### land.ps1 (병합)

submit한 파일은 에디터 트리에 미커밋 사본으로 남아 그냥 `git merge agent/foo`는 "would be overwritten"으로 거부된다. `land.ps1`이 락 안에서 처리한다.
worktree에서 인자 없이 돌리면 그 worktree의 브랜치, 에디터 트리에서는 `-Branch agent/foo`. 결과는 `HarnessOut/land/report.json`. 종료코드 0 = 병합되고 녹색.

1. (락 없음) 브랜치를 체크아웃한 worktree에 미커밋 파일이 있으면 거부(`land.uncommitted`) — 커밋된 것만 병합된다. **submit이 되복사한 `.meta`·ProjectSettings도 커밋할 것.**
2. (락) 아무것도 건드리기 전에 거부(`stage=land`): 에디터 트리가 detached/병합·리베이스 중/staged 변경 있음 ·
   `git merge-tree`로 미리 병합해 충돌(`land.conflicts` → worktree에서 `git merge master`, 해결, 커밋, submit, 다시 land) ·
   브랜치가 `Assets/`에 추가하는 파일·폴더의 `.meta`가 커밋 안 됨(`land.missingMeta`) · 건드리는 모듈에 다른 worktree의 미병합 submit(`land.owner`, `-Takeover`) ·
   계약: 바꾸는 계약 파일에 다른 worktree의 미병합 submit(`land.contractOwner`), 올라간 타입 변경(`land.contractChanged`), 에디터 트리의 계약(미병합 포함)과 이름 중복
   (`land.contractConflicts`) · 덮어쓸 미커밋 변경이 이 브랜치의 submit 사본(또는 같은 내용)이 아님(`land.foreign`, 예: 에디터 트리를 직접 고친 것 — 사람이 커밋·stash).
   커밋된 파일 + 설정 스텝이 쓴 것인 ProjectSettings 파일은 덮어써도 된다(루프가 다시 씀, 위 "프로젝트 설정"의 G5-6).
3. 저널(`Library/Harness/land/pending.json`) + ProjectSettings 파일 사본(harness 프로젝트) → 병합이 건드리는 경로와 그 모듈의 미커밋 사본만 `git stash`
   (stash 목록은 모든 worktree가 공유하므로 바로 `refs/agentharness/land/<runId>`로 옮긴다) → `git merge`.
4. 에디터 트리에서 평소 루프. 녹색이면 병합 유지 + stash 버림 + 소유 해제(`land.releasedOwners`, `land.releasedContracts`) + 다른 미병합 submit이 남지 않았으면 설정 스텝이
   쓴 ProjectSettings를 커밋(`land.settings.commit`, 위 "프로젝트 설정"). 빨가면 `git reset --keep`으로 병합 전 커밋
   (병합한 경로만; 다른 에이전트의 미커밋 submit은 그대로) → stash 복원 → ProjectSettings 복원 → 재컴파일(`land.undo`, `land.restore`). `-KeepOnFail`은 submit과 같다.

- land가 도중에 죽어도 다음에 락을 잡는 loop/uc/submit/land가 저널로 되돌린다 → report에 `recoveredLand`.
- 이미 병합된 브랜치는 아무것도 안 하고 녹색(`land.note`). submit했지만 브랜치에 없는 변경이 남은 모듈은 소유를 유지하고 `land.warning`.
- git 병합이라 브랜치의 중간 커밋도 이력에 들어간다. 루프가 검증하는 건 병합 결과다.

## 에디터 없이 컴파일 체크

```powershell
powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Smoke                   # csc(기본) ~0.1-0.3s/어셈블리
powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Smoke -Backend msbuild  # 웜 ~0.5-2s, 콜드 수십 초
```
- 디스크의 소스를 다시 glob 하므로 방금 만든 파일도 포함되고, 실행마다 전용 임시 폴더라 동시 실행에 안전하다. `-Module A,B` 가능.
- 대상 = 모듈 코드를 컴파일하는 어셈블리를 Unity 규칙대로 계산한 것: asmdef/asmref 폴더(가장 가까운 것) → 그 어셈블리, 나머지 `Assets/` 코드 →
  predefined 어셈블리(`Assets/Plugins`·`Standard Assets` → `*-firstpass`, `Editor` 폴더 → `*-Editor`, 그 외 `Assembly-CSharp`; `predefined: true`, 통째로 검사).
  그래서 asmdef 없는 `modules[]` 폴더도 submit 게이트가 잡는다. 에디터가 컴파일하지 않는 asmdef(`includePlatforms`에 Editor 없음, 예: WebGL 전용)는
  `notCompiledInEditor`로 빼고 검사하지 않는다.
- worktree에서 돌리면 소스는 worktree, 응답 파일·의존 DLL은 에디터 트리 것을 쓴다(출력에 `sourceRoot`/`editorRoot`).
- `-Module`은 그 모듈이 참조하는 프로젝트 어셈블리(`Game.Contracts`)도 함께 검사하고, 한 실행 안에서 의존 순서로 컴파일해
  **방금 만든 DLL을 참조**한다(체인) → worktree에서 추가한 Contracts 타입도 보인다. `-IncludeHarness`면 Harness도 체인.
- `csc`: 에디터가 쓰는 응답 파일(`Library/Bee/artifacts/*/<Asm>.rsp`)을 에디터 빌드 그래프(`Library/Bee/*.dag.json`)에 기록된 그대로의
  dotnet·csc.dll·플래그로 컴파일 → 에디터와 동일한 컴파일러/플래그/분석기(출력 `compiler`). Unity 버전마다 설치 구조가 달라도 된다
  (6.0–6.3 `Data/DotNetSdkRoslyn`, 6.6 `Data/DotNetSdk/sdk/<v>/Roslyn/bincore`). .NET SDK·VS 불필요.
  에디터가 한 번도 컴파일하지 않은 **새 어셈블리**(.rsp 없음)는 같은 종류(Editor 전용/런타임) Harness 어셈블리의 응답 파일에
  asmdef 참조를 붙여 합성해 검사한다(`synthesized: true`; 템플릿의 패키지 참조가 남아 실제보다 약간 관대).
- `msbuild`: Unity가 생성한 `<Asm>.csproj`를 실행마다 재작성(소스 목록 갱신, ProjectReference → 체인 DLL 또는 에디터 DLL) 후 VS 2022 MSBuild.
  csproj가 없으면 `tools/uc.ps1 harness_sync_csproj`(새 어셈블리는 csc만). 이 머신엔 .NET SDK가 없어 `dotnet build`는 불가.
- `-Dependents`(`-Module`과, submit 게이트가 씀; G5-3): 이 실행이 바꾸는 것 — 모듈의 어셈블리와, 에디터 트리와 소스가 다른 참조 어셈블리(계약 추가) — 을
  참조하는 프로젝트 어셈블리도 이 체크아웃 소스로 함께 컴파일한다(`targets[].dependentOf`, 출력 `dependentsOf`). 계약에 다른 모듈이 쓰는 이름을 더하면 그
  모듈이 CS0104로 깨지는 것을 여기서 잡는다(샘플: 계약이 바뀌면 Game.Stage +0.13 s). 모듈끼리 참조하는 기존 프로젝트(`modules[]`)에서는 한 모듈의 공개 API
  변경이 다른 모듈을 깨뜨리는 것도(BagelGame: Game의 속성 이름 → UI의 CS1061). 에디터가 컴파일하지 않는 어셈블리(응답 파일 없음)는 `dependentsSkipped`.
- 검사 집합 밖(다른 모듈, `-IncludeHarness` 없는 Harness)은 **에디터가 마지막으로 컴파일한 DLL**이다 — 집합이 참조하는 쪽은 그게 맞다(submit이 넣을 에디터 트리가
  컴파일하는 그 DLL). 최종 판정은 항상 `loop.ps1` / `submit.ps1`.

## 새 클론 검증 (tools/fresh-clone-test.ps1)

```powershell
powershell -ExecutionPolicy Bypass -File tools/fresh-clone-test.ps1                    # 이 저장소의 HEAD, ~110s
powershell -ExecutionPolicy Bypass -File tools/fresh-clone-test.ps1 -Source https://github.com/geuneda/unitree -Ref master
```
- 짧은 경로(기본 `<저장소 상위>/ah-fresh`; 프로젝트 경로 60자 이하, `%TEMP%` 밖)에 클론 → 클론의 `open.ps1`(첫 임포트) →
  `uc.ps1 harness_setup` → `loop.ps1` N회(`-Loops`, 기본 3) → `quit.ps1` → 클론 삭제. 모두 **클론의 도구**로 돌리므로 **커밋된 코드**를 검사한다
  (미커밋 변경은 report의 `uncommittedNotTested`에 나온다 → 임시 커밋 후 실행).
- 녹색 = 각 단계 성공 + `harness_setup` 뒤 남은 `issues` 없음 + 루프 전부 녹색 + 루프끼리 `build.fingerprint`·`play.events` 동일
  (`-ExpectFingerprint`로 값까지) + `harness_quit`으로 정상 종료 + 종료 뒤 클론의 `git status`가 깨끗.
- 결과: `HarnessOut/fresh-clone/report.json`(`stage` = prepare|clone|version|open|setup|loop|determinism|quit|git), `loop<N>.json`,
  마지막 루프의 `shots/`(Read로 확인), 클론의 `Editor.log`. 실패하면 에디터는 닫고 클론은 남긴다(`kept`) → 다음 실행은 `-Force`.
  `-Keep`은 녹색이어도 클론과 에디터를 남긴다(비교·디버깅용).
- `-UnityVersion <설치된 버전>`: 클론의 `ProjectVersion.txt`를 그 버전으로 바꿔서 연다(P-1; 이때 `git status` 변경은 보고만 한다).
- `-SelfTest`: 루프 뒤 클론에서 `tools/selftest.ps1`(매트릭스 1–8, 루프의 fingerprint를 기대값으로)까지 돌린다 → `selftest.json`,
  report의 `selftest`(`stage=selftest`). worktree는 클론 옆(`ah-fresh-st-a/-b/-o`)에 생겼다가 지워진다. 전체 ~12–17분(ROADMAP O-12 포함).
- 언제: `tools/`, `ProjectSettings/`, `Packages/`, `.gitignore`, 에디터 시작 경로(`[InitializeOnLoad]`)를 바꿨을 때와 공개 전.

## 하네스 자기 검증 (tools/selftest.ps1)

```powershell
powershell -ExecutionPolicy Bypass -File tools/selftest.ps1                                         # 매트릭스 1–8, ~9–14분(O-12 포함)
powershell -ExecutionPolicy Bypass -File tools/selftest.ps1 -Only 1,2,3,4,5,6 -ExpectFingerprint 609b54d2
powershell -ExecutionPolicy Bypass -File tools/fresh-clone-test.ps1 -UnityVersion 6000.0.84f1 -SelfTest   # 9 + 다른 버전
```
- 1 루프 3회(녹색, fingerprint·events 동일, 샷 blank/dark/magenta 없음, 모든 샷 1280x720에 HUD 합성, 기준 이미지: 루프 1이 `HarnessOut/selftest/golden`에
  쓰고 2·3이 픽셀까지 같음(maxDiff 0), 모든 에디터 창을 매 업데이트마다 다시 그리는 플레이의 루프도 픽셀까지 같음(G3-15), 커밋된 이 버전의 기준 이미지와 같음, `harness_golden`의 `ignore`·패치 버전 대체) + 시나리오 도구 루프(`waitScene`·`waitTarget`·UI Toolkit `click`·KeyCode 키 이름·포즈/카메라 캡처) +
  uGUI 합성(편집 모드, 저장하지 않는 픽스처: 오버레이·메인 카메라의 Screen Space - Camera·스택 UI 카메라의 캔버스 → 순서, 선형 공간 블렌드 오차 ≤ 2, 되돌림) +
  카메라(G3-7, 픽스처: 메인 카메라 자식인 스택 Overlay 카메라가 그리는 쿼드가 메인·다른 포즈 모두 화면 중앙, 미니맵 Base 카메라가 오른쪽 위, 앞 depth 카메라는 덮임,
  `"camera"`로 미니맵만, 메인 카메라·타깃·스택 되돌림, 씬 dirty 아님) + 플레이 중 픽스처(오버레이 캔버스·스택 카메라)와 연속 캡처(2x2 시트, `motion` > 0), 하늘만 보는 연속 캡처에서 구름이 게임 시간으로 흐름(G4-5) +
  실제 입력 격리(플레이 동안 실제 키보드 장치에 스페이스를 넣어도 events 그대로·`isolatedDevices` 누름 > 0, 실패·중단한 플레이 뒤에도 실제 장치가 다시 켜짐;
  플레이가 포커스 있음·없음으로 시작하는 두 번 — 없음은 Input System이 먼저 꺼 둔 장치를 `background`로 가져가 세고 도중에 포커스가 돌아와도 끝까지(G3-9), 포커스 변화는
  Unity 안에서 `Application.InvokeFocusChanged`로 만들어 OS 포커스와 무관; UI Toolkit의 "포커스 없으면 입력 무시"가 시나리오 동안 꺼짐(G3-14)) +
  렌더 설정(W4: RP 에셋이 생성물이고 루프 2·3은 다시 쓰지 않음, 지우면 루프 한 번으로 다시 생기고 fingerprint·픽셀·`git status` 같음; 반사 큐브맵에 잘못된 텍셀이
  없고 가장 밝은 텍셀이 태양 방향(2° 안); 앰비언트 = 라이팅 데이터의 큐브맵 SH; `AmbientProbe` 균일 환경 → Flat과 같음·쓰레기 텍셀 거부; 머티리얼 경고 3종과
  `LitMaterial`의 이미션·알파 클립; 하늘·반사(G4-5): `Harness/Sky`, 플레이 뒤 구름 시계 0, Custom 반사 프로브의 큐브맵에 잘못된 텍셀 없고 아치 방향이 하늘이 아니라 돌,
  fingerprint에 큐브맵 모양만, 프로브를 켜고 끈 closeup 렌더에서 받침대·매듭이 그것을 비춤, 루프 2·3은 하늘·프로브 큐브맵을 다시 쓰지 않음) + 프로젝트 설정(G1-5: 설정 스텝이 소유한 값을 루프 2·3은 쓰지 않음; YAML을 손으로 고치고(Mobile 레벨 이름, PC의 LOD 바이어스,
  레이어 이름 바꾸기·추가) Project Settings 창처럼 메모리에서 바꾼 값(PC의 vSync, 에디터의 품질 레벨 → 파이프라인 전환)을 루프 한 번이 되돌리고 6개 모두 `drift`, 파일·에디터에
  코드 값, fingerprint·픽셀·`git status` 같음 — 샘플 버전은 바이트까지) + 콘텐츠 헬퍼(G1-3: 빌드된 불씨의 고정 시드·`AlwaysSimulate`, Halo의 `ClipPlayer`; 픽스처 클립의 경로·컴포넌트·머티리얼 속성·
  Transform 속성 오타 → 경고 한 줄씩, 선형 회전 샘플, 파티클 두 번 시뮬레이션이 같음, 가산 `ParticleMaterial`; 플레이 중 런타임 클립을 받은 `ClipPlayer`의
  끝까지 재생·이벤트 `ClipEvent:RiseEnd`·크로스페이드) + UI 킷(G1-4: 루프의 `play.uiClock`이 `frames`; 빌드된 HUD의 Gauge·ToastStack·킷 버튼, 테마 변수 해석,
  토스트 클래스; 플레이 중 라벨·게이지가 모듈 데이터 객체를 따름(바인딩), REVERSE 버튼을 이름으로 클릭, 페이드 중 토스트 캡처가 두 번 픽셀까지 같음) + GPU 베이크(G4-1:
  텍셀 줄 방향, HLSL 노이즈 = C# `Noise`, 같은 입력이면 건너뜀, fingerprint 키) + 절차적 라이브러리(G4-4: SDF 메시가 닫히고 면 위에, 면 방향, 스플라인 끝점·호 길이,
  포아송 거리·결정성; 빌드된 소품·룬 데칼과 `DecalRendererFeature`·지형 디테일 맵) + `compile-check -IncludeHarness` · 2 C# 컴파일 에러 · 3 런타임 예외 + 핫 루프(G2-1: `[CodeReload] Tick` 본문 수정 →
  `-Hot`이 컴파일·빌드·도메인 리로드 없이 반영, events 같음, golden `changed`, `hot.interpreted`에 Tick의 호출 수·시간(G2-5) → 같은 변경을 Tick이 부르는 새 헬퍼
  2개(인스턴스·static)로 → 여전히 핫(`newMethods`), 샷이 인라인 변경과 같음 → `harness_hot check`: 제네릭 새 메서드·오버로드는 핫 아님(그 줄), 안 쓰는 새 메서드는 핫 →
  되돌리면 교체 해제·`same` → 필드 추가는 전체 루프(사유에 그 줄과 `field m_SelftestField`) → 핫 본문의 예외는 전체 루프가 주입한 줄로 보고) ·
  4 HLSL 에러(재임포트 없는 다음 루프에서도) + 되돌린 상태를 기준 이미지로 → 셰이더 한 줄(스펙큘러 절반) → golden `changed`(rect·diff PNG), 루프는 녹색 +
  파이프라인이 못 그리는 머티리얼(받침대를 `Standard`로 → 샷 `magenta`, `hint`에 `Smoke/Pedestal`, golden `changed`, 루프는 녹색 → 되돌리면 `same`) + 빌더 메시 변경(아치 두께 2배, G3-11 →
  첫 루프가 벌써 새 메시: golden `changed`이고 다음 루프와 같음 → 되돌리면 `same`) · 5 리셋 없는 static(lint) + 계약 lint(W9: 모듈 이름이 아닌 계약 파일,
  다른 네임스페이스의 같은 이벤트 이름, 다른 모듈 파일에 있는 이벤트, 두 모듈이 발행하는 이벤트 → `contract-file`·`contract-name`이 맞는 파일·모듈로) · 6 루프 2개 동시(한쪽이 락 대기) +
  worktree 전용 에디터(W7: 커밋된 코드의 worktree `<저장소>-st-o`에 컴파일 에러를 넣고 `open.ps1 -Own` → 창 없는·automated 에디터가 `Library` 사본으로 그래도
  뜨고 루프가 그 줄을 보고 → 고치면 에디터 트리 루프와 동시에 녹색, 둘 다 대기 없음, fingerprint·events 같음, `render` 없음·`fps.note`, 첫 샷들이 같은 때
  에디터 트리 루프의 샷과 허용치 안에서 같음(커밋된 기준 이미지도 `changed` 0) → `quit.ps1`) · 7 worktree submit(게이트 거부, 강제 submit 되돌림 +
  다른 worktree의 새 모듈·계약은 락 대기 후 유지(계약 파일은 그 worktree 소유, 게이트가 계약을 쓰는 모듈도 컴파일), 런타임 에러 되돌림, sync 직후 kill →
  `recoveredSubmit`; 계약(W9): 올라간 타입 변경 거부(주석·줄바꿈은 같은 해시), 남의 미병합 계약과 같은 이름 거부, 남의 미병합 계약 파일 거부, 다른 모듈이 쓰는
  이름(`Light`)은 게이트가 Stage의 CS0104로 거부(G5-3), 자기 미병합 계약 수정은 녹색; 프로젝트 설정(G1-5): 새 모듈의 설정 스텝이 레이어를 선언 → 에디터 트리의
  `TagManager.asset`이 바뀌고 submit이 worktree로 되복사, 런타임 에러 submit에 넣은 설정 스텝의 레이어는 되돌리며 TagManager도 복원) ·
  8 land(fast-forward + 그 사이 submit 락 대기, 모듈·계약 파일 소유 해제, 이미 병합됨, 미커밋·`.meta` 누락·충돌·에디터 트리 직접 수정 거부, 컴파일 에러 되돌림,
  병합 직후 kill → `recoveredLand`; 계약: 올라간 이름과 같은 이름·올라간 타입 변경 거부, Smoke 파일에 덧붙인 이벤트의 submit → land(병합 커밋) → 소유 해제;
  되복사한 `TagManager.asset`이 모듈과 함께 land돼 에디터 트리 깨끗; G5-6: 두 worktree가 각자 레이어를 더한 모듈을 submit(첫째만 되복사) → 커밋 → land를 두 순서로 —
  먼저 land한 쪽은 설정을 커밋하지 않고(`waitingFor`) 나중 쪽이 두 레이어를 커밋, 에디터 트리 깨끗; 손으로 고친 TagManager(루프가 저장한 뒤에도)는 `foreign`,
  `git checkout` + 루프 뒤 녹색이고 그 편집은 커밋되지 않음; 레이어를 더한 런타임 에러 land는 TagManager를 바이트까지·에디터에서도 되돌림).
  에러는 **주입한 줄 그대로**(file/line/module) 보고돼야 녹색이다.
  1번 끝에는 플레이어 실행(W8): `player.ps1`이 기본 시나리오 + 게임 카메라 캡처를 개발 빌드 플레이어에서 돌린다 — 녹색, 1280x720 창·vSync 0, 에디터와 같은
  `play.events`, 플레이어의 프레임·렌더 통계, 샷이 에디터 것과 파티클 차이 안(바뀐 픽셀 ≤ 1%, 평균 ≤ 2), 그 프레임의 화면 = 캡처(`screen.vsShot` same),
  빌드 뒤 작업 트리 그대로(첫 빌드는 셰이더 컴파일로 ~2분).
- 주입 위치는 샘플 모듈의 표식 줄: `SmokeModule.cs`의 `m_Time += dt;`(컴파일, 64행)·`EventBus.Publish(new SpinnerLap(laps));`(런타임, 71행),
  `SmokeIridescent.shader`의 `Frag` 첫 줄(99행 — W15에서 프로브 반사의 속성·키워드를 더해 87행에서 옮김). 핫 루프는 `Tick`의 `Mathf.Sin(m_Time * 1.6f) * 0.3f`(흔들림 폭; 새 헬퍼 단계는 이것을 `SelftestBob(1.2f)`로 바꾸고 `void Reverse()` 앞에 헬퍼를 더함)와
  `float m_Time;`(필드 추가).
  이 줄들을 바꾸면 `selftest.ps1`의 표식도 바꾼다.
- 6–8은 커밋된 `tools/`·하네스 패키지·`Assets/Game/`·설정 파일을 쓴다. 6은 detached worktree(`<저장소>-st-o`, 전용 에디터; 끝나면 닫고 지운다),
  7–8은 worktree 두 개를 저장소 옆(`<저장소>-st-a/-b`)에 만들고, `selftest/*` 브랜치·
  테스트 커밋(land의 병합 포함)을 만든 뒤 에디터 트리 브랜치를 시작 커밋으로 되돌린다(detached HEAD면 임시 브랜치를 썼다가 되돌린다).
  → 하네스를 고친 중이면 **임시 커밋 후** 돌린다. 도중에 에디터 트리 파일을 고치지 말 것(`git status`를 비교한다).
- 첫 빨간 항목에서 멈추고, 바꾼 파일·worktree·브랜치·커밋을 되돌린 뒤 마지막 루프(`final`)로 녹색과 `git status` 원상을 확인한다.
- 결과: `HarnessOut/selftest/report.json`(`items[].checks[]`, `lines`, `fingerprint`, `shotStats`, `final`), 단계별 report는
  `HarnessOut/selftest/<항목>-<단계>/`. PNG 눈 확인(`shots`)은 여전히 사람·에이전트 몫이다.

## Unity 버전

- 지원: **Unity 6.0 LTS 이상**. 하한은 에디터 연결(`com.unity.pipeline` 0.8.0-exp.1)이 `"unity": "6000.0"`이라서다(2022.3 이하 불가).
- 샘플 프로젝트(이 저장소)는 `ProjectVersion.txt`의 **6000.3.11f1**. 다른 설치 버전으로는 `tools/open.ps1 -UnityVersion <버전>`
  (`ProjectVersion.txt`를 그 버전으로 바꿔 "다른 버전으로 열기" 모달을 건너뛴다 → `git status`에 보인다).
- 검증한 버전(2026-10-01 W15, W16에서 다시 — 같은 fingerprint, `fresh-clone-test.ps1 -UnityVersion <v> -SelfTest`; fingerprint는 W15에서 샘플의 하늘(`Harness/Sky`)·반사 프로브·받침대·매듭 재질이 바뀌어 새 값
  — 6.6은 에디터 창이 있는 화면의 DPI에 따라 fingerprint가 달랐는데(150% 화면, ROADMAP G1-6) W11에서 고정했다. 세 버전 모두 selftest 1번의 루프끼리 `maxDiff` 0,
  하늘·프로브 큐브맵을 루프 2·3이 다시 쓰지 않음, 매듭이 프로브를 비춤(6.0은 `_FORWARD_PLUS`, 6.3·6.6은 `_CLUSTER_LIGHT_LOOP`)):

  | 버전 | URP(내장) | build.fingerprint | 줄(컴파일/런타임/셰이더) | 매트릭스 |
  |---|---|---|---|---|
  | 6000.0.84f1 (6.0 LTS) | 17.0.4 | `a0df2fa8…` | 64 / 71 / 99 | 1–9 녹색(핫 루프·UI 시계·GPU 베이크·`-automated`·창 없는 전용 에디터·하늘·반사 프로브 포함), 샷 81.8/66.1/50.6(6.3과 같음). 플레이어 단계는 건너뜀 — 6.3이 저장한 URP 전역 설정(에셋 버전 10)을 URP 17.0(8)이 빌드에 거부 |
  | 6000.3.11f1 (6.3 LTS, 샘플) | 17.3.0 | `609b54d2…` | 64 / 71 / 99 | 1–9 녹색(같음, 플레이어 실행 포함), 커밋된 기준 이미지와 같음(새 클론은 루프 2부터 픽셀까지) |
  | 6000.6.3f1 (최신 정식) | 17.6.0 | `cadaeca6…` | 64 / 71 / 99 | 1–9 녹색(같음, 플레이어 408 fps), 샷 81.8/66.1/50.6. `render.batches`는 null(6.6엔 그 카운터가 없다, 아래 "함정") |

  `-automated`·`-debugCodeOptimization`·`-batchmode -ignoreCompilerErrors`와 창 없는 에디터의 렌더(O-11 우회 포함)는 세 버전에서 같게 동작했다.

  fingerprint는 버전마다 다르다(URP가 만드는 머티리얼·에셋 직렬화가 다르다). 같은 버전 안에서만 매번 같아야 한다.
- 다른 버전으로 열면 Unity가 다시 쓰는 파일(커밋하지 않는다): `Packages/packages-lock.json`, `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`,
  `ProjectSettings/*`(버전별 새 필드). RP·Renderer 에셋은 W4부터 생성물이라 버전마다 그 버전의 모양으로 만들어진다(gitignore).
  URP·Core 같은 **코어 패키지는 manifest의 버전(17.3.0)과 상관없이 에디터 내장 버전으로 해석된다**.
- 하네스 패키지에는 버전 문자열을 쓰지 않는다. 에디터·컴파일러 경로는 실행 중인 에디터 프로세스 → `unity editors --installed`에서 얻고,
  API 차이는 `Runtime/UnityCompat.cs` 한 곳에서 `#if UNITY_6000_4_OR_NEWER`처럼 가른다(예: 6.4부터 `FindObjectsSortMode` obsolete).
  선택 패키지는 asmdef `versionDefines`로 가른다: `AGENTHARNESS_URP`(URP 카메라 데이터 복사), `AGENTHARNESS_RP_CORE`(`ctx.VolumeProfile`),
  `AGENTHARNESS_INPUT_SYSTEM`(입력 재생), `AGENTHARNESS_PHYSICS`(`ctx.Physics`). 없으면 그 기능만 빠지고 컴파일은 된다(Built-in·구 Input Manager 프로젝트).
  모듈 코드도 버전을 타는 API는 `UnityCompat`을 쓰거나 같은 방식으로 가른다.

## 설정 (ProjectSettings/AgentHarness.json)

하네스가 이 프로젝트를 어떻게 보는지. 에디터 커맨드(`Harness.HarnessConfig`, 파일이 바뀌면 다시 읽음)와 `tools/`(`Get-HarnessConfig`)가 같은 파일을 읽는다.
파일이나 필드가 없으면 기본값 = 기존 프로젝트에 붙은 하네스(아무것도 안 바꾸고, Build Settings 첫 씬을 돈다). 이 샘플은:

```jsonc
{ "setup": "harness",                  // harness: 하네스 전용 프로젝트 | attach(기본): 기존 프로젝트, 설정·Build Settings·남의 씬을 건드리지 않음
  "moduleRoots": ["Assets/Game"],      // 하위 폴더마다 모듈 (모듈 규칙·Builders/ 적용)
  "modules": [],                       // [{ "name": "Gameplay", "path": "Assets/Scripts" }] 폴더 하나 = 모듈 (기존 코드)
  "contracts": "Assets/Game/Contracts", // 공유 이벤트 폴더("계약 폴더" 규칙: lint·submit·land). "" = 없음
  "generatedRoot": "Assets/Generated", "buildScene": "Assets/Scenes/Main.unity",
  "playScene": "build" }               // build | first(Build Settings 첫 활성 씬) | 씬 경로
```
- `installAdded`: install.ps1이 하네스 때문에 더한 패키지(예: `com.unity.inputsystem`). uninstall이 제거하고 출시 빌드 필터가 뺀다.
- `installReplaced`: `[{"name","from","to"}]` install.ps1이 하네스 의존성 버전으로 올린 프로젝트의 직접 의존(예: `com.unity.pipeline` 0.6.0-exp.1 → 0.8.0-exp.1).
  uninstall이 `from`으로 되돌린다(manifest가 아직 `to`일 때만). 커밋되는 파일이라 다른 머신의 uninstall도 정확하다.
- `shots`: `[{"name","scene","pos":[x,y,z],"lookAt":[x,y,z]|"rot":[x,y,z],"fov"}]` 이름 있는 캡처 포즈. 기존 씬에 ShotPreset을 넣지 않고 쓴다
  (`scene`이 있으면 그 씬이 로드됐을 때만). 시나리오 `"preset"`·`"auto"`와 `harness_capture`가 ShotPreset과 함께 쓴다.
- `knownErrors`: 정규식 목록. 프로젝트가 원래 내는 에러(예: 저장소에 없는 SDK 데스크톱 라이브러리)를 `knownErrors`로 돌려 루프를 막지 않게 한다.
- `captureSize`: `[w, h]` 크기를 주지 않은 캡처(시나리오·`harness_capture`)의 크기. 없으면 세로 프로젝트 720x1280, 그 외 1280x720.
- `goldenRoot`: 기준 이미지 폴더(프로젝트 루트 기준, 기본 `golden`). 위 "기준 이미지".

- `attach`에서 `harness_build`는 하네스가 만든 적 없는 씬·에셋(`AgentHarnessGenerated` 라벨 없음)을 덮어쓰거나 지우지 않고, Build Settings를 바꾸지 않는다.
- 씬에 저장 안 한 변경이 있으면 play/capture/build는 씬을 바꾸지 않고 실패한다(`unsaved changes in ...`). 생성된 buildScene은 예외.
- lint: `static-reset`은 Domain Reload가 실제로 꺼져 있을 때만, 모듈 asmdef 어셈블리 + `Harness.Runtime`만(`Assembly-CSharp`는 제외).
  `module-asmdef`·`module-boundary`는 `moduleRoots` 모듈만.

## 기존 프로젝트에 붙이기 (install / uninstall / attach-test)

```powershell
$pkg = 'C:/.../AgentHarness/Packages/com.geuneda.agentharness/Tools~'
powershell -ExecutionPolicy Bypass -File $pkg/install.ps1 -Project C:/dev/MyGame -WhatIf
powershell -ExecutionPolicy Bypass -File $pkg/install.ps1 -Project C:/dev/MyGame [-Scene Assets/X.unity] [-Module Name=Assets/Path,...] [-Source git|local|embed|<UPM 문자열>]
powershell -ExecutionPolicy Bypass -File C:/dev/MyGame/tools/uninstall.ps1          # 에디터를 닫은 뒤
powershell -ExecutionPolicy Bypass -File tools/attach-test.ps1 -Project <git 클론> [-Scene ...] [-Module ...] [-Source ...]   # 매트릭스 10
```
- install이 더하는 것: `Packages/manifest.json` 한 줄(기본 `-Source git` = 이 저장소 git URL `?path=/AgentHarness/Packages/com.geuneda.agentharness#master`;
  `local` = 이 패키지 폴더의 `file:` 경로, 하네스 개발용; `embed` = `Packages/`에 복사), `tools/` 진입점(open·quit·loop·uc·submit·land·
  compile-check·uninstall), `tools/scenarios/default.json`, `tools/AgentHarness.md`(그 프로젝트의 에이전트용 안내서, `templates/AgentHarness.md`),
  설정 파일, `CLAUDE.md`·`AGENTS.md`가 없으면 안내서를 가리키는 `CLAUDE.md`. 설치 전 manifest·lock은 `Library/AgentHarness/install.json`에 남긴다.
- 프로젝트가 하네스 의존성(`package.json`, 지금은 `com.unity.pipeline` 0.8.0-exp.1)을 더 낮은 버전으로 직접 고정하고 있으면 올리고 `installReplaced`에 남긴다
  (UPM에서는 manifest의 직접 의존이 이겨서 하네스가 옛 버전으로 돈다). 버전이 아닌 값(git URL 등)은 비교하지 않고 경고만.
- `-InputShim`: 구 Input Manager 게임용 `Assets/AgentHarness/HarnessInput.cs`(게임 코드가 되는 파일: uninstall은 템플릿 그대로이고 아무도 안 쓸 때만 지운다).
  이미 붙인 프로젝트는 install을 `-InputShim`으로 다시 돌리면 **고치지 않은 옛 템플릿**(SHA-256이 `templates/HarnessInput.previous.txt`에 있음)을 새 템플릿으로
  바꾼다(`modified`). 고친 사본은 그대로 두고 경고한다. 템플릿을 바꿀 때는 바꾸기 전 버전의 해시를 그 파일에 더한다(uninstall도 그 목록을 "그대로"로 본다).
  `-KnownErrors '<정규식>'`: 설정 `knownErrors`.
- Active Input Handling이 New/Both인데 Input System 패키지가 없으면 `com.unity.inputsystem`도 더한다(`installAdded`): `com.unity.pipeline`
  0.8.0-exp.1이 `ENABLE_INPUT_SYSTEM`만 보고 입력 코드를 컴파일해서 그 조합에서 컴파일이 깨진다(아래 "함정").
- Build Settings가 비어 있으면 `-Scene`을 요구하고 후보 씬 목록(`scenes`)을 준다.
- uninstall: 의존성 줄(+ `installAdded`) 제거와 `installReplaced` 복원, lock은 기록(`Library/AgentHarness/install.json`)과 manifest가 맞으면 바이트 그대로 복원,
  기록이 없으면(설치를 커밋하고 다른 머신에서 제거) 하네스만 쓰던 항목 제거 + 올린 버전 되돌림 — 검증한 두 프로젝트에서 이것도 기준선과 바이트까지 같았고
  Unity로 다시 열어도 그대로였다. 설치가 만든 파일
  (내용이 그대로인 것만; `-Force`면 전부), `HarnessOut/`, `Library/Harness`·`Library/AgentHarness` 삭제. 에디터가 열려 있으면 거부.
- 진입점은 패키지를 `Packages/<이름>`(임베드) → manifest의 `file:` → `Library/PackageCache/<이름>@*` 순으로 찾는다. worktree(Library 없음)는 에디터 트리의
  패키지를 쓴다. git URL로 설치하고 아직 한 번도 안 연 체크아웃이면 `open.ps1` 진입점이 배치 모드로 한 번 임포트해(`Logs/Editor-bootstrap.log`) 패키지를 받는다.
- 출시(비개발) 빌드: `Harness.Runtime`은 define 제약으로 빠지고, `HarnessReleaseBuild`(IFilterBuildAssemblies)가 하네스 때문에만 들어온
  `Unity.Pipeline.Attributes`·`Newtonsoft.Json`·(`installAdded`의) `Unity.InputSystem*`을 뺀다(게임 코드가 실제로 참조하면 둔다). 하네스 모듈 위에
  게임을 만든 프로젝트는 출시 빌드에 스크립팅 define `AGENTHARNESS_RUNTIME`이 필요하다.
- attach-test 옵션: `-Scenario <파일>`(예: 부트를 기다리는 시나리오), `-KnownErrors`, `-InputShim`, `-NoBuild`.
- attach-test 녹색 = install 성공, `harness_setup`이 아무것도 안 바꿈, install 뒤와 루프 뒤 `git status`가 install이 보고한 것 + lock뿐, 루프 N회 녹색·
  fingerprint·events 동일, 출시 빌드에 `Harness.*` 없음, uninstall 뒤 `git status` 비어 있음. 빌드가 다시 쓴 프로젝트 설정(하네스와 무관하게 Unity가
  빌드 중 쓰고 종료 때 저장)은 `buildRewrote`로 보고하고 에디터를 닫은 뒤 되돌린다. 빌드가 만든 폴더(추적 파일 없음 — Addressables의
  `AddressableAssetsData/<플랫폼>/`, 안의 content state는 gitignore)는 `.meta`와 함께 지운다(남기면 다음 에디터 시작이 `.meta`를 다시 쓴다).
  빨간 attach-test는 에디터만 닫고 설치·빌드 산출물을 남긴다(조사용) — 다시 돌리기 전에 그 클론의 `tools/uninstall.ps1`과 `git status` 정리.

## 함정 (겪은 것)

- **`[InitializeOnLoad]` 코드는 에셋 임포트 워커 프로세스(`Logs/AssetImportWorker*.log`)에서도 돈다.** 워커의 SessionState는 비어 있어서
  `HarnessConsole`이 "새 세션"으로 보고 `console.ndjson`을 지우고 seq 1부터 썼다 → 루프의 `since <mark>` 조회가 그 뒤 런타임 예외를 놓쳐
  **예외가 났는데 루프가 녹색**이었다(selftest 3번이 새 클론에서 간헐적으로 빨감). 파일·전역 상태를 건드리는 에디터 초기화는
  `AssetDatabase.IsAssetImportWorkerProcess()`면 건너뛴다(`Application.isBatchMode`도 워커에서 참이다).
- **버전마다 내장 패키지가 다르다.** manifest에 그 버전에 없는 내장 모듈이 있으면 에디터가 시작하다 `Package ... cannot be found`로 꺼진다
  (6.0에는 `com.unity.modules.adaptiveperformance`·`vectorgraphics`가, 6.6에는 `com.unity.modules.vr`이 없다). 템플릿 기본 패키지
  `com.unity.visualscripting` 1.9.10은 6.6에서 컴파일 에러(CS0619). 샘플 프로젝트에서는 넷 다 뺐다(아무도 쓰지 않음). 패키지는
  `UnityEditor.PackageManager.Client`(eval)로 추가·제거한다 — packages-lock.json까지 맞게 바뀐다.
- 새로 설치한 Unity 버전의 첫 실행은 **이용 약관 창**(Unity Editor Software Terms)을 띄운다. Pipeline 서버가 뜨기 전이라 `open.ps1`이
  `dialog.title`로 보고한다 → 사람이 동의해야 한다. 시작 시 컴파일 에러가 있으면 "Enter Safe Mode?"도 같은 식으로 보고된다(창 제목은
  `Process.MainWindowTitle`로는 안 보여서 Win32 `EnumWindows`로 읽는다).
- 템플릿에서 온 URP 에셋이 옛 직렬화 버전이면(`Mobile_RPAsset`이 `k_AssetVersion: 12`, URP 17.3은 13) 셰이더 재임포트 같은 작업 뒤
  URP가 모든 RP 에셋을 다시 써서 **에디터 종료 때** 저장한다 → 새 클론의 `git status`가 더러워졌다. W4부터 RP 에셋은 `ISettingsStep`이 그 버전으로
  만드는 생성물이라 커밋하지 않는다.
- **`AssetDatabase.CreateAsset`은 GUID를 고를 수 없다**(먼저 써 둔 `.meta`도 무시하고 새 GUID로 덮어쓴다). `SaveToSerializedFileAndForget`으로 쓴 파일은
  GUID는 지켜지지만 메인 오브젝트 fileID가 1이라 `.meta`의 11400000과 맞지 않는다. → 설정 에셋은 임시 폴더에 `CreateAsset`으로 Unity가 쓰게 한 뒤 그 파일을
  제자리로 옮기고 경로에서 만든 GUID로 `.meta`를 쓴 다음 임포트한다(하위 에셋 fileID 유지). 에셋이 있으면 제자리 덮어쓰기만.
- **URP 17.6은 Renderer Feature 하위 에셋에 `HideInHierarchy`를 켠다**(렌더러를 로드할 때·메뉴로 추가할 때). 코드로 만든 기능에 없으면 6.6에서만 매번
  "내용이 다름"으로 다시 썼다 → `AddRendererFeature`가 모든 버전에서 같이 켠다.
- **`Camera.RenderToCubemap(Cubemap)`은 버전마다 CPU 픽셀이 다르다**: 6.3은 GPU 결과를 **sRGB로 인코딩해서** half-float 큐브맵에 넣고(선형 0.071 → 0.298,
  반사가 실제보다 밝고 태양 HDR이 눌림), **6.6은 성공을 돌려주지만 CPU 픽셀을 채우지 않는다**(초기화 안 된 메모리: half `0xCDCD` = −23.2 또는 0).
  GPU 쪽은 맞게 그려져서 에셋을 만든 직후의 첫 플레이만 정상이고, 저장된 에셋을 다시 읽은 뒤엔 음수 반사로 URP Lit 표면이 전부 검었다(P-4, 비결정적).
  → `BakeSkyReflection`은 큐브 RenderTexture에 렌더하고 `AsyncGPUReadback`으로 면마다 읽어 `SetPixelData`(선형 HDR 그대로, 6.0/6.3/6.6 같음).
  `AmbientProbe.FromCubemap`은 유한·비음수가 아닌 텍셀이 있으면 예외를 던진다(검은 씬 대신 빌드 에러).
- **Built-in으로 시작한 에디터 세션에서 URP로 바꾸면 6.6은 새 셰이더 변형의 첫 그리기를 빼먹었다**: 새 클론(RP 에셋이 생성물이라 Built-in으로 열림) → `harness_setup`이
  URP를 만들고 배정 → selftest 1번 카메라 픽스처의 첫 캡처에서 스택 Overlay 카메라의 새 Unlit 쿼드가 없었다(두 번째 캡처엔 있음, 비동기 셰이더 컴파일은 꺼져 있음,
  6.6 새 클론 2회 연속·같은 클론을 Built-in 시작으로 되돌려 재현; 처음부터 URP로 연 세션은 셰이더 캐시를 지워도 정상, 6.0·6.3은 정상). 전환 뒤 도메인 리로드
  한 번이면 정상 → 설정 스텝이 활성 파이프라인을 바꾸면 `EditorUtility.RequestScriptReload()`, `uc.ps1`·루프가 리로드를 기다린다.
- **에디터에서 품질 레벨을 클릭하면 그 뒤 루프가 모두 그 레벨의 파이프라인으로 돌았다**(W10 전: 샘플에서 Mobile을 누르면 렌더 스케일 0.8·SSAO 없음, 보고 없음).
  `m_CurrentQuality`가 ProjectSettings에 저장되는 "설정"이라서다 → 설정 스텝이 품질 레벨을 선언하면 에디터의 레벨 = 활성 플랫폼의 기본 레벨로 되돌리고 `drift`로 보고.
- **`Time.fixedDeltaTime` 세터는 TimeManager를 dirty로 만들지 않는다**(에디터 모드에서 값은 바뀌지만 저장되지 않음; `Physics.gravity`는 만든다) → 설정 스텝이 쓴
  설정 오브젝트는 하네스가 `SetDirty` + `SaveAssets`. `UnityEngine.QualityLevel`(옛 enum)이 있어서 품질 레벨 값 묶음은 `QualityLevelValues`다.
- 6.6은 새 씬의 **첫 렌더에 기본 환경광(스카이박스 앰비언트·반사)이 아직 없다**(빈 씬 첫 렌더 114.7 → 다음부터 197). 설정별로 밝기를 잴 때 첫 렌더를
  빼지 않으면 먼저 잰 설정만 어둡다(P-4 조사 초기에 "소프트 그림자만 어둡다"로 잘못 본 원인).
- W4 전 `build.fingerprint`는 씬의 GameObject만 훑어서 RenderSettings(안개·앰비언트·스카이박스·반사)와 라이팅 데이터가 바뀌어도 그대로였다 → 지금은 들어간다.
  W6a 전에는 AnimationCurve를 키 개수로만, Gradient는 아예 해시하지 않았다(파티클 커브·색, 클립 키 값을 바꿔도 그대로) → 지금은 모든 키.
- **`EditorUtility.CopySerialized`로 덮어쓴 메시는 다음 플레이·캡처에서 옛 모양으로 그려졌다**: 직렬화 데이터(`mesh.vertices`, fingerprint)는 새 값인데 엔진이 그리는
  데이터는 그대로라, 빌더가 메시를 바꾼 **첫 루프의 샷이 이전 메시**였고 둘째 루프부터 새 메시였다(바위 크기 2배: 루프 1 작음, 루프 2 큼, fingerprint는 둘 다 새 값;
  `UploadMeshData`로도 안 됨). W6 이전부터 있던 문제다(메시를 바꾼 에이전트가 보는 바로 그 루프가 틀림). → 메시는 Mesh API(`Clear` → `SetVertices`/`SetIndices`…),
  큐브맵은 면별 `SetPixelData` + `Apply`로 덮어쓴다. selftest 4번이 "아치 두께 2배 → 첫 루프 = 다음 루프"로 확인한다.
- ShaderLab 속성 `Int`는 역사적으로 float다 — `Material.SetInteger`가 "already exists with a different type" 에러를 낸다. 정수는 `Integer`로 선언한다.
- Unity의 앞면은 **시계 방향**(왼손 좌표계, 앞면 법선 = `cross(b − a, c − a)`). 흔히 쓰는 아이코스피어 표는 이미 그 순서였는데 반대로 뒤집어 바위가 속이 빈 껍데기로
  보였다 → 새 메시 생성기는 추측하지 말고 selftest 1번의 방향 검사(면 법선이 정점 노멀·바깥쪽과 같은 쪽인 비율)로 잰다.
- GPU 베이크를 리드백하면 Direct3D·Metal·Vulkan은 렌더 타깃의 첫 줄이 위다(`SystemInfo.graphicsUVStartsAtTop`) — `BakeTexture`가 뒤집어 `uv.y = 0`이 Texture2D의
  아래 줄(0행)이 되게 한다(selftest가 uv를 구워 확인).
- `ctx.CacheHit`·`ctx.LoadAsset`은 매 빌드 에셋을 디스크에서 다시 읽는다(빌드가 새 씬을 만들며 앞 빌드의 에셋이 내려간다). 29만 정점 메시가 ~90 ms였다 → 큰 생성 메시는
  멀리 보이는 부분을 덜 쪼갠다(샘플 바위 14만 정점, 소품 스텝 ~60 ms).
- `eval_file`은 메인 스레드 작업이 5 s를 넘으면 `Main thread operation timed out after 5000ms`로 끊긴다 — 무거운 실험(CPU 1024² 베이크)은 나눠서 돌린다.
- **새로 만든 `.anim`과 제자리 덮어쓴 `.anim`의 직렬화가 다르다**: `AnimationUtility.SetEditorCurves` 직후 저장한 클립은 파생 바인딩 캐시 `m_ClipBindingConstant`가
  채워져 있고, `CopySerialized`로 덮어쓴 뒤에는 비어 있다(재생은 같다). 생성물을 지운 뒤 첫 빌드만 fingerprint가 달랐다 → 그 경로는 해시에서 뺐다.
  생성물 폴더를 지우고 루프 2회로 첫 빌드 = 다음 빌드를 확인하는 것이 이런 차이를 잡는 방법이다(새 클론의 첫 루프가 그 경우).
- **애니메이션 커브의 머티리얼 속성(`material._X`)은 이름이 틀려도 "풀린다"**: `AnimationUtility.GetEditorCurveValueType`이 `material._BaseColr`에도 Single을 준다
  (경로·컴포넌트·Transform 속성 오타는 null). 셰이더에 있는 이름은 렌더러의 `GetAnimatableBindings`로만 안다. 또 HDR 색은 그 목록에 `.x/.y/.z/.w`로 나오는데
  `.r` 키와 `.x` 키는 뜻이 다르다(`.r` 0.75 → 감마에서 선형으로 0.52 = `SetColor`, `.x` 0.75 → 그대로).
- 파티클 색(`startColor`, Color over Lifetime)은 입자마다 8비트로 저장된다 — HDR 시작 색은 1로 잘린다. 블룸이 걸리는 발광은 머티리얼의 `_BaseColor`(HDR)로 준다.
- **UI Toolkit의 transition·타이머는 실시간이다**: 고정 시간 간격(`Time.captureDeltaTime`)은 게임 시간만 바꾼다. 1초 opacity transition을 같은 게임 시간에
  재니 실행마다 0.73/0.79/0.79(그 사이 실시간 0.136–0.166 s). `Time.unscaledTime`도 캡처 간격과 상관없이 실시간이다(그걸로 바꿔도 0.745/0.779/0.786) → 러너가
  직접 미는 프레임 시계(`PanelClock`)로 바꾸자 0.450/0.450/0.450. 6.0에는 패널별 시간 함수가 없고(DLL 메타데이터로 확인) 정적 `Panel.TimeSinceStartup`(ms)만 있다.
  또 6.0은 편집 모드에서 런타임 패널의 데이터 바인딩을 갱신하지 않는다(패널 `Update()`를 불러도 그대로) — 플레이 중에는 된다.
- **새 `[UxmlElement]` 컨트롤과 그걸 쓰는 UXML을 한 번에 만들면** 루프의 `AssetDatabase.Refresh`가 스크립트를 컴파일하기 전에 UXML을 먼저 임포트해서
  `editorErrors`에 `Element 'X' is missing a UxmlElementAttribute ...`가 한 번 나온다. 컴파일 뒤 Unity가 다시 임포트해 플레이·캡처는 정상이었다(다음 루프엔 없음).
- UI Toolkit 레이아웃은 패널의 물리 픽셀 격자로 반올림된다. 작은 Game 뷰(366x305 → 배율 0.24, 1 px = 4.2 단위)에서는 폭 200이 198.7, 25%가 23.5%로
  배치됐다. 캡처는 캡처 크기로 다시 배치하므로 그 격자를 따른다(1280x720에서 게이지 0.749 → 0.762). 편집 모드에서 UI 크기를 재는 검사는 허용치를 둔다.
- `Playable.SetTime`을 한 번만 불러 클립을 되감으면 그 사이의 애니메이션 이벤트가 다음 평가에서 발행된다(0.78 s → 0: 0.5 s 이벤트가 한 번 더). 같은 값으로
  두 번 부르면 안 나간다(`ClipPlayer.Play`). PlayableGraph의 애니메이션 이벤트는 `AnimationPlayableOutput`의 Animator가 붙은 오브젝트의 컴포넌트가 받는다.
- Game 뷰 크기 목록(`PlayModeWindow.SetCustomRenderingResolution`이 여기에 추가한다)과 에디터 기본 레이아웃은 **사용자 전역**이다
  (`%APPDATA%\Unity\Editor-5.x\Preferences\GameViewSizes.asset`, `Layouts\current\default-6000.dwlt`). 하네스·실험 코드에서 바꾸지 않는다.
- **에디터에서 `ScriptableObject.CreateInstance<PanelSettings>()`를 하면 Unity가 `Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss`를 만든다**
  (내부 훅 `PanelSettings.GetOrCreateDefaultTheme`; `Assets/`에 테마가 없을 때). 하네스 테마가 패키지로 옮겨진 뒤 빌드마다 새 파일이 생겼다
  → `BuildContext.UIDocument`가 생성하는 동안만 훅을 하네스 테마로 바꾸고, 그래도 생기면 지운다.
- **`com.unity.pipeline` 0.8.0-exp.1은 Active Input Handling이 New/Both이고 Input System 패키지가 없으면 컴파일되지 않는다**(`RuntimeInputCommand.cs`가
  `#if ENABLE_INPUT_SYSTEM`만 본다). 에디터가 "Enter Safe Mode?"에서 멈춘다(`open.ps1`이 `dialog`로 보고). install.ps1이 이 조합이면 Input System을 더한다.
- 프로젝트에 지원 종료 패키지(예: Unity 6의 `com.unity.ide.vscode`)가 있으면 Unity가 열 때마다 "This project contains one or more deprecated packages.
  Do you want to open Package Manager?" 모달을 띄운다. `open.ps1`이 로그의 `... is deprecated` 줄로 `deprecatedPackages`를 보고한다 → 제거·교체할 것.
- UPM이 git 패키지를 받을 때 저장소의 `.gitattributes`와 무관하게 이 머신의 줄바꿈(CRLF)으로 체크아웃한다 → 텍스트를 비교하는 도구는 줄바꿈을 정규화한다.
- 플레이어 빌드는 `Library/Bee/artifacts/<hash>P*.dag/`에 `UNITY_EDITOR` 없는 응답 파일을 남긴다. compile-check가 "가장 최근" rsp를 쓰다가 그것을 골라
  `#if UNITY_EDITOR` 안의 에러를 놓쳤다 → 에디터 컴파일의 rsp(`…EDbg.dag` > `…E.dag`)만 쓴다(최신 순은 믿지 않는다: Bee는 입력이 같으면 rsp를 다시 쓰지 않음).
- 기존 씬은 편집 모드에서도 `[ExecuteAlways]` 스크립트가 값을 바꾼다(Cinemachine의 카메라 FOV, UI Toolkit의 숨은 `UIRenderer`). 로드된 씬을 해시하면
  루프마다 fingerprint가 달라서, 빌드하지 않는 프로젝트는 씬 파일 + 의존 에셋의 `GetAssetDependencyHash`로 fingerprint를 낸다.
- Unity는 플레이어를 빌드하면서 URP 에셋·`ProjectSettings.asset`(예: Input System이 `preloadedAssets`에 설정을 넣음)·`GraphicsSettings.asset`을
  다시 쓰고, 에디터가 종료할 때 한 번 더 저장한다. 하네스 없이 한 대조 빌드도 같았다 → 에디터를 닫은 뒤 되돌려야 한다.
- **구 Input Manager(`Input.GetKey`, `Input.mousePosition`)는 에디터에서 OS 입력을 직접 읽는다.** `Input.mousePosition`이 Game 뷰 밖의 실제 커서를 따라가고,
  Game 뷰에 보낸 이벤트(`EditorWindow.SendEvent`, 내부 `EditorGUIUtility.QueueGameViewInputEvent`)는 Game 뷰의 OnGUI까지는 가지만 `Input`에도 게임의
  `OnGUI`에도 닿지 않았다(Active Input Handling Old/Both 둘 다, 포커스 있음). → 게임 쪽 훅(`[AgentHarnessInput]`, `HarnessInput.cs`).
- **`<Keyboard>/space` 같은 바인딩은 가상 키보드만이 아니라 실제 키보드도 받는다.** 시나리오가 입력을 에디터 포커스와 무관하게 받게 하므로
  다른 창에서 누른 키가 게임 이벤트가 됐다(루프 ~25회에 1회 `play.events`가 달랐다, G3-6) → 시나리오 동안 실제 장치를 끈다. Input System의
  `LeavePlayMode`는 백그라운드 때문에 꺼진 장치만 켜고 `DisableDevice`로 끈 장치는 그대로 두므로, 끈 쪽이 반드시 다시 켜야 한다(러너 `Finish`,
  에디터의 `EnteredEditMode`). 실제 장치는 플레이 진입·에디터 포커스 때 상태 이벤트(sync)를 보내므로 "막은 입력"은 이벤트 수가 아니라 누름으로 센다.
- **에디터에 포커스가 없는 채 플레이 모드에 들어가면 Input System이 실제 장치를 모두 끈다**(게임 설정이 기본 `ResetAndDisableNonBackgroundDevices`일 때,
  `disabledWhileInBackground`). 그 장치의 이벤트는 `InputSystem.onEvent` 전에 버려지고, **`InputDevice.enabled`는 에디터 입력 업데이트 동안에는 그 장치를 켜진 것으로
  읽는다** — eval·에디터 코드가 본 값이 게임이 보는 값과 다르다. `IgnoreFocus`인 동안은 포커스가 돌아와도 켜지지 않는다. 격리는 그 장치도 가져간다(G3-9).
- **UI Toolkit 런타임은 앱 포커스가 없으면 입력을 통째로 버린다**(`DefaultEventSystem`, 데스크톱 OS; Unity Remote 연결 때만 예외) — 코드가 보낸 가상 마우스 클릭도.
  uGUI + `InputSystemUIInputModule`은 `runInBackground`면 포커스를 무시한다. 시나리오 동안은 하네스가 UI Toolkit 쪽 판정을 끈다(G3-14).
- **6.6은 코드로 만든 `PanelSettings`에 에디터 창이 있는 화면의 DPI를 넣는다**(`referenceDpi`: 150% 화면 144, 창 없는 에디터 96; 6.3·6.0은 96) → 빌더가 만들면 에셋과
  fingerprint가 화면을 따라간다(G1-6). `ctx.UIDocument`는 96으로 고정한다 — PanelSettings를 직접 만들면 `referenceDpi`·`fallbackDpi`를 정할 것.
- **Input System이 포커스를 아는 경로는 `Application.focusChanged` 하나다** → 테스트에서는 내부 `Application.InvokeFocusChanged(bool)`로 포커스 잃음·복귀를 만든다
  (selftest 1번). OS 포커스를 실제로 옮기면(다른 창 앞으로) 사람이 쓰는 창과 다투어 불안정하다. 도메인 리로드 뒤에는 다른 창이 앞에 있어도 `Application.isFocused`가
  true일 수 있다(ROADMAP O-13) — `fps.editorFocused`는 참고값이다.
- **URP는 Base 카메라를 겹쳐 그리지 않는다.** Depth-only(Uninitialized) Base 카메라를 depth를 높여 하나 더 두면 Game 뷰에서 앞 카메라의 씬이 지워지고
  그 카메라 것만 남는다(Built-in은 겹쳐진다). UI 카메라·무기 카메라는 메인 카메라의 스택에 Overlay로 넣는다. W2까지의 캡처는 그런 카메라의 캔버스를
  씬 위에 합성해 게임과 다르게 찍었다 → G3-7 캡처는 화면의 카메라를 실제 순서대로 그린다.
- **URP 렌더 요청(`RenderPipeline.SubmitRenderRequest`)은 스택 Overlay 카메라의 Screen Space - Camera 캔버스를 그리지 않는다**(요청한 베이스 카메라의 캔버스는
  그린다). Unity가 UI를 요청한 카메라에 대해서만 준비하는 것으로 보인다 — Game 뷰 프레임은 모든 카메라를 넘기므로 그려진다. 캡처는 그 캔버스를 따로 그려 합성한다.
  또 Screen Space - Camera 캔버스의 `rect`는 카메라 `targetTexture`를 바꾼 직후 `ForceUpdateCanvases`로 읽으면 한 번 늦게 따라왔다. 렌더할 때 다시 맞춰져서
  찍힌 결과는 캡처 크기 배치였다(미니맵 카메라 캔버스의 40px 요소가 캡처에서도 40px).
- **에디터는 처음 만난 셰이더 변형을 백그라운드에서 컴파일하고 그동안 그 오브젝트를 빼고 그린다**(Editor 설정 Asynchronous Shader Compilation). 새 클론(새 Library)의
  첫 플레이 t=0.5 캡처에 지형·하늘·후처리가 없었고(매듭만; 기준 이미지 `changed`, `meanDiff` 67.6), 편집 모드 픽스처의 첫 캡처엔 새 Unlit 쿼드가 없었다.
  재현: 에디터를 닫고 `Library/ShaderCache*`와 `Library/LastSceneManagerSetup.txt`를 지운 뒤 열어 루프 한 번. 캡처만 동기로 바꾸는 방법은 모두 안 됐다 —
  `ShaderUtil.allowAsyncCompilation = false`, `ShaderUtil.SetAsyncCompilation(cmd, false)` 명령 버퍼, 캡처 동안만 `EditorSettings.asyncShaderCompilation` 끄기
  (앞 프레임의 Game·Scene 뷰가 이미 비동기로 요청해 둔 변형은 그대로 빠진다), 캡처 전에 `ShaderUtil.anythingCompiling`을 기다리기(메인 스레드를 막으면
  컴파일이 끝나지 않는다: 20 s 동안 참). → 프로젝트 설정으로 끈다: `harness_setup`의 `syncShaders`(`setup: harness`면 적용, 샘플은 커밋됨; `attach`면 권장만).
  켜진 프로젝트에서 캡처 순간에 컴파일 중이었으면 `shotStats[].shadersCompiling`과 `hint`.
- `Object.FindObjectsByType`은 `HideFlags.DontSave` 오브젝트를 돌려주지 않는다. 캡처·클릭 대상 탐색도 그걸 쓰므로, 에디터 테스트 픽스처는 일반 오브젝트로
  만들고(편집 모드에서 스크립트로 만든 오브젝트는 씬을 dirty로 만들지 않았다) 끝나면 씬을 다시 연다(selftest 1번의 uGUI 픽스처).
- Input System 패키지가 있는 프로젝트의 Active Input Handling을 Old로 바꾸면 Input System이 "백엔드를 켤까요?" 모달을 띄워 에디터 메인 스레드가 멈춘다.
- **클론·worktree도 원본 프로젝트와 PlayerPrefs를 공유한다**(에디터에서는 company/product별 레지스트리). 사내 프로젝트 클론의 시나리오가 개발용 로그인
  대화상자를 건너뛰자 원본에 저장된 선택(라이브 서버)으로 로그인했다. 서버 선택 같은 버튼은 시나리오에서 명시적으로 누르고, 바꾼 PlayerPrefs는 되돌린다.
- 프로젝트에 이미 `Tools/`가 있으면(Windows는 대소문자 무시) 진입점이 그 폴더에 들어간다. 이름이 겹치는 기존 파일이 없으면 문제없고 uninstall은 자기 파일만 지운다.
- 에이전트의 Bash 도구(Git Bash)로 넘긴 명령은 작은따옴표·`<<'EOF'` 안에서도 `\\`가 `\`로 줄어든다(확인: `r"a\\b"`가 3글자).
  heredoc Python으로 `.ps1`을 고치다 정규식·경로가 조용히 깨진 적 있다 → 백슬래시가 든 편집은 Edit 도구로 한다.
- **Pipeline 코드 리로드(`[CodeReload]`)의 두 백엔드**: `reload_file`(Assembly.Load)은 교체한 본문이 public 멤버만 쓸 수 있어 private 필드를 쓰는 모듈 Tick을
  거부한다(`accessibility violation`). `reload_file_editor_interpreter`는 private도 되지만 C# 부분집합이다(`try/catch`면 "No Methods Applied"). 인터프리터 본문이
  던진 예외는 Pipeline이 `CodeReload: Error invoking override ...`로 **줄 없이** 로그하고(보고 위치 = 메서드 선언 줄) **원래 본문을 이어서 돌린다** —
  예외 전까지 한 일이 두 번 된다(`SpinDirectionChanged` 1 → 2). 편집 모드에서 적용한 교체는 Domain Reload가 꺼져 있으면 플레이 모드까지 유지된다.
- **도메인 리로드 직후 에디터는 첫 두 update 틱 사이에 ~0.9 s를 네이티브 작업에 쓴다**(창 다시 그리기로 보인다; `update`·`delayCall` 콜백 중 20 ms 넘는 것은
  없었다). 그 사이 온 메인 스레드 명령은 모두 기다린다 → 리로드 뒤 첫 명령이 ~0.9 s(W5 전에는 빌드가 "JIT 워밍업"으로 ~2 s였던 것의 절반). 2 s 쉬었다 보내면 즉시.
- **도메인 리로드는 에디터를 오래 띄워 둘수록 느려졌다**(같은 코드로 새로 연 에디터 2.5 s → 루프·실험 ~1시간 뒤 3.5 s, `FinalizeReload` 쪽). 시간을 잴 때는 새로 연다.
- **창 없는 에디터(`-batchmode`, `-quit` 없음)는 세션에서 처음 그리는 일부 오브젝트를 쓰레기 값으로 그렸다**(W7): 새로 연 뒤 첫 캡처에서 매듭이 노랑·흰색,
  선돌이 검정이었다(매번 다른 색; 같은 머티리얼의 아치는 정상 → 머티리얼이 아니라 그 메시의 첫 그리기). 두 번째로 그리면 같은 프레임 안에서도 정상이었고,
  하늘만 보는 작은 렌더로 미리 그려도 소용없었다(그 오브젝트를 그려야 함). `-force-gfx-mt`로도 같았다. 창 에디터는 Game·Scene 뷰가 먼저 그려서 드러나지 않는다
  → `HarnessCapture.Render`가 `Application.isBatchMode`면 카메라들을 한 번 버리고 다시 그린다(캡처 한 장 +수 ms).
- **창 없는 에디터의 메인 루프는 쉬지 않는다**: 유휴일 때 `EditorApplication.update`가 초당 ~6만 번(1코어의 120%). 틱마다 `Thread.Sleep(1)`을 넣었더니
  초당 63틱이었다 — Windows 타이머 기본 해상도(15.6 ms) 때문이고, 그동안 메인 스레드 명령이 틱을 기다려 ping이 17 → 32 ms. → `HarnessHeadless`가 이 프로세스의
  타이머를 1 ms로(`timeBeginPeriod(1)`) 두고 할 일 없는 틱(플레이·컴파일·임포트 아님)만 1 ms 잔다: 유휴 CPU ~8%, ping ~8 ms.
- 창 없는 에디터는 Game 뷰가 없다: `UnityStats`(batches·SetPass)가 0, 플레이 중 아무것도 그리지 않아 fps가 창 에디터와 비교되지 않는다, `"screen"` 캡처 불가.
  디더링 순번이 달라 창 에디터의 기준 이미지와 픽셀까지 같지는 않다(`meanDiff` ~0.47, 최대 2 — 허용치 안이라 `same`; 창 없는 에디터끼리는 픽셀까지 같다).
- **`-automated`면 `EditorUtility.DisplayDialog`는 곧바로 `false`(취소), `DisplayDialogComplex`는 1(취소)을 돌려주고 로그에 아무것도 남기지 않는다**.
  Pipeline이 대화상자를 보는 `EditorDialogEvents`는 6.7부터라 6.0–6.6에서는 자동으로 닫힌 대화상자를 알 길이 없다(에디터 작업이 "취소"로 끝났으면 의심).
  `InternalEditorUtility.isHumanControllingUs`가 거짓이 되어 창 배치를 종료 때 저장하지 않고, 검색 인덱스 모니터(`SearchMonitor`)도 끈다.
- **시작할 때 스크립트가 컴파일되지 않으면**: 창 있는 `-automated` 에디터는 묻지 않고 Safe Mode(창 제목 `... - SAFE MODE - ...`, Pipeline 서버 없음, 백그라운드에서
  파일을 고쳐도 나오지 않음), `-batchmode`는 "Scripts have compiler errors."로 종료(코드 1). `-batchmode -ignoreCompilerErrors`는 마지막으로 성공한 어셈블리로
  뜬다(창 에디터에는 효과 없음). 그 컴파일은 하네스 코드가 올라오기 전이라 에러가 `compile.json`에 없고, 바뀐 게 없으면 `recompile`이 다시 컴파일하지 않는다
  (`failed`) → `HarnessConsole`이 세션에 한 번 `RequestScriptCompilation()`으로 다시 컴파일해 에러를 받는다.
- **다른 프로젝트 폴더로 복사한 `Library/`는 쓸 수 있다**(에셋을 다시 임포트하지 않음, 스크립트는 경로가 바뀌어 전부 다시 컴파일 ~17 s). 복사하면 안 되는 것:
  `Library/Pipeline/.unity-pipeline-port`(원래 에디터의 pid·포트 — 도구가 원래 에디터에 붙는다), `ilpp.pid`, LMDB 락 파일, 하네스 저널.
- Mono의 `RuntimeHelpers.PrepareMethod`는 아무것도 컴파일하지 않는다(612개 메서드 5 ms). `RuntimeMethodHandle.GetFunctionPointer()`는 JIT한다(55 ms).
  하지만 JIT가 `beforefieldinit` 타입의 static 초기화를 그 스레드에서 돌릴 수 있어 Unity API를 부르는 초기화가 백그라운드에서 실패하면 그 타입이 도메인 끝까지
  망가진다 → 하네스 코드를 백그라운드에서 미리 JIT하지 않는다(얻는 것 ~75 ms). Roslyn처럼 Unity API가 없는 코드만 데운다.
- `CompilationPipeline.GetAssemblies`는 호출마다 ~60 ms(어셈블리 67개, 소스 4,400개)다. 빌드가 두 번, lint가 한 번 불렀다 → 다음 컴파일까지 캐시(`HarnessPaths.Assemblies`).
- compile-check는 에디터가 마지막으로 쓴 응답 파일(`.rsp`)의 참조를 쓴다 → asmdef에 **참조를 새로 더하면** 에디터가 한 번 컴파일할 때까지 그 참조의 타입이
  CS0103으로 나온다(루프로 컴파일하면 사라짐). msbuild 백엔드는 Unity가 만든 `.csproj`를 쓰므로 그 뒤에도 `tools/uc.ps1 harness_sync_csproj`로 다시 만들어야 한다
  (안 하면 CS0234).
- **URP는 `targetTexture`가 있는 카메라를 그 텍스처의 형식으로 렌더한다**(`UniversalRenderPipelineCore.CreateRenderTextureDescriptor`: "External texture replaces
  internal (intermediate) color buffer"). 캡처가 8비트 sRGB RT에 렌더 요청을 보내던 동안 HDR 파이프라인의 이미션·가산 파티클이 톤 매핑·블룸 전에 1로 잘려
  매듭의 흰·청록 테두리가 분홍으로, 불씨가 어둡게 찍혔다(실제 화면과 같은 프레임 비교에서 바뀐 픽셀 0.99%). 에디터 캡처끼리는 매번 같아서 기준 이미지로는 안
  보였다 → 캡처는 그 카메라의 HDR 형식 RT에 그린 뒤 8비트 sRGB로 `Blit`해 읽는다(같은 프레임 비교 0.06% → 워터마크만, 기준 이미지 갱신).
- Unity는 `Library/` 안으로 플레이어를 빌드하지 않는다(`Invalid build path ... internal work directory`) → `HarnessOut/player-build/`.
- **URP는 대상 텍스처가 있는 카메라의 MSAA를 그 텍스처의 `antiAliasing`으로 정한다**(`InitializeStackedCameraData`) → 1샘플 캡처 RT는 MSAA를 껐다.
- **URP의 DBuffer 데칼은 그 앞에 에디터 GUI가 그렸는지에 따라 가장자리 픽셀이 달랐다**(W13, G3-15; URP 17.3, D3D11): 같은 코드의 루프끼리 샘플 closeup의 룬 원이
  받침대 모서리와 만나는 픽셀 6–7개가 두 값 중 하나였다(채널 차이 47). 플레이 중 마지막 카메라 렌더와 캡처 사이에 에디터 창이 다시 그려지면 데칼 가장자리 2픽셀이
  2–3단계 달라지고 SMAA의 에지 판정이 그것을 키운다. `InternalEditorUtility.RepaintAllViews()`를 매 에디터 업데이트마다 부르면 10/10, 그냥 두면 1/15. 편집 모드에서는
  에디터 프레임마다 첫 캡처만 달랐고, 같은 프레임에 앞서 그린 카메라 렌더가 GPU로 넘어간 뒤(`ReadPixels`·`AsyncGPUReadback`·`GL.Flush`)에는 같았다. 렌더 그래프 텍스처
  풀·`UNITY_HDR_ON`·`GL.sRGBWrite`·SSAO·그림자·디더링·`copyDepthMode`·`intermediateTextureMode`는 원인이 아니었다. 캡처 직전에 한 번 더 그려 버리는 우회는 플레이 중
  GUI 부하에서 듣지 않았고 디더링 순번만 밀었다. ScreenSpace 데칼은 같은 부하에서 8/8 픽셀까지 같았다 → 샘플은 ScreenSpace, selftest 1번이 그 부하로 본다.
- **URP 17.3의 ScreenSpace 데칼 패스는 중간 텍스처 없는 카메라에서 예외를 던진다**: `DecalScreenSpaceRenderPass`가 `resourceData.cameraColor`로
  `RenderingUtils.SetScaleBiasRt`를 부르는데, 후처리·HDR·MSAA·깊이/불투명 텍스처 없이 타깃에 바로 그리는 카메라에서는 그 핸들이 비어 NullReferenceException →
  "Render Graph Execution error". 캡처의 uGUI 레이어를 그리는 숨은 UI 카메라가 그런 카메라라 uGUI 합성이 비고 플레이 중 캡처가 런타임 에러를 냈다 → 그 카메라는
  렌더러의 renderer feature를 끄고 그린다(`CaptureUi.RenderWithoutFeatures`). 게임의 그런 카메라(후처리 없는 보조 카메라)는 여전히 이 에러를 낸다(URP 쪽).
- **반사 프로브를 끄거나 지워도 같은 에디터 프레임의 렌더는 계속 그것을 쓴다**(W15): 컬링이 프로브를 프레임이 시작될 때의 상태로 본다(`enabled = false`, GameObject 끄기,
  텍스처 null, 크기 0, 멀리 옮기기 모두 그대로; 다음 프레임부터 반영). 빌드는 한 프레임이라 프로브 렌더가 **앞 빌드 씬의 프로브와 그 큐브맵**(같은 에셋)을 비췄고,
  받침대 재질을 바꾸면 프로브가 2–3 빌드에 걸쳐 수렴했다(루프마다 큐브맵을 다시 씀 → 첫 루프의 샷이 다음 루프와 달랐다) → 끈 뒤 `ReflectionProbe.UpdateCachedState()`.
- **카메라의 첫 렌더가 앞서 그린 다른 카메라의 상태를 이어받는다**(W15, URP 17.3): near/far가 다른 카메라 뒤의 첫 하늘 렌더는 태양 원반 가장자리 텍셀 몇 개가 half 1–6단계
  달랐다(같은 near/far 카메라를 먼저 한 번 그리면 같음). 큐브맵이 "에디터가 바로 전에 무엇을 그렸나"(빌드 프레임인지, 컴파일 뒤인지)를 따라 다시 쓰였다 → 빌드의 큐브
  렌더는 같은 카메라로 두 번 그리고 둘째를 쓴다(창 없는 에디터의 첫 메시 그리기 O-11 우회와 같은 자리).
- **Unity 6의 셰이더 매크로 `UNITY_VERSION`은 문서 예(2020.3.0 = 202030)와 형식이 다르다**: 6000.3.11f1 = 60030011, 6000.0.84f1 = 60000084(6000.<마이너>를 붙인
  수 × 10000 + 패치; 셰이더로 직접 읽어 확인). `#if UNITY_VERSION >= 600010`은 6.0에서도 참이라 6.0의 매듭이 프로브를 받지 못했다(W15 6.0 새 클론) → 6.1+는 `>= 60010000`.
  `#pragma`를 감싼 `#if UNITY_VERSION`은 6.0–6.6 모두 평가된다.
- `NativeArray<byte>` 인덱서로 큐브맵 4 MB를 바이트 비교하면 Debug 코드 최적화에서 수십 ms였다(프로브 단계 ~110 ms 중 대부분) → `AsReadOnlySpan().SequenceEqual(...)`.
- **Unity의 증분 플레이어 빌드가 앞선 빌드의 플레이어 데이터를 다시 썼다**: 출시 빌드(하네스 없음) 뒤 같은 프로젝트의 개발 빌드에서 "player data was not rebuilt"와
  함께 `ScriptingAssemblies.json`이 출시 빌드 목록 그대로 → `Harness.Runtime.dll`은 Managed에 있는데 로드되지 않아 `RuntimeInitializeOnLoadMethod`가 불리지 않았다
  (`RuntimeInitializeOnLoads.json`에는 있음). `CleanBuildCache`면 맞게 나온다(Fluid-Sim 9.6 s). 개발 → 출시 순서는 괜찮았다.
- PowerShell 5.1의 `Get-Content` 줄(문자열)에는 `PSPath`·`PSDrive`·`PSProvider` 속성이 붙어 있어 `ConvertTo-Json -Depth 20`이 그 객체 그래프를 펼치며 몇 분씩
  CPU를 썼다(보고서의 `logTail`) → 로그 꼬리는 `Read-HarnessLogTail`(순수 문자열).
- **에디터 플레이 모드와 플레이어는 첫 프레임들이 다르다**: 에디터는 게임을 한 프레임 돌린 뒤(`EnteredPlayMode`) 러너를 띄우고 처음 두 프레임이 0.02 s(=
  `Time.fixedDeltaTime`)였고, 플레이어는 첫 프레임만 0.02 s다. 그대로 두면 플레이어의 캡처가 한 프레임 어긋났다(매듭이 프레임당 회전만큼) → `PlayerRun`이 첫 프레임
  끝에 러너를 띄우고 둘째 프레임을 0.02 s로, 러너는 고정 간격을 Start가 아니라 첫 Update에서 켠다(에디터에서는 같은 프레임이라 동작이 같다). 그리고 플레이어는
  **첫 씬을 불러오며 파티클을 한 번(0.02 s) 진행해 둔다**(AfterSceneLoad에 이미 `time=0.02`, 에디터는 0; 로드 시점의 `Time.deltaTime`이라 `timeScale`·되감기로도
  안 됨 — `Simulate(0, restart)`는 프리웜을 다시 돌려 더 달라졌다) → 플레이어와 에디터의 샷은 파티클 둘레만 다르다.
- LateUpdate에서 읽은 전역 `_Time`은 이전 프레임 값이지만 URP 렌더 요청은 카메라마다 현재 시간을 넣는다(캡처 전에 `_Time`을 바꿔도 결과가 같았다).
- 개발 빌드는 화면 오른쪽 아래에 "Development Build"를 그리고(back buffer에 들어간다) 에러가 나면 개발자 콘솔을 띄운다 → 화면 비교에서 그 자리를 빼고
  (`compare.screenIgnore`), 플레이어 실행은 `Debug.developerConsoleEnabled = false`.
- 개발 빌드 플레이어의 스택은 `(at C:/<프로젝트>/Assets/X.cs:78)`처럼 **절대 경로**이고, 코드가 최적화돼 줄이 어긋날 수 있다(Script Debugging 빌드면 정확).
  `HarnessLogParse`는 절대 경로 프레임도 프로젝트 경로로 바꿔 모듈을 찾는다.
- 플레이어 빌드 동안 Input System이 설정 에셋을 Preloaded Assets에 넣었다가 빌드 뒤 메모리에서만 뺀다 → 디스크의 `ProjectSettings.asset`은 에디터가 저장할 때까지
  바뀐 채다. `AssetDatabase.SaveAssetIfDirty(PlayerSettings)`·`SaveToSerializedFileAndForget`은 프로젝트 설정 파일을 다시 쓰지 않았고 `SaveAssets()`는 썼다.
  URP도 첫 빌드에 전역 설정의 런타임 목록과 기본 Volume 프로필(새 필드, 스크립트가 없는 컴포넌트 제거)을, PlayerSettings에 Standalone 배칭 항목을 쓴다.
- PowerShell의 `[math]::Min(1, 170 / 1280)`은 첫 인자의 정수 오버로드를 골라 0이다 → 실수로 `[math]::Min(1.0, ...)`.
- **TextMesh Pro는 글자마다 SDF 스케일(uv0.w)을 캔버스 렌더 모드별로 계산해 넣는다**(W16, G3-16; `TextMeshProUGUI.GenerateTextMesh`: Screen Space - Overlay는
  `lossyScale / scaleFactor`, Screen Space - Camera는 `lossyScale`) — 그리고 그 뒤로는 lossyScale이 20% 넘게 바뀔 때만 고친다. 캔버스의 렌더 모드만 바꾸면 글자 메시는
  옛 모드의 스케일 그대로라 SDF 가장자리 폭이 scaleFactor만큼 틀린다(캡처가 오버레이를 Camera로 그리던 동안: 사내 프로젝트 A의 scaleFactor 0.46 → ~2배 날카로움).
  모드를 바꾼 뒤 `ForceMeshUpdate()`.
- **Pipeline `build_status`의 `errors[]`에는 줄이 없다**(0.8.0-exp.1 `BuildIssue`: 줄을 파싱하고 `file`만 남김, ROADMAP O-14) → 같은 응답의 `buildSteps[].messages[]`
  (`type`·`content` 원문)에서 읽는다.
- **에디터에서 컴파일되는 코드가 플레이어 빌드에서 안 될 수 있다**: 에디터는 모든 플랫폼의 UnityEngine API를 갖고 있어 `Handheld.Vibrate()`(모바일 전용)가 런타임 검사
  (`Application.isMobilePlatform`) 안에만 있어도 컴파일되고, Windows 플레이어 빌드는 CS0103으로 실패했다(사내 프로젝트 A) — 플랫폼 API는 `#if UNITY_ANDROID || UNITY_IOS`로.
- **Unity 6.6에는 Render 분류의 `Batches Count`·`Draw Calls Count`가 없다**(종류별 `Standard`/`SRP Batcher`/`BRG`/… `Draw Calls Count`로 나뉨). 그 이름의
  `ProfilerRecorder.StartNew(ProfilerCategory.Render, …)`는 분류와 상관없이 **UI Toolkit의 같은 이름 카운터**에 붙어 0을 읽었다(W7까지 6.6 루프의 `render.batches` 0) →
  Render 분류에서 이름으로 찾고, 없으면 draw call은 종류별 합, batches는 null.
- 더 새 Unity가 저장한 URP 에셋(전역 설정 `m_AssetVersion`, 파이프라인 `k_AssetVersion`)은 오래된 URP가 내려 쓰지 않고, 플레이어 빌드 전 검사
  (`URPBuildDataValidator`: "is not at last version")가 빌드를 거부한다. 실패한 빌드는 전처리기가 만든 파일(`Assets/Resources/PerformanceTestRun*.json`)을 남겼다.

- `Mathf.SmoothStep(from, to, t)`는 GLSL `smoothstep`이 **아니다**(값 보간). `PMath.Smoothstep(e0, e1, x)`를 써라. 지형이 전부 눈으로 나온 원인.
- `UnityEngine.Object`에 `?.` 금지(에디터의 fake null). `TryGetComponent`를 쓴다.
- 에디터 명령/빌더에서 `EditorApplication.delayCall` 금지 — 포커스 없는 에디터에선 실행되지 않는다(`harness_play`가 73s 멈췄던 원인).
  `EditorApplication.update` 한 번짜리 콜백이나 직접 호출을 쓴다.
- 머티리얼은 `ctx.LitMaterial()`/`ctx.Material()`로 만든다. `ShaderGUI.ValidateMaterial`을 불러 URP Lit의 키워드(`_NORMALMAP` 등 텍스처에 따른 것 포함)·
  태그·패스·레거시 프로퍼티를 맞추므로 첫 빌드와 이후 빌드가 같아진다. **이미션만은 키워드가 아니라 GI 플래그가 켠다**: `_EmissionColor` + `EnableKeyword("_EMISSION")`은
  검증이 끈다(`globalIlluminationFlags`가 기본 `EmissiveIsBlack`) → `LitMaterial`의 `Emission`, 또는 `m.globalIlluminationFlags = RealtimeEmissive`.
  `Material.SetFloat`은 셰이더에 없는 이름도 조용히 저장한다(`GetPropertyNames`에는 보이고 직렬화 목록엔 없음) → `ctx.Material`이 경고한다.
- 프로젝트는 짧은 경로(60자 이하)에 둔다. 길면 Windows 260자 제한으로 Unity 패키지 파일 로드가 실패한다. `%TEMP%` 아래도 피한다(Burst DLL 차단).
- 스카이박스 앰비언트는 원래 라이팅 베이크가 만든다(씬의 RenderSettings에는 앰비언트 프로브가 저장되지 않는다). Unity 6.0+의 공개 API
  `new LightingDataAsset(scene)` + `SetAmbientProbe` + `Lightmapping.SetLightingDataAssetForScene`으로 베이크 없이 넣을 수 있다 → `ctx.SkyAmbient`.
  `SphericalHarmonicsL2.Evaluate`의 기저는 정규화 상수 없는 {1, y, z, x, xy, yz, 3z²−1, xz, x²−y²}이고 균일 radiance c → 계수0 = c(Flat 앰비언트 c)다.
- 캡처 카메라는 메인 카메라 설정(후처리 포함)을 복사해 오프스크린 렌더한다. 메인 카메라가 없으면 캡처 실패.
- 에디터 플레이 모드 FPS는 에디터 오버헤드·autotick 영향을 받는다. 절대값이 아니라 **변경 전후 비교**용이다(`editorFocused` 확인). 실제 성능은
  `tools/player.ps1`(개발 빌드 플레이어, 샘플은 에디터의 ~3.8배).
- Code Optimization은 Debug(정확한 예외 줄 번호). Release면 throw 위치가 메서드 끝 줄로 보고되고, **절차적 메시·텍스처의 float 결과가 달라져
  build fingerprint도 바뀐다**(F-6: 당시 해시로 Debug `b012cf35…` / Release `6b977ecd…`; 지금 스모크 씬 Debug는 `609b54d2…`).
  `CompilationPipeline.codeOptimization`은 에디터 세션 동안만 유지돼서 재시작하면 Release(사용자 전역 "Code Optimization On Startup")로 돌아간다
  → `HarnessCodeOptimization`([InitializeOnLoad])이 도메인이 로드될 때마다 이 프로젝트만 Debug로 되돌린다(재컴파일 1회; 그래서 이 프로젝트에선
  Release가 유지되지 않는다). 전역 EditorPrefs는 다른 프로젝트에 영향을 주므로 건드리지 않는다. `open.ps1`은 명령줄 `-debugCodeOptimization`으로 열어서
  처음부터 Debug다(W7: 재시작 때의 그 재컴파일 + 리로드 ~10 s가 없다).
- fingerprint는 에셋의 하위 객체를 정렬해서 해시한다. `LoadAllAssetsAtPath`는 하위 객체를 로컬 fileID 순서로 주는데, 그 순서는 에셋의 이력에 따라
  다르다(새로 만든 URP `.mat`은 숨은 `AssetVersion`이 머티리얼 앞, 오래된 것은 뒤). 정렬 전에는 새 클론이 같은 코드로 다른 fingerprint를 냈다.
- 에디터를 막 열면 `unity status`가 ready여도 Pipeline 서버가 잠시 **503 Server Busy**를 준다. 루프 시작 ping은 연결 끊김·401과 함께
  busy도 에디터 프로세스가 살아 있는 동안 최대 120s 기다린다(`timings.editorWaitSec`, 재시작 직후 ~27s). `open.ps1`로 열면 이 대기가 open에서 끝난다.
- 에디터를 코드로 닫기는 `tools/quit.ps1`. Pipeline `quit`은 플레이어용이라 편집 모드에서 실패하고(`DontDestroyOnLoad`),
  `delayCall`은 포커스 없는 에디터에서 안 돈다 → `harness_quit`이 `EditorApplication.update`(Pipeline 디스패처가 도는 곳이라 백그라운드에서도 돈다)에서
  응답이 나간 뒤 `Exit(0)`을 부른다.
- `-logFile` 없이 연 에디터(`unity open`, Hub)는 모두 사용자 전역 `Editor.log`(Windows는 `%LOCALAPPDATA%\Unity\Editor\`) 하나에 쓰고,
  나중에 뜬 에디터는 파일 처음부터 **덮어쓴다**. 두 에디터가 각자의 위치에 번갈아 써서 로그가 뒤섞이고(O-7, 1.4GB까지 커진 적 있음),
  한 프로젝트의 로그만 따로 볼 수 없다 → 에디터는 `tools/open.ps1`로 연다(`<프로젝트>/Logs/Editor.log`).
- PowerShell에서 `uc.ps1`을 `powershell -File`로 부르면 JSON 인자의 따옴표가 벗겨진다 → `& ./tools/uc.ps1 ...`처럼 같은 프로세스에서 호출.
- PowerShell 5.1: `tools/*.ps1`은 ASCII만 쓴다(BOM 없는 UTF-8 비ASCII는 깨진다). 인자는 `uc.ps1`에 JSON 한 덩어리로.
- 직전 컴파일이 실패한 상태에서 Pipeline `recompile`은 컴파일 시작 전의 옛 실패를 보고할 수 있다. `Invoke-HarnessRecompile`(loop.ps1)은
  컴파일 세대 번호로 이를 피한다 — 직접 `recompile_status`만 믿지 말 것.
- 도메인 리로드 때 Pipeline 서버는 새 토큰으로 재시작한다. 그 사이 요청은 연결 끊김 또는 **401 Unauthorized**(`success` 필드 없음)를 받는다.
  `Invoke-UnityCommand`는 둘 다 `unreachable`로 돌려주고 폴러와 루프 시작 ping은 재시도한다. 락이 리로드 직후 다음 에이전트로
  넘어가는 submit 흐름에서 처음 드러났다(루프가 `stage=editor`로 실패, StrictMode 모듈이 `success` 접근에서 예외).
- `refs/stash`는 한 저장소의 **모든 worktree가 공유**한다. 에이전트가 자기 worktree에서 `git stash pop`을 하면 다른 worktree(에디터 트리)가
  방금 만든 stash를 가져갈 수 있다. land는 stash를 만들자마자 목록에서 빼 전용 ref에 둔다.
- PowerShell 5.1에서 git 출력은 `Invoke-HarnessGit`(Process, UTF-8, stderr 캡처, 종료코드)로 받는다. `&` 호출은 콘솔 코드페이지로 경로가 깨지고
  stderr가 에러 레코드가 된다. 자식 프로세스 stdin을 리다이렉트하면 .NET이 콘솔 인코딩의 BOM을 먼저 써 넣는다(`--stdin-paths` 첫 경로가 깨짐) → 인자로 넘긴다.
- `Harness.psm1`은 `Set-StrictMode -Version Latest`라 해시테이블의 **없는 키를 속성 문법**(`$h.text`)으로 읽으면 예외다(모듈 밖 스크립트에서는 `$null`) →
  있을 수도 없을 수도 있는 키는 `$h['text']`. `git status --porcelain` 경로는 저장소 루트 기준, `git ls-tree`·`hash-object`·`show <rev>:./x`는 `-C` 폴더 기준이다.
- `CompilationPipeline.GetAssemblies(AssembliesType.Editor)`를 도메인 리로드 뒤 처음 부르면 ~60 ms다(Player 목록과 따로 캐시) — lint의 계약 검사가 그것과 Builders
  어셈블리까지 훑어 리로드 직후 83–158 ms였다 → static-reset이 이미 받은 Player 목록으로 21–30 ms.

## 문제 해결

- `stage=editor`: 에디터가 없거나 응답 없음 → `tools/open.ps1`(열려 있으면 준비될 때까지 기다리기만 한다). `safeMode: true`면 시작할 때 컴파일되지 않은
  스크립트 때문에 Safe Mode다 → `compileErrors`(에디터 로그에서 읽음)를 고치고 `quit.ps1 -Force` → `open.ps1`(또는 `open.ps1 -Headless`: 그래도 뜬다).
  `open.ps1`이 띄우지 않은 에디터면 `Logs/Editor.log`나 사용자 전역 로그의 `error CS`를 본다.
  `open.ps1`의 `dialog`나 `editor_status`의 `blocked_by_dialog`는 모달 다이얼로그다 → 사람에게 닫아 달라고 한다(`-automated` 에디터는 `DisplayDialog`로
  막히지 않는다; 이용 약관처럼 에디터가 뜨기 전의 창은 여전히 사람 몫).
- worktree에서 `loop.ps1`이 `stage=editor`이고 `own`을 말하면: 그 worktree에 `Library/`가 있어 자기 에디터를 쓰는데 떠 있지 않다 → `open.ps1 -Own`
  (에디터 트리를 다시 쓰려면 `quit.ps1` 뒤 그 worktree의 `Library/`를 지운다).
- 플레이가 끝나지 않음: 시나리오 타임아웃(duration+70s) 후 자동 종료. 수동: `tools/uc.ps1 editor_stop`.
- 빌드 fingerprint가 매번 바뀜: 빌더가 비결정적(시드 없는 랜덤, 시간, Dictionary 순회 순서 등)이거나 Code Optimization이 Release다
  (`build.warnings`에 경고). 덤프가 바뀌면 직전 덤프가 `Library/Harness/fingerprint.prev.txt`로 남으니 `fingerprint.txt`와 diff한다.
- `stage=submit`: worktree에서 `loop.ps1`을 돌렸거나(→ `submit.ps1`), `-Module` 폴더가 없거나,
  다른 worktree가 그 모듈을 미병합 상태로 올려 두었거나(`submit.owner`), 에디터 트리 브랜치에 이 worktree에 없는 그 모듈 커밋이 있거나(→ `git merge master`),
  계약 규칙(위 "계약 폴더"): 올라간 타입을 바꿈(`submit.contractChanged` → 새 타입으로), 이미 있는 이름(`submit.contractConflicts` → 이름을 바꾼다),
  다른 worktree의 미병합 계약 파일(`submit.contractOwner`). `error`를 읽는다.
- `stage=compile`인데 에러가 남의 모듈(예: `Stage`)에 있고 `submit.phase=check`: 내 계약 추가가 그 모듈을 깨뜨렸다(CS0104 모호한 이름) → 내 타입 이름을 바꾼다.
- report에 `recoveredSubmit`: 이전 submit이 도중에 죽어 이번 실행이 되돌렸다. 그 에이전트는 다시 submit하면 된다.
  되돌리기가 실패하면 `Library/Harness/submit/pending.json`과 같은 폴더의 `<runId>/` 백업을 본다.
- `stage=land`: `error`와 사유 필드(`land.uncommitted/conflicts/missingMeta/owner/foreign/contractOwner/contractChanged/contractConflicts`)를 읽는다.
  이때 에디터 트리는 손대지 않은 상태다.
- report에 `recoveredLand`: 이전 land가 도중에 죽어 이번 실행이 병합을 되돌렸다. 그 에이전트는 다시 land하면 된다. 되돌리기가 실패하면
  저널이 `Library/Harness/land/failed-<runId>.json`으로 옮겨지고(`recoveredLand.error`), stash는 `refs/agentharness/land/<runId>`에 남는다
  (`git stash apply <sha>`로 직접 복원).
- 하네스 상태 파일: `Library/Harness/`(play_state.json, console.ndjson, compile.json, buildcache.json, fingerprint(.prev).txt,
  submit/(pending.json, owners.json, contracts.json), land/).
