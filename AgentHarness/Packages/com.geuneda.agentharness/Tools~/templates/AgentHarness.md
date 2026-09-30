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

- `open.ps1`은 에디터를 `-automated`로 연다: 에디터의 모달 대화상자(`EditorUtility.DisplayDialog`)가 사람을 기다리지 않고 기본값(취소)으로 바로 닫힌다.
  사람이 그 에디터에서 작업하면 `-Interactive`. `-Headless`는 창 없는 에디터(`-batchmode`, GPU로 렌더): Game 뷰가 없어 `"screen"` 캡처는 안 되고
  `fps`에 렌더가 빠지지만(`render` 없음), 한 바퀴가 ~1.3 s 빠르고 스크립트가 컴파일되지 않아도 마지막으로 성공한 어셈블리로 떠서 루프가 에러를 보고한다.
  report의 `editor`(`mode`: `window`/`headless`, `automated`)가 어느 에디터였는지다.

- 종료코드 0 = 녹색. `report.json`의 `ok`/`stage`(editor|compile|build|shader|play|runtime|lint|shots)와
  `compileErrors`/`runtimeErrors`(file·line·module)를 본다. `editorErrors`는 Unity·패키지 내부 에러라 실패로 치지 않는다.
  `knownErrors`(설정의 `knownErrors` 정규식에 맞은 에러)와 `teardownErrors`(시나리오가 끝난 뒤 플레이 모드를 나가며 난 에러, 예: 끝나지 않은 부트의 취소)도
  실패는 아니지만 읽어 본다.
- **매 루프 후 `shots`의 PNG를 Read 툴로 직접 연다.** `shotStats[].blank`(평평한 화면)는 실패, `dark`(98% 검정)는 의심,
  `magenta`는 렌더 파이프라인이 못 그리는 머티리얼(URP에서 Built-in 셰이더, 없는·깨진 셰이더)일 가능성이 크다. `hint`가 있으면 이유다
  (마젠타로 그린 렌더러, 화면에 그리는 다른 카메라). 샷에는 화면에 그리는 카메라들(`shotStats[].cameras`)과 스크린 공간 UI(`shotStats[].ui`)가
  들어 있다(아래 "캡처").
- **기준 이미지(golden)가 있으면** 샷마다 `shotStats[].golden.status`(`same`/`changed`/`missing`)와 바뀐 곳(`rect`, `diff` 이미지)이 나온다.
  실패로 치지 않는다. 의도하지 않았는데 `changed`면 `diff` PNG(바뀐 픽셀 빨강)를 연다. 화면을 의도대로 바꿨고 샷이 맞으면
  `tools/loop.ps1 -UpdateGolden`으로 기준 이미지를 갱신해 커밋한다(아래 "기준 이미지").
- `play`: `probeReady`, `frames`, `events`(하네스 모듈의 EventBus 발행 수), `scenes`(로드된 씬과 시각), `waits`, `clicks`, `inputBackends`,
  `isolatedDevices`(시나리오 동안 끈 실제 장치와 막은 키·버튼 누름 수 — 사람이 그 사이 키보드를 만져도 결과가 같다),
  `uiClock`(`mode: "frames"` = 시나리오 동안 UI Toolkit 패널의 USS transition·`schedule` 타이머가 실시간 대신 프레임 × `fixedDeltaTime`을 따랐다 →
  UI 애니메이션 도중의 캡처도 매번 같다; `panels` = 따른 패널 수).
  `fps`는 에디터 플레이 모드 값이라 변경 전후 비교용.
- `timings.playEnterSec`: 플레이 진입 시간. 이 프로젝트가 Domain Reload를 켜 두었으면 여기에 리로드 시간이 들어간다.
- 옵션: `-Scenario tools/scenarios/x.json`, `-NoPlay`(편집 모드 캡처만), `-Out HarnessOut/x`, `-UpdateGolden`(녹색일 때 샷을 기준 이미지로),
  `-Hot`(아래).
