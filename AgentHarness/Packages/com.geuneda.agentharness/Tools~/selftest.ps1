<#
.SYNOPSIS
  Harness self-test (O-3): the verification matrix of docs/ROADMAP.md as one script. Run it on the Editor tree after
  changing the harness, and per Unity version in a fresh clone (tools/fresh-clone-test.ps1 -SelfTest, P-1).

  1 loop x3: green, same build.fingerprint and play.events (-ExpectFingerprint), no blank shots; compile-check of
    every assembly (csc, the Editor's response files)
  2 C# compile error in Smoke -> stage=compile at the injected file/line, module Smoke; reverted -> green
  3 runtime exception in Smoke -> stage=runtime at the injected line; reverted -> green
  4 HLSL error in the Smoke shader -> stage=shader at the injected line, again in the next loop (no reimport);
    reverted -> green
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

function Invoke-Loop([string]$Tag) { Invoke-Tool $root 'loop.ps1' $Tag }

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
function Test-GreenAgain([string]$Tag) {
    $r = Invoke-Loop $Tag
    Test-Check 'green again after the revert' ([bool]$r.ok) (Get-Summary $r)
    $r
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

# ---- matrix 1-6: the Editor tree -----------------------------------------------------------------------------------
function Invoke-Item1 {
    Start-Item 1 'loop x3: green, deterministic, compile-check'
    $runs = @()
    for ($i = 1; $i -le 3; $i++) {
        $r = Invoke-Loop "1-loop$i"
        $runs += $r
        Test-Check "loop $i green" ([bool]$r.ok) (Get-Summary $r)
    }
    $fps = @($runs | ForEach-Object { if ($_.build) { $_.build.fingerprint } } | Select-Object -Unique)
    $evs = @($runs | ForEach-Object { Get-Events $_ } | Select-Object -Unique)
    Test-Check 'same build.fingerprint' ($fps.Count -eq 1 -and [bool]$fps[0]) ($fps -join ', ')
    Test-Check 'same play.events' ($evs.Count -eq 1) ($evs -join ' | ')
    if ($ExpectFingerprint) { Test-Check "fingerprint $ExpectFingerprint" ([bool]$fps[0] -and $fps[0].StartsWith($ExpectFingerprint)) $fps[0] }
    $blank = @($runs | ForEach-Object { @($_.shotStats | Where-Object { $_.blank -or $_.dark -or $_.error }) })
    Test-Check 'shots taken, none blank or dark' (@($runs[-1].shots).Count -gt 0 -and $blank.Count -eq 0) "$(@($runs[-1].shots).Count) shots, $($blank.Count) blank/dark"
    $report.fingerprint = $fps[0]
    $report.events = $evs[0]
    $report.shots = @($runs[-1].shots)
    $report['shotStats'] = @($runs[-1].shotStats | ForEach-Object { "$($_.name) $($_.meanLuma)/$($_.stdLuma)" })
    $state.item['loopSec'] = @($runs | ForEach-Object { $_.durationSec })
    $state.item['editorErrors'] = @($runs | ForEach-Object { @($_.editorErrors | ForEach-Object { $_.msg }) } | Select-Object -Unique)

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
    Start-Item 4 'HLSL error'
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
    [void](Test-GreenAgain '4-restored')
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

    # 8a. Land (fast-forward) while A's submit waits for the lock.
    $dl = Get-OutDir '8a-land'; $da = Get-OutDir '8a-waiting-submit'
    $tl = Start-Tool $B 'land.ps1' @('-Out', $dl)
    [void](Wait-Until { Test-Path -LiteralPath $LandJournal } 60 $tl)
    $ta = Start-Tool $A 'submit.ps1' @('-Module', 'Smoke', '-Out', $da)
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
