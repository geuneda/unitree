# 업스트림 신고서 초안

ROADMAP "상시"의 신고 항목을 바로 낼 수 있게 정리한 것이다. 본문(영어)은 그대로 붙여 넣는다.

- **Unity 엔진·URP** → 에디터 메뉴 Help > Report a Bug...(Unity Bug Reporter). 이메일과 재현 프로젝트(아래 "재현")가 필요하다.
  하네스 저장소 전체를 첨부하지 말고 항목별 최소 재현 프로젝트를 첨부한다.
- **Pipeline 패키지**(`com.unity.pipeline` 0.8.0-exp.1, 실험판) → 공개 저장소가 없다(`package.json`의 repository는 Unity 내부 GitHub).
  Unity Discussions에 글을 올린다(로그인 필요).
- 상태: **재현 확인** = 최소 재현 스크립트로 확인함(그대로 낼 수 있음), **원인 확인** = 하네스에서 원인까지 확인했고 재현 절차를 적었지만 최소 프로젝트로는
  아직 돌려 보지 않음(내기 전에 한 번 돌린다), **재현 필요** = 하네스에서만 봤다(빈 프로젝트로 줄이기 전에는 내지 않는다).
- 고쳐진 버전이 나오면 ROADMAP "상시"에 적힌 대로 하네스의 우회를 걷어내고 매트릭스로 확인한다.

| ID | 대상 | 제목 | 상태 | 하네스 우회 |
|---|---|---|---|---|
| U1 | Unity (TextCore) | Kerning pairs from FontEngine carry uninitialized featureLookupFlags | 재현 확인 — [`upstream/KerningFlagsRepro.cs`](upstream/KerningFlagsRepro.cs), 6.3·6.6 Mac 독립 배치; W19 Windows 6.3은 같은 본문을 Editor에서 확인(독립 배치는 남음) | `Runtime/KerningFlags.cs` (W18, G3-17; Windows 기준 이미지 W19) |
| U2 | Unity | `Camera.RenderToCubemap(Cubemap)` leaves CPU pixels unfilled (6.6) / sRGB-encoded (6.3) | 원인 확인 | 큐브 RT + `AsyncGPUReadback` (W4, P-4) |
| U3 | URP 17.3 | Screen Space decal pass throws in `RenderingUtils.SetScaleBiasRt` for a camera without an intermediate texture | 원인 확인 | `CaptureUi.RenderWithoutFeatures` (W13) |
| U4 | URP 17.3 | DBuffer decal edge pixels differ depending on whether the Editor GUI drew first | 재현 필요 | 샘플은 ScreenSpace 데칼 (W13, G3-15) |
| U5 | URP | A camera's first render inherits state from a camera with other near/far planes | 재현 필요 | 같은 카메라로 두 번 렌더 (W15) |
| U6 | Unity (build) | Incremental player build keeps the previous build's `ScriptingAssemblies.json` | 재현 필요 | `player.ps1`의 `CleanBuildCache` 재빌드 (W8) |
| U7 | Unity (Search) | `SearchInit.IndexationOnStartup` ArgumentOutOfRangeException after a domain reload | 재현 필요 | `editorErrors`로만 분류 (O-6) |
| U8 | Unity | A -batchmode Editor draws a mesh wrong the first time it is drawn in the session | 재현 필요 | batchmode면 두 번 렌더 (O-11) |
| U9 | Unity | `Application.isFocused` stays true after a domain reload while another app is in front | 재현 필요 | 포커스 판정에 쓰지 않음 (O-13) |
| P1 | Pipeline | `RuntimeInputCommand.cs` compiles Input System code under `ENABLE_INPUT_SYSTEM` without the package | 원인 확인 | install이 Input System 추가 (O-1) |
| P2 | Pipeline | Release builds carry `Unity.Pipeline.Attributes` and `Newtonsoft.Json` | 원인 확인 | `HarnessReleaseBuild` (O-1) |
| P3 | Pipeline | Code reload interpreter: `try/catch` is not supported | 원인 확인 | 핫 루프가 전체 루프로 (G2-5) |
| P4 | Pipeline | Code reload interpreter: an exception in a replaced body is logged without a line and the original body runs on | 원인 확인 | 전체 루프가 줄을 다시 얻음 (G2-5) |
| P5 | Pipeline | `build_status` errors drop the line that `BuildMessage.Parse` read | 원인 확인 | `player.ps1`이 빌드 단계 메시지를 직접 파싱 (O-14) |
| P6 | Pipeline | After a domain reload the descriptor's token can lag the server's for ~14 s (401) | 재현 필요 | 없음 (O-12) |

