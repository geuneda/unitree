# AgentHarness — Unity 6 LTS(6000.3.11f1) + URP 에이전트 하네스

이 문서만 읽고 바로 루프를 돌릴 수 있어야 한다. 게임은 아직 없다 — `Assets/Game/Stage`, `Assets/Game/Smoke`는
하네스를 검증하는 스모크 씬이다(절차적 지형 + 커스텀 HLSL + 라이트 + Volume 후처리 + 회전 오브젝트 + UI Toolkit HUD).

## 왜 이 하네스가 있나

에이전트가 Three.js로 만든 웹 3D 게임의 완성도가 높은 건 모델이 3D를 잘해서가 아니라 작업 환경 덕분이다.

| # | Three.js 환경의 성질 | Unity 기본 상태 | 이 하네스가 복원하는 방법 |
|---|---|---|---|
| 1 | 모든 게 텍스트(JS 코드) | 씬/프리팹이 GUID로 얽힌 YAML, GUI 중심 도구 | 씬은 `IBuildStep` 코드가 생성, HLSL·UXML/USS·코드로 만든 머티리얼/Volume/라이팅 |
| 2 | 수정→새로고침이 초 단위 | 컴파일 + 도메인 리로드 | Domain Reload 끔, 모듈별 asmdef, 빌드 캐시, 에디터 없는 컴파일 체크 |
| 3 | 스크린샷·콘솔·FPS를 눈/기계로 확인 | 에이전트가 화면을 못 봄 | `harness_capture/play`가 PNG + 이미지 통계, `harness_console/stats`가 JSON |
| 4 | 에셋 없이 절차적 생성 + 셰이더 + 후처리 | 에셋 임포트 중심 | `Harness.Procedural`(Mesh/Noise/Texture 베이크), URP Volume을 코드로 |
| 5 | 레지스트리 구조라 병렬 작업이 쉬움 | 에디터 하나를 공유 | `GameRoot.Register` + `EventBus`, 모듈 폴더 격리, 에디터 조작 뮤텍스, 에이전트별 git worktree + `submit.ps1` 트랜잭션 |

모든 설계 결정의 기준: **"Three.js 환경의 어떤 성질을 복원하는가"**. URP·물리·엔진 기능을 쓰니 결과는 그 이상을 노린다.

**아직 해결 안 된 격차는 [`docs/ROADMAP.md`](docs/ROADMAP.md)에 성질 1~5별로 기록돼 있다.** 하네스를 개선할 때는 거기서 항목을 고르고,
해결하면 체크 + 검증 방법·측정값을 남긴다. 하네스를 고친 뒤에는 ROADMAP의 "검증 매트릭스"를 다시 돌린다.

## 빠른 시작 (새로고침 + 스크린샷 + 콘솔 = 한 방)

```powershell
unity status                      # state "ready" 인 에디터가 있어야 한다. 없으면:
unity open <이 폴더 절대경로>      # 그 다음 unity status 가 ready 가 될 때까지 대기
powershell -ExecutionPolicy Bypass -File tools/loop.ps1
```

