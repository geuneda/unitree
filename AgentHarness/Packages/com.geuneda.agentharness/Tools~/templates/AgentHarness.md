# AgentHarness (이 프로젝트에 붙어 있는 에이전트 하네스)

이 프로젝트에는 [AgentHarness](https://github.com/geuneda/unitree) 패키지(`com.geuneda.agentharness`)가 붙어 있다.
명령 하나로 **재컴파일 → 씬 열기 → 플레이 모드 시나리오(입력 재생 + 캡처) → 콘솔·FPS 수집**을 하고 결과를 JSON으로 준다.
`tools/*.ps1`은 패키지의 `Tools~/`를 부르는 얇은 진입점이라 패키지를 올리면 도구도 같이 바뀐다.

## 루프

```powershell
powershell -ExecutionPolicy Bypass -File tools/open.ps1   # 에디터를 열고(이미 열려 있으면 기다리기만) 쓸 수 있을 때까지 대기
powershell -ExecutionPolicy Bypass -File tools/loop.ps1   # 한 바퀴: HarnessOut/latest/report.json (stdout에도 같은 JSON)
powershell -ExecutionPolicy Bypass -File tools/quit.ps1   # 끝낼 때: 락을 잡고 정상 종료
```

- 종료코드 0 = 녹색. `report.json`의 `ok`/`stage`(editor|compile|build|shader|play|runtime|lint|shots)와
  `compileErrors`/`runtimeErrors`(file·line·module)를 본다. `editorErrors`는 Unity·패키지 내부 에러라 실패로 치지 않는다.
- **매 루프 후 `shots`의 PNG를 Read 툴로 직접 연다.** `shotStats[].blank`(평평한 화면)는 실패, `dark`(98% 검정)는 의심.
- `play`: `probeReady`, `frames`, `events`(하네스 모듈의 EventBus 발행 수). `fps`는 에디터 플레이 모드 값이라 변경 전후 비교용.
- `timings.playEnterSec`: 플레이 진입 시간. 이 프로젝트가 Domain Reload를 켜 두었으면 여기에 리로드 시간이 들어간다.
- 옵션: `-Scenario tools/scenarios/x.json`, `-NoPlay`(편집 모드 캡처만), `-Out HarnessOut/x`.
- 커맨드 하나: `& ./tools/uc.ps1 <command> '<JSON>'` (예: `harness_capture '{"preset":"main"}'`, `harness_console`, `harness_ping`).
  목록은 `unity command --detail compact`. 임시 C#(`eval_file`)은 `HarnessOut/scripts/`에 둔다(HarnessOut은 git이 무시한다).

## 어떤 씬을 도는가

`ProjectSettings/AgentHarness.json`의 `playScene`: `"first"`(Build Settings의 첫 활성 씬), 씬 경로(`"Assets/Scenes/Level1.unity"`),
또는 `"build"`(코드 빌더가 만든 씬). 시나리오 JSON의 `"scene"`이 있으면 그것이 우선한다 → 씬마다 시나리오를 따로 둘 수 있다.
씬에 저장 안 한 변경이 있으면 하네스는 씬을 바꾸지 않고 실패한다(사람의 작업을 버리지 않는다).

## 시나리오 (tools/scenarios/*.json)

```jsonc
{ "name": "default", "scene": "", "durationSec": 3.0, "fixedDeltaTime": 0.0166667, "warmupSec": 0.25, "readyTimeoutSec": 10,
  "events": [ { "t": 1.0, "type": "keyTap", "key": "Space", "hold": 0.1 } ],
  "captures": [ { "t": 0.5, "preset": "auto" }, { "t": 1.5, "preset": "main" }, { "t": 2.5, "preset": "screen" } ] }
```
- `t`는 첫 씬 로드 뒤 게임 시간(초). `fixedDeltaTime`이면 매번 같은 프레임에서 캡처한다.
- 입력(`keyDown/keyUp/keyTap/mouse*/scroll/stick/pad*`)은 Input System 가상 장치로 들어간다 → **Input System 패키지를 쓰는 게임에서만** 동작한다
  (구 Input Manager의 `Input.GetKey`는 재생할 수 없다. 이 경우 이벤트는 에러로 보고된다).
- 캡처 `preset`: `"auto"`(ShotPreset이 없으면 메인 카메라) · `"main"` · `"screen"`(Game 뷰 그대로 = 스크린 공간 UI 포함, Game 뷰 탭이 보여야 함).

## 규칙 (기존 프로젝트)

1. 씬·프리팹·ScriptableObject(`.unity/.prefab/.asset`) YAML을 손으로 고치지 않는다. 에디터 API로 고친다
   (`& ./tools/uc.ps1 eval_file '{"file":"HarnessOut/scripts/x.cs"}'`, 저장은 `EditorSceneManager.SaveScene`/`AssetDatabase.SaveAssets`).
   C#·셰이더·UXML/USS·JSON 같은 텍스트 에셋은 직접 고친다.
2. 하네스는 프로젝트 설정을 바꾸지 않는다. `& ./tools/uc.ps1 harness_setup`은 권장 사항만 보여 주고, 바꾸려면 명시한다:
   `harness_setup '{"apply":"domainReload"}'`(플레이 진입이 빨라지지만 모든 가변 static을 SubsystemRegistration에서 초기화해야 한다).
3. 에디터 조작은 `tools/*.ps1`로만(프로젝트별 락). `unity command`로 직접 play/build를 부르지 않는다.
4. 여러 에이전트가 동시에 일하면 `ProjectSettings/AgentHarness.json`의 `modules`에 모듈 폴더를 등록하고, 각자 git worktree에서
   `tools/submit.ps1 -Module <이름>` → 커밋 → `tools/land.ps1`. 에러의 `module`도 이 등록으로 채워진다.

## 설정 (ProjectSettings/AgentHarness.json)

```jsonc
{
  "setup": "attach",                       // attach: 아무것도 지우거나 바꾸지 않음 | harness: 하네스 전용 프로젝트(설정 적용)
  "playScene": "first",                    // "first" | "build" | "Assets/…/X.unity"
  "modules": [ { "name": "Gameplay", "path": "Assets/Scripts/Gameplay" } ],   // 폴더 하나 = 모듈 하나
  "moduleRoots": [],                       // 하위 폴더마다 모듈(Assets/Game/<Module>/ 규약, asmdef·EventBus 규칙 적용)
  "contracts": "",                         // 모듈 간 공유 이벤트 폴더(추가만)
  "generatedRoot": "Assets/AgentHarness/Generated", "buildScene": "Assets/AgentHarness/Main.unity"   // 코드 빌더(IBuildStep)를 쓸 때
}
```

## 문제 해결

- `open.ps1`의 `dialog`: 에디터가 모달 대화상자(Safe Mode, 이용 약관 등)에 막혀 있다 → 사람에게 답을 부탁하고 `open.ps1`을 다시 부른다(그 에디터를 기다린다).
  `deprecatedPackages`가 함께 나오면 Unity가 열 때마다 묻는 지원 종료 패키지다 → `Packages/manifest.json`에서 빼거나 바꾼다.
- `stage=editor`: 에디터가 없거나 응답이 없다 → `tools/open.ps1`. 시작 시 컴파일 에러면 Safe Mode다(`Logs/Editor.log`의 `error CS`).
- `stage=shots`(`blank`): 화면이 비었다. 편집 모드(`-NoPlay`)에서는 플레이 중에만 그리는 게임이 검게 나온다. 카메라가 런타임에 생기는 게임이면 메인 카메라가 없다고 나온다.
- `unsaved changes in ...`: 에디터에 저장 안 한 씬이 있어 하네스가 씬을 바꾸지 않았다.

## 제거

`powershell -ExecutionPolicy Bypass -File tools/uninstall.ps1` (에디터를 닫은 뒤). 설치가 더한 것만 지운다:
패키지 의존성, `tools/`의 진입점·기본 시나리오·이 문서, 설정 파일, 설치가 만든 `CLAUDE.md`, `HarnessOut/`.
