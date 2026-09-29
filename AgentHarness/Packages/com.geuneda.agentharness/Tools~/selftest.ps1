<#
.SYNOPSIS
  Harness self-test (O-3): the verification matrix of docs/ROADMAP.md as one script. Run it on the Editor tree after
  changing the harness, and per Unity version in a fresh clone (tools/fresh-clone-test.ps1 -SelfTest, P-1).

  1 loop x3: green, same build.fingerprint and play.events (-ExpectFingerprint), no blank/dark/magenta shots, every
    shot 1280x720 with the HUD composited (G3-1); golden images (G3-4): loop 1 writes them (-UpdateGolden, to
    HarnessOut), loops 2-3 are the same pixel for pixel, the committed goldens of this Unity version (if any) are the
    same, and harness_golden leaves "ignore" regions (from the top left) out and falls back to another patch of the
    same major.minor; compile-check of every assembly (csc, the Editor's response files);
    one more loop with the scenario tools of P-5 (waitScene, waitTarget, a UI Toolkit click by name, KeyCode key names,
    a capture from a pose and one from a named camera); screen-space uGUI (G3-1) on a fixture that is never saved: an
    overlay, a Screen Space - Camera canvas of the main camera and one of a UI camera in its stack composited in order,
    blended in linear space, and put back; cameras (G3-7) on a fixture: an overlay camera in the main camera's stack
    (child of it, drawing a quad) seen from the main camera and from another pose, a minimap Base camera drawn after
    the main camera in its viewport, one before it covered, a capture from the minimap camera by name drawing it alone,
    everything put back; a play-mode fixture: a capture sequence (G3-3, contact sheet, motion) with an overlay canvas
    and a stack camera made during the play; real input (G3-6): Space pressed on the real keyboard during a loop leaves
    play.events as they were, and the real devices, disabled while a scenario plays, are enabled again after it, after
    a failed play and after a stopped one
  2 C# compile error in Smoke -> stage=compile at the injected file/line, module Smoke; reverted -> green
  3 runtime exception in Smoke -> stage=runtime at the injected line; reverted -> green
  4 HLSL error in the Smoke shader -> stage=shader at the injected line, again in the next loop (no reimport);
    reverted -> green (and the golden images of item 4). A one-line shader change (specular halved) -> golden
    "changed" with a diff image, loop green. A material the pipeline cannot draw (Standard in URP, G3-5) -> magenta
    shots whose hint names the renderer and whose golden changed, loop still green; reverted -> no magenta, golden same
  5 mutable static without a reset -> stage=lint (static-reset); removed -> green
  6 two loops at once -> both green, one of them waited for the lock
  7 worktrees + submit.ps1: the compile-check gate refuses a broken module without touching the Editor tree; a forced
    broken submit is reverted while another worktree's new module + contract waits for the lock and is kept; a runtime
    error submit is reverted; a submit killed after its sync is rolled back by the next loop; a changed contract is
    refused
  8 land.ps1: fast-forward land while another worktree's submit waits for the lock; already landed; refusals
    (uncommitted, missing .meta, conflict, a foreign edit in the Editor tree); a compile error land is undone; a land
    killed after its merge is undone by the next loop; the last land is kept
  Stops at the first red item (-KeepGoing: runs the rest too). Every item puts back what it changed. 7-8 need tools/, the harness package and
  Assets/Game/ committed (the worktrees run the committed code); they create temporary worktrees next to the
  repository, branches selftest/*, and commits, and reset the Editor tree's branch to where it was. A final loop
  checks that the Editor tree is green again with the git status it had.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/selftest.ps1
  powershell -ExecutionPolicy Bypass -File tools/selftest.ps1 -Only 1,2,3,4,5,6 -ExpectFingerprint 5887385e

  Exit code 0 = every item green. Report: HarnessOut/selftest/report.json (also on stdout); the report of every
  loop/submit/land it ran under HarnessOut/selftest/<item>-<step>/. Open item 1's shots (report.shots) with Read.
#>
param(
    [string]$Only = '1,2,3,4,5,6,7,8',   # a string: 'powershell -File ... -Only 1,2' passes "1,2" as one argument
    [string]$ExpectFingerprint,
    [string]$Out = 'HarnessOut/selftest',
    [switch]$KeepGoing   # run the remaining items after a red one (e.g. to see everything that differs in another Unity version)
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
$root = Get-HarnessProjectRoot
$outAbs = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path (Get-HarnessWorkRoot) $Out }
$clock = [Diagnostics.Stopwatch]::StartNew()
$items = @($Only -split ',' | Where-Object { $_.Trim() } | ForEach-Object { [int]$_.Trim() } | Sort-Object -Unique)
$ps = Get-HarnessPowerShell
# Worktrees find the Editor tree themselves (git worktree list); never an Editor this variable may point at.
$childEnv = @{ AGENTHARNESS_EDITOR_ROOT = $null; AGENTHARNESS_WORK_ROOT = $null }
$gitId = @('-c', 'user.name=AgentHarness selftest', '-c', 'user.email=selftest@agentharness.invalid')

$SmokeCs = 'Assets/Game/Smoke/SmokeModule.cs'
$SmokeShader = 'Assets/Game/Smoke/Shaders/SmokeIridescent.shader'
$SmokeContracts = 'Assets/Game/Contracts/SmokeEvents.cs'
$ProbeCs = 'Assets/Game/Probe/ProbeModule.cs'
$SubmitJournal = Join-Path $root 'Library/Harness/submit/pending.json'
$LandJournal = Join-Path $root 'Library/Harness/land/pending.json'
# Injections: a marker line of the sample modules and what replaces it on that line.
$CompileMarker = 'm_Time += dt;'
$CompileBroken = 'm_Time += selftestUndefined;'
$RuntimeMarker = 'EventBus.Publish(new SpinnerLap(laps));'
$RuntimeBroken = 'if (laps == 1) throw new InvalidOperationException("selftest: runtime error"); EventBus.Publish(new SpinnerLap(laps));'
$ShaderMarker = 'float3 n = normalize(i.normalWS);'   # the first one in the forward pass's fragment function
$ShaderAnchor = 'half4 Frag(Varyings i) : SV_Target'
$ShaderBroken = 'float3 n = normalize(i.selftestMissing);'
$SmokeBuilder = 'Assets/Game/Smoke/Builders/SmokeBuildStep.cs'
$MagentaMarker = '"PedestalStone", "Universal Render Pipeline/Lit"'
$MagentaBroken = '"PedestalStone", "Standard"'   # a Built-in pipeline shader: URP draws it with its error material
$VisualMarker = 'half spec = pow(saturate(dot(n, h)), _Gloss) * atten;'
$VisualChanged = 'half spec = pow(saturate(dot(n, h)), _Gloss) * atten * 0.5h;'   # a valid one-line change: specular halved

$report = [ordered]@{ ok = $false; stage = ''; project = $root.Replace('\', '/'); projectVersion = (Get-HarnessProjectVersion); unityVersion = $null
    items = @(); fingerprint = $null; events = $null; lines = [ordered]@{}; shots = @() }
$state = @{ item = $null; itemClock = $null; saved = [ordered]@{}; tools = New-Object System.Collections.ArrayList; wt = $null }

# ---- results ------------------------------------------------------------------------------------------------------
function Start-Item([int]$N, [string]$Name) {
    $state.item = [ordered]@{ item = $N; name = $Name; ok = $true; sec = 0; checks = @() }
    $state.itemClock = [Diagnostics.Stopwatch]::StartNew()
    [Console]::Error.WriteLine("selftest: $N $Name")
}

function Test-Check([string]$What, [bool]$Condition, $Actual = $null) {
    $c = [ordered]@{ check = $What; ok = $Condition }
    if (-not $Condition -or $null -ne $Actual) { $c['actual'] = $Actual }
    $state.item.checks += , $c
    if (-not $Condition) { $state.item.ok = $false; [Console]::Error.WriteLine("selftest:   FAILED $What ($Actual)") }
}

function Complete-Item {
    if (-not $state.item) { return }
    $state.item.sec = [math]::Round($state.itemClock.Elapsed.TotalSeconds, 2)
    $report.items += , $state.item
    $ok = $state.item.ok
    $state.item = $null
    if (-not $ok) { throw 'item failed' }
}

# ---- tools -------------------------------------------------------------------------------------------------------
# A tools/*.ps1 of a checkout (the Editor tree or a worktree), started in a child PowerShell. Reports are read from the
# -Out folder each call gets (never from stdout).
function Start-Tool([string]$Project, [string]$Name, [string[]]$Arguments = @()) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $ps
    $psi.Arguments = (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Project "tools/$Name")) + $Arguments | ForEach-Object { ConvertTo-HarnessArg $_ }) -join ' '
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($k in $childEnv.Keys) { $psi.EnvironmentVariables.Remove($k) }
    $p = [Diagnostics.Process]::Start($psi)
    $p.StandardInput.Close()
    $t = [pscustomobject]@{ proc = $p; out = $p.StandardOutput.ReadToEndAsync(); err = $p.StandardError.ReadToEndAsync(); name = $Name }
    [void]$state.tools.Add($t)
    $t
}

function Wait-Tool($Tool, [int]$TimeoutSec = 900) {
    if (-not $Tool.proc.WaitForExit($TimeoutSec * 1000)) { try { $Tool.proc.Kill() } catch { }; [void]$Tool.proc.WaitForExit(15000) }
    $Tool.proc.WaitForExit()
    $state.tools.Remove($Tool)
    [pscustomobject]@{ code = $Tool.proc.ExitCode; err = $(if ($Tool.err.Wait(5000)) { $Tool.err.Result.Trim() } else { '' }) }
}

function Stop-Tool($Tool) {
    try { if (-not $Tool.proc.HasExited) { $Tool.proc.Kill() } } catch { }
    [void]$Tool.proc.WaitForExit(15000)
    $state.tools.Remove($Tool)
}

function Get-OutDir([string]$Tag) {
    $d = Join-Path $outAbs $Tag
    if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }
    $d
}

function Read-ToolReport([string]$Dir, $Result) {
    $f = Join-Path $Dir 'report.json'
    if (Test-Path -LiteralPath $f) { return [IO.File]::ReadAllText($f) | ConvertFrom-Json }
    [pscustomobject]@{ ok = $false; stage = 'no-report'; error = "no report.json (exit $($Result.code)): $($Result.err)" }
}

# Runs a tool to the end: its report.
function Invoke-Tool([string]$Project, [string]$Name, [string]$Tag, [string[]]$Arguments = @()) {
    $dir = Get-OutDir $Tag
    $r = Wait-Tool (Start-Tool $Project $Name (@($Arguments) + @('-Out', $dir)))
    Read-ToolReport $dir $r
}

function Invoke-Loop([string]$Tag, [string[]]$Arguments = @()) { Invoke-Tool $root 'loop.ps1' $Tag $Arguments }

function Wait-Until([scriptblock]$Condition, [int]$TimeoutSec = 60, $Tool = $null) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        if (& $Condition) { return $true }
        if ($Tool -and $Tool.proc.HasExited) { return [bool](& $Condition) }
        Start-Sleep -Milliseconds 100
    }
    $false
}

# A loop/submit/land report in one line; what went unnoticed (events, editorErrors) is what explains a surprise.
function Get-Summary($r) {
    $s = "stage=$($r.stage) $($r.error)".Trim()
    if ($r.stage -notin @('done', 'no-report')) { return $s }
    $ee = @($r.editorErrors | ForEach-Object { "$($_.msg)".Substring(0, [math]::Min(120, "$($_.msg)".Length)) })
    "$s events=$(Get-Events $r)$(if ($ee.Count) { " editorErrors=[$($ee -join ' | ')]" })"
}

# ---- files -------------------------------------------------------------------------------------------------------
function Get-Abs([string]$Base, [string]$Rel) { [IO.Path]::Combine($Base, $Rel) }

# Remember an Editor-tree file (or that it did not exist) before changing it; Restore-EditorFiles puts it back.
function Protect-EditorFile([string]$Rel) {
    if ($state.saved.Contains($Rel)) { return }
    $abs = Get-Abs $root $Rel
    $state.saved[$Rel] = if ([IO.File]::Exists($abs)) { [IO.File]::ReadAllBytes($abs) } else { $null }
}

function Restore-EditorFiles {
    foreach ($rel in @($state.saved.Keys)) {
        $abs = Get-Abs $root $rel
        $bytes = $state.saved[$rel]
        if ($null -eq $bytes) {
            foreach ($p in @($abs, "$abs.meta")) { if ([IO.File]::Exists($p)) { [IO.File]::Delete($p) } }
        } else {
            [IO.File]::WriteAllBytes($abs, $bytes)
            [IO.File]::SetLastWriteTimeUtc($abs, [DateTime]::UtcNow)
        }
    }
    $state.saved.Clear()
}

# Replace $Marker on the one line that has it (with -After: the first one below the one line that has $After);
# returns that line's number. Keeps the BOM and line endings.
function Edit-Line([string]$Abs, [string]$Marker, [string]$Replacement, [string]$After) {
    $bytes = [IO.File]::ReadAllBytes($Abs)
    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $enc = New-Object Text.UTF8Encoding($bom)
    $text = $enc.GetString($bytes, $(if ($bom) { 3 } else { 0 }), $bytes.Length - $(if ($bom) { 3 } else { 0 }))
    $lines = $text.Split("`n")
    $from = 0
    if ($After) {
        $a = @(for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].Contains($After)) { $i } })
        if ($a.Count -ne 1) { throw "$Abs has $($a.Count) lines with '$After' (expected 1)" }
        $from = $a[0] + 1
    }
    $hits = @(for ($i = $from; $i -lt $lines.Count; $i++) { if ($lines[$i].Contains($Marker)) { $i } })
    if ($After) { $hits = @($hits | Select-Object -First 1) }
    if ($hits.Count -ne 1) { throw "$Abs has $($hits.Count) lines with '$Marker' (expected 1)" }
    $lines[$hits[0]] = $lines[$hits[0]].Replace($Marker, $Replacement)
    [IO.File]::WriteAllText($Abs, ($lines -join "`n"), $enc)
    [IO.File]::SetLastWriteTimeUtc($Abs, [DateTime]::UtcNow)
    $hits[0] + 1
}

function Write-TextFile([string]$Abs, [string]$Text) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Abs))
    [IO.File]::WriteAllText($Abs, $Text.Replace("`r`n", "`n"), (New-Object Text.UTF8Encoding($false)))
}

function Get-Events($r) { (@($r.play.events | ForEach-Object { "$($_.name)=$($_.count)" }) -join ',') }
function Get-EventCount($r, [string]$Name) { $e = @($r.play.events | Where-Object { $_.name -eq $Name }); if ($e.Count) { [int]$e[0].count } else { 0 } }
function Get-FirstError($r, [string]$Kind = $null) {
    $e = @($r.compileErrors | Where-Object { -not $Kind -or $_.kind -eq $Kind })
    if ($e.Count) { $e[0] } else { [pscustomobject]@{ file = ''; line = 0; msg = ''; module = ''; kind = '' } }
}

# Editor tree git status (repo-relative path -> XY) as one comparable string.
function Get-EditorStatus { (@((Get-HarnessGitStatus $root).GetEnumerator() | ForEach-Object { "$($_.Value) $($_.Key)" } | Sort-Object) -join '; ') }
# Status entries that were not there when 7 started ("XY path").
function Get-NewStatus { @((Get-HarnessGitStatus $root).GetEnumerator() | ForEach-Object { "$($_.Value) $($_.Key)" } | Where-Object { $state.wt.base -notcontains $_ } | Sort-Object) }
function Get-Head([string]$Dir) { (Invoke-HarnessGit $Dir @('rev-parse', 'HEAD') -Check).out.Trim() }
function Invoke-Git([string]$Dir, [string[]]$Arguments) { [void](Invoke-HarnessGit $Dir $Arguments -Check) }

# A reverted change must leave the Editor tree green again.
function Test-GreenAgain([string]$Tag, [string[]]$Arguments = @()) {
    $r = Invoke-Loop $Tag $Arguments
    Test-Check 'green again after the revert' ([bool]$r.ok) (Get-Summary $r)
    $r
}

# Golden comparison per shot of a loop report: "name:status" (G3-4).
function Get-Golden($r) { @($r.shotStats | ForEach-Object { "$($_.name):$(if ($_.golden) { $_.golden.status } else { 'none' })" }) }
function Get-GoldenDetail($r) { (@($r.shotStats | ForEach-Object { "$($_.name) $(if ($_.golden) { $_.golden | ConvertTo-Json -Compress })" }) -join ' | ') }

# harness_golden under the Editor lock: its result.
function Invoke-GoldenCommand([hashtable]$Params) {
    [void](Enter-HarnessLock)
    try { $r = Invoke-UnityCommand -Name 'harness_golden' -Params $Params -TimeoutSec 120 } finally { Exit-HarnessLock }
    if (-not $r.success) { throw "harness_golden failed: $($r.error)" }
    $r.result
}

# ---- sample module written by 7-8 (ASCII) ---------------------------------------------------------------------------
$ProbeAsmdef = @'
{
    "name": "Game.Probe",
    "rootNamespace": "Game.Probe",
    "references": [
        "Harness.Runtime",
        "Game.Contracts"
    ],
    "autoReferenced": false
}
'@
$ProbeModuleText = @'
using System;
using Game.Contracts;
using Harness;
using UnityEngine;

namespace Game.Probe
{
    // Written by tools/selftest.ps1 (matrix 7-8) in a temporary worktree; gone when the test ends.
    public sealed class ProbeModule : IGameModule
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => GameRoot.Register(new ProbeModule());

        public string Name => "Probe";
        public int Order => 50;

        IDisposable m_Sub;

        public void Init(GameContext ctx)
        {
            m_Sub = EventBus.Subscribe<SpinnerLap>(e => EventBus.Publish(new ProbeEcho(e.Lap)));
        }

        public void Tick(float dt)
        {
            // selftest-probe
        }

        public void Dispose() { m_Sub?.Dispose(); }
    }
}
'@
$ProbeEventsText = @'
namespace Game.Contracts
{
    /// <summary>Published by Probe (tools/selftest.ps1) for every SpinnerLap it receives.</summary>
    public readonly struct ProbeEcho
    {
        public readonly int Lap;
        public ProbeEcho(int lap) { Lap = lap; }
    }
}
'@
$LintText = @'
namespace Game.Smoke
{
    // Written by tools/selftest.ps1 (matrix 5): a mutable static without a SubsystemRegistration reset.
    static class SelftestLint
    {
        public static int Counter;
    }
}
'@

# Item 1's scenario-tools loop (written to HarnessOut, which git ignores).
$ScenarioToolsText = @'
{
    "name": "selftest-scenario-tools",
    "durationSec": 1.2,
    "fixedDeltaTime": 0.0166667,
    "events": [
        { "t": 0.0, "type": "waitScene", "scene": "Main" },
        { "t": 0.05, "type": "waitTarget", "target": "laps" },
        { "t": 0.1, "type": "click", "target": "laps" },
        { "t": 0.3, "type": "keyTap", "key": "Return" },
        { "t": 0.5, "type": "keyTap", "key": "Alpha1" }
    ],
    "captures": [
        { "t": 0.6, "name": "top", "pos": [0, 40, -0.01], "lookAt": [0, 0, 0], "fov": 50 },
        { "t": 0.9, "camera": "Main Camera", "name": "cam" }
    ]
}
'@

# Item 1's screen-space uGUI check (G3-1), an eval_file body: opens the play scene, adds canvases of the three kinds a
# capture composites (an overlay, a Screen Space - Camera canvas of the main camera, one of a UI camera in the main
# camera's stack - URP layers cameras with the stack; a second full-screen Base camera would cover the scene), captures
# the main camera with and without UI and reopens the scene (the fixture is never saved).
$UiFixtureText = @'
var scenePath = Harness.Editor.HarnessPaths.ResolvePlayScene("", out var sceneError);
if (scenePath == null) throw new System.InvalidOperationException("no play scene: " + sceneError);
UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
var main = Harness.HarnessCapture.FindMainCamera();
var made = new List<UnityEngine.GameObject>();
System.Func<string, UnityEngine.Transform, UnityEngine.GameObject> make = (name, parent) =>
{
    var go = new UnityEngine.GameObject(name, typeof(UnityEngine.RectTransform));
    go.layer = 5;
    if (parent != null) go.transform.SetParent(parent, false); else made.Add(go);
    return go;
};
System.Func<string, UnityEngine.RenderMode, UnityEngine.Camera, UnityEngine.Color, UnityEngine.Vector2, UnityEngine.Vector2, UnityEngine.Canvas> canvas = (name, mode, cam, color, anchor, size) =>
{
    var go = make(name, null);
    var c = go.AddComponent<UnityEngine.Canvas>();
    c.renderMode = mode; c.worldCamera = cam; c.planeDistance = 1f;
    var s = go.AddComponent<UnityEngine.UI.CanvasScaler>();
    s.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new UnityEngine.Vector2(1920, 1080); s.matchWidthOrHeight = 0.5f;
    var img = make("Image", go.transform);
    img.AddComponent<UnityEngine.UI.Image>().color = color;
    var rt = (UnityEngine.RectTransform)img.transform;
    rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
    rt.anchoredPosition = new UnityEngine.Vector2(anchor.x > 0.5f ? -40f : 40f, anchor.y > 0.5f ? -40f : 40f);
    rt.sizeDelta = size;
    return c;
};
System.Func<UnityEngine.Canvas, string> describe = c => c.renderMode + "|" + (c.worldCamera != null ? c.worldCamera.name : "-") + "|" + c.planeDistance + "|" + ((UnityEngine.RectTransform)c.transform).rect.size;
var result = new Dictionary<string, object>();
var stack = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(main).cameraStack;
UnityEngine.Camera other = null;
try
{
    var camGo = make("[selftest ui camera]", null);
    other = camGo.AddComponent<UnityEngine.Camera>();
    other.cullingMask = 1 << 5;
    UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(other).renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
    stack.Add(other);
    // (1053, 60) red at 50% over the scene, (1153, 643) green drawn with the scene, (127, 77) blue over the scene (x, y from the bottom left of 1280x720)
    var canvases = new[]
    {
        canvas("[selftest overlay]", UnityEngine.RenderMode.ScreenSpaceOverlay, null, new UnityEngine.Color(1f, 0f, 0f, 0.5f), new UnityEngine.Vector2(1f, 0f), new UnityEngine.Vector2(600f, 300f)),
        canvas("[selftest main camera canvas]", UnityEngine.RenderMode.ScreenSpaceCamera, main, UnityEngine.Color.green, new UnityEngine.Vector2(1f, 1f), new UnityEngine.Vector2(300f, 150f)),
        canvas("[selftest camera canvas]", UnityEngine.RenderMode.ScreenSpaceCamera, other, UnityEngine.Color.blue, new UnityEngine.Vector2(0f, 0f), new UnityEngine.Vector2(300f, 150f)),
    };
    UnityEngine.Canvas.ForceUpdateCanvases();
    var before = string.Join("; ", System.Array.ConvertAll(canvases, c => describe(c)));
    var doc = UnityEngine.Object.FindFirstObjectByType<UnityEngine.UIElements.UIDocument>();
    var ps = doc != null ? doc.panelSettings : null;
    var panelBefore = ps != null ? (ps.targetTexture != null) + "|" + ps.clearColor + "|" + ps.colorClearValue : "";

    var dir = "HarnessOut/selftest/ui-fixture";
    System.IO.Directory.CreateDirectory(dir);
    var t = main.transform;
    var plain = Harness.HarnessCapture.Capture(main, t.position, t.rotation, main.fieldOfView, 1280, 720, dir + "/no-ui.png", false);
    var shot = Harness.HarnessCapture.Capture(main, t.position, t.rotation, main.fieldOfView, 1280, 720, dir + "/ui.png", true);

    System.Func<string, UnityEngine.Texture2D> load = f => { var x = new UnityEngine.Texture2D(2, 2); UnityEngine.ImageConversion.LoadImage(x, System.IO.File.ReadAllBytes(f)); return x; };
    var a = load(dir + "/no-ui.png");
    var b = load(dir + "/ui.png");
    UnityEngine.Color32 under = a.GetPixel(1053, 60), red = b.GetPixel(1053, 60), green = b.GetPixel(1153, 643), blue = b.GetPixel(127, 77);
    UnityEngine.Object.DestroyImmediate(a);
    UnityEngine.Object.DestroyImmediate(b);
    // 50% red over the pixel without UI, blended in linear space (on the stored values in a Gamma color space project)
    var linear = UnityEngine.QualitySettings.activeColorSpace == UnityEngine.ColorSpace.Linear;
    System.Func<float, byte, int> over = (src, dst) => linear
        ? UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.LinearToGammaSpace(UnityEngine.Mathf.Clamp01(src + UnityEngine.Mathf.GammaToLinearSpace(dst / 255f) * 0.5f)) * 255f)
        : UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Clamp01(src + dst / 255f * 0.5f) * 255f);
    var redDiff = System.Math.Max(System.Math.Abs(red.r - over(0.5f, under.r)), System.Math.Max(System.Math.Abs(red.g - over(0f, under.g)), System.Math.Abs(red.b - over(0f, under.b))));
    result["ui"] = shot.ui;
    result["colorSpace"] = linear ? "linear" : "gamma";
    result["uiError"] = shot.uiError ?? "";
    result["error"] = (plain.error ?? "") + (shot.error ?? "");
    result["size"] = shot.width + "x" + shot.height;
    result["red"] = red.ToString() + " over " + under;
    result["redDiff"] = redDiff;
    result["green"] = green.ToString();
    result["greenOk"] = green.g > 100 && green.g > green.r + 40 && green.g > green.b + 40;
    result["blue"] = blue.ToString();
    result["blueOk"] = blue.b > 200 && blue.r < 40 && blue.g < 40;
    result["canvasesRestored"] = before == string.Join("; ", System.Array.ConvertAll(canvases, c => describe(c)));
    result["panelRestored"] = ps == null || panelBefore == (ps.targetTexture != null) + "|" + ps.clearColor + "|" + ps.colorClearValue;
    result["renderMs"] = UnityEngine.Mathf.Round(shot.renderMs);
}
finally
{
    if (other != null) stack.Remove(other);
    foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
}
return result;
'@

# Item 1's camera check (G3-7), an eval_file body: an overlay camera in the main camera's stack, child of it, drawing a red
# quad straight ahead (an FPS weapon camera); a minimap Base camera drawn after the main camera in the top right quarter
# (blue); a Base camera drawn before it (cyan, covered by the main camera's clear). Captures from the main camera, from
# another pose (the quad hangs off the main camera: same place on screen) and from the minimap camera by name (alone,
# full frame); checks that the cameras and the main camera are put back. Never saved.
$CameraFixtureText = @'
var scenePath = Harness.Editor.HarnessPaths.ResolvePlayScene("", out var sceneError);
if (scenePath == null) throw new System.InvalidOperationException("no play scene: " + sceneError);
UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var main = Harness.HarnessCapture.FindMainCamera();
var stack = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(main).cameraStack;
var made = new List<UnityEngine.Object>();
var result = new Dictionary<string, object>();
const int layer = 30;   // not used by the smoke scene
var maskBefore = main.cullingMask;
var stackBefore = stack.Count;
UnityEngine.Camera overlay = null;
try
{
    main.cullingMask &= ~(1 << layer);   // only the overlay camera draws the quad
    System.Func<string, UnityEngine.Transform, UnityEngine.Camera> camera = (name, parent) =>
    {
        var go = new UnityEngine.GameObject(name);
        made.Add(go);
        if (parent != null) go.transform.SetParent(parent, false);
        return go.AddComponent<UnityEngine.Camera>();
    };
    overlay = camera("[selftest overlay camera]", main.transform);
    overlay.cullingMask = 1 << layer;
    UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(overlay).renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
    stack.Add(overlay);
    var quad = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Quad);
    made.Add(quad);
    quad.name = "[selftest weapon]";
    quad.layer = layer;
    quad.transform.SetParent(overlay.transform, false);
    quad.transform.localPosition = new UnityEngine.Vector3(0f, 0f, 2f);
    quad.transform.localScale = UnityEngine.Vector3.one * 0.3f;
    var mat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Unlit"));
    made.Add(mat);
    mat.SetColor("_BaseColor", UnityEngine.Color.red);
    quad.GetComponent<UnityEngine.Renderer>().sharedMaterial = mat;
    var minimap = camera("[selftest minimap camera]", null);
    minimap.transform.SetPositionAndRotation(new UnityEngine.Vector3(0f, 80f, 0f), UnityEngine.Quaternion.Euler(90f, 0f, 0f));
    minimap.rect = new UnityEngine.Rect(0.75f, 0.75f, 0.25f, 0.25f);
    minimap.depth = main.depth + 1;
    minimap.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
    minimap.backgroundColor = UnityEngine.Color.blue;
    minimap.cullingMask = 0;
    var under = camera("[selftest under camera]", null);
    under.depth = main.depth - 1;
    under.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
    under.backgroundColor = UnityEngine.Color.cyan;
    under.cullingMask = 0;

    var t = main.transform;
    var mainPos = t.position; var mainRot = t.rotation;
    var dirty = scene.isDirty;
    var dir = "HarnessOut/selftest/camera-fixture";
    System.IO.Directory.CreateDirectory(dir);
    var atMain = Harness.HarnessCapture.Capture(main, t.position, t.rotation, main.fieldOfView, 1280, 720, dir + "/main.png", true);
    var posePos = new UnityEngine.Vector3(0f, 30f, -40f);
    var atPose = Harness.HarnessCapture.Capture(main, posePos, UnityEngine.Quaternion.LookRotation(-posePos), 55f, 1280, 720, dir + "/pose.png", true);
    var alone = Harness.HarnessCapture.Capture(minimap, minimap.transform.position, minimap.transform.rotation, minimap.fieldOfView, 1280, 720, dir + "/minimap.png", true);

    System.Func<string, UnityEngine.Color32[]> load = f => { var x = new UnityEngine.Texture2D(2, 2); UnityEngine.ImageConversion.LoadImage(x, System.IO.File.ReadAllBytes(f)); var p = x.GetPixels32(); UnityEngine.Object.DestroyImmediate(x); return p; };
    System.Func<UnityEngine.Color32[], int, int, UnityEngine.Color32> at = (p, x, y) => p[y * 1280 + x];   // from the bottom left
    System.Func<UnityEngine.Color32, bool> red = c => c.r > 150 && c.g < 60 && c.b < 60;
    System.Func<UnityEngine.Color32, bool> blue = c => c.b > 200 && c.r < 40 && c.g < 40;
    var m = load(dir + "/main.png"); var p2 = load(dir + "/pose.png"); var a = load(dir + "/minimap.png");
    var cyan = 0;
    foreach (var c in m) if (c.g > 200 && c.b > 200 && c.r < 40) cyan++;
    var mp = Harness.HarnessCapture.HierarchyPath(main.transform);
    result["cameras"] = string.Join(",", atMain.cameras);
    result["camerasExpected"] = "[selftest under camera]," + mp + "," + mp + "/[selftest overlay camera] (overlay),[selftest minimap camera]";
    result["poseCameras"] = string.Join(",", atPose.cameras);
    result["aloneCameras"] = string.Join(",", alone.cameras);
    result["error"] = (atMain.error ?? "") + (atPose.error ?? "") + (alone.error ?? "");
    result["mainCenter"] = at(m, 640, 360).ToString();
    result["mainQuad"] = red(at(m, 640, 360));
    result["poseCenter"] = at(p2, 640, 360).ToString();
    result["poseQuad"] = red(at(p2, 640, 360));
    result["mainMinimap"] = blue(at(m, 1120, 630)) && !blue(at(m, 800, 630));
    result["poseMinimap"] = blue(at(p2, 1120, 630));
    result["aloneBlue"] = blue(at(a, 640, 360)) && blue(at(a, 20, 20));
    result["cyanPixels"] = cyan;
    result["mainPutBack"] = t.position == mainPos && t.rotation == mainRot;
    result["targetsPutBack"] = minimap.targetTexture == null && under.targetTexture == null && overlay.targetTexture == null;
    result["stack"] = stack.Count == stackBefore + 1 && stack[stack.Count - 1] == overlay;
    result["sceneDirtied"] = scene.isDirty && !dirty;
    result["renderMs"] = UnityEngine.Mathf.Round(atMain.renderMs);
}
finally
{
    if (overlay != null) stack.Remove(overlay);
    main.cullingMask = maskBefore;
    for (var i = made.Count - 1; i >= 0; i--) if (made[i] != null) UnityEngine.Object.DestroyImmediate(made[i]);
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
}
return result;
'@

# Item 1's play-mode check (G3-3 with uGUI and a stack in play mode): on the next play, a Screen Space - Overlay canvas and
# an overlay camera in the main camera's stack drawing a red quad are made (they end with the play); the scenario takes a
# sequence of the main camera. Disarms itself on that play (or unused, after 120 s).
$SequenceArmText = @'
var deadline = System.DateTime.UtcNow.AddSeconds(120);
System.Action<UnityEditor.PlayModeStateChange> onChange = null;
onChange = c =>
{
    if (c != UnityEditor.PlayModeStateChange.EnteredPlayMode) return;
    UnityEditor.EditorApplication.playModeStateChanged -= onChange;
    if (System.DateTime.UtcNow > deadline) return;
    var main = Harness.HarnessCapture.FindMainCamera();
    const int layer = 30;
    main.cullingMask &= ~(1 << layer);
    var camGo = new UnityEngine.GameObject("[selftest play overlay camera]");
    camGo.transform.SetParent(main.transform, false);
    var overlay = camGo.AddComponent<UnityEngine.Camera>();
    overlay.cullingMask = 1 << layer;
    UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(overlay).renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
    UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(main).cameraStack.Add(overlay);
    var quad = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Quad);
    quad.name = "[selftest play weapon]";
    quad.layer = layer;
    quad.transform.SetParent(camGo.transform, false);
    quad.transform.localPosition = new UnityEngine.Vector3(0.5f, -0.3f, 2f);
    quad.transform.localScale = UnityEngine.Vector3.one * 0.3f;
    var mat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Unlit"));
    mat.SetColor("_BaseColor", UnityEngine.Color.red);
    quad.GetComponent<UnityEngine.Renderer>().sharedMaterial = mat;
    var cgo = new UnityEngine.GameObject("[selftest play overlay]", typeof(UnityEngine.RectTransform));
    cgo.layer = 5;
    cgo.AddComponent<UnityEngine.Canvas>().renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
    var img = new UnityEngine.GameObject("Image", typeof(UnityEngine.RectTransform));
    img.layer = 5;
    img.transform.SetParent(cgo.transform, false);
    img.AddComponent<UnityEngine.UI.Image>().color = new UnityEngine.Color(0f, 1f, 0f, 1f);
    var rt = (UnityEngine.RectTransform)img.transform;
    rt.anchorMin = rt.anchorMax = rt.pivot = new UnityEngine.Vector2(0f, 0f);
    rt.anchoredPosition = new UnityEngine.Vector2(20f, 20f);
    rt.sizeDelta = new UnityEngine.Vector2(160f, 90f);
};
UnityEditor.EditorApplication.playModeStateChanged += onChange;
return "armed";
'@
$SequenceScenarioText = @'
{
    "name": "selftest-sequence",
    "durationSec": 0.6,
    "fixedDeltaTime": 0.0166667,
    "captures": [
        { "t": 0.1, "preset": "main", "name": "seq", "frames": 4, "every": 5 }
    ]
}
'@

# Item 1's real input checks (G3-6), eval_file bodies (no usings). The first one presses and releases Space on every real
# (native) keyboard every few input updates of the next play session - queued on the Input System device, the path the
# OS's key events take - like a person typing in another window. It disarms itself when play mode exits (or unused, after 120 s).
$RealInputArmText = @'
const string key = "AgentHarness.selftest.realInput";
UnityEditor.SessionState.SetInt(key, 0);
var deadline = System.DateTime.UtcNow.AddSeconds(120);
var updates = 0;
var queued = 0;
System.Action tick = () =>
{
    if (!UnityEngine.Application.isPlaying) return;
    updates++;
    if (updates % 6 != 0) return;
    var down = (updates / 6) % 2 == 1;
    foreach (var d in UnityEngine.InputSystem.InputSystem.devices)
    {
        var kb = d as UnityEngine.InputSystem.Keyboard;
        if (kb == null || !kb.native) continue;
        if (down) UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Space));
        else UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
        queued++;
    }
    UnityEditor.SessionState.SetInt(key, queued);
};
System.Action<UnityEditor.PlayModeStateChange> onChange = null;
onChange = c =>
{
    if (c == UnityEditor.PlayModeStateChange.EnteredPlayMode)
    {
        if (System.DateTime.UtcNow > deadline) { UnityEditor.EditorApplication.playModeStateChanged -= onChange; return; }
        UnityEngine.InputSystem.InputSystem.onBeforeUpdate += tick;
    }
    else if (c == UnityEditor.PlayModeStateChange.ExitingPlayMode)
    {
        UnityEngine.InputSystem.InputSystem.onBeforeUpdate -= tick;
        UnityEditor.EditorApplication.playModeStateChanged -= onChange;
    }
};
UnityEditor.EditorApplication.playModeStateChanged += onChange;
return "armed";
'@

# The real devices, the disabled ones among them, and how many Space events the armed play queued.
$RealInputStateText = @'
var native = new List<string>();
var disabled = new List<string>();
foreach (var d in UnityEngine.InputSystem.InputSystem.devices)
{
    if (!d.native) continue;
    native.Add(d.name);
    if (!d.enabled) disabled.Add(d.name);
}
return new Dictionary<string, object> { { "injected", UnityEditor.SessionState.GetInt("AgentHarness.selftest.realInput", -1) }, { "native", native }, { "disabled", disabled } };
'@

# A play that fails (waitTarget times out) and one that is stopped in the middle.
$RealInputFailText = '{ "name": "selftest-fail", "durationSec": 1, "events": [ { "t": 0.1, "type": "waitTarget", "target": "SelftestNoSuchTarget", "timeoutSec": 0.5 } ] }'
$RealInputLongText = '{ "name": "selftest-long", "durationSec": 30, "events": [ { "t": 0.5, "type": "keyTap", "key": "Space" } ] }'

# ---- matrix 1-6: the Editor tree -----------------------------------------------------------------------------------
# eval_file under the Editor lock: the body's return value.
function Invoke-Eval([string]$File) {
    [void](Enter-HarnessLock)
    try { $r = Invoke-UnityCommand -Name 'eval_file' -Params @{ file = $File } -TimeoutSec 30 } finally { Exit-HarnessLock }
    if (-not $r.success -or -not $r.result.success) { throw "eval_file $File failed: $($r.error) $($r.result | ConvertTo-Json -Compress -Depth 6)" }
    $r.result.result
}

function Get-DisabledDevices($State) { (@($State.disabled) | Sort-Object) -join ',' }
function Invoke-Item1 {
    Start-Item 1 'loop x3: green, deterministic, compile-check'
    # Golden images (G3-4) of this run: loop 1 writes them, loops 2-3 compare.
    $gold = Join-Path $outAbs 'golden'
    if (Test-Path -LiteralPath $gold) { Remove-Item -LiteralPath $gold -Recurse -Force }
    $runs = @()
    for ($i = 1; $i -le 3; $i++) {
        $r = Invoke-Loop "1-loop$i" $(if ($i -eq 1) { @('-Golden', $gold, '-UpdateGolden') } else { @('-Golden', $gold) })
        $runs += $r
        Test-Check "loop $i green" ([bool]$r.ok) (Get-Summary $r)
    }
    $written = @($runs[0].golden.updated)
    Test-Check 'golden: loop 1 wrote its shots (-UpdateGolden)' ($written.Count -gt 0 -and $written.Count -eq @($runs[0].shots).Count -and -not $runs[0].golden.error) "$($written -join ',') $($runs[0].golden.error)"
    $notSame = @($runs[1..2] | ForEach-Object { Get-Golden $_ } | Where-Object { $_ -notlike '*:same' })
    $diffs = @($runs[1..2] | ForEach-Object { @($_.shotStats) } | ForEach-Object { if ($_.golden) { [double]$_.golden.maxDiff } })
    Test-Check 'golden: loops 2-3 same as loop 1' ($notSame.Count -eq 0 -and $diffs.Count -eq 2 * $written.Count) "$((@($runs[1..2] | ForEach-Object { Get-Golden $_ }) -join ',')) maxDiff=$(($diffs | Measure-Object -Maximum).Maximum)"
    $state.item['goldenMaxDiff'] = ($diffs | Measure-Object -Maximum).Maximum
    # The goldens committed with the project, for this Unity version (another patch of it) if any.
    $items3 = @($runs[2].shots | ForEach-Object { [ordered]@{ path = $_; name = [IO.Path]::GetFileNameWithoutExtension($_) } })
    $committed = Invoke-GoldenCommand @{ shots = (ConvertTo-Json -InputObject $items3 -Depth 5 -Compress); key = 'default'; out = (Join-Path $outAbs 'golden-committed') }
    if ($committed.from) {
        $bad = @($committed.results | Where-Object { $_.status -ne 'same' } | ForEach-Object { "$($_.name):$($_.status) mean=$($_.meanDiff) ratio=$($_.changedRatio) $($_.diff)" })
        Test-Check "golden: same as the committed goldens ($($committed.from))" ($bad.Count -eq 0) ($bad -join ' | ')
    }
    $state.item['committedGolden'] = $(if ($committed.from) { "$($committed.from): same $($committed.same)/$(@($committed.results).Count)" } else { "none for $($committed.version)" })
    $fps = @($runs | ForEach-Object { if ($_.build) { $_.build.fingerprint } } | Select-Object -Unique)
    $evs = @($runs | ForEach-Object { Get-Events $_ } | Select-Object -Unique)
    Test-Check 'same build.fingerprint' ($fps.Count -eq 1 -and [bool]$fps[0]) ($fps -join ', ')
    Test-Check 'same play.events' ($evs.Count -eq 1) ($evs -join ' | ')
    if ($ExpectFingerprint) { Test-Check "fingerprint $ExpectFingerprint" ([bool]$fps[0] -and $fps[0].StartsWith($ExpectFingerprint)) $fps[0] }
    $blank = @($runs | ForEach-Object { @($_.shotStats | Where-Object { $_.blank -or $_.dark -or $_.magenta -or $_.error }) })
    Test-Check 'shots taken, none blank, dark or magenta' (@($runs[-1].shots).Count -gt 0 -and $blank.Count -eq 0) "$(@($runs[-1].shots).Count) shots, $($blank.Count) blank/dark/magenta"
    # G3-1: the offscreen shots show the screen-space UI (the HUD), laid out at the capture size.
    $noHud = @($runs | ForEach-Object { @($_.shotStats) } | Where-Object { "$($_.width)x$($_.height)" -ne '1280x720' -or @($_.ui) -notcontains 'uitk:SmokeHudPanel' -or $_.uiError })
    Test-Check 'shots 1280x720 with the HUD composited (ui)' ($noHud.Count -eq 0) (@($noHud | ForEach-Object { "$($_.name) $($_.width)x$($_.height) ui=[$(@($_.ui) -join ',')] $($_.uiError)" }) -join ' | ')
    $report.fingerprint = $fps[0]
    $report.events = $evs[0]
    $report.shots = @($runs[-1].shots)
    $report['shotStats'] = @($runs[-1].shotStats | ForEach-Object { "$($_.name) $($_.meanLuma)/$($_.stdLuma)" })
    $state.item['loopSec'] = @($runs | ForEach-Object { $_.durationSec })
    $state.item['editorErrors'] = @($runs | ForEach-Object { @($_.editorErrors | ForEach-Object { $_.msg }) } | Select-Object -Unique)

    # Scenario tools (P-5), on the smoke scene: waits that are already satisfied, a click on the HUD's UI Toolkit label,
    # KeyCode names for the Input System, a pose capture from above and one from the scene camera by name.
    $scenario = Join-Path $outAbs 'scenario-tools.json'
    [void][IO.Directory]::CreateDirectory($outAbs)
    [IO.File]::WriteAllText($scenario, $ScenarioToolsText, (New-Object Text.UTF8Encoding($false)))
    $r = Invoke-Tool $root 'loop.ps1' '1-scenario-tools' @('-Scenario', $scenario.Replace('\', '/'))
    Test-Check 'scenario tools: loop green' ([bool]$r.ok) (Get-Summary $r)
    $w = @($r.play.waits | ForEach-Object { "$($_.type):$($_.target)" })
    Test-Check 'waitScene Main + waitTarget laps satisfied' (($w -join ',') -eq 'waitScene:Main,waitTarget:laps') ($w -join ',')
    $c = @($r.play.clicks)
    Test-Check 'click laps found as a UI Toolkit element' ($c.Count -eq 1 -and $c[0].via -eq 'uitk') (($c | ForEach-Object { "$($_.target) $($_.via) $($_.x),$($_.y)" }) -join ' | ')
    Test-Check 'input events applied (click 3 + Return/Alpha1 taps 4)' ([int]$r.play.inputEventsApplied -eq 7) $r.play.inputEventsApplied
    $shots = @($r.shotStats | ForEach-Object { "$($_.name):$($_.preset)$(if ($_.blank) { ':BLANK' })$(if ($_.error) { ":$($_.error)" })" })
    Test-Check 'pose shot + named camera shot' (($shots -join ',') -eq 'top:pose,cam:camera') ($shots -join ',')
    $state.item['scenarioTools'] = [ordered]@{ waits = $w; clicks = @($c | ForEach-Object { "$($_.target) $($_.via)" }); shots = $shots }

    # Screen-space uGUI (G3-1), in edit mode on a fixture that is never saved: an overlay (red at 50%), a Screen Space -
    # Camera canvas of the main camera (green, drawn with the scene) and one of another camera (blue, over the scene).
    $fixture = (Join-Path $outAbs 'ui-fixture.cs').Replace('\', '/')
    Write-TextFile $fixture $UiFixtureText
    $u = Invoke-Eval $fixture
    $order = @($u.ui) -join ','
    Test-Check 'uGUI: drawn in order (main camera canvas, other camera canvas, overlay, HUD)' ($order -eq 'ugui:[selftest main camera canvas],ugui:[selftest camera canvas],ugui:[selftest overlay],uitk:SmokeHudPanel' -and -not $u.uiError -and -not $u.error) "$order $($u.uiError) $($u.error)"
    Test-Check 'uGUI: 1280x720, the overlay blended in linear space (red within 2 of the expected)' ($u.size -eq '1280x720' -and [int]$u.redDiff -le 2) "$($u.size) $($u.red) diff=$($u.redDiff)"
    Test-Check 'uGUI: main camera canvas drawn with the scene (green), the other camera canvas over it (blue)' ([bool]$u.greenOk -and [bool]$u.blueOk) "green $($u.green) blue $($u.blue)"
    Test-Check 'uGUI: canvases and the HUD panel put back' ([bool]$u.canvasesRestored -and [bool]$u.panelRestored) "canvases=$($u.canvasesRestored) panel=$($u.panelRestored)"
    $state.item['uiFixture'] = [ordered]@{ ui = @($u.ui); red = $u.red; green = $u.green; blue = $u.blue; renderMs = $u.renderMs }

    # Cameras (G3-7), in edit mode on a fixture that is never saved: a stack overlay camera hanging off the main camera,
    # a minimap Base camera after it, one before it; from the main camera, from another pose, from the minimap by name.
    $fixture = (Join-Path $outAbs 'camera-fixture.cs').Replace('\', '/')
    Write-TextFile $fixture $CameraFixtureText
    $k = Invoke-Eval $fixture
    Test-Check 'cameras: drawn in order (under, main, its stack overlay, minimap)' ($k.cameras -eq $k.camerasExpected -and $k.poseCameras -eq $k.camerasExpected -and -not $k.error) "$($k.cameras) $($k.error)"
    Test-Check 'cameras: the overlay camera draws its quad, also from another pose (it hangs off the main camera)' ([bool]$k.mainQuad -and [bool]$k.poseQuad) "main $($k.mainCenter) pose $($k.poseCenter)"
    # Covered = its cyan clear shows nowhere (it would fill the frame); the knot's rim can have a few cyan pixels itself.
    Test-Check 'cameras: the minimap in its viewport over the main camera, the camera before it covered' ([bool]$k.mainMinimap -and [bool]$k.poseMinimap -and [int]$k.cyanPixels -lt 9216) "minimap=$($k.mainMinimap)/$($k.poseMinimap) cyan=$($k.cyanPixels)"
    Test-Check 'cameras: "camera": minimap draws that camera alone, full frame' ($k.aloneCameras -eq '[selftest minimap camera]' -and [bool]$k.aloneBlue) "$($k.aloneCameras) blue=$($k.aloneBlue)"
    Test-Check 'cameras: main camera, targets and stack put back, scene not dirtied' ([bool]$k.mainPutBack -and [bool]$k.targetsPutBack -and [bool]$k.stack -and -not [bool]$k.sceneDirtied) "main=$($k.mainPutBack) targets=$($k.targetsPutBack) stack=$($k.stack) dirtied=$($k.sceneDirtied)"
    $state.item['cameraFixture'] = [ordered]@{ cameras = $k.cameras; main = $k.mainCenter; pose = $k.poseCenter; renderMs = $k.renderMs }

    # In play mode (G3-3): a capture sequence, with an overlay canvas and a stack camera the play made.
    $arm = (Join-Path $outAbs 'sequence-arm.cs').Replace('\', '/')
    $seqScenario = (Join-Path $outAbs 'sequence.json').Replace('\', '/')
    Write-TextFile $arm $SequenceArmText
    Write-TextFile $seqScenario $SequenceScenarioText
    [void](Invoke-Eval $arm)
    $r = Invoke-Tool $root 'loop.ps1' '1-sequence' @('-Scenario', $seqScenario)
    $q = @($r.shotStats | Where-Object { $_.name -eq 'seq' })[0]
    Test-Check 'sequence: loop green' ([bool]$r.ok) (Get-Summary $r)
    Test-Check 'sequence: 4 frames every 5 in a 2x2 contact sheet, t per frame' ($q -and [int]$q.frames -eq 4 -and [int]$q.every -eq 5 -and $q.sheet -eq '2x2' -and @($q.times).Count -eq 4) "$(if ($q) { "frames=$($q.frames) every=$($q.every) sheet=$($q.sheet) times=$(@($q.times) -join ',')" })"
    $motion = @($q.motion | ForEach-Object { [double]$_ })
    Test-Check 'sequence: motion between frames (the knot spins)' ($motion.Count -eq 3 -and @($motion | Where-Object { $_ -le 0.5 }).Count -eq 0) ($motion -join ',')
    Test-Check 'sequence: the play-mode overlay canvas and stack camera in the frames' (@($q.ui) -contains 'ugui:[selftest play overlay]' -and @($q.cameras | Where-Object { $_ -like '*[[]selftest play overlay camera] (overlay)' }).Count -eq 1) "ui=[$(@($q.ui) -join ',')] cameras=[$(@($q.cameras) -join ',')]"
    $state.item['sequence'] = [ordered]@{ sheet = $q.sheet; motion = $motion; shot = @($r.shots)[0] }

    # harness_golden: "ignore" regions (fractions from the top left) and another patch of the same major.minor.
    $gi = Join-Path $outAbs 'golden-ignore'
    if (Test-Path -LiteralPath $gi) { Remove-Item -LiteralPath $gi -Recurse -Force }
    $v = "$($runs[2].unityVersion)" -split '\.'
    $patch0 = "$($v[0]).$($v[1]).0a1"
    $shot = @($runs[2].shots)[0]
    $gdir = Join-Path $gi "$patch0/ignore"
    [void][IO.Directory]::CreateDirectory($gdir)
    Copy-Item -LiteralPath @($runs[2].shots)[1] -Destination (Join-Path $gdir (Split-Path -Leaf $shot))   # another shot as its golden
    $compare = { param($ignore) $i = [ordered]@{ path = $shot; name = 'probe' }; if ($ignore) { $i['ignore'] = $ignore }; @((Invoke-GoldenCommand @{ shots = (ConvertTo-Json -InputObject @($i) -Depth 5 -Compress); golden = $gi; key = 'ignore'; out = (Join-Path $outAbs 'golden-ignore-out') }).results)[0] }
    $plain = & $compare $null
    $all = & $compare @([ordered]@{ x = 0; y = 0; w = 1; h = 1 })
    $left = & $compare @([ordered]@{ x = 0; y = 0; w = 0.5; h = 1 })
    $top = & $compare @([ordered]@{ x = 0; y = 0; w = 1; h = 0.5 })
    Test-Check "golden: another shot is changed, with a diff image, from version $patch0" ($plain.status -eq 'changed' -and $plain.diff -and (Test-Path -LiteralPath $plain.diff) -and "$($plain.golden)" -like "*/$patch0/*") "$($plain.status) $($plain.golden) $($plain.diff)"
    Test-Check 'golden: ignore everything -> same; ignore the left / top half -> changed only right / below it' ($all.status -eq 'same' -and $left.status -eq 'changed' -and [int]@($left.rect)[0] -ge 640 -and $top.status -eq 'changed' -and [int]@($top.rect)[1] -ge 360) "all=$($all.status) left=$($left.status) [$(@($left.rect) -join ',')] top=$($top.status) [$(@($top.rect) -join ',')]"

    # Real input (G3-6): Space pressed on the real keyboard during a loop does not reach the game; the real devices are
    # disabled only while a scenario plays, also when the play fails or is stopped.
    $files = @{}
    foreach ($kv in @{ arm = @('realinput-arm.cs', $RealInputArmText); state = @('realinput-state.cs', $RealInputStateText); fail = @('realinput-fail.json', $RealInputFailText); long = @('realinput-long.json', $RealInputLongText) }.GetEnumerator()) {
        $files[$kv.Key] = (Join-Path $outAbs $kv.Value[0]).Replace('\', '/')
        Write-TextFile $files[$kv.Key] $kv.Value[1]
    }
    $before = Invoke-Eval $files.state
    [void](Invoke-Eval $files.arm)
    $r = Invoke-Loop '1-real-input'
    $s = Invoke-Eval $files.state
    $injected = $s.injected
    $presses = [int](@($r.play.isolatedDevices | ForEach-Object { [int]$_.presses }) | Measure-Object -Sum).Sum
    $isolated = @($r.play.isolatedDevices | ForEach-Object { "$($_.name)=$($_.presses)" }) -join ','
    Test-Check 'real input: loop green' ([bool]$r.ok) (Get-Summary $r)
    Test-Check 'real input: Space queued on the real keyboard during the play' ([int]$s.injected -gt 0) "injected=$($s.injected) native=$(@($s.native) -join ',')"
    Test-Check 'real input: same play.events' ((Get-Events $r) -eq $evs[0]) (Get-Events $r)
    Test-Check 'real input: its presses kept out (play.isolatedDevices)' ($presses -gt 0) $isolated
    Test-Check 'real input: real devices enabled again' ((Get-DisabledDevices $s) -eq (Get-DisabledDevices $before)) "before [$(Get-DisabledDevices $before)] after [$(Get-DisabledDevices $s)]"
    $r = Invoke-Tool $root 'loop.ps1' '1-real-input-fail' @('-Scenario', $files.fail)
    $s = Invoke-Eval $files.state
    Test-Check 'failed play: stage=play, real devices enabled again' ($r.stage -eq 'play' -and (Get-DisabledDevices $s) -eq (Get-DisabledDevices $before)) "$(Get-Summary $r) disabled [$(Get-DisabledDevices $s)]"
    $dir = Get-OutDir '1-real-input-stop'
    $t = Start-Tool $root 'loop.ps1' @('-Scenario', $files.long, '-Out', $dir)
    $during = $null
    $running = Wait-Until { $p = Invoke-UnityCommand -Name 'harness_play_status' -TimeoutSec 5; $p.success -and $p.result.state -eq 'running' } 120 $t
    if ($running) {
        Start-Sleep -Milliseconds 1000
        $p = Invoke-UnityCommand -Name 'eval_file' -Params @{ file = $files.state } -TimeoutSec 30   # the loop holds the lock
        if ($p.success -and $p.result.success) { $during = $p.result.result }
        [void](Invoke-UnityCommand -Name 'editor_stop' -TimeoutSec 30)
    }
    $r = Read-ToolReport $dir (Wait-Tool $t 180)
    $s = Invoke-Eval $files.state
    Test-Check 'stopped play: real devices disabled while it ran' ($running -and $during -and @($during.disabled).Count -gt 0 -and @($during.disabled).Count -eq @($during.native).Count) "running=$running disabled [$(if ($during) { Get-DisabledDevices $during })]"
    Test-Check 'stopped play: stage=play, real devices enabled again' ($r.stage -eq 'play' -and (Get-DisabledDevices $s) -eq (Get-DisabledDevices $before)) "$(Get-Summary $r) $($r.play.error) disabled [$(Get-DisabledDevices $s)]"
    $state.item['realInput'] = [ordered]@{ injected = $injected; isolated = $isolated; native = @($s.native) }

    $cc = Invoke-HarnessProcess $ps @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tools/compile-check.ps1'), '-IncludeHarness') -Environment $childEnv -TimeoutSec 300
    $j = $null; try { $j = $cc.out | ConvertFrom-Json } catch { }
    Test-Check 'compile-check -IncludeHarness (csc) green' ($j -and $j.ok) $(if ($j) { @($j.compileErrors | ForEach-Object { "$($_.assembly): $($_.msg)" }) -join ' | ' } else { "$($cc.err) $($cc.out)" })
    if ($j) { $state.item['compileCheck'] = [ordered]@{ compiler = $j.compiler; targets = @($j.targets).Count; sec = $j.durationSec } }
    Complete-Item
}

function Invoke-Item2 {
    Start-Item 2 'C# compile error'
    Protect-EditorFile $SmokeCs
    $line = Edit-Line (Get-Abs $root $SmokeCs) $CompileMarker $CompileBroken
    $report.lines['compile'] = $line
    $r = Invoke-Loop '2-compile'
    $e = Get-FirstError $r
    Test-Check 'stage=compile' ($r.stage -eq 'compile') (Get-Summary $r)
    Test-Check "error at ${SmokeCs}:$line" ($e.file -eq $SmokeCs -and [int]$e.line -eq $line) "$($e.file):$($e.line)"
    Test-Check 'module Smoke, CS0103' ($e.module -eq 'Smoke' -and $e.msg -like 'CS0103*') "$($e.module) $($e.msg)"
    Restore-EditorFiles
    [void](Test-GreenAgain '2-restored')
    Complete-Item
}

function Invoke-Item3 {
    Start-Item 3 'runtime exception'
    Protect-EditorFile $SmokeCs
    $line = Edit-Line (Get-Abs $root $SmokeCs) $RuntimeMarker $RuntimeBroken
    $report.lines['runtime'] = $line
    $r = Invoke-Loop '3-runtime'
    $e = @($r.runtimeErrors)
    $e0 = if ($e.Count) { $e[0] } else { [pscustomobject]@{ file = ''; line = 0; module = ''; msg = '' } }
    Test-Check 'stage=runtime' ($r.stage -eq 'runtime') (Get-Summary $r)
    Test-Check "exception at ${SmokeCs}:$line" ($e0.file -eq $SmokeCs -and [int]$e0.line -eq $line) "$($e0.file):$($e0.line)"
    Test-Check 'module Smoke, the injected message' ($e0.module -eq 'Smoke' -and "$($e0.msg)" -like '*selftest: runtime error*') "$($e0.module) $($e0.msg)"
    Restore-EditorFiles
    [void](Test-GreenAgain '3-restored')
    Complete-Item
}

function Invoke-Item4 {
    Start-Item 4 'shader errors: HLSL, magenta material'
    Protect-EditorFile $SmokeShader
    $line = Edit-Line (Get-Abs $root $SmokeShader) $ShaderMarker $ShaderBroken -After $ShaderAnchor
    $report.lines['shader'] = $line
    foreach ($tag in @('4-shader', '4-shader-again')) {
        $r = Invoke-Loop $tag
        $e = Get-FirstError $r 'shader'
        $what = if ($tag -eq '4-shader') { 'loop' } else { 'next loop (no reimport)' }
        Test-Check "${what}: stage=shader" ($r.stage -eq 'shader') (Get-Summary $r)
        Test-Check "${what}: error at ${SmokeShader}:$line, module Smoke" ($e.file -eq $SmokeShader -and [int]$e.line -eq $line -and $e.module -eq 'Smoke') "$($e.file):$($e.line) $($e.module) $($e.msg)"
    }
    Restore-EditorFiles
    # The reverted state is this item's golden images (G3-4).
    $gold = Join-Path $outAbs 'golden4'
    if (Test-Path -LiteralPath $gold) { Remove-Item -LiteralPath $gold -Recurse -Force }
    $r = Test-GreenAgain '4-restored' @('-Golden', $gold, '-UpdateGolden')
    Test-Check 'golden written' (@($r.golden.updated).Count -gt 0) ($r.golden | ConvertTo-Json -Compress)

    # A valid one-line shader change (specular halved): the loop is green, the golden comparison shows where it changed.
    Protect-EditorFile $SmokeShader
    [void](Edit-Line (Get-Abs $root $SmokeShader) $VisualMarker $VisualChanged)
    $r = Invoke-Loop '4-visual' @('-Golden', $gold)
    $changed = @($r.shotStats | Where-Object { $_.golden -and $_.golden.status -eq 'changed' })
    $withDiff = @($changed | Where-Object { $_.golden.diff -and (Test-Path -LiteralPath $_.golden.diff) -and @($_.golden.rect).Count -eq 4 })
    Test-Check 'shader change: loop green' ([bool]$r.ok) (Get-Summary $r)
    Test-Check 'shader change: golden changed, with where (rect) and a diff image' ($changed.Count -gt 0 -and $withDiff.Count -eq $changed.Count -and [int]$r.golden.changed -eq $changed.Count) (Get-GoldenDetail $r)
    $state.item['visualChange'] = @($r.shotStats | ForEach-Object { if ($_.golden) { "$($_.name) $($_.golden.status) mean=$($_.golden.meanDiff) ratio=$($_.golden.changedRatio)" } })
    Restore-EditorFiles

    # A material the render pipeline cannot draw (G3-5): URP draws the Built-in Standard shader magenta, without an error.
    Protect-EditorFile $SmokeBuilder
    [void](Edit-Line (Get-Abs $root $SmokeBuilder) $MagentaMarker $MagentaBroken)
    $r = Invoke-Loop '4-magenta' @('-Golden', $gold)
    $m = @($r.shotStats | Where-Object { $_.magenta })
    $named = @($m | Where-Object { "$($_.hint)" -like '*Smoke/Pedestal*Standard*' })
    $shots = @($r.shotStats | ForEach-Object { "$($_.name) magenta=$($_.magenta) $($_.magentaRatio) $($_.hint)" }) -join ' | '
    Test-Check 'magenta material: loop green (reported, not a failure)' ([bool]$r.ok) (Get-Summary $r)
    Test-Check 'magenta material: magenta shots, hint names Smoke/Pedestal (Standard)' ($m.Count -gt 0 -and $named.Count -eq $m.Count) $shots
    Test-Check 'magenta material: golden changed on the magenta shots' (@($m | Where-Object { -not $_.golden -or $_.golden.status -ne 'changed' }).Count -eq 0) (Get-GoldenDetail $r)
    Restore-EditorFiles
    $r = Test-GreenAgain '4-magenta-restored' @('-Golden', $gold)
    Test-Check 'no magenta after the revert' (@($r.shotStats | Where-Object { $_.magenta }).Count -eq 0) (@($r.shotStats | ForEach-Object { "$($_.name) $($_.magentaRatio)" }) -join ' | ')
    Test-Check 'golden same again after the reverts' (@(Get-Golden $r | Where-Object { $_ -notlike '*:same' }).Count -eq 0 -and @(Get-Golden $r).Count -gt 0) (Get-GoldenDetail $r)
    $state.item['magenta'] = @($m | ForEach-Object { "$($_.name) $($_.magentaRatio) golden=$($_.golden.status)" })
    Complete-Item
}

function Invoke-Item5 {
    Start-Item 5 'lint: static without reset'
    $rel = 'Assets/Game/Smoke/SelftestLint.cs'
    Protect-EditorFile $rel
    Write-TextFile (Get-Abs $root $rel) $LintText
    $r = Invoke-Loop '5-lint'
    $l = @($r.lint | Where-Object { $_.rule -eq 'static-reset' -and "$($_.message)$($_.file)" -like '*SelftestLint*' })
    Test-Check 'stage=lint' ($r.stage -eq 'lint') (Get-Summary $r)
    Test-Check 'static-reset on SelftestLint, module Smoke' ($l.Count -gt 0 -and $l[0].module -eq 'Smoke') (@($r.lint | ForEach-Object { "$($_.rule) $($_.module) $($_.message)" }) -join ' | ')
    Restore-EditorFiles
    [void](Test-GreenAgain '5-restored')
    Complete-Item
}

function Invoke-Item6 {
    Start-Item 6 'two loops at once'
    $da = Get-OutDir '6-loopA'; $db = Get-OutDir '6-loopB'
    $a = Start-Tool $root 'loop.ps1' @('-Out', $da)
    Start-Sleep -Milliseconds 300
    $b = Start-Tool $root 'loop.ps1' @('-Out', $db)
    $ra = Read-ToolReport $da (Wait-Tool $a)
    $rb = Read-ToolReport $db (Wait-Tool $b)
    Test-Check 'first loop green' ([bool]$ra.ok) (Get-Summary $ra)
    Test-Check 'second loop green' ([bool]$rb.ok) (Get-Summary $rb)
    $waits = @([double]$ra.timings.lockWaitSec, [double]$rb.timings.lockWaitSec)
    Test-Check 'one waited for the lock' (($waits | Measure-Object -Maximum).Maximum -gt 0) ($waits -join ' / ')
    Complete-Item
}

# ---- matrix 7-8: worktrees, submit, land -------------------------------------------------------------------------
function New-SelftestWorktrees {
    $top = (Invoke-HarnessGit $root @('rev-parse', '--show-toplevel') -Check).out.Trim()
    $prefix = (Invoke-HarnessGit $root @('rev-parse', '--show-prefix') -Check).out.Trim().TrimEnd('/')
    # The worktrees run the committed tools, harness and modules. Other uncommitted files (e.g. ProjectSettings/,
    # Packages/ and the URP settings assets rewritten by another Unity version) stay as they are: submit and land never
    # touch them.
    $scope = @('tools/', 'Packages/com.geuneda.agentharness/', 'Assets/Game/', 'ProjectSettings/AgentHarness.json') | ForEach-Object { if ($prefix) { "$prefix/$_" } else { $_ } }
    $dirty = @((Get-HarnessGitStatus $top).Keys | Where-Object { $p = $_; @($scope | Where-Object { $p.StartsWith($_) }).Count } | Sort-Object)
    if ($dirty.Count) { throw "7-8 need tools/, Packages/com.geuneda.agentharness/, Assets/Game/ and ProjectSettings/AgentHarness.json committed (the worktrees run the committed code): $($dirty -join ', ')" }
    $branch = (Invoke-HarnessGit $top @('symbolic-ref', '-q', '--short', 'HEAD')).out.Trim()
    $tag = [guid]::NewGuid().ToString('N').Substring(0, 6)
    $wt = [ordered]@{ top = $top; branch = $branch; head0 = (Get-Head $top); base = @((Get-HarnessGitStatus $top).GetEnumerator() | ForEach-Object { "$($_.Value) $($_.Key)" })
        dirs = @(); branches = @(); detached = $null }
    $state.wt = $wt
    if (-not $branch) {
        # land merges into a branch: a detached checkout (e.g. a fresh clone) gets a temporary one at the same commit.
        $wt.branch = "selftest/editor-$tag"
        Invoke-Git $top @('switch', '--quiet', '-c', $wt.branch)
        $wt.detached = $wt.branch
    }
    foreach ($n in @('a', 'b')) {
        $dir = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $top) "$(Split-Path -Leaf $top)-st-$n"))
        if (Test-Path -LiteralPath $dir) {
            # A worktree left by an interrupted selftest: remove it; anything else is not ours.
            $listed = (Invoke-HarnessGit $top @('worktree', 'list', '--porcelain') -Check).out -split "`n" | Where-Object { $_ -like 'worktree *' } | ForEach-Object { $_.Substring(9) }
            if (-not @($listed | Where-Object { Test-HarnessSamePath $_ $dir }).Count) { throw "$dir exists and is not a worktree of $top; remove it" }
            Invoke-Git $top @('worktree', 'remove', '--force', $dir)
        }
        $br = "selftest/$n-$tag"
        Invoke-Git $top @('worktree', 'add', '--quiet', '-b', $br, $dir, $wt.head0)
        $wt.dirs += $dir
        $wt.branches += $br
        $wt[$n] = if ($prefix) { Join-Path $dir $prefix } else { $dir }
        $wt["branch$n"] = $br
    }
}

function Remove-SelftestWorktrees {
    $wt = $state.wt
    if (-not $wt) { return @() }
    $notes = @()
    foreach ($d in $wt.dirs) {
        $r = Invoke-HarnessGit $wt.top @('worktree', 'remove', '--force', $d)
        if ($r.code -ne 0) { $notes += "worktree remove ${d}: $($r.err)" }
    }
    [void](Invoke-HarnessGit $wt.top @('worktree', 'prune'))
    foreach ($b in $wt.branches) { [void](Invoke-HarnessGit $wt.top @('branch', '-D', $b)) }
    # Back to the commit the test started from (it landed and committed on top of it).
    if ((Get-Head $wt.top) -ne $wt.head0) {
        $r = Invoke-HarnessGit $wt.top @('reset', '--keep', $wt.head0)
        if ($r.code -ne 0) { $notes += "reset --keep $($wt.head0): $($r.err)" }
    }
    if ($wt.detached -and (Get-Head $wt.top) -eq $wt.head0) {
        $r = Invoke-HarnessGit $wt.top @('switch', '--quiet', '--detach', $wt.head0)
        if ($r.code -eq 0) { [void](Invoke-HarnessGit $wt.top @('branch', '-D', $wt.detached)) } else { $notes += "switch --detach $($wt.head0): $($r.err)" }
    }
    # Ownership records of the removed worktrees (a red run can leave them).
    $owners = Get-HarnessOwners
    $mine = @($owners.Keys | Where-Object { $o = $owners[$_]; @($wt.dirs | Where-Object { "$($o.workRoot)".Replace('\', '/').StartsWith($_.Replace('\', '/'), [StringComparison]::OrdinalIgnoreCase) }).Count })
    if ($mine.Count) { foreach ($m in $mine) { $owners.Remove($m) }; Save-HarnessOwners $owners }
    $state.wt = $null
    $notes
}

function Invoke-Item7 {
    Start-Item 7 'worktrees: submit.ps1'
    New-SelftestWorktrees
    $wt = $state.wt
    $A = $wt.a; $B = $wt.b

    # 7a. The compile-check gate refuses without touching the Editor tree.
    $line = Edit-Line (Get-Abs $A $SmokeCs) $CompileMarker $CompileBroken
    $r = Invoke-Tool $A 'submit.ps1' '7a-gate' @('-Module', 'Smoke')
    $e = Get-FirstError $r
    Test-Check 'gate: stage=compile, phase=check' ($r.stage -eq 'compile' -and $r.submit.phase -eq 'check') "$(Get-Summary $r) phase=$($r.submit.phase)"
    Test-Check "gate: error at ${SmokeCs}:$line" ($e.file -eq $SmokeCs -and [int]$e.line -eq $line -and $e.module -eq 'Smoke') "$($e.file):$($e.line) $($e.module)"
    Test-Check 'gate: Editor tree untouched' (@(Get-NewStatus).Count -eq 0) (@(Get-NewStatus) -join '; ')

    # 7b. Forced broken submit (A) is reverted; B's new module + contract waits for the lock and is kept.
    Write-TextFile (Get-Abs $B 'Assets/Game/Probe/Game.Probe.asmdef') $ProbeAsmdef
    Write-TextFile (Get-Abs $B $ProbeCs) $ProbeModuleText
    Write-TextFile (Get-Abs $B 'Assets/Game/Contracts/ProbeEvents.cs') $ProbeEventsText
    $da = Get-OutDir '7b-forced'; $db = Get-OutDir '7b-newmodule'
    $ta = Start-Tool $A 'submit.ps1' @('-Module', 'Smoke', '-SkipCheck', '-Out', $da)
    [void](Wait-Until { Test-Path -LiteralPath $SubmitJournal } 60 $ta)
    $tb = Start-Tool $B 'submit.ps1' @('-Module', 'Probe', '-Out', $db)
    $ra = Read-ToolReport $da (Wait-Tool $ta)
    $rb = Read-ToolReport $db (Wait-Tool $tb)
    $e = Get-FirstError $ra
    Test-Check 'forced: stage=compile at the injected line' ($ra.stage -eq 'compile' -and $e.file -eq $SmokeCs -and [int]$e.line -eq $line) "$(Get-Summary $ra) $($e.file):$($e.line)"
    Test-Check 'forced: reverted, restore compile ok' ($ra.submit.reverted -and $ra.submit.restore.ok) "reverted=$($ra.submit.reverted) restore=$($ra.submit.restore | ConvertTo-Json -Compress)"
    Test-Check 'new module: green and kept' ($rb.ok -and $rb.submit.kept) (Get-Summary $rb)
    Test-Check 'new module: waited for the lock' ([double]$rb.timings.lockWaitSec -gt 0) $rb.timings.lockWaitSec
    $laps = Get-EventCount $rb 'SpinnerLap'
    Test-Check 'new module: ProbeEcho = SpinnerLap' ($laps -gt 0 -and (Get-EventCount $rb 'ProbeEcho') -eq $laps) (Get-Events $rb)
    Test-Check 'new module: .meta files copied back' (@($rb.submit.metaWrittenBack).Count -ge 4) (@($rb.submit.metaWrittenBack) -join ', ')
    $state.item['newModuleCheck'] = @($rb.submit.check.targets)
    Invoke-Git $A @('checkout', '--', '.')

    # 7c. A runtime error submit is reverted.
    $line = Edit-Line (Get-Abs $A $SmokeCs) $RuntimeMarker $RuntimeBroken
    $r = Invoke-Tool $A 'submit.ps1' '7c-runtime' @('-Module', 'Smoke')
    $e0 = @($r.runtimeErrors) | Select-Object -First 1
    Test-Check "runtime: stage=runtime at ${SmokeCs}:$line" ($r.stage -eq 'runtime' -and $e0 -and $e0.file -eq $SmokeCs -and [int]$e0.line -eq $line) "$(Get-Summary $r) $(if ($e0) { "$($e0.file):$($e0.line)" })"
    Test-Check 'runtime: reverted, restore compile ok' ($r.submit.reverted -and $r.submit.restore.ok) "reverted=$($r.submit.reverted)"

    # 7d. A submit killed right after its sync is rolled back by the next loop.
    $mine = [IO.File]::ReadAllText((Get-Abs $A $SmokeCs))
    $dk = Get-OutDir '7d-killed'
    $tk = Start-Tool $A 'submit.ps1' @('-Module', 'Smoke', '-SkipCheck', '-Out', $dk)
    $synced = Wait-Until { (Test-Path -LiteralPath $SubmitJournal) -and [IO.File]::ReadAllText((Get-Abs $root $SmokeCs)) -eq $mine } 60 $tk
    Stop-Tool $tk
    Test-Check 'kill: the submit was killed after its sync' ($synced -and (Test-Path -LiteralPath $SubmitJournal)) "synced=$synced journal=$(Test-Path -LiteralPath $SubmitJournal)"
    $r = Invoke-Loop '7d-recovered'
    Test-Check 'kill: next loop reports recoveredSubmit and is green' ($r.ok -and $r.recoveredSubmit -and -not $r.recoveredSubmit.error) "$(Get-Summary $r) recovered=$($r.recoveredSubmit | ConvertTo-Json -Compress)"
    Test-Check 'kill: Smoke is back to the committed version' (-not @(Get-NewStatus | Where-Object { $_ -like '*Game/Smoke/*' }).Count) (@(Get-NewStatus) -join '; ')
    Invoke-Git $A @('checkout', '--', '.')

    # 7e. Contracts are add-only.
    [void](Edit-Line (Get-Abs $A $SmokeContracts) 'public readonly int Lap;' 'public readonly int Lap; // selftest')
    $r = Invoke-Tool $A 'submit.ps1' '7e-contract' @('-Module', 'Smoke')
    Test-Check 'changed contract: stage=submit (add-only)' ($r.stage -eq 'submit' -and "$($r.error)" -like '*add-only*') (Get-Summary $r)
    Invoke-Git $A @('checkout', '--', '.')

    $left = @(Get-NewStatus | Where-Object { $_ -notmatch '(^\?\? |/)Assets/Game/(Probe(/|\.meta$)|Contracts/ProbeEvents\.cs)' })
    Test-Check "Editor tree: only B's submitted module is uncommitted" ($left.Count -eq 0) ($left -join ', ')
    Complete-Item
}

function Invoke-Item8 {
    Start-Item 8 'worktrees: land.ps1'
    $wt = $state.wt
    if (-not $wt) { throw 'item 8 needs item 7 (its worktrees and submitted module)' }
    $A = $wt.a; $B = $wt.b; $top = $wt.top
    Invoke-Git $B @('add', '-A')
    Invoke-Git $B ($gitId + @('commit', '--quiet', '-m', 'selftest: Probe module'))

    # 8a. Land (fast-forward) while A's submit waits for the lock. A's worktree is clean (7 put it back): the submit skips its
    # compile-check gate, which can take as long as the whole land (4.9 s on 6000.6 in a fresh clone) and then never waits.
    $dl = Get-OutDir '8a-land'; $da = Get-OutDir '8a-waiting-submit'
    $tl = Start-Tool $B 'land.ps1' @('-Out', $dl)
    [void](Wait-Until { Test-Path -LiteralPath $LandJournal } 60 $tl)
    $ta = Start-Tool $A 'submit.ps1' @('-Module', 'Smoke', '-SkipCheck', '-Out', $da)
    $rl = Read-ToolReport $dl (Wait-Tool $tl)
    $ra = Read-ToolReport $da (Wait-Tool $ta)
    Test-Check 'land: green, merged, kept' ($rl.ok -and $rl.land.merged -and $rl.land.kept) (Get-Summary $rl)
    Test-Check 'land: fast-forward, Probe released' ($rl.land.fastForward -and @($rl.land.releasedOwners) -contains 'Probe') "ff=$($rl.land.fastForward) released=$(@($rl.land.releasedOwners) -join ',')"
    Test-Check 'land: Editor tree clean, at the branch' (@(Get-NewStatus).Count -eq 0 -and (Get-Head $top) -eq (Get-Head $B)) (@(Get-NewStatus) -join '; ')
    Test-Check 'submit during the land: waited, green' ($ra.ok -and [double]$ra.timings.lockWaitSec -gt 0) "$(Get-Summary $ra) wait=$($ra.timings.lockWaitSec)"

    # 8b. Already landed.
    $r = Invoke-Tool $B 'land.ps1' '8b-already'
    Test-Check 'already landed: green, nothing done' ($r.ok -and "$($r.land.note)" -like 'already landed*') "$(Get-Summary $r) $($r.land.note)"

    $headBefore = Get-Head $top
    # 8c. Uncommitted files in the branch's worktree.
    Write-TextFile (Get-Abs $B 'Assets/Game/Probe/Notes.txt') 'selftest'
    $r = Invoke-Tool $B 'land.ps1' '8c-uncommitted'
    Test-Check 'uncommitted: stage=land, land.uncommitted' ($r.stage -eq 'land' -and @($r.land.uncommitted).Count -gt 0) (Get-Summary $r)
    Remove-Item -LiteralPath (Get-Abs $B 'Assets/Game/Probe/Notes.txt') -Force

    # 8d. A new file without its .meta.
    Write-TextFile (Get-Abs $B 'Assets/Game/Probe/Extra.cs') "namespace Game.Probe { static class Extra { } }`n"
    Invoke-Git $B @('add', '-A')
    Invoke-Git $B ($gitId + @('commit', '--quiet', '-m', 'selftest: no .meta'))
    $r = Invoke-Tool $B 'land.ps1' '8d-missing-meta'
    Test-Check 'missing .meta: stage=land, land.missingMeta' ($r.stage -eq 'land' -and @($r.land.missingMeta | Where-Object { $_ -like '*Extra.cs.meta' }).Count -gt 0) "$(Get-Summary $r) $(@($r.land.missingMeta) -join ', ')"
    Invoke-Git $B @('reset', '--quiet', '--hard', 'HEAD~1')

    # 8e. A compile error lands red and is undone.
    $line = Edit-Line (Get-Abs $B $ProbeCs) '// selftest-probe' 'selftestUndefined();'
    Invoke-Git $B ($gitId + @('commit', '--quiet', '-am', 'selftest: compile error'))
    $r = Invoke-Tool $B 'land.ps1' '8e-compile'
    $e = Get-FirstError $r
    Test-Check "compile error: stage=compile at ${ProbeCs}:$line" ($r.stage -eq 'compile' -and $e.file -eq $ProbeCs -and [int]$e.line -eq $line -and $e.module -eq 'Probe') "$(Get-Summary $r) $($e.file):$($e.line) $($e.module)"
    Test-Check 'compile error: undone, restore compile ok' ($r.land.reverted -and $r.land.restore.ok) "reverted=$($r.land.reverted) undo=$(@($r.land.undo) -join '; ')"
    Test-Check 'compile error: HEAD and git status as before' ((Get-Head $top) -eq $headBefore -and @(Get-NewStatus).Count -eq 0) (@(Get-NewStatus) -join '; ')
    Invoke-Git $B @('reset', '--quiet', '--hard', 'HEAD~1')

    # 8f. A land killed right after its merge is undone by the next loop.
    [void](Edit-Line (Get-Abs $B $ProbeCs) '// selftest-probe' '// selftest-probe (landed)')
    Invoke-Git $B ($gitId + @('commit', '--quiet', '-am', 'selftest: change'))
    $dk = Get-OutDir '8f-killed'
    $tk = Start-Tool $B 'land.ps1' @('-Out', $dk)
    $merged = Wait-Until { (Test-Path -LiteralPath $LandJournal) -and ([IO.File]::ReadAllText($LandJournal) | ConvertFrom-Json).mergedHead } 60 $tk
    Stop-Tool $tk
    Test-Check 'kill: the land was killed after its merge' ($merged -and (Test-Path -LiteralPath $LandJournal)) "merged=$merged journal=$(Test-Path -LiteralPath $LandJournal)"
    $r = Invoke-Loop '8f-recovered'
    Test-Check 'kill: next loop reports recoveredLand and is green' ($r.ok -and $r.recoveredLand -and -not $r.recoveredLand.error) "$(Get-Summary $r) recovered=$($r.recoveredLand | ConvertTo-Json -Compress)"
    Test-Check 'kill: HEAD and git status as before' ((Get-Head $top) -eq $headBefore -and @(Get-NewStatus).Count -eq 0) "HEAD=$(Get-Head $top) new=$(@(Get-NewStatus) -join '; ')"

    # 8g. A conflict with a commit of the Editor tree's branch.
    [void](Edit-Line (Get-Abs $root $ProbeCs) '// selftest-probe' '// selftest-probe (editor tree)')
    Invoke-Git $root ($gitId + @('commit', '--quiet', '-m', 'selftest: conflicting change', '--', $ProbeCs))
    $headConflict = Get-Head $top
    $r = Invoke-Tool $B 'land.ps1' '8g-conflict'
    Test-Check 'conflict: stage=land, land.conflicts' ($r.stage -eq 'land' -and @($r.land.conflicts | Where-Object { $_ -like '*ProbeModule.cs' }).Count -gt 0) "$(Get-Summary $r) $(@($r.land.conflicts) -join ', ')"
    Test-Check 'conflict: nothing touched' ((Get-Head $top) -eq $headConflict -and @(Get-NewStatus).Count -eq 0) (@(Get-NewStatus) -join '; ')
    Invoke-Git $top @('reset', '--quiet', '--keep', $headBefore)

    # 8h. An uncommitted edit in the Editor tree that the land would replace.
    Protect-EditorFile $ProbeCs
    [void](Edit-Line (Get-Abs $root $ProbeCs) '// selftest-probe' '// selftest-probe (edited in the Editor tree)')
    $statusForeign = Get-EditorStatus
    $r = Invoke-Tool $B 'land.ps1' '8h-foreign'
    Test-Check 'foreign edit: stage=land, land.foreign' ($r.stage -eq 'land' -and @($r.land.foreign | Where-Object { $_ -like '*ProbeModule.cs' }).Count -gt 0) "$(Get-Summary $r) $(@($r.land.foreign) -join ', ')"
    Test-Check 'foreign edit: nothing touched' ((Get-Head $top) -eq $headBefore -and (Get-EditorStatus) -eq $statusForeign) (Get-EditorStatus)
    Restore-EditorFiles

    # 8i. The change lands.
    $r = Invoke-Tool $B 'land.ps1' '8i-land'
    Test-Check 'land: green, kept, Editor tree clean' ($r.ok -and $r.land.kept -and @(Get-NewStatus).Count -eq 0 -and (Get-Head $top) -eq (Get-Head $B)) "$(Get-Summary $r) new=$(@(Get-NewStatus) -join '; ')"
    Complete-Item
}

# ---- run -----------------------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force $outAbs | Out-Null
$failed = $null
try {
    if (Test-HarnessWorktree) { throw "run selftest.ps1 in the Editor tree ($root), not in an agent worktree" }
    $ping = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 10
    if (-not $ping.success) { $report.stage = 'editor'; throw "Editor not reachable: $($ping.error). Open it with tools/open.ps1." }
    if ($ping.result.PSObject.Properties.Name -contains 'unityVersion') { $report.unityVersion = $ping.result.unityVersion }
    $statusBefore = Get-EditorStatus
    $report['gitStatusBefore'] = $statusBefore
    foreach ($n in $items) {
        $fn = "Invoke-Item$n"
        if (-not (Get-Command $fn -ErrorAction SilentlyContinue)) { throw "no matrix item $n (1-8)" }
        try { & $fn }
        catch {
            $msg = $_.Exception.Message
            if ($state.item) {
                # Thrown inside the item, before Complete-Item recorded it.
                if ($msg -ne 'item failed') { $state.item.checks += , [ordered]@{ check = 'ran to the end'; ok = $false; actual = "$msg at $($_.InvocationInfo.PositionMessage)" } }
                $state.item.ok = $false
                try { Complete-Item } catch { }
            } elseif ($msg -ne 'item failed') { throw }
            if (-not $KeepGoing) { throw 'item failed' }
            Restore-EditorFiles   # the next item starts from the committed files
        }
    }
} catch {
    $failed = $_.Exception.Message
    if ($failed -ne 'item failed' -and -not $report.stage) { $report['error'] = $failed }
} finally {
    foreach ($t in @($state.tools)) { Stop-Tool $t }
    Restore-EditorFiles
    $notes = @(Remove-SelftestWorktrees)
    if ($notes.Count) { $report['cleanupErrors'] = $notes }
}

# Whatever happened: the Editor tree must be as it was, and green.
if ($report.stage -ne 'editor' -and @($report.items).Count -gt 0) {
    $r = Invoke-Loop 'final'
    $status = Get-EditorStatus
    $report['final'] = [ordered]@{ ok = [bool]$r.ok; stage = $r.stage; error = $r.error; gitStatus = $status }
    if ($status -ne $report.gitStatusBefore) { $report.final.ok = $false; $report.final['error'] = "git status changed: before '$($report.gitStatusBefore)', after '$status'" }
}
$red = @($report.items | Where-Object { -not $_.ok })
$report.ok = -not $failed -and $red.Count -eq 0 -and @($report.items).Count -eq $items.Count -and $report.final -and $report.final.ok
$report.stage = if ($report.ok) { 'done' } elseif ($report.stage) { $report.stage } elseif ($red.Count) { "item$($red[0].item)" } else { 'final' }
Save-HarnessReport $report $outAbs $clock $null
exit $(if ($report.ok) { 0 } else { 1 })
