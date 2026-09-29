# AgentHarness — Unity 6(6.0 LTS 이상) + URP 에이전트 하네스

이 문서만 읽고 바로 루프를 돌릴 수 있어야 한다. 게임은 아직 없다 — `Assets/Game/Stage`, `Assets/Game/Smoke`는
하네스를 검증하는 스모크 씬이다(절차적 지형 + 커스텀 HLSL + 라이트 + Volume 후처리 + 회전 오브젝트 + UI Toolkit HUD).
샘플 프로젝트는 Unity 6000.3.11f1로 고정돼 있고, 하네스(`Assets/Harness/`, `tools/`)는 Unity 6.0 LTS 이상에서 돈다(아래 "Unity 버전").

## 왜 이 하네스가 있나

에이전트가 Three.js로 만든 웹 3D 게임의 완성도가 높은 건 모델이 3D를 잘해서가 아니라 작업 환경 덕분이다.

| # | Three.js 환경의 성질 | Unity 기본 상태 | 이 하네스가 복원하는 방법 |
|---|---|---|---|
| 1 | 모든 게 텍스트(JS 코드) | 씬/프리팹이 GUID로 얽힌 YAML, GUI 중심 도구 | 씬은 `IBuildStep` 코드가 생성, HLSL·UXML/USS·코드로 만든 머티리얼/Volume/라이팅 |
| 2 | 수정→새로고침이 초 단위 | 컴파일 + 도메인 리로드 | Domain Reload 끔, 모듈별 asmdef, 빌드 캐시, 에디터 없는 컴파일 체크 |
| 3 | 스크린샷·콘솔·FPS를 눈/기계로 확인 | 에이전트가 화면을 못 봄 | `harness_capture/play`가 PNG + 이미지 통계, `harness_console/stats`가 JSON |
| 4 | 에셋 없이 절차적 생성 + 셰이더 + 후처리 | 에셋 임포트 중심 | `Harness.Procedural`(Mesh/Noise/Texture 베이크), URP Volume을 코드로 |
| 5 | 레지스트리 구조라 병렬 작업이 쉬움 | 에디터 하나를 공유 | `GameRoot.Register` + `EventBus`, 모듈 폴더 격리, 에디터 조작 뮤텍스, 에이전트별 git worktree + `submit.ps1`/`land.ps1` 트랜잭션 |

모든 설계 결정의 기준: **"Three.js 환경의 어떤 성질을 복원하는가"**. URP·물리·엔진 기능을 쓰니 결과는 그 이상을 노린다.

**아직 해결 안 된 격차는 [`docs/ROADMAP.md`](docs/ROADMAP.md)에 성질 1~5와 이식성(Unity 버전·기존 프로젝트·macOS)별로 기록돼 있다.** 하네스를 개선할 때는 거기서 항목을 고르고,
해결하면 체크 + 검증 방법·측정값을 남긴다. 하네스를 고친 뒤에는 ROADMAP의 "검증 매트릭스"를 다시 돌린다(1–8 = `tools/selftest.ps1`, 아래 "하네스 자기 검증").

## 빠른 시작 (새로고침 + 스크린샷 + 콘솔 = 한 방)

```powershell
powershell -ExecutionPolicy Bypass -File tools/open.ps1   # 에디터가 없으면 열고, 쓸 수 있을 때까지 기다린다(이미 열려 있으면 기다리기만)
powershell -ExecutionPolicy Bypass -File tools/loop.ps1
powershell -ExecutionPolicy Bypass -File tools/quit.ps1   # 끝낼 때: 락을 잡고 정상 종료, 프로세스가 끝날 때까지 대기
```

`open.ps1`은 에디터 로그를 `Logs/Editor.log`(직전 것은 `Editor-prev.log`)에 따로 쓰게 하고, 첫 응답 뒤 Debug 재컴파일까지 끝나
3초간 idle일 때 돌아온다(재시작 ~30s, 새 클론 첫 임포트는 수 분). `unity open`이나 Hub로 열면 `-logFile`이 없어 여러 에디터가
사용자 전역 `Editor.log` 하나를 서로 덮어쓴다(아래 "함정"). 실패하면 JSON의 `error`, `dialog`(모달 다이얼로그 — 사람이 답해야 함), `logTail`을 본다.
Pipeline 서버가 뜨기 전의 다이얼로그(새로 설치한 버전의 이용 약관, Safe Mode, 패키지 에러)는 로그가 60 s 멈추고 에디터 창 제목이
진행 창이 아니면 `dialog.title`로 보고한다. 에디터는 그대로 두니 사람이 답한 뒤 `open.ps1`을 다시 부르면 그 에디터를 기다린다
(`open.ps1`이 띄운 pid는 `Logs/harness-editor.json`에 남아서, 락 파일이 생기기 전에도 두 번째 에디터를 띄우지 않는다).
다른 설치 버전으로 열기: `open.ps1 -UnityVersion <버전>`(아래 "Unity 버전").