---

## U1. Kerning pairs from FontEngine carry uninitialized featureLookupFlags (TextCore, macOS and Windows)

**Environment**: Unity 6000.3.11f1 and 6000.6.3f1, macOS 26.7, Apple M4 Pro (Metal Editor); Unity 6000.3.11f1, Windows 11 (D3D11 Editor).

**What happens**: The `GlyphPairAdjustmentRecord`s that a dynamic `FontAsset` gets from `FontEngine.GetPairAdjustmentRecords`
(`FontAsset.UpdateGlyphAdjustmentRecords`, used for glyphs added at runtime) have garbage in `featureLookupFlags`. On macOS, about a
quarter of the records hold values such as `0x100B6`, `0x10588`, `0x470`, `0x7261`, different in every Editor session. The
text generator honours `FontFeatureLookupFlags.IgnoreSpacingAdjustments` (0x100) of a pair (`TextGenerator`: the character
spacing of the pair becomes 0), so text with `letter-spacing` loses its spacing after random pairs, differently from session
to session. TextMesh Pro checks the same flag (`TMP_Text`, `TextMeshPro`) and gets its runtime pairs from the same FontEngine call.
UI Toolkit text is affected with the Standard text generator (the default in 6.0-6.4); 6.6's default Advanced Text Generator
does not read these records.

Observed in a UI Toolkit runtime panel with the default runtime font (NotInter): a 13 px bold label with `letter-spacing: 3px`
("AGENT HARNESS / SMOKE") was 0.27 px narrower after "AG" and after "GE" in some Editor sessions and not in others (the
"AG" record's flags were 0x100B6 in one session, 0x10588 in the next). Pixel comparisons of screenshots between sessions fail
on every label with letter spacing.

Windows follow-up (2026-10-02, 6000.3.11f1): all 5,492 pairs of a dynamic NotInter lookup held `0x9E41433F`, including
IgnoreSpacingAdjustments. The previous Windows golden images consistently contained that incorrect spacing. In fresh
Editor sessions, disabling the workaround reproduced those images pixel for pixel; flushing the font queue and marking
all text dirty without clearing flags also reproduced them. Restoring the workaround reproduced the corrected images
pixel for pixel across sessions. The AG/LA/PA/AC records changed only in flags (to None); placement and advance were unchanged.

**Expected**: Pair adjustment records that come from a font file have `featureLookupFlags == None` (the font has no such flag;
it is set by hand on a font asset).

**Steps to reproduce** (attached project: an empty project plus one Editor script, `Assets/Editor/KerningFlagsRepro.cs`):
1. Run three times: `Unity -batchmode -quit -projectPath <project> -executeMethod KerningFlagsRepro.Run -logFile -`
2. Each run creates a dynamic font asset of the OS font Arial (`FontAsset.CreateFontAsset("Arial", "Regular", 90)`), adds
   A-Z and a-z with font features (`TryAddCharacters(..., includeFontFeatures: true)`), and logs how many of its pair records
   have flags.

**Actual** (macOS, three runs each):

| Unity | pairs | flags not None | with IgnoreSpacingAdjustments | sample values |
|---|---|---|---|---|
| 6000.3.11f1 | 96 | 48, 50, 58 | 21, 20, 14 | `0x1503D5`, `0x65006E6F`, `0x6E006374`, `0x29`, `0x1` |
| 6000.6.3f1 | 96 | 40, 41, 54 | 5, 16, 24 | `0x5A5D0091`, `0x370006`, `0xFFFFFFFF`, `0x66002000`, `0x2` |

(`0x6E69676E` = "ngin" and `0x65006E6F` look like string memory.)

**Windows follow-up**: running the same Arial creation/inspection body once via `eval_file` in the sample Editor
(6000.3.11f1) returned 353 pairs, all 353 nonzero and all 353 with IgnoreSpacingAdjustments, all `0x9E41433F`.
The Windows empty-project, independent batch-process reproduction has not been run yet.

**Expected**: `flagsNotNone=0` every run.

**Workaround**: clear `featureLookupFlags` of the dynamic font assets' records (`FontFeatureTable.glyphPairAdjustmentRecords`
and `m_GlyphPairAdjustmentRecordLookup`, internal) and mark the text dirty (`TextElement.MarkDirtyText`).

