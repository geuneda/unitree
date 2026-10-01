<#
.SYNOPSIS
  Open this project's Editor and wait until the harness answers. Use it instead of 'unity open':
  - the Editor writes its own log, <project>/Logs/Editor.log (the previous one becomes Editor-prev.log). Editors started
    without -logFile all append to one user-wide Editor.log, which grew to 1.4 GB with two Editors open (O-7).
  - it starts the Editor with -automated (W7, G2-2): EditorUtility.DisplayDialog* return their default (cancel) at once
    instead of blocking the Editor until a person answers, and the window layout is not saved on exit. -Interactive leaves
    it out (a person works in that Editor). And with -debugCodeOptimization: Debug from the start, no extra recompile.
  - it returns when harness_ping answers and the Editor is idle, i.e. after the first import.
  If an Editor already has the project open, it only waits for it (its log is wherever that Editor was told).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/open.ps1
  powershell -ExecutionPolicy Bypass -File tools/open.ps1 -Headless    # no window: -batchmode, renders with the GPU
  powershell -ExecutionPolicy Bypass -File tools/open.ps1 -Own         # in an agent worktree: its own (headless) Editor
  powershell -ExecutionPolicy Bypass -File tools/open.ps1 -NoWait      # start it and return
  powershell -ExecutionPolicy Bypass -File tools/open.ps1 -UnityVersion <installed 6.x>   # open in another Editor version

  Prints one JSON object. Exit code 0 = ready. On failure: "error", "dialog" when a modal dialog blocks the Editor
  (a person has to answer it), "safeMode" + "compileErrors" when a windowed Editor started in Safe Mode (the scripts do
  not compile; -automated enters it without asking: fix them, quit.ps1 -Force, open.ps1 again), and "logTail" (end of the
  Editor log). From an agent worktree this opens the Editor tree, unless -Own.
  Dialogs before the harness can answer (software terms of a newly installed version, package errors) are
  seen from outside: the log stops growing while the Editor window shows the dialog's title (-DialogSec).
  -Headless: -batchmode without -quit. No window and no Game view (a "screen" capture is refused, fps has no rendering
  in it); a loop is ~1.3 s faster (play mode renders nothing between captures). When the scripts do not compile it starts
  anyway on the last good assemblies (-ignoreCompilerErrors) and the loop reports the errors.
  -Own (G5-1, G1-2): in an agent worktree, an Editor of its own. Its Library/ starts as a copy of the Editor tree's (taken
  under the Editor lock while that Editor is idle); then loop.ps1, uc.ps1 and quit.ps1 there drive this Editor, in
  parallel with the Editor tree's. submit.ps1 and land.ps1 still integrate into the Editor tree. Headless unless -Window.
  It opens with the Editor tree's Unity version (whose Library it copied), writing it to the worktree's ProjectVersion.txt
  when that says another one.
  -UnityVersion first writes that version to ProjectSettings/ProjectVersion.txt (what Unity itself does once you confirm
  its "open in another version" dialog; without it that dialog blocks the start), so the change shows in git status.