`tools/loop.ps1` = recompile → (C# 컴파일 에러면 즉시 중단) → lint → `harness_build` → `harness_shaders` → `harness_play`(기본 3컷)
→ `harness_console` + `harness_stats` → `HarnessOut/latest/report.json` (stdout에도 같은 JSON). 종료코드 0 = 전부 녹색.

**매 루프 후 반드시**: `report.json`의 `ok/stage`를 보고, `shots`의 PNG를 **Read 툴로 직접 열어** 눈으로 확인한다.
`shotStats[].blank=true`(평평/검은 화면)면 렌더가 깨진 것이다. `dark=true`(픽셀 98% 이상이 거의 검정)는 실패로 치지 않지만
조명이 빠진 화면일 가능성이 크다 — PNG를 열어 본다.

옵션: `-Scenario tools/scenarios/x.json`, `-Out HarnessOut/x`, `-NoPlay`(편집 모드 캡처만), `-NoCompile`.
**여러 에이전트가 동시에 작업하면** 이 폴더를 직접 고치지 말고 각자 worktree에서 `tools/submit.ps1`을 쓰고, 끝나면 커밋해서
`tools/land.ps1`로 병합한다(아래 "병렬 에이전트").

### report.json

```jsonc
{
  "ok": true, "stage": "done",            // 실패 시 stage = editor|compile|build|shader|play|runtime|lint|shots|submit|land
  "compileErrors": [{"file","line","msg","module"}],       // C# 에러, 또는 kind:"shader" (HLSL 에러, 상태 기반)
  "runtimeErrors": [{"type","msg","file","line","module","count","stack"}],   // count = 같은 에러 폴딩 수
  "editorErrors": [{"type","msg","count","stack"}],   // Unity/패키지 내부 에러(Assets/ 흔적 없음). 실패 사유는 아니지만 읽어볼 것
  "fps": {"avg","min","p95ms","p99ms","hitches","cpuMainAvgMs","samples","editorFocused"},
  "shots": ["C:/.../HarnessOut/latest/shot0_closeup.png", ...],
  "durationSec": 3.5, "unityVersion": "6000.3.11f1",   // 루프를 돌린 에디터 버전
  "timings": {"lockWaitSec","editorWaitSec","compileSec","buildSec","playSec","collectSec"},   // editorWaitSec: 시작 시 리로드·busy 대기(있을 때만)
  "build": {"fingerprint","steps":[{"type","module","ms","error","file","line"}], ...},
  "play": {"success","probeReady","frames","gameSec","modules","failedModules","inputEventsApplied",
           "events":[{"name":"SpinnerLap","count":2}]},     // EventBus 발행 횟수 → 게임플레이를 기계적으로 검증
  "render": {"batches","setPassCalls","drawCalls","triangles","vertices"},
  "shotStats": [{"name","preset","t","meanLuma","stdLuma","blank","dark","error"}],
  "lint": [{"rule","module","file","message"}], "warningCount": 0,
  "submit": {"phase","synced","kept","reverted","written","deleted","contractsAdded","metaWrittenBack",   // submit.ps1만.
             "errorModules","restore","check","owner","takeover"},   // timings에 checkSec/syncSec/restoreSec 추가
  "land": {"branch","into","phase","head":{"before","after"},"merged","fastForward","kept","reverted",  // land.ps1만.
           "modules","files","stash":{"sha","paths","dropped"},"releasedOwners","errorModules","undo","restore",
           "conflicts","missingMeta","owner","foreign","uncommitted","note","warning"},   // 거부 사유는 해당 필드에. timings에 checkSec/mergeSec/restoreSec
  "recoveredSubmit": {"runId","workRoot","modules","files"},     // 도중에 죽은 submit을 이번 실행이 되돌렸을 때만
  "recoveredLand": {"runId","branch","steps"}                    // 도중에 죽은 land를 이번 실행이 되돌렸을 때만
}
```

측정된 한 바퀴 시간(이 머신): 코드 변경 없음 **~3.5s**, 셰이더만 수정 **~4s**(도메인 리로드 없음), 모듈 C# 1줄 수정 **~9s**
(컴파일+도메인 리로드 ~4s, 빌드 ~2s(리로드 직후 JIT), 플레이 ~2.8s), 컴파일 에러 보고 **~1s**.

## 규칙 (반드시 지킬 것)

1. **`.unity` / `.prefab` / `.asset` YAML 직접 수정 금지.** 씬은 `Assets/Game/<Module>/Builders/`의 `IBuildStep`이 만든다.
   `Assets/Scenes/Main.unity`와 `Assets/Generated/`는 빌드 산출물이며 gitignore 되어 있다(고쳐도 다음 빌드에 덮어써진다).
   프로젝트 설정도 YAML이 아니라 에디터 API(`harness_setup` 등)로 바꾼다.
2. **텍스트로 쓸 수 있는 형태만.** 셰이더 = 손으로 쓴 HLSL `.shader`(Shader Graph 금지), UI = UI Toolkit UXML/USS(uGUI 프리팹 금지),
   머티리얼·파티클·Volume·라이팅·PanelSettings = 빌더 코드(`BuildContext`)로 생성. Animator/Timeline 같은 GUI 에셋이 필요하면 코드로 생성한다.
3. **자기 모듈 폴더 밖 수정 금지.** 작업 범위는 `Assets/Game/<Module>/` 하나. 모듈 간 공유 이벤트 타입만
   `Assets/Game/Contracts/`에 **추가**(기존 타입 수정 금지). `Assets/Harness/`, `tools/`는 하네스 작업일 때만 고친다.
4. **에디터 조작은 한 번에 하나씩.** 에디터는 하나다. `tools/loop.ps1`과 `tools/uc.ps1`은 프로젝트별 시스템 뮤텍스를 잡으므로
   병렬 에이전트는 자동으로 줄을 선다(`timings.lockWaitSec`). recompile/build/play/capture를 `unity command`로 직접 호출해
   락을 우회하지 말 것. **병렬 에이전트는 각자 git worktree에서 코드를 쓰고 `tools/submit.ps1 -Module <M>`으로 에디터에 넣고,
   커밋한 뒤 `tools/land.ps1`로 병합한다** (아래 "병렬 에이전트"). 여럿이 에디터 트리를 직접 고치면 한 명의 컴파일 에러가 모두의 루프를 막는다.
   에디터 트리에서 `git merge`/`git stash`를 직접 하지 말 것 — land가 락 안에서 한다.
5. **Domain Reload가 꺼져 있다.** 플레이 사이에 static이 유지된다. 가변 static(필드·자동 프로퍼티·이벤트, static readonly 컬렉션 포함)이 있는
   타입은 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics()`에서 초기화한다.
   `harness_lint`가 검사하며 위반 시 루프는 `stage=lint`로 실패한다.
6. **결정성.** 빌더는 `ctx.Seed(...)` / `Noise.Rng`만 쓴다(`UnityEngine.Random`·시드 없는 `System.Random` 금지). 같은 코드면 `build.fingerprint`가 같아야 한다.
   시나리오는 기본 `fixedDeltaTime`(Time.captureDeltaTime)으로 돌아서 `t=1.5`의 화면·이벤트 수가 매번 같다.
7. 런타임 오브젝트에 `HideFlags.DontSave` 금지 — 플레이 모드가 끝나도 살아남아 다음 플레이에서 중복 Tick 된다(실제로 겪은 버그).

## 폴더 구조

```
Assets/Harness/Runtime/        런타임 계약: GameRoot, IGameModule, EventBus, HarnessProbe, ShotPreset, ScriptedInput,
                               ScenarioRunner, HarnessCapture   (asmdef Harness.Runtime)
Assets/Harness/Runtime/Procedural/  MeshBuilder, Noise(Perlin/fBm/Ridged/Worley/Rng), TextureBaker, PMath
Assets/Harness/Editor/         [CliCommand] harness_* 와 BuildContext/IBuildStep     (asmdef Harness.Editor, Editor 전용)
Assets/Harness/UI/             DefaultRuntimeTheme.tss (UI Toolkit 기본 테마, 텍스트)
Assets/Game/Contracts/         모듈 간 이벤트 타입 (Game.Contracts, 추가만)
Assets/Game/<Module>/          런타임 코드 (Game.<Module>.asmdef) + Shaders/*.shader + UI/*.uxml|uss
Assets/Game/<Module>/Builders/ IBuildStep 구현 (Game.<Module>.Builders.asmdef, Editor 전용)
Assets/Generated/, Assets/Scenes/Main.unity   빌드 산출물 (gitignore, 직접 수정 금지)
tools/loop.ps1                 원커맨드 루프          tools/uc.ps1          커맨드 1개 호출(JSON 인자)
tools/submit.ps1               worktree의 모듈 → 에디터 트리, 트랜잭션 루프(실패 시 되돌림)
tools/land.ps1                 worktree 브랜치 → 에디터 트리 브랜치로 병합, 트랜잭션 루프(실패 시 되돌림)
tools/compile-check.ps1        에디터 없는 컴파일 검사  tools/Harness.psm1    HTTP 클라이언트·락·루프·submit/land 저널·git
tools/open.ps1 / quit.ps1      에디터 열기(프로젝트별 로그, 준비 대기) / 정상 종료(락)
tools/fresh-clone-test.ps1     새 클론 검증: 짧은 경로에 클론 → open → harness_setup → 루프 N회 → quit → 삭제
tools/selftest.ps1             검증 매트릭스 1–8 자동 실행(에러 주입·동시 루프·worktree submit/land)
tools/scenarios/*.json         플레이 시나리오        HarnessOut/           캡처·result.json·report.json (gitignore)
AgentScripts/                  eval_file / run_script 용 임시 C# (gitignore)
```

## 새 모듈 만들기 (파일 충돌 없이 병렬 작업)

`Assets/Game/<Name>/` 아래에만 파일을 만든다.

`Game.<Name>.asmdef`:
```json
{ "name": "Game.<Name>", "rootNamespace": "Game.<Name>",
  "references": ["Harness.Runtime", "Game.Contracts", "Unity.InputSystem"], "autoReferenced": false }
```
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
    public void Tick(float dt) { }        // 매 프레임 (예외는 GameRoot가 잡아 로그 → report.runtimeErrors)
    public void Dispose() { m_Sub?.Dispose(); }
}
```
모든 모듈 Init이 끝나면 `HarnessProbe.Ready = true` — 시나리오 시계는 이때 0이다. 모듈끼리는 **EventBus로만** 통신한다
(`EventBus.Publish(new X(...))`, 공유 이벤트 struct는 `Assets/Game/Contracts/`).

빌드 스텝:
```csharp
public sealed class FooBuildStep : IBuildStep
{
    public int Order => 100;   // 0-99 환경(카메라/라이트/하늘/후처리) · 100-899 콘텐츠 · 900+ 마무리
    public void Build(BuildContext ctx)
    {
        var mesh = ctx.SaveMesh(MeshBuilder.Sphere(0.5f).ToMesh("Ball"), "Ball");       // Assets/Generated/Foo/Ball.asset
        var mat  = ctx.Material("BallMat", "Game/Foo/MyShader", m => m.SetColor("_BaseColor", Color.red));
        var go   = ctx.MeshObject("Props/Ball", mesh, mat);                              // 씬: Foo/Props/Ball
        var tex  = ctx.SaveTexture(TextureBaker.Bake(256, 256, (u, v) => Color.white), "BallTex"); // PNG (Read 가능)
        ctx.VolumeProfile("Post", p => p.Add<Bloom>(true).intensity.value = 1f);         // Volume 오버라이드
        ctx.UIDocument("HUD", "Assets/Game/Foo/UI/Hud.uxml");                            // UI Toolkit
        ctx.Shot("foo_close", new Vector3(0, 2, -4), Vector3.zero, 45f);                // 캡처 프리셋
        if (!ctx.CacheHit("bake", new[] { "Big.png" }, someParam)) { /* 무거운 베이크 후 저장 */ }
    }
}
```
`BuildContext`는 산출물을 제자리 덮어쓰기(GUID 유지)하고, 이번 빌드에서 아무도 만들지 않은 `Assets/Generated` 에셋은 지운다.
`ctx.CacheHit`: 스텝 어셈블리·Harness.Runtime 코드와 입력이 같으면 재생성을 건너뛴다(`harness_build {"no_cache":true}`로 무시).
환경 헬퍼: `ctx.BakeSkyReflection()`(스카이박스→큐브맵 반사, 베이크 불필요), `ctx.Create(path, types)`, `ctx.Root()`, `ctx.Seed(salt)`.

## 에디터 커맨드 (모두 JSON 반환)

호출: `tools/uc.ps1 <command> '<JSON 인자>'` (권장: 빠르고 PowerShell 인용 문제 없음, 락 적용)
또는 `unity command <command> --arg value --format json`.

| 커맨드 | 하는 일 |
|---|---|
| `harness_build` | Builders의 IBuildStep을 Order 순으로 빈 씬에 실행 → `Main.unity` 저장. `{ok, fingerprint, steps[], cacheHits, deletedAssets}`. `dry_run`, `no_cache` |
| `harness_capture` | `{"preset":"all"\|"<name>"\|"main","out":"HarnessOut/capture"}` 편집 모드 오프스크린 1280x720 PNG + `meanLuma/stdLuma/blank` |
| `harness_play` | `{"scenario":"tools/scenarios/default.json"\|"{...inline}","out":"HarnessOut/play"}` 즉시 반환 → `harness_play_status` 폴링 |
| `harness_play_status` | `entering\|running\|exiting\|done\|failed` + 끝나면 `result`(result.json) |
| `harness_console` | `{"since":<mark>}` 최신 컴파일 에러(file,line,msg,module) + mark 이후 런타임 에러/경고 수. 응답의 `mark`를 다음에 넘긴다 |
| `harness_stats` | 플레이 중이면 live, 아니면 마지막 결과: fps avg/min/p95ms, batches, SetPass, tris |
| `harness_lint` | static-reset / module-asmdef / module-boundary 규칙 검사 |
| `harness_shaders` | Assets/ 셰이더의 현재 컴파일 에러(file, line, msg, module). 셰이더 에러는 로그가 아니라 상태라 매 루프 조회 |
| `harness_ping` | domainReloads, isCompiling, isPlaying, compileFailed, mark, unityVersion |
| `harness_setup` | 프로젝트 설정 멱등 적용(Domain Reload off, runInBackground, Debug 코드 최적화, 템플릿 샘플 삭제) |
| `harness_sync_csproj` | .sln/.csproj 생성(사용자 외부 에디터 설정은 복원) — compile-check msbuild 백엔드용 |
| `harness_quit` | 응답 ~0.3s 뒤 `EditorApplication.Exit(0)`(저장 확인 없음). 직접 부르지 말고 `tools/quit.ps1`(락 + 종료 대기) |

Pipeline 패키지 기본 커맨드도 쓸 수 있다: `recompile`/`recompile_status`, `eval_file {"file":"AgentScripts/x.cs"}`(C# 본문, using 불가 → 정규화된 이름 사용),
`run_script`, `get_scene_hierarchy`, `editor_status`(모달 다이얼로그 확인), `editor_stop`, `set_autotick`. 목록: `unity command --detail compact`.

## 시나리오 (tools/scenarios/*.json)

```jsonc
{ "name": "default", "durationSec": 3.0, "fixedDeltaTime": 0.0166667, "warmupSec": 0.25, "readyTimeoutSec": 10,
  "events": [ { "t": 1.0, "type": "keyTap", "key": "Space", "hold": 0.1 } ],
  "captures": [ { "t": 0.5, "preset": "auto" }, { "t": 1.5, "preset": "auto" }, { "t": 2.5, "preset": "auto" } ] }
