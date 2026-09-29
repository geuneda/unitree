<#
.SYNOPSIS
  Open this project's Editor and wait until the harness answers. Use it instead of 'unity open':
  - the Editor writes its own log, <project>/Logs/Editor.log (the previous one becomes Editor-prev.log). Editors started
    without -logFile all append to one user-wide Editor.log, which grew to 1.4 GB with two Editors open (O-7).
  - it returns when harness_ping answers and the Editor is idle, i.e. after the first import and the Debug recompile.
  If an Editor already has the project open, it only waits for it (its log is wherever that Editor was told).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/open.ps1
  powershell -ExecutionPolicy Bypass -File tools/open.ps1 -NoWait      # start it and return

  Prints one JSON object. Exit code 0 = ready. On failure: "error", "dialog" when a modal dialog blocks the Editor
  (a person has to answer it), and "logTail" (end of the Editor log). From an agent worktree this opens the Editor tree.
#>
param(
    [int]$TimeoutSec = 900,   # a fresh clone imports for minutes
    [switch]$NoWait
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
$root = Get-HarnessProjectRoot
$log = Join-Path $root 'Logs/Editor.log'
$clock = [Diagnostics.Stopwatch]::StartNew()
$result = [ordered]@{ ok = $false; project = $root.Replace('\', '/'); pid = $null; version = $null; launched = $false; logFile = $null; openSec = 0 }

function Finish([int]$Code) {
    $result.openSec = [math]::Round($clock.Elapsed.TotalSeconds, 2)
    if ($Code -ne 0 -and $result.logFile) { $result['logTail'] = @(Read-HarnessLogTail $log 40) }
    $result | ConvertTo-Json -Depth 10
    exit $Code
}

$editor = Get-HarnessEditorProcess
if ($editor -or (Test-HarnessProjectOpen)) {
    $result['alreadyOpen'] = $true
    if ($editor) { $result.pid = $editor.Id }
} else {
    $result.version = Get-HarnessProjectVersion
    $exe = Find-HarnessEditorExe $result.version
    if (-not $exe) {
        $result['error'] = "Unity $($result.version) (ProjectSettings/ProjectVersion.txt) is not installed: unity install $($result.version)"
        Finish 1
    }
    $logDir = Split-Path -Parent $log
    New-Item -ItemType Directory -Force $logDir | Out-Null
    if (Test-Path -LiteralPath $log) { Move-Item -LiteralPath $log -Destination (Join-Path $logDir 'Editor-prev.log') -Force }
    $argLine = (@('-projectPath', $root, '-logFile', $log) | ForEach-Object { ConvertTo-HarnessArg $_ }) -join ' '
    $p = Start-Process -FilePath $exe -ArgumentList $argLine -PassThru
    $result.pid = $p.Id
    $result.launched = $true
    $result.logFile = $log.Replace('\', '/')
}
if ($NoWait) { $result.ok = $true; Finish 0 }

# Ready = harness_ping answers and the Editor stays idle for 3 s (a fresh session recompiles once for Debug code
# optimization right after the first answer).
$idleSince = $null
$dialogSince = $null
while ($true) {
    if ($clock.Elapsed.TotalSeconds -ge $TimeoutSec) { $result['error'] = "The Editor was not ready within $TimeoutSec s"; break }
    if ($result.pid -and -not (Get-Process -Id $result.pid -ErrorAction SilentlyContinue)) {
        $result['error'] = 'The Editor exited while starting (license, crash, or a startup dialog was closed): see logTail'
        break
    }
    $editor = Get-HarnessEditorProcess
    if ($editor) {
        $result.pid = $editor.Id
        $ping = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 10
        if ($ping.success) {
            $dialogSince = $null
            if (-not $ping.result.isCompiling -and -not $ping.result.isUpdating) {
                if ($null -eq $idleSince) { $idleSince = $clock.Elapsed.TotalSeconds }
                elseif ($clock.Elapsed.TotalSeconds - $idleSince -ge 3) {
                    $result.ok = $true
                    $result['compileFailed'] = [bool]$ping.result.compileFailed
                    $result['domainReloads'] = [int]$ping.result.domainReloads
                    break
                }
            } else { $idleSince = $null }
        } else {
            $idleSince = $null
            if ($ping.PSObject.Properties.Name -contains 'busyReason' -and $ping.busyReason -eq 'blocked_by_dialog') {
                if ($ping.PSObject.Properties.Name -contains 'dialogs') { $result['dialog'] = $ping.dialogs }
                if ($null -eq $dialogSince) { $dialogSince = $clock.Elapsed.TotalSeconds }
                elseif ($clock.Elapsed.TotalSeconds - $dialogSince -ge 20) {
                    $result['error'] = 'A modal dialog blocks the Editor (see "dialog"); a person has to answer it'
                    break
                }
            } elseif (-not $ping.unreachable -and -not ($ping.PSObject.Properties.Name -contains 'busy')) {
                $result['lastError'] = $ping.error   # e.g. harness_ping unknown: Harness.Editor did not compile
            }
        }
    }
    Start-Sleep -Milliseconds 500
}
if ($result.logFile -and (Test-Path -LiteralPath $log)) { $result['logMB'] = [math]::Round((Get-Item -LiteralPath $log).Length / 1MB, 2) }
Finish $(if ($result.ok) { 0 } else { 1 })