- **핫 루프 `tools/loop.ps1 -Hot`**: 마지막 전체 루프가 컴파일한 뒤 바뀐 것이 `[CodeReload]`(`using Unity.Pipeline.CodeReload;`) 메서드의 **본문뿐**이면
  컴파일·도메인 리로드 없이 그 본문을 Pipeline 인터프리터로 바꿔 넣고 같은 시나리오를 돈다(`report.hot.applied`, 컴파일+리로드만큼 빠름).
  필드·시그니처·다른 메서드·새 파일·에셋이 바뀌었거나, 인터프리터가 못 돌리는 구문(`try/catch` 등)이거나, 바꾼 본문이 플레이 중 예외를 던지면
  전체 루프를 돌고 이유를 `hot.fallback`에 적는다(예외는 전체 루프가 정확한 줄로 보고). 조건: Domain Reload가 꺼져 있을 것
  (`harness_setup {"apply":"domainReload"}`), 메서드가 public이고 void·`IEnumerator`일 것, 그 코드의 어셈블리가 `Unity.Pipeline`을 참조할 것
  (`Assembly-CSharp`는 자동, asmdef는 `"Unity.Pipeline"`, `"Unity.Pipeline.Attributes"`를 references에). 바뀐 게 무엇이고 핫으로 되는지만 보려면
  `& ./tools/uc.ps1 harness_hot '{"mode":"check"}'`.
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
  "captures": [ { "t": 0.05, "preset": "main", "name": "menu" },                    // 화면의 카메라들 + 스크린 공간 UI(캡처 크기로 배치)
                { "t": 1.5, "preset": "screen" },                                    // Game 뷰 그대로(Game 뷰 크기)
                { "t": 2.0, "preset": "main", "name": "run", "frames": 8, "every": 4 },   // 연속 캡처: 8프레임을 한 장의 시트로
                { "t": 2.5, "name": "top", "pos": [0, 30, -0.1], "lookAt": [0, 0, 0], "fov": 50,
                  "ignore": [ { "x": 0.8, "y": 0, "w": 0.2, "h": 0.1 } ] } ] }             // 기준 이미지 비교에서 뺄 곳(시계 등)