#>
param(
    [int]$TimeoutSec = 900,   # a fresh clone imports for minutes
    [switch]$NoWait,
    [string]$UnityVersion,    # default: ProjectSettings/ProjectVersion.txt
    [int]$DialogSec = 60,     # before the Pipeline server is up: log silent this long + a dialog title = blocked
    [switch]$Headless,        # -batchmode: no window
    [switch]$Interactive,     # without -automated: dialogs wait for a person
    [switch]$Own,             # agent worktree: an Editor of its own (headless unless -Window)
    [switch]$Window           # with -Own: a windowed Editor
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
$clock = [Diagnostics.Stopwatch]::StartNew()
$result = [ordered]@{ ok = $false; project = (Get-HarnessProjectRoot).Replace('\', '/'); pid = $null; version = $null; launched = $false; logFile = $null; openSec = 0 }

# "[Package Manager] <name> is deprecated: ..." lines of the Editor log (the Package Manager registers packages early on).
function Get-DeprecatedPackages([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $s = New-Object IO.FileStream($Path, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        $buf = New-Object byte[] ([math]::Min($s.Length, 4MB))
        $n = $s.Read($buf, 0, $buf.Length)
        $text = [Text.Encoding]::UTF8.GetString($buf, 0, $n)
    } finally { $s.Dispose() }
    @([regex]::Matches($text, '\[Package Manager\] (\S+) is deprecated') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}

function Finish([int]$Code) {
    $result.openSec = [math]::Round($clock.Elapsed.TotalSeconds, 2)
    if ($result.logFile) {
        # Deprecated packages make Unity ask "This project contains one or more deprecated packages. Do you want to open
        # Package Manager?" on every start (a modal dialog a person must answer). Name them so they can be replaced.
        $deprecated = @(Get-DeprecatedPackages $log)
        if ($deprecated.Count -gt 0) {
            $result['deprecatedPackages'] = $deprecated
            if ($result.Contains('dialog')) { $result['error'] += " The project has deprecated packages ($($deprecated -join ', ')): Unity asks about them in a dialog on every start; remove or replace them in Packages/manifest.json." }
        }
    }
    if ($Code -ne 0 -and $result.logFile) { $result['logTail'] = @(Read-HarnessLogTail $log 40) }
    $result | ConvertTo-Json -Depth 10
    exit $Code
}

if ($Own) {
    # An Editor of this agent worktree's own (W7): a copy of the Editor tree's Library/ makes it one more Editor project.
    $work = Get-HarnessWorkRoot
    $tree = Get-HarnessIntegrationRoot
    if (Test-HarnessSamePath $tree $work) {
        $result['error'] = "-Own gives an agent worktree (git worktree add) an Editor of its own; this is the Editor tree itself ($work): open.ps1 without -Own"
        Finish 1
    }
    if (-not $Window) { $Headless = [switch]$true }
    $result['own'] = $true
    # The copied Library belongs to the Editor tree's Unity version (it may have been opened with -UnityVersion, which the
    # worktree's committed ProjectVersion.txt does not say): open this Editor with that version too.
    $treeVersion = Get-HarnessProjectVersion $tree
    if (-not $UnityVersion -and $treeVersion -and $treeVersion -ne (Get-HarnessProjectVersion $work)) { $UnityVersion = $treeVersion }
    if (-not (Test-Path -LiteralPath (Join-Path $work 'Library'))) {
        $from = Join-Path $tree 'Library'
        if (Test-Path -LiteralPath $from) {
            # Under the Editor tree's lock, with its Editor idle: no loop or import writes the asset database while it is copied.
            Set-HarnessEditorRoot $tree
            $lockWait = Enter-HarnessLock
            try {
                if (Get-HarnessEditorProcess) { [void](Wait-HarnessIdle -TimeoutSec 120) }
                $result['seeded'] = Copy-HarnessLibrary -From $from -To (Join-Path $work 'Library')
                $result.seeded['lockWaitSec'] = $lockWait
            } finally { Exit-HarnessLock }
        } else {
            [void][IO.Directory]::CreateDirectory((Join-Path $work 'Library'))
            $result['seeded'] = [ordered]@{ note = "the Editor tree ($tree) has no Library/ yet: this Editor imports the project from scratch" }
        }
    }
    Set-HarnessEditorRoot $work
    $result.project = $work.Replace('\', '/')
}
$root = Get-HarnessProjectRoot
$log = Join-Path $root 'Logs/Editor.log'

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
    $p = Start-HarnessDetached $exe @(Get-HarnessEditorArguments -Root $root -Log $log -Headless:$Headless -Interactive:$Interactive)
    Save-HarnessLaunchedEditor $p
    $result.pid = $p.Id
    $result.launched = $true
    $result.logFile = $log.Replace('\', '/')
    $result['mode'] = if ($Headless) { 'headless' } else { 'window' }
    $result['automated'] = -not $Interactive
}
if ($NoWait) { $result.ok = $true; Finish 0 }

# Ready = harness_ping answers and the Editor stays idle for 2 s (an Editor opened elsewhere without
# -debugCodeOptimization recompiles once for Debug right after the first answer).
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
                elseif ($clock.Elapsed.TotalSeconds - $idleSince -ge 2) {
                    $result.ok = $true
                    $result['compileFailed'] = [bool]$ping.result.compileFailed
                    $result['domainReloads'] = [int]$ping.result.domainReloads
                    if ($ping.result.PSObject.Properties.Name -contains 'editorMode') {
                        $mode = [string]$ping.result.editorMode
                        if ($result.alreadyOpen -and (($Headless -and $mode -ne 'headless') -or ($Window -and $mode -ne 'window'))) {
                            $result['note'] = "an Editor was already open ($mode); quit.ps1 first to start it the other way"
                        }
                        $result['mode'] = $mode
                        $result['automated'] = [bool]$ping.result.automated
                    }
                    if ($result.compileFailed) { $result['note'] = 'the scripts do not compile: it runs the last good assemblies; loop.ps1 reports the errors' }
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
    } elseif ($result.pid -and (Test-HarnessSafeMode $result.pid)) {
        # A windowed Editor whose scripts did not compile at startup: -automated enters Safe Mode without asking, and Safe
        # Mode runs no Pipeline server. Its log has the errors.
        $result['safeMode'] = $true
        if ($result.logFile) { $result['compileErrors'] = @(Get-HarnessLogCompileErrors $log) }
        $result['error'] = 'The Editor started in Safe Mode: the scripts do not compile (compileErrors). Fix them, then tools/quit.ps1 -Force and tools/open.ps1 again; or open.ps1 -Headless, which starts on the last good assemblies so that loop.ps1 reports the errors.'
        break
    } elseif ($result.pid -and $result.logFile) {
        # No Pipeline server yet, so no harness_ping to ask. A dialog shown this early stops the log, and the Editor
        # process then has a window with the dialog's title ("Unity Editor Software Terms", "Enter Safe Mode?" of an
        # -Interactive Editor, ...).
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
