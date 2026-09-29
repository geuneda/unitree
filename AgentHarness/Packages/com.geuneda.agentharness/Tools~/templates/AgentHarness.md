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
  `knownErrors`(설정의 `knownErrors` 정규식에 맞은 에러)와 `teardownErrors`(시나리오가 끝난 뒤 플레이 모드를 나가며 난 에러, 예: 끝나지 않은 부트의 취소)도
  실패는 아니지만 읽어 본다.
- **매 루프 후 `shots`의 PNG를 Read 툴로 직접 연다.** `shotStats[].blank`(평평한 화면)는 실패, `dark`(98% 검정)는 의심. `hint`가 있으면 이유다
  (예: 화면이 오버레이 UI뿐이라 카메라 렌더가 비었다 → 캡처를 `"screen"`으로).
- `play`: `probeReady`, `frames`, `events`(하네스 모듈의 EventBus 발행 수), `scenes`(로드된 씬과 시각), `waits`, `clicks`, `inputBackends`.
  `fps`는 에디터 플레이 모드 값이라 변경 전후 비교용.
- `timings.playEnterSec`: 플레이 진입 시간. 이 프로젝트가 Domain Reload를 켜 두었으면 여기에 리로드 시간이 들어간다.
- 옵션: `-Scenario tools/scenarios/x.json`, `-NoPlay`(편집 모드 캡처만), `-Out HarnessOut/x`.
- 커맨드 하나: `& ./tools/uc.ps1 <command> '<JSON>'` (예: `harness_capture '{"preset":"main"}'`, `harness_console`, `harness_ping`).
  목록은 `unity command --detail compact`. 임시 C#(`eval_file`)은 `HarnessOut/scripts/`에 둔다(HarnessOut은 git이 무시한다).
- 에디터 없이 컴파일 검사: `tools/compile-check.ps1 [-Module <이름>]` (asmdef 폴더든 `Assembly-CSharp` 폴더든 모듈 코드를 컴파일하는 어셈블리를 검사).

## 어떤 씬을 도는가

`ProjectSettings/AgentHarness.json`의 `playScene`: `"first"`(Build Settings의 첫 활성 씬), 씬 경로(`"Assets/Scenes/Level1.unity"`),
또는 `"build"`(코드 빌더가 만든 씬). 시나리오 JSON의 `"scene"`이 있으면 그것이 우선한다 → 씬마다 시나리오를 따로 둘 수 있다.
씬에 저장 안 한 변경이 있으면 하네스는 씬을 바꾸지 않고 실패한다(사람의 작업을 버리지 않는다).

## 시나리오 (tools/scenarios/*.json)

```jsonc
{ "name": "boot-to-level", "scene": "", "durationSec": 3.0, "fixedDeltaTime": 0.0166667, "warmupSec": 0.25, "readyTimeoutSec": 10,
  "mouseSpace": "pixels",
  "events": [
    { "t": 0.0, "type": "waitTarget", "target": "PlayButton", "timeoutSec": 60 },   // 부트·로딩이 끝나 버튼이 보일 때까지 시계 정지
    { "t": 0.1, "type": "click", "target": "PlayButton" },                          // uGUI/씬 오브젝트 이름·경로 또는 UI Toolkit 요소 이름
    { "t": 0.3, "type": "waitScene", "scene": "Level1", "timeoutSec": 60 },         // 그 씬이 로드될 때까지 시계 정지
    { "t": 1.0, "type": "keyTap", "key": "Space", "hold": 0.1 } ],
  "captures": [ { "t": 0.05, "preset": "screen", "name": "menu" },                  // Game 뷰 그대로(스크린 공간 UI 포함)
                { "t": 1.5, "preset": "main" },                                      // 메인 카메라(오프스크린 1280x720, UI 없음)
                { "t": 2.5, "name": "top", "pos": [0, 30, -0.1], "lookAt": [0, 0, 0], "fov": 50 } ] }
```
- `t`는 첫 씬 로드 뒤 **게임 시간**(초). `fixedDeltaTime`이면 매번 같은 프레임에서 캡처한다. 로딩(네트워크, Addressables)은 벽시계로 걸려서
  고정 `t`로는 "로딩이 끝났을 때"를 맞출 수 없다 → `waitTarget`/`waitScene`으로 기다린다. 기다린 시간은 `play.waits`에 나오고, 제한 시간
  (`timeoutSec`, 기본 30)을 넘기면 시나리오가 실패한다.