## U2. `Camera.RenderToCubemap(Cubemap)` leaves CPU pixels unfilled (6.6) or sRGB-encoded (6.3)

**Environment**: Unity 6000.6.3f1 and 6000.3.11f1, URP 17.6 / 17.3, Windows 11 (D3D12).

**What happens**: `Camera.RenderToCubemap(Cubemap cubemap)` with a linear `RGBAHalf` cubemap returns true, and:
- 6000.6.3f1: the cubemap's CPU pixels are not written (they keep uninitialized memory, e.g. half `0xCDCD` = -23.2, or zeros in
  the same session); the GPU copy is right, so the first use looks fine and a saved asset is garbage.
- 6000.3.11f1: the CPU pixels are written sRGB-encoded although the cubemap is linear (linear 0.071 comes back as 0.298).

**Steps to reproduce**: empty scene with a procedural skybox; create `new Cubemap(128, TextureFormat.RGBAHalf, false)`, call
`camera.RenderToCubemap(cube)`, read `cube.GetPixels(CubemapFace.PositiveX)` and compare with a readback of a cube
RenderTexture rendered by the same camera (`RenderToCubemap(RenderTexture)` + `AsyncGPUReadback`).

**Expected**: the CPU pixels equal the GPU result, linear.

## U3. URP 17.3 Screen Space decal pass throws for a camera without an intermediate texture

**Environment**: Unity 6000.3.11f1, URP 17.3, Windows 11 (D3D12).

**What happens**: With a Decal renderer feature using the Screen Space technique, rendering a camera that draws straight into
its target texture (no post-processing, HDR off, MSAA off, no depth/opaque texture) throws `NullReferenceException` in
`RenderingUtils.SetScaleBiasRt` from `DecalScreenSpaceRenderPass` ("Render Graph Execution error"):
`resourceData.cameraColor` is not valid for such a camera.

**Steps to reproduce**: URP renderer with a Decal feature (Technique: Screen Space); a camera with `targetTexture` set and all of
the above off; `camera.Render()`.

**Expected**: no exception (the pass skipped or given the target).

## U4–U9 (재현 필요)

- **U4** DBuffer 데칼: 플레이 중 `SubmitRenderRequest`로 그린 카메라의 데칼 가장자리 픽셀이 그 앞에 에디터 GUI가 그렸는지에 따라 2–3단계 다르다(SMAA가 키움).
  `InternalEditorUtility.RepaintAllViews()`를 매 업데이트마다 부르면 10/10, ScreenSpace 데칼은 0/8. 빈 프로젝트(데칼 하나 + 캡처 카메라)로 줄여서 낸다. (ROADMAP G3-15)