`tools/loop.ps1` = recompile → (C# 컴파일 에러면 즉시 중단) → lint → `harness_build` → `harness_shaders` → `harness_play`(기본 3컷)
→ `harness_console` + `harness_stats` → `HarnessOut/latest/report.json` (stdout에도 같은 JSON). 종료코드 0 = 전부 녹색.

**매 루프 후 반드시**: `report.json`의 `ok/stage`를 보고, `shots`의 PNG를 **Read 툴로 직접 열어** 눈으로 확인한다.
`shotStats[].blank=true`(평평/검은 화면)면 렌더가 깨진 것이다.

옵션: `-Scenario tools/scenarios/x.json`, `-Out HarnessOut/x`, `-NoPlay`(편집 모드 캡처만), `-NoCompile`.
**여러 에이전트가 동시에 작업하면** 이 폴더를 직접 고치지 말고 각자 worktree에서 `tools/submit.ps1`을 쓴다(아래 "병렬 에이전트").

### report.json

```jsonc
{
  "ok": true, "stage": "done",            // 실패 시 stage = editor|compile|build|shader|play|runtime|lint|shots|submit
  "compileErrors": [{"file","line","msg","module"}],       // C# 에러, 또는 kind:"shader" (HLSL 에러, 상태 기반)
  "runtimeErrors": [{"type","msg","file","line","module","count","stack"}],   // count = 같은 에러 폴딩 수
  "editorErrors": [{"type","msg","count","stack"}],   // Unity/패키지 내부 에러(Assets/ 흔적 없음). 실패 사유는 아니지만 읽어볼 것
  "fps": {"avg","min","p95ms","p99ms","hitches","cpuMainAvgMs","samples","editorFocused"},
  "shots": ["C:/.../HarnessOut/latest/shot0_closeup.png", ...],
  "durationSec": 3.5,
  "timings": {"lockWaitSec","compileSec","buildSec","playSec","collectSec"},
  "build": {"fingerprint","steps":[{"type","module","ms","error","file","line"}], ...},
  "play": {"success","probeReady","frames","gameSec","modules","failedModules","inputEventsApplied",
           "events":[{"name":"SpinnerLap","count":2}]},     // EventBus 발행 횟수 → 게임플레이를 기계적으로 검증
  "render": {"batches","setPassCalls","drawCalls","triangles","vertices"},
  "shotStats": [{"name","preset","t","meanLuma","stdLuma","blank","error"}],
  "lint": [{"rule","module","file","message"}], "warningCount": 0,
  "submit": {"phase","synced","kept","reverted","written","deleted","contractsAdded","metaWrittenBack",   // submit.ps1만.
             "errorModules","restore","check"},                  // timings에 checkSec/syncSec/restoreSec 추가
  "recoveredSubmit": {"runId","workRoot","modules","files"}      // 도중에 죽은 submit을 이번 실행이 되돌렸을 때만
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
   락을 우회하지 말 것. **병렬 에이전트는 각자 git worktree에서 코드를 쓰고 `tools/submit.ps1 -Module <M>`으로 에디터에 넣는다**
   (아래 "병렬 에이전트"). 여럿이 에디터 트리를 직접 고치면 한 명의 컴파일 에러가 모두의 루프를 막는다.
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
tools/compile-check.ps1        에디터 없는 컴파일 검사  tools/Harness.psm1    HTTP 클라이언트·락·루프·submit 저널
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
| `harness_ping` | domainReloads, isCompiling, isPlaying, compileFailed, mark |
| `harness_setup` | 프로젝트 설정 멱등 적용(Domain Reload off, runInBackground, Debug 코드 최적화, 템플릿 샘플 삭제) |
| `harness_sync_csproj` | .sln/.csproj 생성(사용자 외부 에디터 설정은 복원) — compile-check msbuild 백엔드용 |

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

## 병렬 에이전트: worktree + submit (남의 컴파일 에러에 막히지 않기)

에디터는 이 폴더(에디터 트리) 하나에 묶여 있다. 여러 에이전트가 여기서 직접 코드를 고치면 한 명의 쓰다 만 코드가
Unity 도메인 리로드를 막아 **모두의 루프가 `stage=compile`로 멈춘다.** 그래서 병렬 작업은 에이전트별 worktree에서 한다:

```powershell
# 1회: 에디터가 연 체크아웃에서 에이전트별 worktree를 만든다 (Library/가 없으니 에디터도 임포트도 필요 없다)
git worktree add ..\wt-foo -b agent/foo
cd ..\wt-foo\AgentHarness                                                        # 이후 편집·명령은 모두 여기서
powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Foo    # 에디터 없이 ~0.5s, 동시 실행 OK
powershell -ExecutionPolicy Bypass -File tools/submit.ps1 -Module Foo          # 에디터 트리에서 루프(트랜잭션)
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
- 한 모듈은 한 에이전트만 submit한다(미러링이라 마지막 submit이 이긴다). 모듈 삭제·`Assets/Harness`·`tools/`는 submit 대상이 아니다
  (하네스 작업은 에디터 트리에서 직접).
- 에디터 트리 찾기: `Library/`가 없는 체크아웃이면 `git worktree list`의 메인 worktree에서 같은 하위 경로.
  git worktree가 아닌 복사본이면 `$env:AGENTHARNESS_EDITOR_ROOT`에 에디터 트리 경로를 준다.
- 병합: submit한 파일은 메인 트리에 미커밋 사본으로 남아 `git merge agent/foo`가 "would be overwritten"으로 거부된다.
  다른 루프가 돌지 않을 때 메인 트리에서 그 브랜치 경로만 치우고 병합한다(내용은 같다):
  `$p = git diff --name-only HEAD...agent/foo; git stash push -u -m land-foo -- $p; git merge agent/foo` → 확인 후 `git stash drop`(stash가 생겼을 때만).

## 에디터 없이 컴파일 체크

```powershell
powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Smoke                   # csc(기본) ~0.1-0.3s/어셈블리
powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Smoke -Backend msbuild  # 웜 ~0.5-2s, 콜드 수십 초
```
- 디스크의 소스를 다시 glob 하므로 방금 만든 파일도 포함되고, 실행마다 전용 임시 폴더라 동시 실행에 안전하다. `-Module A,B` 가능.
- worktree에서 돌리면 소스는 worktree, 응답 파일·의존 DLL은 에디터 트리 것을 쓴다(출력에 `sourceRoot`/`editorRoot`).
- `-Module`은 그 모듈이 참조하는 프로젝트 어셈블리(`Game.Contracts`)도 함께 검사하고, 한 실행 안에서 의존 순서로 컴파일해
  **방금 만든 DLL을 참조**한다(체인) → worktree에서 추가한 Contracts 타입도 보인다. `-IncludeHarness`면 Harness도 체인.
- `csc`: 에디터가 쓰는 응답 파일(`Library/Bee/artifacts/*/<Asm>.rsp`)과 에디터 내장 Roslyn → 에디터와 동일한 플래그/분석기. .NET SDK·VS 불필요.
  에디터가 한 번도 컴파일하지 않은 **새 어셈블리**(.rsp 없음)는 같은 종류(Editor 전용/런타임) Harness 어셈블리의 응답 파일에
  asmdef 참조를 붙여 합성해 검사한다(`synthesized: true`; 템플릿의 패키지 참조가 남아 실제보다 약간 관대).
- `msbuild`: Unity가 생성한 `<Asm>.csproj`를 실행마다 재작성(소스 목록 갱신, ProjectReference → 체인 DLL 또는 에디터 DLL) 후 VS 2022 MSBuild.
  csproj가 없으면 `tools/uc.ps1 harness_sync_csproj`(새 어셈블리는 csc만). 이 머신엔 .NET SDK가 없어 `dotnet build`는 불가.
- 한계: 검사 집합 밖(다른 모듈, `-IncludeHarness` 없는 Harness)은 **에디터가 마지막으로 컴파일한 DLL** 기준이다.
  최종 판정은 항상 `loop.ps1` / `submit.ps1`.

## 함정 (겪은 것)

- `Mathf.SmoothStep(from, to, t)`는 GLSL `smoothstep`이 **아니다**(값 보간). `PMath.Smoothstep(e0, e1, x)`를 써라. 지형이 전부 눈으로 나온 원인.
- `UnityEngine.Object`에 `?.` 금지(에디터의 fake null). `TryGetComponent`를 쓴다.
- 에디터 명령/빌더에서 `EditorApplication.delayCall` 금지 — 포커스 없는 에디터에선 실행되지 않는다(`harness_play`가 73s 멈췄던 원인).
  `EditorApplication.update` 한 번짜리 콜백이나 직접 호출을 쓴다.
- 머티리얼은 `ctx.Material()`로 만든다. `ShaderGUI.ValidateMaterial`을 불러 URP Lit의 태그·패스·레거시 프로퍼티를 맞추므로
  첫 빌드와 이후 빌드가 같아진다. 그래도 텍스처에 따른 키워드(`_NORMALMAP` 등)는 직접 켠다.
- 프로젝트는 짧은 경로(60자 이하)에 둔다. 길면 Windows 260자 제한으로 Unity 패키지 파일 로드가 실패한다. `%TEMP%` 아래도 피한다(Burst DLL 차단). 스카이박스 앰비언트는 라이팅 베이크가 필요해서 Trilight + `ctx.BakeSkyReflection()`을 쓴다.
- 캡처 카메라는 메인 카메라 설정(후처리 포함)을 복사해 오프스크린 렌더한다. 메인 카메라가 없으면 캡처 실패.
- 에디터 플레이 모드 FPS는 에디터 오버헤드·autotick 영향을 받는다. 절대값이 아니라 **변경 전후 비교**용이다(`editorFocused` 확인).
- Code Optimization은 Debug(정확한 예외 줄 번호). Release면 throw 위치가 메서드 끝 줄로 보고된다.
- PowerShell 5.1: `tools/*.ps1`은 ASCII만 쓴다(BOM 없는 UTF-8 비ASCII는 깨진다). 인자는 `uc.ps1`에 JSON 한 덩어리로.
- 직전 컴파일이 실패한 상태에서 Pipeline `recompile`은 컴파일 시작 전의 옛 실패를 보고할 수 있다. `Invoke-HarnessRecompile`(loop.ps1)은
  컴파일 세대 번호로 이를 피한다 — 직접 `recompile_status`만 믿지 말 것.
- 도메인 리로드 때 Pipeline 서버는 새 토큰으로 재시작한다. 그 사이 요청은 연결 끊김 또는 **401 Unauthorized**(`success` 필드 없음)를 받는다.
  `Invoke-UnityCommand`는 둘 다 `unreachable`로 돌려주고 폴러와 루프 시작 ping은 재시도한다. 락이 리로드 직후 다음 에이전트로
  넘어가는 submit 흐름에서 처음 드러났다(루프가 `stage=editor`로 실패, StrictMode 모듈이 `success` 접근에서 예외).

## 문제 해결

- `stage=editor`: 에디터가 없거나 응답 없음. `unity status`; 열려 있는데 연결이 안 되면 Safe Mode(시작 시 컴파일 에러)일 수 있다 →
  `unity pipeline list`, `Logs/Editor.log`에서 `error CS` 확인 후 소스 수정 → 에디터 재시작. `editor_status`가 `blocked_by_dialog`면 사람에게 다이얼로그를 닫아 달라고 한다.
- 플레이가 끝나지 않음: 시나리오 타임아웃(duration+70s) 후 자동 종료. 수동: `tools/uc.ps1 editor_stop`.
- 빌드 fingerprint가 매번 바뀜: 빌더가 비결정적(시드 없는 랜덤, 시간, Dictionary 순회 순서 등). `Library/Harness/fingerprint.txt` 두 개를 diff.
- `stage=submit`: worktree에서 `loop.ps1`을 돌렸거나(→ `submit.ps1`), 기존 Contracts 파일을 고쳤거나(추가만 허용), `-Module` 폴더가 없다. `error`를 읽는다.
- report에 `recoveredSubmit`: 이전 submit이 도중에 죽어 이번 실행이 되돌렸다. 그 에이전트는 다시 submit하면 된다.
  되돌리기가 실패하면 `Library/Harness/submit/pending.json`과 같은 폴더의 `<runId>/` 백업을 본다.
- 하네스 상태 파일: `Library/Harness/`(play_state.json, console.ndjson, compile.json, buildcache.json, fingerprint.txt, submit/).