- 입력: `keyDown/keyUp/keyTap`(키 이름: `Space`, `Digit1`/`Alpha1`, `Enter`/`Return`, `LeftCtrl`/`LeftControl` 등 두 이름 모두) ·
  `mouseMove/mousePos/mouseDown/mouseUp/scroll` · `click`(`target` 또는 `x`,`y`) · `stick`/`padDown`/`padUp` · `releaseAll`.
  좌표는 Game 뷰 픽셀(원점 왼쪽 아래)이다. Game 뷰 크기는 사람의 레이아웃에 따라 다르므로 `"mouseSpace": "normalized"`(0..1)나 `target`을 쓴다.
- **Input System을 쓰는 게임**: 입력은 가상 장치로 그대로 들어간다(InputAction, `Keyboard.current`, UI 입력 모듈).
- **구 Input Manager(`Input.GetKey` 등)를 쓰는 게임**: 이것은 코드로 누를 수 없다(에디터에서 OS 입력을 직접 읽는다). 입력을 `HarnessInput`으로 읽게 한다:
  설치할 때 `-InputShim`이면 `Assets/AgentHarness/HarnessInput.cs`가 생긴다 — `UnityEngine.Input`과 멤버 이름이 같아서 `Input.` → `HarnessInput.`이면
  되고, 실제 입력은 그대로, 시나리오 입력이 더해진다(빌드에서는 그냥 `Input`). 자체 입력 계층이 있으면 그 정적 메서드
  `void M(string type, string key, Vector2 value)`에 `[AgentHarnessInput]`을 붙이면 하네스가 시나리오 입력을 넘긴다(`play.inputHooks`).
- 캡처 `preset`: `"auto"`(샷이 없으면 메인 카메라) · `"main"` · `"screen"`(Game 뷰 그대로, Game 뷰 탭이 보여야 함) · 샷 이름(설정 `shots`) ·
  `"camera": "<카메라 이름>"` · `"pos"` + `"lookAt"`/`"rot"` + `"fov"`(그 자리에서, 메인 카메라 설정으로).

## 규칙 (기존 프로젝트)

1. 씬·프리팹·ScriptableObject(`.unity/.prefab/.asset`) YAML을 손으로 고치지 않는다. 에디터 API로 고친다
   (`& ./tools/uc.ps1 eval_file '{"file":"HarnessOut/scripts/x.cs"}'`, 저장은 `EditorSceneManager.SaveScene`/`AssetDatabase.SaveAssets`).
   C#·셰이더·UXML/USS·JSON 같은 텍스트 에셋은 직접 고친다.
2. 하네스는 프로젝트 설정을 바꾸지 않는다. `& ./tools/uc.ps1 harness_setup`은 권장 사항만 보여 주고, 바꾸려면 명시한다:
   `harness_setup '{"apply":"domainReload"}'`(플레이 진입이 빨라지지만 모든 가변 static을 SubsystemRegistration에서 초기화해야 한다).
3. 에디터 조작은 `tools/*.ps1`로만(프로젝트별 락). `unity command`로 직접 play/build를 부르지 않는다.
4. 여러 에이전트가 동시에 일하면 `ProjectSettings/AgentHarness.json`의 `modules`에 모듈 폴더를 등록하고, 각자 git worktree에서
   `tools/submit.ps1 -Module <이름>` → 커밋 → `tools/land.ps1`. 에러의 `module`도 이 등록으로 채워진다. asmdef 없는 폴더도 된다
   (submit의 사전 컴파일 검사가 그 폴더가 들어가는 `Assembly-CSharp`을 검사한다).
