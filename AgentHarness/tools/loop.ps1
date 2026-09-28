<#
.SYNOPSIS
  One-shot agent loop - the Unity equivalent of "save, reload, screenshot, read console":
  recompile -> (stop on compile errors) -> harness_build -> harness_play (3 shots by default)
  -> harness_console + harness_stats -> one report.json (also printed to stdout).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/loop.ps1
  powershell -ExecutionPolicy Bypass -File tools/loop.ps1 -Scenario tools/scenarios/default.json -Out HarnessOut/latest
  powershell -ExecutionPolicy Bypass -File tools/loop.ps1 -NoPlay      # edit-mode captures only (faster, no gameplay)

  Exit code 0 = everything green (no compile/build/runtime errors, no blank shots), 1 = see report.json "stage".
#>
param(
    [string]$Scenario = 'tools/scenarios/default.json',
    [string]$Out = 'HarnessOut/latest',
    [switch]$NoPlay,
    [switch]$NoCompile,
    [int]$TimeoutSec = 180
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
$root = Get-HarnessProjectRoot
$outAbs = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
$total = [Diagnostics.Stopwatch]::StartNew()
$timings = [ordered]@{}

# One Editor: loops from parallel agents queue here (machine-wide mutex per project).
$timings['lockWaitSec'] = Enter-HarnessLock

function Lap([string]$name, [Diagnostics.Stopwatch]$sw) { $timings[$name] = [math]::Round($sw.Elapsed.TotalSeconds, 2) }

function Write-Report([System.Collections.IDictionary]$report, [int]$exitCode) {
    $report['durationSec'] = [math]::Round($total.Elapsed.TotalSeconds, 2)
    $report['timings'] = $timings
    New-Item -ItemType Directory -Force $outAbs | Out-Null
    $json = $report | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText((Join-Path $outAbs 'report.json'), $json, (New-Object Text.UTF8Encoding($false)))
    Exit-HarnessLock
    $json
    exit $exitCode
}

function New-Report {
    [ordered]@{
        ok            = $false
        stage         = ''
        compileErrors = @()
        runtimeErrors = @()
        fps           = $null
        shots         = @()
        durationSec   = 0
        report        = (Join-Path $outAbs 'report.json').Replace('\', '/')
    }
}

# Fallback parser for recompile_status strings: "Assets\X.cs(12,5): error CS0103: msg"
function ConvertFrom-CompilerString([string]$s) {
    if ($s -match '^(?<file>.*?)\((?<line>\d+),(?<col>\d+)\):\s*error\s+(?<code>\w+):\s*(?<msg>.*)$') {
        $file = $Matches.file.Replace('\', '/')
        $module = if ($file -match 'Assets/Game/([^/]+)/') { $Matches[1] } elseif ($file -like 'Assets/Harness/*') { 'Harness' } else { '' }
        return [ordered]@{ file = $file; line = [int]$Matches.line; msg = "$($Matches.code): $($Matches.msg)"; module = $module }
    }
    [ordered]@{ file = ''; line = 0; msg = $s; module = '' }
}

$report = New-Report

# ---- 0. Editor reachable, not playing ---------------------------------------------------------------
$ping = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 10
if (-not $ping.success) {
    $report.stage = 'editor'
    $report['error'] = "Editor not reachable: $($ping.error). Open it with 'unity open $root' and wait for 'unity status' = ready. If it is open, it may be in Safe Mode (compile errors at startup): run 'unity pipeline list'."
    Write-Report $report 1
}
# Keep the Editor ticking while it is not the foreground app (otherwise compile/play stall). Idempotent.
Invoke-UnityCommand -Name 'set_autotick' -Params @{ enable = $true } -TimeoutSec 10 | Out-Null
if ($ping.result.isPlaying -or $ping.result.willChangePlaymode) {
    Invoke-UnityCommand -Name 'editor_stop' | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 200; $ping = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 5 } while ((-not $ping.success -or $ping.result.isPlaying) -and $sw.Elapsed.TotalSeconds -lt 30)
}
$mark = [int]$ping.result.mark

# ---- 1. Recompile (stop immediately on errors) -----------------------------------------------------
if (-not $NoCompile) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $rc = Invoke-HarnessRecompile -TimeoutSec $TimeoutSec
    Lap 'compileSec' $sw
    if (-not $rc.ok) {
        $report.stage = 'compile'
        $con = Invoke-UnityCommand -Name 'harness_console' -Params @{ since = $mark } -TimeoutSec 10
        $errs = @()
        if (@($rc.compileErrors).Count -gt 0) {
            $errs = @($rc.compileErrors | ForEach-Object { [ordered]@{ file = $_.file; line = $_.line; msg = "$($_.code): $($_.msg)"; module = $_.module } })
        } elseif ($con.success -and @($con.result.compileErrors).Count -gt 0) {
            $errs = @($con.result.compileErrors | ForEach-Object { [ordered]@{ file = $_.file; line = $_.line; msg = "$($_.code): $($_.msg)"; module = $_.module } })
        } else {
            $errs = @($rc.errors | Where-Object { $_ } | ForEach-Object { ConvertFrom-CompilerString $_ })
        }
        if ($errs.Count -eq 0) { $errs = @([ordered]@{ file = ''; line = 0; msg = "compile failed ($($rc.status)); open the Editor console"; module = '' }) }
        $report.compileErrors = $errs
        Write-Report $report 1
    }
}

# ---- 2. Lint (reported, never blocks) + build -------------------------------------------------------
$sw = [Diagnostics.Stopwatch]::StartNew()
$lint = Invoke-UnityCommand -Name 'harness_lint' -TimeoutSec 30
$lintIssues = @("harness_lint failed: $($lint.error)")
if ($lint.success) { $lintIssues = @($lint.result.issues | ForEach-Object { [ordered]@{ rule = $_.rule; module = $_.module; file = $_.file; message = $_.message } }) }
$build = Invoke-UnityCommand -Name 'harness_build' -TimeoutSec $TimeoutSec
# Shader compile errors are state (a broken .shader stays broken without logging again): query them every loop.
$shaders = Invoke-UnityCommand -Name 'harness_shaders' -TimeoutSec 30
$shaderErrors = @()
if ($shaders.success) { $shaderErrors = @($shaders.result.errors | ForEach-Object { [ordered]@{ file = $_.file; line = $_.line; msg = "shader '$($_.shader)': $($_.msg)"; module = $_.module; kind = 'shader' } }) }
$report.compileErrors = $shaderErrors
Lap 'buildSec' $sw
$report['build'] = if ($build.success) {
    [ordered]@{ ok = $build.result.ok; fingerprint = $build.result.fingerprint; gameObjects = $build.result.gameObjects; durationMs = $build.result.durationMs; steps = @($build.result.steps | ForEach-Object { [ordered]@{ type = $_.type; module = $_.module; ms = $_.ms; error = $_.error; file = $_.file; line = $_.line } }); warnings = @($build.result.warnings) }
} else { [ordered]@{ ok = $false; error = $build.error } }
$report['lint'] = $lintIssues
if (-not $build.success -or -not $build.result.ok) {
    $report.stage = 'build'
    $report['error'] = if ($build.success) { $build.result.error } else { $build.error }
    $con = Invoke-UnityCommand -Name 'harness_console' -Params @{ since = $mark } -TimeoutSec 10
    if ($con.success) { $report.runtimeErrors = @($con.result.runtimeErrors | ForEach-Object { [ordered]@{ type = $_.type; msg = $_.message; file = $_.file; line = $_.line; module = $_.module; count = $_.count } }) }
    Write-Report $report 1
}

# ---- 3. Play scenario (or edit-mode capture) ---------------------------------------------------------
$sw = [Diagnostics.Stopwatch]::StartNew()
$playResult = $null
if ($NoPlay) {
    $cap = Invoke-UnityCommand -Name 'harness_capture' -Params @{ preset = 'all'; out = $outAbs } -TimeoutSec 60
    if ($cap.success) { $shotObjs = @($cap.result.shots) } else { $shotObjs = @(); $report['error'] = $cap.error }
} else {
    $start = Invoke-UnityCommand -Name 'harness_play' -Params @{ scenario = $Scenario; out = $outAbs } -TimeoutSec 30
    if (-not $start.success -or -not $start.result.ok) {
        $report.stage = 'play'
        $report['error'] = if ($start.success) { $start.result.error } else { $start.error }
        Write-Report $report 1
    }
    $state = $null
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        Start-Sleep -Milliseconds 200
        $st = Invoke-UnityCommand -Name 'harness_play_status' -TimeoutSec 5
        if ($st.success -and $st.result.id -eq $start.result.id -and $st.result.state -in @('done', 'failed')) { $state = $st.result; break }
    }
    if ($null -eq $state) {
        Invoke-UnityCommand -Name 'editor_stop' | Out-Null
        $report.stage = 'play'
        $report['error'] = "play did not finish within $TimeoutSec s"
        Write-Report $report 1
    }
    $playResult = $state.result
    $shotObjs = @(); if ($playResult) { $shotObjs = @($playResult.shots) }
    if ($state.state -eq 'failed') { $report['playError'] = $state.error }
}
Lap 'playSec' $sw