```
- `t`는 HarnessProbe.Ready 이후 게임 시간(초). 입력은 Input System 가상 디바이스(`ScriptedInput`)로 들어가므로 InputAction·`Keyboard.current` 그대로 동작.
- 이벤트: `keyDown/keyUp/keyTap`(Key 이름) · `mouseMove/mousePos/mouseDown/mouseUp/scroll`(`x`,`y`, `key`=Left/Right) · `stick`(`key`=left/right, `x`,`y`) · `padDown/padUp`(GamepadButton) · `releaseAll`.
- 캡처 `preset`: ShotPreset 이름 · `"auto"`(이름순 다음 프리셋) · `"main"`(메인 카메라 그대로) · `"screen"`(Game 뷰 그대로 = **UI 오버레이 포함**, 해상도는 Game 뷰 크기, Game 뷰 탭이 보여야 함).
  나머지는 오프스크린 렌더라 **스크린 공간 UI가 안 찍힌다**. HUD 확인은 `"screen"`을 쓴다(`tools/scenarios/screen.json`).
- 새 게임플레이를 넣으면 `default.json`의 입력/캡처와 기대 이벤트 수를 같이 갱신한다.

## 병렬 에이전트: worktree + submit + land (남의 컴파일 에러에 막히지 않기)

에디터는 이 폴더(에디터 트리) 하나에 묶여 있다. 여러 에이전트가 여기서 직접 코드를 고치면 한 명의 쓰다 만 코드가
Unity 도메인 리로드를 막아 **모두의 루프가 `stage=compile`로 멈춘다.** 그래서 병렬 작업은 에이전트별 worktree에서 한다:

```powershell
# 1회: 에디터가 연 체크아웃에서 에이전트별 worktree를 만든다 (Library/가 없으니 에디터도 임포트도 필요 없다)
git worktree add ..\wt-foo -b agent/foo
cd ..\wt-foo\AgentHarness                                                        # 이후 편집·명령은 모두 여기서
powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Foo    # 에디터 없이 ~0.5s, 동시 실행 OK
powershell -ExecutionPolicy Bypass -File tools/submit.ps1 -Module Foo          # 에디터 트리에서 루프(트랜잭션)
git add -A; git commit -m "Foo: ..."                                             # submit이 되복사한 .meta까지
powershell -ExecutionPolicy Bypass -File tools/land.ps1                         # 이 브랜치를 에디터 트리 브랜치에 병합(트랜잭션)
```

`submit.ps1` = ① worktree 소스로 compile-check(락 없음; 실패면 아무것도 복사하지 않고 `stage=compile`, `submit.phase=check`)
→ ② 에디터 락 → 덮어쓰거나 지울 파일을 백업하고 저널을 남긴 뒤 `Assets/Game/<Module>/`를 에디터 트리로 미러링,
`Assets/Game/Contracts/`는 **새 파일만** 추가 → ③ 에디터 트리에서 평소 루프 → ④ 녹색이면 유지하고 Unity가 만든 `.meta`를
worktree로 되복사(**커밋할 것**), 아니면 **백업으로 되돌리고 재컴파일**해 에디터 트리를 submit 전 상태로 돌려놓는다.
결과는 loop와 같은 report.json + `submit` 필드(worktree의 `HarnessOut/submit/`). 종료코드 0 = 녹색이고 반영됨.

- 컴파일 실패는 항상 되돌린다. 런타임/린트/셰이더/샷 실패도 기본은 되돌린다. `-KeepOnFail`은 유지 — `submit.errorModules`가
  남의 모듈일 때(에디터 트리가 이미 빨간 상태)만 쓴다.
- submit이 도중에 죽어도(타임아웃·kill) 다음에 락을 잡는 loop/uc/submit이 저널(`Library/Harness/submit/pending.json`)로
  되돌린다 → 그 report에 `recoveredSubmit`.
- worktree에서 `loop.ps1`은 거부된다(`stage=submit`): 에디터가 컴파일하는 건 worktree가 아니라 에디터 트리다.
  `uc.ps1`은 worktree에서도 에디터 트리의 에디터에 붙는다(JSON 안의 상대 경로는 에디터 트리 기준).
- 시나리오는 worktree의 파일을 절대 경로로 넘기므로 worktree에서 고친 `tools/scenarios/*.json`이 그대로 쓰인다.
- **한 모듈 = 한 에이전트**(강제): submit은 모듈별로 마지막에 반영한 worktree를 `Library/Harness/submit/owners.json`에 기록한다.
  다른 살아 있는 worktree가 올린 미병합 변경이 에디터 트리에 남아 있는 모듈은 `stage=submit`으로 거부된다(`submit.owner`).
  그 작업이 버려졌을 때만 `-Takeover`. land가 그 브랜치의 모듈 소유를 해제한다.
- 에디터 트리 브랜치에 그 모듈을 건드린 커밋이 있는데 worktree에 없으면 거부된다(미러링하면 병합된 작업을 되돌리게 된다) → `git merge master`.
- 모듈 삭제·`Assets/Harness`·`tools/`는 submit 대상이 아니다(하네스 작업은 에디터 트리에서 직접). land로는 병합된다.
- 에디터 트리 찾기: `Library/`가 없는 체크아웃이면 `git worktree list`의 메인 worktree에서 같은 하위 경로.
  git worktree가 아닌 복사본이면 `$env:AGENTHARNESS_EDITOR_ROOT`에 에디터 트리 경로를 준다.

### land.ps1 (병합)

submit한 파일은 에디터 트리에 미커밋 사본으로 남아 그냥 `git merge agent/foo`는 "would be overwritten"으로 거부된다. `land.ps1`이 락 안에서 처리한다.
worktree에서 인자 없이 돌리면 그 worktree의 브랜치, 에디터 트리에서는 `-Branch agent/foo`. 결과는 `HarnessOut/land/report.json`. 종료코드 0 = 병합되고 녹색.

1. (락 없음) 브랜치를 체크아웃한 worktree에 미커밋 파일이 있으면 거부(`land.uncommitted`) — 커밋된 것만 병합된다. **submit이 되복사한 `.meta`도 커밋할 것.**
2. (락) 아무것도 건드리기 전에 거부(`stage=land`): 에디터 트리가 detached/병합·리베이스 중/staged 변경 있음 ·
   `git merge-tree`로 미리 병합해 충돌(`land.conflicts` → worktree에서 `git merge master`, 해결, 커밋, submit, 다시 land) ·
   브랜치가 `Assets/`에 추가하는 파일·폴더의 `.meta`가 커밋 안 됨(`land.missingMeta`) · 건드리는 모듈에 다른 worktree의 미병합 submit(`land.owner`, `-Takeover`) ·
   덮어쓸 미커밋 변경이 이 브랜치의 submit 사본(또는 같은 내용)이 아님(`land.foreign`, 예: 에디터 트리를 직접 고친 것 — 사람이 커밋·stash).
3. 저널(`Library/Harness/land/pending.json`) → 병합이 건드리는 경로와 그 모듈의 미커밋 사본만 `git stash`
   (stash 목록은 모든 worktree가 공유하므로 바로 `refs/agentharness/land/<runId>`로 옮긴다) → `git merge`.
4. 에디터 트리에서 평소 루프. 녹색이면 병합 유지 + stash 버림 + 소유 해제(`land.releasedOwners`). 빨가면 `git reset --keep`으로 병합 전 커밋
   (병합한 경로만; 다른 에이전트의 미커밋 submit은 그대로) → stash 복원 → 재컴파일(`land.undo`, `land.restore`). `-KeepOnFail`은 submit과 같다.

- land가 도중에 죽어도 다음에 락을 잡는 loop/uc/submit/land가 저널로 되돌린다 → report에 `recoveredLand`.
- 이미 병합된 브랜치는 아무것도 안 하고 녹색(`land.note`). submit했지만 브랜치에 없는 변경이 남은 모듈은 소유를 유지하고 `land.warning`.
- git 병합이라 브랜치의 중간 커밋도 이력에 들어간다. 루프가 검증하는 건 병합 결과다.

## 에디터 없이 컴파일 체크

```powershell
powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Smoke                   # csc(기본) ~0.1-0.3s/어셈블리
powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Smoke -Backend msbuild  # 웜 ~0.5-2s, 콜드 수십 초
```
- 디스크의 소스를 다시 glob 하므로 방금 만든 파일도 포함되고, 실행마다 전용 임시 폴더라 동시 실행에 안전하다. `-Module A,B` 가능.
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
- 한계: 검사 집합 밖(다른 모듈, `-IncludeHarness` 없는 Harness)은 **에디터가 마지막으로 컴파일한 DLL** 기준이다.
  최종 판정은 항상 `loop.ps1` / `submit.ps1`.

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
  report의 `selftest`(`stage=selftest`). worktree는 클론 옆(`ah-fresh-st-a/-b`)에 생겼다가 지워진다. 전체 ~4분.
- 언제: `tools/`, `ProjectSettings/`, `Packages/`, `.gitignore`, 에디터 시작 경로(`[InitializeOnLoad]`)를 바꿨을 때와 공개 전.

## 하네스 자기 검증 (tools/selftest.ps1)

```powershell
powershell -ExecutionPolicy Bypass -File tools/selftest.ps1                                         # 매트릭스 1–8, ~3.5분
powershell -ExecutionPolicy Bypass -File tools/selftest.ps1 -Only 1,2,3,4,5,6 -ExpectFingerprint 5887385e
powershell -ExecutionPolicy Bypass -File tools/fresh-clone-test.ps1 -UnityVersion 6000.0.84f1 -SelfTest   # 9 + 다른 버전
```
- 1 루프 3회(녹색, fingerprint·events 동일, 샷 blank/dark 없음) + `compile-check -IncludeHarness` · 2 C# 컴파일 에러 · 3 런타임 예외 ·
  4 HLSL 에러(재임포트 없는 다음 루프에서도) · 5 리셋 없는 static(lint) · 6 루프 2개 동시 · 7 worktree submit(게이트 거부, 강제 submit 되돌림 +
  다른 worktree의 새 모듈·계약은 락 대기 후 유지, 런타임 에러 되돌림, sync 직후 kill → `recoveredSubmit`, 계약 수정 거부) ·
  8 land(fast-forward + 그 사이 submit 락 대기, 이미 병합됨, 미커밋·`.meta` 누락·충돌·에디터 트리 직접 수정 거부, 컴파일 에러 되돌림,
  병합 직후 kill → `recoveredLand`). 에러는 **주입한 줄 그대로**(file/line/module) 보고돼야 녹색이다.
- 주입 위치는 샘플 모듈의 표식 줄: `SmokeModule.cs`의 `m_Time += dt;`(컴파일, 61행)·`EventBus.Publish(new SpinnerLap(laps));`(런타임, 68행),
  `SmokeIridescent.shader`의 `Frag` 첫 줄(87행). 이 줄들을 바꾸면 `selftest.ps1`의 표식도 바꾼다.
- 7–8은 커밋된 `tools/`·`Assets/Harness/`·`Assets/Game/`을 쓰는 worktree 두 개를 저장소 옆(`<저장소>-st-a/-b`)에 만들고, `selftest/*` 브랜치·
  테스트 커밋(land의 병합 포함)을 만든 뒤 에디터 트리 브랜치를 시작 커밋으로 되돌린다(detached HEAD면 임시 브랜치를 썼다가 되돌린다).
  → 하네스를 고친 중이면 **임시 커밋 후** 돌린다. 도중에 에디터 트리 파일을 고치지 말 것(`git status`를 비교한다).
- 첫 빨간 항목에서 멈추고, 바꾼 파일·worktree·브랜치·커밋을 되돌린 뒤 마지막 루프(`final`)로 녹색과 `git status` 원상을 확인한다.
- 결과: `HarnessOut/selftest/report.json`(`items[].checks[]`, `lines`, `fingerprint`, `shotStats`, `final`), 단계별 report는
  `HarnessOut/selftest/<항목>-<단계>/`. PNG 눈 확인(`shots`)은 여전히 사람·에이전트 몫이다.

## Unity 버전

- 지원: **Unity 6.0 LTS 이상**. 하한은 에디터 연결(`com.unity.pipeline` 0.8.0-exp.1)이 `"unity": "6000.0"`이라서다(2022.3 이하 불가).
- 샘플 프로젝트(이 저장소)는 `ProjectVersion.txt`의 **6000.3.11f1**. 다른 설치 버전으로는 `tools/open.ps1 -UnityVersion <버전>`
  (`ProjectVersion.txt`를 그 버전으로 바꿔 "다른 버전으로 열기" 모달을 건너뛴다 → `git status`에 보인다).
- 검증한 버전(2026-09-29, `fresh-clone-test.ps1 -UnityVersion <v> -SelfTest`):

  | 버전 | URP(내장) | build.fingerprint | 줄(컴파일/런타임/셰이더) | 매트릭스 |
  |---|---|---|---|---|
  | 6000.0.84f1 (6.0 LTS) | 17.0.4 | `882e811b…` | 61 / 68 / 87 | 1–9 녹색, 샷은 6.3과 같은 밝기 |
  | 6000.3.11f1 (6.3 LTS, 샘플) | 17.3.0 | `5887385e…` | 61 / 68 / 87 | 1–9 녹색 |
  | 6000.6.3f1 (최신 정식) | 17.6.0 | `4a3c5c8c…` | 61 / 68 / 87 | 2–8 녹색, **1 빨강**: 첫 플레이 뒤 조명이 검다(`dark`, ROADMAP P-4) |

  fingerprint는 버전마다 다르다(URP가 만드는 머티리얼·에셋 직렬화가 다르다). 같은 버전 안에서만 매번 같아야 한다.
- 다른 버전으로 열면 Unity가 다시 쓰는 파일(커밋하지 않는다): `Packages/packages-lock.json`, `Assets/Settings/*RPAsset.asset`·
  `UniversalRenderPipelineGlobalSettings.asset`, `ProjectSettings/*`(버전별 새 필드). URP·Core 같은 **코어 패키지는 manifest의 버전(17.3.0)과
  상관없이 에디터 내장 버전으로 해석된다**.
- `Assets/Harness/`·`tools/`에는 버전 문자열을 쓰지 않는다. 에디터·컴파일러 경로는 실행 중인 에디터 프로세스 → `unity editors --installed`에서 얻고,
  API 차이는 `Assets/Harness/Runtime/UnityCompat.cs` 한 곳에서 `#if UNITY_6000_4_OR_NEWER`처럼 가른다(예: 6.4부터 `FindObjectsSortMode` obsolete).
  모듈 코드도 버전을 타는 API는 `UnityCompat`을 쓰거나 같은 방식으로 가른다.

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
  URP가 모든 RP 에셋을 다시 써서 **에디터 종료 때** 저장한다 → 새 클론의 `git status`가 더러워졌다. 에디터 API(SerializedObject + SaveAssetIfDirty)로
  그 버전이 쓰는 모양 그대로 저장해 커밋했다.
- Game 뷰 크기 목록(`PlayModeWindow.SetCustomRenderingResolution`이 여기에 추가한다)과 에디터 기본 레이아웃은 **사용자 전역**이다
  (`%APPDATA%\Unity\Editor-5.x\Preferences\GameViewSizes.asset`, `Layouts\current\default-6000.dwlt`). 하네스·실험 코드에서 바꾸지 않는다.
- 에이전트의 Bash 도구(Git Bash)로 넘긴 명령은 작은따옴표·`<<'EOF'` 안에서도 `\\`가 `\`로 줄어든다(확인: `r"a\\b"`가 3글자).
  heredoc Python으로 `.ps1`을 고치다 정규식·경로가 조용히 깨진 적 있다 → 백슬래시가 든 편집은 Edit 도구로 한다.

- `Mathf.SmoothStep(from, to, t)`는 GLSL `smoothstep`이 **아니다**(값 보간). `PMath.Smoothstep(e0, e1, x)`를 써라. 지형이 전부 눈으로 나온 원인.
- `UnityEngine.Object`에 `?.` 금지(에디터의 fake null). `TryGetComponent`를 쓴다.
- 에디터 명령/빌더에서 `EditorApplication.delayCall` 금지 — 포커스 없는 에디터에선 실행되지 않는다(`harness_play`가 73s 멈췄던 원인).
  `EditorApplication.update` 한 번짜리 콜백이나 직접 호출을 쓴다.
- 머티리얼은 `ctx.Material()`로 만든다. `ShaderGUI.ValidateMaterial`을 불러 URP Lit의 태그·패스·레거시 프로퍼티를 맞추므로
  첫 빌드와 이후 빌드가 같아진다. 그래도 텍스처에 따른 키워드(`_NORMALMAP` 등)는 직접 켠다.
- 프로젝트는 짧은 경로(60자 이하)에 둔다. 길면 Windows 260자 제한으로 Unity 패키지 파일 로드가 실패한다. `%TEMP%` 아래도 피한다(Burst DLL 차단). 스카이박스 앰비언트는 라이팅 베이크가 필요해서 Trilight + `ctx.BakeSkyReflection()`을 쓴다.
- 캡처 카메라는 메인 카메라 설정(후처리 포함)을 복사해 오프스크린 렌더한다. 메인 카메라가 없으면 캡처 실패.
- 에디터 플레이 모드 FPS는 에디터 오버헤드·autotick 영향을 받는다. 절대값이 아니라 **변경 전후 비교**용이다(`editorFocused` 확인).
- Code Optimization은 Debug(정확한 예외 줄 번호). Release면 throw 위치가 메서드 끝 줄로 보고되고, **절차적 메시·텍스처의 float 결과가 달라져
  build fingerprint도 바뀐다**(F-6: 당시 해시로 Debug `b012cf35…` / Release `6b977ecd…`; 지금 스모크 씬 Debug는 `5887385e…`).
  `CompilationPipeline.codeOptimization`은 에디터 세션 동안만 유지돼서 재시작하면 Release(사용자 전역 "Code Optimization On Startup")로 돌아간다
  → `HarnessCodeOptimization`([InitializeOnLoad])이 도메인이 로드될 때마다 이 프로젝트만 Debug로 되돌린다(재컴파일 1회; 그래서 이 프로젝트에선
  Release가 유지되지 않는다). 전역 EditorPrefs는 다른 프로젝트에 영향을 주므로 건드리지 않는다.
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

## 문제 해결

- `stage=editor`: 에디터가 없거나 응답 없음 → `tools/open.ps1`(열려 있으면 준비될 때까지 기다리기만 한다). 열려 있는데 연결이 안 되면
  Safe Mode(시작 시 컴파일 에러)일 수 있다 → `unity pipeline list`, `Logs/Editor.log`에서 `error CS` 확인 후 소스 수정 → `quit.ps1 -Force` → `open.ps1`.
  `open.ps1`의 `dialog`나 `editor_status`의 `blocked_by_dialog`는 모달 다이얼로그다 → 사람에게 닫아 달라고 한다.
- 플레이가 끝나지 않음: 시나리오 타임아웃(duration+70s) 후 자동 종료. 수동: `tools/uc.ps1 editor_stop`.
- 빌드 fingerprint가 매번 바뀜: 빌더가 비결정적(시드 없는 랜덤, 시간, Dictionary 순회 순서 등)이거나 Code Optimization이 Release다
  (`build.warnings`에 경고). 덤프가 바뀌면 직전 덤프가 `Library/Harness/fingerprint.prev.txt`로 남으니 `fingerprint.txt`와 diff한다.
- `stage=submit`: worktree에서 `loop.ps1`을 돌렸거나(→ `submit.ps1`), 기존 Contracts 파일을 고쳤거나(추가만 허용), `-Module` 폴더가 없거나,
  다른 worktree가 그 모듈을 미병합 상태로 올려 두었거나(`submit.owner`), 에디터 트리 브랜치에 이 worktree에 없는 그 모듈 커밋이 있다(→ `git merge master`). `error`를 읽는다.
- report에 `recoveredSubmit`: 이전 submit이 도중에 죽어 이번 실행이 되돌렸다. 그 에이전트는 다시 submit하면 된다.
  되돌리기가 실패하면 `Library/Harness/submit/pending.json`과 같은 폴더의 `<runId>/` 백업을 본다.
- `stage=land`: `error`와 사유 필드(`land.uncommitted/conflicts/missingMeta/owner/foreign`)를 읽는다. 이때 에디터 트리는 손대지 않은 상태다.
- report에 `recoveredLand`: 이전 land가 도중에 죽어 이번 실행이 병합을 되돌렸다. 그 에이전트는 다시 land하면 된다. 되돌리기가 실패하면
  저널이 `Library/Harness/land/failed-<runId>.json`으로 옮겨지고(`recoveredLand.error`), stash는 `refs/agentharness/land/<runId>`에 남는다
  (`git stash apply <sha>`로 직접 복원).
- 하네스 상태 파일: `Library/Harness/`(play_state.json, console.ndjson, compile.json, buildcache.json, fingerprint(.prev).txt,
  submit/(pending.json, owners.json), land/).