- **U5** 카메라 상태: near/far가 다른 카메라 뒤 첫 하늘 렌더의 태양 원반 가장자리 텍셀이 half 1–6단계 다르다. 하늘만 있는 씬에서 near/far가 다른 카메라 둘로
  `RenderToCubemap`. (ROADMAP W15)
- **U6** 증분 플레이어 빌드: 출시 빌드 뒤 다른 폴더로 개발 빌드 → "player data was not rebuilt"로 `ScriptingAssemblies.json`이 출시 빌드 것(define 제약으로
  어셈블리 집합이 다름). 6.0 Fluid-Sim에서 재현. (ROADMAP W8)
- **U7** Unity Search: 도메인 리로드 직후 `UnityEditor.Search.SearchInit.IndexationOnStartup` ArgumentOutOfRangeException. 스택과 빈도를 모아서. (O-6)
- **U8** `-batchmode` 에디터의 첫 메시 그리기가 쓰레기 값(매번 다른 색), 두 번째는 정상. 빈 프로젝트 재현부터. (O-11)
- **U9** 다른 창이 앞에 있는 채 도메인 리로드 → `Application.isFocused`·`InternalEditorUtility.isApplicationActive` true. 빈 프로젝트 재현부터. (O-13)

## P1. `RuntimeInputCommand.cs` uses Input System types under `ENABLE_INPUT_SYSTEM` alone

**Package**: com.unity.pipeline 0.8.0-exp.1.

**What happens**: In a project whose Active Input Handling is "Input System Package (New)" or "Both" but which does not have the
`com.unity.inputsystem` package, `Runtime/Commands/RuntimeInputCommand.cs` does not compile: its blocks are under
`#if ENABLE_INPUT_SYSTEM` (set by the player setting), while the assembly's own `PIPELINE_HAS_INPUT_SYSTEM_PACKAGE`
(versionDefines on Unity.Pipeline.asmdef) is used only in one place. The Editor then stops at the Safe Mode dialog.

**Expected**: the Input System code under `#if ENABLE_INPUT_SYSTEM && PIPELINE_HAS_INPUT_SYSTEM_PACKAGE`.

## P2. Release builds carry `Unity.Pipeline.Attributes` and `Newtonsoft.Json`

**What happens**: A non-development player build of a project that has the package includes `Unity.Pipeline.Attributes.dll`
and `Newtonsoft.Json.dll` even when no game code uses them.

**Expected**: the package's runtime assemblies restricted to the Editor and development builds (define constraints), or
documented as part of release builds.

## P3. Code reload interpreter: `try/catch` is not supported

**What happens**: The editor interpreter behind `reload_file_editor_interpreter` does not support `try/catch` in a
`[CodeReload]` method body, so any body with exception handling needs a full recompile and domain reload.

## P4. Code reload interpreter: exceptions in a replaced body lose their line and the original body runs on

**What happens**: When a replaced body throws, the interpreter logs the exception (message prefixed `CodeReload:`) without a
file/line and the method continues with its original (compiled) body, so the game keeps running code the user replaced.

**Expected**: the exception reported with the source line of the replaced body, and the call failing like compiled code.

## P5. `build_status` errors drop the line

**What happens**: `BuildIssue.From` (`Editor/Commands/Build/BuildModels.cs`) parses `File.cs(line,col): message` with
`BuildMessage.Parse` but keeps only `File` and `Message`: the `errors[]` of `build_status` have no line. A compile error that
only the player build hits (an API missing on the target platform) is reported without its line.

**Expected**: `line` (and column) in `BuildIssue`.

## P6. (재현 필요) 도메인 리로드 뒤 디스크립터 토큰 지연

리로드 뒤 ~14–17 s 동안 모든 요청이 401("Editor token rotated by a domain reload") — `Library/Pipeline/.unity-pipeline-port`의 토큰이 새 서버 토큰보다 늦게
바뀐다. `SecurityTokenManager.GetOrCreateToken`·`CreateInstanceDescriptor`·`UpdateHeartBeat` 시점 조사부터. (O-12)