# ---- 4. Console + stats -----------------------------------------------------------------------------
$sw = [Diagnostics.Stopwatch]::StartNew()
$con = Invoke-UnityCommand -Name 'harness_console' -Params @{ since = $mark } -TimeoutSec 10
$stats = if ($NoPlay) { $null } else { Invoke-UnityCommand -Name 'harness_stats' -TimeoutSec 10 }
Lap 'collectSec' $sw

$runtimeErrors = @()
$warningCount = 0
if ($con.success) {
    $runtimeErrors = @($con.result.runtimeErrors | ForEach-Object { [ordered]@{ type = $_.type; msg = $_.message; file = $_.file; line = $_.line; module = $_.module; count = $_.count; stack = $_.stack } })
    $warningCount = [int]$con.result.counts.warning
}
$report.runtimeErrors = $runtimeErrors
$report['warningCount'] = $warningCount
$report.shots = @($shotObjs | Where-Object { $_.path } | ForEach-Object { $_.path })
$report['shotStats'] = @($shotObjs | ForEach-Object { [ordered]@{ name = $_.name; preset = $_.preset; t = $_.t; meanLuma = [math]::Round($_.meanLuma, 1); stdLuma = [math]::Round($_.stdLuma, 1); blank = $_.blank; error = $_.error } })
if ($stats -and $stats.success -and $stats.result.ok) {
    $f = $stats.result.fps
    $report.fps = [ordered]@{ avg = [math]::Round($f.avg, 1); min = [math]::Round($f.min, 1); p95ms = [math]::Round($f.p95ms, 2); p99ms = [math]::Round($f.p99ms, 2); hitches = $f.hitches; cpuMainAvgMs = [math]::Round($f.cpuMainAvgMs, 2); samples = $f.samples; editorFocused = $stats.result.editorFocused }
    $r = $stats.result.render
    $report['render'] = [ordered]@{ batches = [math]::Round($r.batches, 1); setPassCalls = [math]::Round($r.setPassCalls, 1); drawCalls = [math]::Round($r.drawCalls, 1); triangles = [math]::Round($r.triangles); vertices = [math]::Round($r.vertices) }
}
if ($playResult) {
    $report['play'] = [ordered]@{ success = $playResult.success; error = $playResult.error; probeReady = $playResult.probeReady; readySec = [math]::Round($playResult.readySec, 2); wallSec = [math]::Round($playResult.wallSec, 2); gameSec = [math]::Round($playResult.gameSec, 2); frames = $playResult.frames; modules = @($playResult.modules); failedModules = @($playResult.failedModules); inputEventsApplied = $playResult.inputEventsApplied; events = @($playResult.events | ForEach-Object { [ordered]@{ name = $_.name; count = $_.count } }) }
}

$blank = @($shotObjs | Where-Object { $_.blank -or $_.error }).Count
$playOk = $NoPlay -or ($playResult -and $playResult.success)
$lintCount = @($lintIssues).Count
$report.ok = ($runtimeErrors.Count -eq 0) -and $playOk -and ($blank -eq 0) -and ($shotObjs.Count -gt 0) -and ($lintCount -eq 0) -and ($shaderErrors.Count -eq 0)
$report.stage = if ($report.ok) { 'done' } elseif ($shaderErrors.Count -gt 0) { 'shader' } elseif (-not $playOk) { 'play' } elseif ($runtimeErrors.Count -gt 0) { 'runtime' } elseif ($lintCount -gt 0) { 'lint' } else { 'shots' }
Write-Report $report ($(if ($report.ok) { 0 } else { 1 }))