5. 시나리오가 게임의 서버·계정·결제를 건드릴 수 있다. 개발용 대화상자에서 서버를 고르는 게임이면 시나리오가 테스트 서버를 명시적으로 누르게 하고,
   구매 팝업 같은 것은 누르지 않는다. PlayerPrefs는 같은 프로젝트의 다른 체크아웃과 공유된다.

## 설정 (ProjectSettings/AgentHarness.json)

```jsonc
{
  "setup": "attach",                       // attach: 아무것도 지우거나 바꾸지 않음 | harness: 하네스 전용 프로젝트(설정 적용)
  "playScene": "first",                    // "first" | "build" | "Assets/…/X.unity"
  "modules": [ { "name": "Gameplay", "path": "Assets/Scripts/Gameplay" } ],   // 폴더 하나 = 모듈 하나
  "moduleRoots": [],                       // 하위 폴더마다 모듈(Assets/Game/<Module>/ 규약, asmdef·EventBus 규칙 적용)
  "contracts": "",                         // 모듈 간 공유 이벤트 폴더(추가만)
  "shots": [ { "name": "overview", "scene": "Level1", "pos": [0, 20, -20], "lookAt": [0, 0, 0], "fov": 50 } ],   // 이름 있는 캡처 포즈
  "knownErrors": [ "^\\[Analytics\\] init failed" ],   // 이 프로젝트가 원래 내는 에러(정규식): knownErrors로 보고, 루프를 막지 않음
  "generatedRoot": "Assets/AgentHarness/Generated", "buildScene": "Assets/AgentHarness/Main.unity",   // 코드 빌더(IBuildStep)를 쓸 때
  "installAdded": [], "installReplaced": []   // 설치가 더한/올린 의존성(uninstall이 되돌림) - 손대지 않는다
}
```

## 문제 해결

- `open.ps1`의 `dialog`: 에디터가 모달 대화상자(Safe Mode, 이용 약관 등)에 막혀 있다 → 사람에게 답을 부탁하고 `open.ps1`을 다시 부른다(그 에디터를 기다린다).
  `deprecatedPackages`가 함께 나오면 Unity가 열 때마다 묻는 지원 종료 패키지다 → `Packages/manifest.json`에서 빼거나 바꾼다.
- `stage=editor`: 에디터가 없거나 응답이 없다 → `tools/open.ps1`. 시작 시 컴파일 에러면 Safe Mode다(`Logs/Editor.log`의 `error CS`).
- `stage=shots`(`blank`): 화면이 비었다. `hint`를 본다. 메뉴·타이틀처럼 UI뿐인 화면은 `"screen"`으로 찍는다. 편집 모드(`-NoPlay`)에서는 플레이 중에만
  그리는 게임이 검게 나온다. 카메라가 런타임에 생기는 게임이면 메인 카메라가 없다고 나온다.
- `stage=play` + `waitScene/waitTarget ... after Ns`: 그 씬·요소가 제한 시간 안에 나오지 않았다. 에러 메시지의 로드된 씬 목록과 `Logs/Editor.log`를 본다
  (게임이 로그인·입력을 기다리는 중일 수 있다 → 그 버튼을 `waitTarget` + `click`).
- `unsaved changes in ...`: 에디터에 저장 안 한 씬이 있어 하네스가 씬을 바꾸지 않았다.
- `nothing takes scenario input`: 구 Input Manager 게임에 입력 이벤트를 넣었다 → 위 "구 Input Manager" 참고.

## 제거

`powershell -ExecutionPolicy Bypass -File tools/uninstall.ps1` (에디터를 닫은 뒤). 설치가 더한 것만 지운다:
패키지 의존성(올린 버전은 원래대로), `tools/`의 진입점·기본 시나리오·이 문서, 설정 파일, 설치가 만든 `CLAUDE.md`, `HarnessOut/`.
`Assets/AgentHarness/HarnessInput.cs`는 게임 코드가 쓰고 있으면 남긴다.
