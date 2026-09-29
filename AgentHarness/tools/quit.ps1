<#
.SYNOPSIS
  Close this project's Editor normally and wait for the process to end (O-4). Takes the Editor lock first, so a
  loop/submit/land of another agent finishes before the Editor goes away. Uses harness_quit (EditorApplication.Exit:
  no save prompts); Pipeline's own 'quit' fails in edit mode.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/quit.ps1
  powershell -ExecutionPolicy Bypass -File tools/quit.ps1 -Force     # kill it if it has not exited after -TimeoutSec

  Prints one JSON object. Exit code 0 = no Editor is serving this project any more (also when none was running).
  From an agent worktree this closes the Editor tree's Editor.
#>
param(
    [int]$TimeoutSec = 30,
    [switch]$Force,
    [int]$LockTimeoutSec = 900
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
$clock = [Diagnostics.Stopwatch]::StartNew()
$result = [ordered]@{ ok = $false; project = (Get-HarnessProjectRoot).Replace('\', '/'); pid = $null; method = $null; quitSec = 0 }

function Finish([int]$Code) {
    $result.quitSec = [math]::Round($clock.Elapsed.TotalSeconds, 2)
    $result | ConvertTo-Json -Depth 10
    exit $Code
}

$editor = Get-HarnessEditorProcess
if (-not $editor) {
    if (Test-HarnessProjectOpen) {
        # Open, but without a Pipeline server (still importing, or Safe Mode): nothing to talk to, and no pid to kill.
        $result['error'] = 'An Editor has this project open but its Pipeline server is not up (importing, or Safe Mode). Close it by hand.'
        Finish 1
    }
    $result.ok = $true
    $result.method = 'none'
    $result['note'] = 'no Editor is running for this project'
    Finish 0
}
$result.pid = $editor.Id

$result['lockWaitSec'] = Enter-HarnessLock -TimeoutSec $LockTimeoutSec
try {
    $sentAt = $null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $editor.HasExited -and $sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        # Ask again every 10 s: a domain reload in between drops the scheduled exit.
        if ($null -eq $sentAt -or $sw.Elapsed.TotalSeconds - $sentAt -ge 10) {
            $r = Invoke-UnityCommand -Name 'harness_quit' -TimeoutSec 10
            if ($r.success) { $sentAt = $sw.Elapsed.TotalSeconds; $result.method = 'harness_quit' }
            elseif ($r.PSObject.Properties.Name -contains 'busyReason') {
                $result['busyReason'] = $r.busyReason
                if ($r.PSObject.Properties.Name -contains 'dialogs') { $result['dialogs'] = $r.dialogs }
            } elseif (-not $r.unreachable) { $result['lastError'] = $r.error }
        }
        [void]$editor.WaitForExit(250)
    }
    if (-not $editor.HasExited -and $Force) {
        Stop-Process -Id $editor.Id -Force
        [void]$editor.WaitForExit(15000)
        $result.method = 'kill'
    }
} finally { Exit-HarnessLock }

$result.ok = $editor.HasExited
if (-not $result.ok) {
    $result['error'] = "The Editor (pid $($editor.Id)) did not exit within $TimeoutSec s" +
        $(if ($result.Contains('busyReason')) { " (busy: $($result.busyReason); a modal dialog needs a person)" } else { '' }) + '. Use -Force to kill it.'
}
Finish $(if ($result.ok) { 0 } else { 1 })