```
- `t`는 첫 씬 로드 뒤 **게임 시간**(초). `fixedDeltaTime`이면 매번 같은 프레임에서 캡처한다. 로딩(네트워크, Addressables)은 벽시계로 걸려서
  고정 `t`로는 "로딩이 끝났을 때"를 맞출 수 없다 → `waitTarget`/`waitScene`으로 기다린다. 기다린 시간은 `play.waits`에 나오고, 제한 시간
  (`timeoutSec`, 기본 30)을 넘기면 시나리오가 실패한다.
- 입력: `keyDown/keyUp/keyTap`(키 이름: `Space`, `Digit1`/`Alpha1`, `Enter`/`Return`, `LeftCtrl`/`LeftControl` 등 두 이름 모두) ·
  `mouseMove/mousePos/mouseDown/mouseUp/scroll` · `click`(`target` 또는 `x`,`y`) · `stick`/`padDown`/`padUp` · `releaseAll`.
  좌표는 Game 뷰 픽셀(원점 왼쪽 아래)이다. Game 뷰 크기는 사람의 레이아웃에 따라 다르므로 `"mouseSpace": "normalized"`(0..1)나 `target`을 쓴다.
- **Input System을 쓰는 게임**: 입력은 가상 장치로 그대로 들어간다(InputAction, `Keyboard.current`, UI 입력 모듈). 시나리오 동안 실제 키보드·
  마우스·게임패드는 꺼진다(끝나면, 실패·중단해도 다시 켜진다).
- **구 Input Manager(`Input.GetKey` 등)를 쓰는 게임**: 이것은 코드로 누를 수 없다(에디터에서 OS 입력을 직접 읽는다). 입력을 `HarnessInput`으로 읽게 한다:
  설치할 때 `-InputShim`이면 `Assets/AgentHarness/HarnessInput.cs`가 생긴다 — `UnityEngine.Input`과 멤버 이름이 같아서 `Input.` → `HarnessInput.`이면
  된다. 평소엔 실제 입력 그대로, 시나리오 동안엔 시나리오 입력만(마우스는 시나리오가 옮기기 전까지 화면 중앙; 빌드에서는 그냥 `Input`).
  `Input.`으로 직접 읽는 코드에는 실제 입력이 섞인다. 자체 입력 계층이 있으면 그 정적 메서드
  `void M(string type, string key, Vector2 value)`에 `[AgentHarnessInput]`을 붙이면 하네스가 시나리오 입력을 넘긴다(`play.inputHooks`).
  `type`이 `"begin"`이면 그때부터 실제 입력을 무시하고, `"end"`면 되돌린다. 하네스를 올린 뒤 install을 `-InputShim`으로 다시 돌리면 고치지 않은
  옛 `HarnessInput.cs`가 새 버전으로 바뀐다(고친 사본은 경고만).
- 캡처 `preset`: `"auto"`(샷이 없으면 메인 카메라) · `"main"` · `"screen"`(Game 뷰 그대로, Game 뷰 탭이 보여야 함 — 창 없는 에디터에서는 에러) · 샷 이름(설정 `shots`) ·
  `"camera": "<카메라 이름>"` · `"pos"` + `"lookAt"`/`"rot"` + `"fov"`(그 자리에서, 메인 카메라 설정으로).
- `"screen"` 말고는 오프스크린으로 **Game 뷰가 합치는 카메라들을 같은 순서로** 렌더한다: 화면에 그리는 Base 카메라를 depth 순으로(viewport·clear
  그대로, 미니맵·분할 화면), 각 카메라의 URP 카메라 스택까지, 메인 카메라 자리에 캡처 포즈의 카메라. 포즈가 메인 카메라와 다르면 메인 카메라를 그 포즈로
  잠깐 옮겨서 거기 달린 것(무기 오버레이 카메라와 무기)이 따라온다. 그린 카메라는 `shotStats[].cameras`(아래부터). `"camera": "<이름>"`이면 그 카메라와
  그 스택만 전체 화면으로. URP에서 전체 화면 Base 카메라 두 개는 게임에서도 뒤의 것이 앞의 것을 덮는다(겹쳐 그리려면 카메라 스택).
- 그 위에 **스크린 공간 UI(uGUI 캔버스, UI Toolkit 패널)를 캡처 크기로 다시 배치해 합성**한다 → 메뉴·HUD·팝업이 사람의 Game 뷰 크기와 상관없이 같은
  모양으로 찍힌다. `shotStats[].ui`(아래부터): 메인 카메라와 다른 Base 카메라의 Screen Space - Camera 캔버스는 그 카메라가 그리고(후처리 포함), 스택
  Overlay 카메라의 캔버스(렌더 요청으로는 그려지지 않는다)는 카메라들 위에, 오버레이 캔버스·UI Toolkit 패널은 맨 위에(sortingOrder 순). 캔버스 모드·카메라
  타깃·패널 타깃을 잠깐 바꿨다 같은 프레임에 되돌린다 — UI 코드가 크기 변화에 반응해 게임이 이상해지면 그 캡처에 `"ui": false`.
- 연속 캡처: `"frames": N`(+ `"every": k`프레임 간격)이면 t부터 N프레임을 **한 장의 PNG(시트, 칸마다 t)**로 찍는다. `shotStats[]`에 `frames`, `sheet`(열x행),
  `times`, `motion`(프레임 사이 평균 밝기 차이, 0이면 아무것도 안 움직임). 포즈는 첫 프레임에 고정(`"main"`·`"camera"`는 카메라를 따라감).
- 캡처 크기: 시나리오 `"width"`/`"height"` → 설정 `captureSize` → 세로 게임(Player Settings 기본 방향)이면 720x1280, 아니면 1280x720.

## 기준 이미지 (golden/)

- `tools/loop.ps1 -UpdateGolden`: 루프가 녹색이면 샷을 `golden/<Unity 버전>/<시나리오 name>/<샷 파일>.png`로 쓴다(그 폴더의 다른 PNG는 지움). 커밋한다.
- 이후 루프마다 같은 이름의 기준 이미지와 비교한다(`report.golden`: `same`/`changed`/`missing` 수, 샷마다 `shotStats[].golden`: `meanDiff`, `changedRatio`,
  `ssim`, `rect` = 바뀐 곳 `[x, y, w, h]` 픽셀(왼쪽 위 기준), `diff` = 바뀐 픽셀을 빨강으로 칠한 PNG). 같은 머신·같은 버전이면 픽셀까지 같다.
  채널 차이 24 초과 픽셀이 0.01% 넘거나 평균 차이가 0.5 넘으면 `changed`. 실패로 치지 않는다.
- 에디터의 Asynchronous Shader Compilation이 켜져 있으면 임포트 직후·새 머신의 첫 캡처에서 셰이더가 컴파일 중인 오브젝트가 빠진다(`shotStats[].shadersCompiling`).
  기준 이미지를 쓰려면 끈다: `& ./tools/uc.ps1 harness_setup '{"apply":"syncShaders"}'`(Editor 설정 한 줄; 그동안 그 프레임이 컴파일을 기다린다).
- Unity 버전마다 따로 둔다(렌더가 다르다). 그 버전 폴더가 없으면 같은 major.minor의 가장 가까운 버전 것을 쓴다(`golden.from`).
- 매번 다른 글자(시계, 네트워크 값)가 있는 샷은 캡처에 `"ignore": [{ "x", "y", "w", "h" }]`(이미지 비율, 왼쪽 위 기준)로 그 영역을 빼거나 `"golden": false`.
  `"screen"` 샷(Game 뷰 크기)은 비교하지 않는다. `-NoPlay`의 샷은 `golden/<버전>/capture/`.

## 규칙 (기존 프로젝트)

1. 씬·프리팹·ScriptableObject(`.unity/.prefab/.asset`) YAML을 손으로 고치지 않는다. 에디터 API로 고친다
   (`& ./tools/uc.ps1 eval_file '{"file":"HarnessOut/scripts/x.cs"}'`, 저장은 `EditorSceneManager.SaveScene`/`AssetDatabase.SaveAssets`).
   C#·셰이더·UXML/USS·JSON 같은 텍스트 에셋은 직접 고친다.
2. 하네스는 프로젝트 설정을 바꾸지 않는다. `& ./tools/uc.ps1 harness_setup`은 권장 사항만 보여 주고, 바꾸려면 명시한다:
   `harness_setup '{"apply":"domainReload"}'`(플레이 진입이 빨라지지만 모든 가변 static을 SubsystemRegistration에서 초기화해야 한다),
   `'{"apply":"syncShaders"}'`(셰이더를 동기 컴파일 — 임포트 직후의 캡처에도 모든 오브젝트가 찍힌다).
3. 에디터 조작은 `tools/*.ps1`로만(프로젝트별 락). `unity command`로 직접 play/build를 부르지 않는다.
4. 여러 에이전트가 동시에 일하면 `ProjectSettings/AgentHarness.json`의 `modules`에 모듈 폴더를 등록하고, 각자 git worktree에서
   `tools/submit.ps1 -Module <이름>` → 커밋 → `tools/land.ps1`. 에러의 `module`도 이 등록으로 채워진다. asmdef 없는 폴더도 된다
   (submit의 사전 컴파일 검사가 그 폴더가 들어가는 `Assembly-CSharp`을 검사한다).
   루프를 서로 기다리지 않으려면 worktree에서 `tools/open.ps1 -Own`: 그 worktree에 에디터 트리 `Library/`의 사본을 두고 창 없는 에디터를 따로 띄운다
   → 그 worktree의 `loop.ps1`·`uc.ps1`·`quit.ps1`은 그 에디터를 쓰고 동시에 돈다(submit·land는 여전히 에디터 트리로). 비용: 에디터 하나당 메모리
   ~2 GB, `Library/` 크기만큼 디스크, 처음 열 때 스크립트 전체 재컴파일. 끝나면 그 worktree에서 `quit.ps1` 뒤 worktree를 지운다.
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
  "captureSize": [ 720, 1560 ],            // 크기를 주지 않은 캡처의 크기(없으면 세로 게임 720x1280, 그 외 1280x720)
  "goldenRoot": "golden",                  // 기준 이미지 폴더(프로젝트 루트 기준)
  "generatedRoot": "Assets/AgentHarness/Generated", "buildScene": "Assets/AgentHarness/Main.unity",   // 코드 빌더(IBuildStep)를 쓸 때
                                           // (렌더 설정 코드 ISettingsStep은 "setup": "harness"에서만 적용 - 이 프로젝트의 RP 에셋은 그대로)
  "installAdded": [], "installReplaced": []   // 설치가 더한/올린 의존성(uninstall이 되돌림) - 손대지 않는다
}
```

## 문제 해결

- `open.ps1`의 `dialog`: 에디터가 시작 전 대화상자(이용 약관, 패키지 에러 등)에 막혀 있다 → 사람에게 답을 부탁하고 `open.ps1`을 다시 부른다(그 에디터를 기다린다).
  `deprecatedPackages`가 함께 나오면 Unity가 열 때마다 묻는 지원 종료 패키지다 → `Packages/manifest.json`에서 빼거나 바꾼다.
- `open.ps1`·루프의 `safeMode` + `compileErrors`: 시작할 때 스크립트가 컴파일되지 않아 창 있는 에디터가 Safe Mode로 들어갔다(`-automated`라 묻지 않는다,
  하네스와 연결되지 않는다) → 에러를 고치고 `tools/quit.ps1 -Force` → `tools/open.ps1`. 또는 `open.ps1 -Headless`(그래도 떠서 루프가 에러를 보고한다).
- `stage=editor`: 에디터가 없거나 응답이 없다 → `tools/open.ps1`.
- `stage=shots`(`blank`): 화면이 비었다. `hint`와 `cameras`를 본다(메인 카메라가 텍스처에 그리는 게임이면 화면에 그리는 카메라를 `"camera"`로). 편집 모드(`-NoPlay`)에서는 플레이 중에만
  그리는 게임이 검게 나온다. 카메라가 런타임에 생기는 게임이면 메인 카메라가 없다고 나온다.
- `stage=play` + `waitScene/waitTarget ... after Ns`: 그 씬·요소가 제한 시간 안에 나오지 않았다. 에러 메시지의 로드된 씬 목록과 `Logs/Editor.log`를 본다
  (게임이 로그인·입력을 기다리는 중일 수 있다 → 그 버튼을 `waitTarget` + `click`).
- `unsaved changes in ...`: 에디터에 저장 안 한 씬이 있어 하네스가 씬을 바꾸지 않았다.
- `nothing takes scenario input`: 구 Input Manager 게임에 입력 이벤트를 넣었다 → 위 "구 Input Manager" 참고.

## 제거

`powershell -ExecutionPolicy Bypass -File tools/uninstall.ps1` (에디터를 닫은 뒤). 설치가 더한 것만 지운다:
패키지 의존성(올린 버전은 원래대로), `tools/`의 진입점·기본 시나리오·이 문서, 설정 파일, 설치가 만든 `CLAUDE.md`, `HarnessOut/`.
`Assets/AgentHarness/HarnessInput.cs`는 게임 코드가 쓰고 있으면 남긴다. 커밋한 `golden/`은 프로젝트의 것이라 남긴다.
