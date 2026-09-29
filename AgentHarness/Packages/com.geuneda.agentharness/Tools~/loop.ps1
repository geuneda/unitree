<#
.SYNOPSIS
  One-shot agent loop - the Unity equivalent of "save, reload, screenshot, read console":
  recompile -> (stop on compile errors) -> harness_build -> harness_play (3 shots by default)
  -> harness_console + harness_stats -> one report.json (also printed to stdout).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/loop.ps1
  powershell -ExecutionPolicy Bypass -File tools/loop.ps1 -Scenario tools/scenarios/default.json -Out HarnessOut/latest
  powershell -ExecutionPolicy Bypass -File tools/loop.ps1 -NoPlay      # edit-mode captures only (faster, no gameplay)
  powershell -ExecutionPolicy Bypass -File tools/loop.ps1 -UpdateGolden   # the shots look right: make them the golden images

  Exit code 0 = everything green (no compile/build/runtime errors, no blank shots), 1 = see report.json "stage".
  Shots are compared with the golden images (golden/<Unity version>/<scenario>/, report "golden"); a difference is
  reported, not a failure.
  In an agent worktree use tools/submit.ps1 instead (it runs this same loop on the Editor tree, as a transaction).
#>
param(
    [string]$Scenario = 'tools/scenarios/default.json',
    [string]$Out = 'HarnessOut/latest',
    [switch]$NoPlay,
    [switch]$NoCompile,
    [int]$TimeoutSec = 180,
    [string]$Golden,           # golden image root (default: goldenRoot of ProjectSettings/AgentHarness.json, "golden")
    [switch]$UpdateGolden      # write this loop's shots as the golden images of this Unity version (only when green)
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
$root = Get-HarnessProjectRoot
$outAbs = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path (Get-HarnessWorkRoot) $Out }
$clock = [Diagnostics.Stopwatch]::StartNew()
$timings = [ordered]@{}

# The loop compiles whatever is in the Editor tree. From an agent worktree that is not this checkout's code.
if (Test-HarnessWorktree) {
    $report = [ordered]@{ ok = $false; stage = 'submit'; compileErrors = @(); runtimeErrors = @(); fps = $null; shots = @(); durationSec = 0
        error = "This is an agent worktree ($(Get-HarnessWorkRoot)); the Editor runs on $root. Run tools/submit.ps1 -Module <YourModule> to test your module there." }
    Save-HarnessReport $report $outAbs $clock $timings
    exit 1
}

# One Editor: loops from parallel agents queue here (machine-wide mutex per project).
$timings['lockWaitSec'] = Enter-HarnessLock
try {
    $report = Invoke-HarnessLoop -Scenario $Scenario -OutDir $outAbs -NoPlay:$NoPlay -NoCompile:$NoCompile -TimeoutSec $TimeoutSec -Timings $timings -GoldenRoot $Golden -UpdateGolden:$UpdateGolden
    Add-HarnessRecovery $report
} finally { Exit-HarnessLock }
Save-HarnessReport $report $outAbs $clock $timings
exit $(if ($report.ok) { 0 } else { 1 })
