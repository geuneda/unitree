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
  powershell -ExecutionPolicy Bypass -File tools/open.ps1 -UnityVersion <installed 6.x>   # open in another Editor version

  Prints one JSON object. Exit code 0 = ready. On failure: "error", "dialog" when a modal dialog blocks the Editor
  (a person has to answer it), and "logTail" (end of the Editor log). From an agent worktree this opens the Editor tree.
  Dialogs before the harness can answer (software terms of a newly installed version, Safe Mode, package errors) are
  seen from outside: the log stops growing while the Editor window shows the dialog's title (-DialogSec).
  -UnityVersion first writes that version to ProjectSettings/ProjectVersion.txt (what Unity itself does once you confirm
  its "open in another version" dialog; without it that dialog blocks the start), so the change shows in git status.
#>
param(
    [int]$TimeoutSec = 900,   # a fresh clone imports for minutes
    [switch]$NoWait,
    [string]$UnityVersion,    # default: ProjectSettings/ProjectVersion.txt
    [int]$DialogSec = 60      # before the Pipeline server is up: log silent this long + a dialog title = blocked
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
$started = Get-HarnessLaunchedEditor   # an earlier open.ps1's Editor, maybe still before its lock file (a dialog)
if ($editor -or $started -or (Test-HarnessProjectOpen)) {
    $result['alreadyOpen'] = $true
    if ($editor) { $result.pid = $editor.Id } elseif ($started) { $result.pid = $started.Id; $result.logFile = $log.Replace('\', '/') }
} else {
    $projectVersion = Get-HarnessProjectVersion
    $result.version = if ($UnityVersion) { $UnityVersion } else { $projectVersion }
    $exe = Find-HarnessEditorExe $result.version
    if (-not $exe) {
        $installed = @(Get-HarnessInstalledEditors | ForEach-Object { $_.version })
        $result['error'] = "Unity $($result.version) $(if ($UnityVersion) { '' } else { '(ProjectSettings/ProjectVersion.txt) ' })is not installed: unity install $($result.version)" +
            ", or open the project with an installed Unity 6 Editor: -UnityVersion <version> (installed: $($installed -join ', '))"
        Finish 1
    }
    if ($result.version -ne $projectVersion) {
        # Another version: Unity would ask in a modal dialog first. A project that names this version opens directly.
        $f = Join-Path $root 'ProjectSettings/ProjectVersion.txt'
        [IO.File]::WriteAllText($f, "m_EditorVersion: $($result.version)`n", (New-Object Text.UTF8Encoding($false)))
        $result['projectVersion'] = $projectVersion
    }
    $logDir = Split-Path -Parent $log
    New-Item -ItemType Directory -Force $logDir | Out-Null
    if (Test-Path -LiteralPath $log) { Move-Item -LiteralPath $log -Destination (Join-Path $logDir 'Editor-prev.log') -Force }
    $argLine = (@('-projectPath', $root, '-logFile', $log) | ForEach-Object { ConvertTo-HarnessArg $_ }) -join ' '
    $p = Start-Process -FilePath $exe -ArgumentList $argLine -PassThru
    Save-HarnessLaunchedEditor $p
    $result.pid = $p.Id
    $result.launched = $true
    $result.logFile = $log.Replace('\', '/')
}
if ($NoWait) { $result.ok = $true; Finish 0 }

# Ready = harness_ping answers and the Editor stays idle for 3 s (a fresh session recompiles once for Debug code
# optimization right after the first answer).
$idleSince = $null
$dialogSince = $null
$logLength = -1
$logSince = 0
# Window titles of the Editor's own progress windows while it starts (not dialogs).
$progressTitle = '^(Opening project|Hold on|Importing|Compiling|Loading|Initializing|Resolving|Refreshing|Updating)'
while ($true) {
    if ($clock.Elapsed.TotalSeconds -ge $TimeoutSec) {
        $result['error'] = "The Editor was not ready within $TimeoutSec s"
        if ($result.pid) { $result['windows'] = @(Get-HarnessWindowTitles $result.pid) }
        break
    }
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
    } elseif ($result.pid -and $result.logFile) {
        # No Pipeline server yet, so no harness_ping to ask. A dialog shown this early stops the log, and the Editor
        # process then has a window with the dialog's title ("Unity Editor Software Terms", "Enter Safe Mode?", ...).
        $len = if (Test-Path -LiteralPath $log) { (Get-Item -LiteralPath $log).Length } else { 0 }
        if ($len -ne $logLength) { $logLength = $len; $logSince = $clock.Elapsed.TotalSeconds }
        elseif ($clock.Elapsed.TotalSeconds - $logSince -ge $DialogSec) {
            $titles = @(Get-HarnessWindowTitles $result.pid | Where-Object { $_ -notmatch $progressTitle })
            if ($titles.Count -gt 0) {
                $result['dialog'] = [ordered]@{ title = $titles[0]; windows = $titles; logSilentSec = [math]::Round($clock.Elapsed.TotalSeconds - $logSince) }
                $result['error'] = "The Editor shows '$($titles[0])' and has logged nothing for $($result.dialog.logSilentSec) s: a dialog that a person has to answer. It stays open; run open.ps1 again afterwards (it waits for that Editor)."
                break
            }
        }
    }
    Start-Sleep -Milliseconds 500
}
if ($result.logFile -and (Test-Path -LiteralPath $log)) { $result['logMB'] = [math]::Round((Get-Item -LiteralPath $log).Length / 1MB, 2) }
Finish $(if ($result.ok) { 0 } else { 1 })
