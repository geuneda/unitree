<#
.SYNOPSIS
  Fresh-clone test (O-8): clone the repository to a short path, open the clone in its own Editor, run harness_setup and
  -Loops loops there, check that they are green and deterministic and that the clone's git status stays clean, close
  the Editor and delete the clone. Every step runs the clone's own tools (open.ps1, uc.ps1, loop.ps1, quit.ps1), i.e.
  COMMITTED code: commit (or temp-commit) what you want to test first. Uncommitted changes are listed in the report.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/fresh-clone-test.ps1
  powershell -ExecutionPolicy Bypass -File tools/fresh-clone-test.ps1 -Source https://github.com/geuneda/unitree -Ref master
  powershell -ExecutionPolicy Bypass -File tools/fresh-clone-test.ps1 -UnityVersion <installed>   # another Editor version (P-1)
  powershell -ExecutionPolicy Bypass -File tools/fresh-clone-test.ps1 -SelfTest                   # + tools/selftest.ps1 in the clone
  powershell -ExecutionPolicy Bypass -File tools/fresh-clone-test.ps1 -Keep                      # leave the clone and its Editor open

  Exit code 0 = green. Report: HarnessOut/fresh-clone/report.json (also on stdout) + the clone's loop reports
  (loop<N>.json), the last loop's shots (shots/), the clone's Editor log (Editor.log) and with -SelfTest the
  verification matrix report (selftest.json; its worktrees go next to the clone and are removed again).
  A failed run closes the clone's Editor but keeps the clone for inspection; replace it next time with -Force.
#>
param(
    [string]$Source,              # default: this repository
    [string]$Ref = 'HEAD',        # commit, branch or tag of -Source
    [string]$Dest,                # default: <repository parent>/ah-fresh
    [string]$UnityVersion,        # default: the clone's ProjectSettings/ProjectVersion.txt
    [int]$Loops = 3,
    [string]$ExpectFingerprint,   # optional: build.fingerprint (or its prefix) every loop must produce
    [switch]$SelfTest,            # then run the clone's tools/selftest.ps1 (verification matrix 1-8)
    [switch]$Keep,
    [switch]$Force,
    [string]$Out = 'HarnessOut/fresh-clone',
    [int]$OpenTimeoutSec = 1800
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
$work = Get-HarnessWorkRoot
$outAbs = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $work $Out }
$clock = [Diagnostics.Stopwatch]::StartNew()
$timings = [ordered]@{}
$report = [ordered]@{ ok = $false; stage = ''; source = $null; ref = $Ref; commit = $null; clone = $null; project = $null; unityVersion = $null }
$state = @{ phase = 'prepare'; project = $null; pid = $null; versionChanged = $false }
$ps = Get-HarnessPowerShell
# The clone's tools must drive the clone's Editor, never the one this variable may point at.
$childEnv = @{ AGENTHARNESS_EDITOR_ROOT = $null; AGENTHARNESS_WORK_ROOT = $null }

function Set-Failure([string]$Message) { $report.stage = $state.phase; $report['error'] = $Message }

# Run one of the clone's tools/*.ps1 in a child PowerShell; parses its stdout as JSON when it is JSON.
function Invoke-CloneTool([string]$Name, [string[]]$Arguments = @(), [int]$TimeoutSec = 600) {
    $script = Join-Path $state.project "tools/$Name"
    $r = Invoke-HarnessProcess $ps (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script) + $Arguments) -Environment $childEnv -TimeoutSec $TimeoutSec
    $json = $null
    try { $json = $r.out | ConvertFrom-Json } catch { }
    [pscustomobject]@{ code = $r.code; json = $json; out = $r.out; err = $r.err.Trim(); timedOut = $r.timedOut; sec = $r.sec }
}

function Remove-Tree([string]$Path) {
    for ($i = 0; $i -lt 5; $i++) {
        if (-not (Test-Path -LiteralPath $Path)) { return $null }
        try { Remove-Item -LiteralPath $Path -Recurse -Force; return $null }
        catch { $last = $_.Exception.Message; Start-Sleep -Seconds 2 }   # a just-closed Editor or the indexer still holds files
    }
    if (Test-Path -LiteralPath $Path) { return "could not delete ${Path}: $last" }
    $null
}

function Get-LoopSummary($r) {
    [ordered]@{
        ok = $r.ok; stage = $r.stage; durationSec = $r.durationSec; timings = $r.timings
        fingerprint = if ($r.build) { $r.build.fingerprint } else { $null }
        events = (@($r.play.events | ForEach-Object { "$($_.name)=$($_.count)" }) -join ',')
        shots = @($r.shots).Count; blank = @($r.shotStats | Where-Object { $_.blank -or $_.error }).Count
        compileErrors = @($r.compileErrors); runtimeErrors = @($r.runtimeErrors).Count
        editorErrors = @($r.editorErrors | ForEach-Object { $_.msg }); warningCount = $r.warningCount
        fpsAvg = if ($r.fps) { $r.fps.avg } else { $null }; error = $r.error
    }
}

function Invoke-FreshClone {
    # ---- prepare: where, what -------------------------------------------------------------------------------------
    $top = (Invoke-HarnessGit $work @('rev-parse', '--show-toplevel') -Check).out.Trim()
    $prefix = (Invoke-HarnessGit $work @('rev-parse', '--show-prefix') -Check).out.Trim().TrimEnd('/')
    if (-not $Dest) { $script:Dest = Join-Path (Split-Path -Parent $top) 'ah-fresh' }
    $destFull = [IO.Path]::GetFullPath($Dest).TrimEnd('\', '/')
    $state.project = if ($prefix) { Join-Path $destFull $prefix } else { $destFull }
    $report.clone = $destFull.Replace('\', '/')
    $report.project = $state.project.Replace('\', '/')
    $report['projectPathLength'] = $state.project.Length
    if ($Source) { $report.source = $Source } else {
        $report.source = $top
        $report.commit = (Invoke-HarnessGit $work @('rev-parse', '--verify', "$Ref^{commit}") -Check).out.Trim()
        $dirty = @((Get-HarnessGitStatus $work).Keys | Sort-Object)
        if ($dirty.Count -gt 0) { $report['uncommittedNotTested'] = @($dirty | Select-Object -First 20) }
    }
    # Unity cannot read its package files under a long path (F-5), and Burst's JIT DLLs are blocked under %TEMP% (O-5).
    if ($state.project.Length -gt 60) { Set-Failure "project path is $($state.project.Length) chars; keep it at 60 or less (-Dest)"; return }
    if ($state.project.StartsWith([IO.Path]::GetTempPath().TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) { Set-Failure 'do not test under the temp folder (Burst DLLs are blocked there); pass another -Dest'; return }
    if (Test-Path -LiteralPath $destFull) {
        if (-not $Force) { Set-Failure "$destFull exists (a kept or failed run?); pass -Force to replace it"; return }
        if (Test-Path -LiteralPath (Join-Path $state.project 'tools/quit.ps1')) { [void](Invoke-CloneTool 'quit.ps1' @('-Force') -TimeoutSec 120) }
        $e = Remove-Tree $destFull
        if ($e) { Set-Failure $e; return }
    }

    # ---- clone ------------------------------------------------------------------------------------------------------
    $state.phase = 'clone'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $parent = Split-Path -Parent $destFull
    New-Item -ItemType Directory -Force $parent | Out-Null
    $r = Invoke-HarnessGit $parent @('clone', '--quiet', '--no-checkout', $report.source, $destFull)
    if ($r.code -ne 0) { Set-Failure "git clone failed: $($r.err)"; return }
    if (-not $report.commit) {
        foreach ($c in @($Ref, "origin/$Ref")) {
            $r = Invoke-HarnessGit $destFull @('rev-parse', '--verify', '--quiet', "$c^{commit}")
            if ($r.code -eq 0) { $report.commit = $r.out.Trim(); break }
        }
        if (-not $report.commit) { Set-Failure "ref '$Ref' not found in $($report.source)"; return }
    }
    $r = Invoke-HarnessGit $destFull @('checkout', '--quiet', '--detach', $report.commit)
    if ($r.code -ne 0) { Set-Failure "git checkout $($report.commit) failed: $($r.err)"; return }
    $timings['cloneSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)

    # ---- Unity version ----------------------------------------------------------------------------------------------
    $state.phase = 'version'
    $projectVersion = Get-HarnessProjectVersion $state.project
    $report.unityVersion = if ($UnityVersion) { $UnityVersion } else { $projectVersion }
    $installed = @(Get-HarnessInstalledEditors)
    if (-not ($installed | Where-Object { $_.version -eq $report.unityVersion })) {
        Set-Failure "Unity $($report.unityVersion) is not installed (installed: $(($installed | ForEach-Object { $_.version }) -join ', ')): unity install $($report.unityVersion)"
        return
    }
    if ($report.unityVersion -ne $projectVersion) {
        # Opening a project in another version asks for confirmation in a dialog; a project that says this version does not.
        $f = Join-Path $state.project 'ProjectSettings/ProjectVersion.txt'
        [IO.File]::WriteAllText($f, "m_EditorVersion: $($report.unityVersion)`n", (New-Object Text.UTF8Encoding($false)))
        $state.versionChanged = $true
        $report['projectVersion'] = $projectVersion
    }

    # ---- open (first import) ----------------------------------------------------------------------------------------
    $state.phase = 'open'
    $r = Invoke-CloneTool 'open.ps1' @('-TimeoutSec', "$OpenTimeoutSec") -TimeoutSec ($OpenTimeoutSec + 120)
    $timings['openSec'] = $r.sec
    if ($r.json) { $report['open'] = $r.json; $state.pid = $r.json.pid }
    if ($r.code -ne 0 -or -not $r.json -or -not $r.json.ok) {
        Set-Failure $(if ($r.json -and $r.json.error) { "open.ps1: $($r.json.error)" } else { "open.ps1 failed ($($r.code)): $($r.err) $($r.out)" })
        return
    }

    # ---- harness_setup ----------------------------------------------------------------------------------------------
    $state.phase = 'setup'
    $r = Invoke-CloneTool 'uc.ps1' @('harness_setup') -TimeoutSec 300
    $timings['setupSec'] = $r.sec
    if ($r.json -and $r.json.success) { $report['setup'] = [ordered]@{ changed = @($r.json.result.changed); issues = @($r.json.result.issues) } }
    if ($r.code -ne 0 -or -not $r.json -or -not $r.json.success -or -not $r.json.result.ok) { Set-Failure "harness_setup failed: $($r.err) $($r.out)"; return }
    if (@($r.json.result.issues).Count -gt 0) { Set-Failure "settings still wrong after harness_setup: $(@($r.json.result.issues) -join '; ')"; return }

    # ---- loops ------------------------------------------------------------------------------------------------------
    $state.phase = 'loop'
    $report['loops'] = @()
    $timings['loopSec'] = @()
    for ($i = 1; $i -le $Loops; $i++) {
        $loopOut = "HarnessOut/loop$i"
        $r = Invoke-CloneTool 'loop.ps1' @('-Out', $loopOut) -TimeoutSec 900
        $timings['loopSec'] += $r.sec
        $file = Join-Path $state.project "$loopOut/report.json"
        if (-not (Test-Path -LiteralPath $file)) { Set-Failure "loop $i wrote no report ($($r.code)): $($r.err)"; return }
        Copy-Item -LiteralPath $file -Destination (Join-Path $outAbs "loop$i.json") -Force
        $s = Get-LoopSummary ([IO.File]::ReadAllText($file) | ConvertFrom-Json)
        $report['loops'] += , $s
        if (-not $s.ok) { Set-Failure "loop $i is red: stage=$($s.stage) $($s.error)"; return }
    }
    $shotDir = Join-Path $outAbs 'shots'
    New-Item -ItemType Directory -Force $shotDir | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $state.project "HarnessOut/loop$Loops") -Filter '*.png' | Copy-Item -Destination $shotDir -Force
    $report['shots'] = @(Get-ChildItem -LiteralPath $shotDir -Filter '*.png' | ForEach-Object { $_.FullName.Replace('\', '/') })

    # ---- determinism: same code -> same scene and the same gameplay events, in every loop ----------------------------
    $state.phase = 'determinism'
    $fps = @($report.loops | ForEach-Object { $_.fingerprint } | Select-Object -Unique)
    $events = @($report.loops | ForEach-Object { $_.events } | Select-Object -Unique)
    $report['fingerprint'] = $fps[0]
    if ($fps.Count -ne 1 -or -not $fps[0]) { Set-Failure "build.fingerprint differs between loops: $($fps -join ', ')"; return }
    if ($events.Count -ne 1) { Set-Failure "play.events differ between loops: $($events -join ' | ')"; return }
    if ($ExpectFingerprint -and -not $fps[0].StartsWith($ExpectFingerprint)) { Set-Failure "fingerprint $($fps[0]) is not the expected $ExpectFingerprint"; return }

    # ---- selftest: the verification matrix, in this clone and with its Editor version (P-1) -------------------------
    if ($SelfTest) {
        $state.phase = 'selftest'
        # -KeepGoing: one red item (e.g. a rendering difference of this Unity version) must not hide the others.
        $r = Invoke-CloneTool 'selftest.ps1' @('-Out', 'HarnessOut/selftest', '-ExpectFingerprint', $fps[0], '-KeepGoing') -TimeoutSec 3600
        $timings['selftestSec'] = $r.sec
        $file = Join-Path $state.project 'HarnessOut/selftest/report.json'
        if (-not (Test-Path -LiteralPath $file)) { Set-Failure "selftest wrote no report ($($r.code)): $($r.err)"; return }
        Copy-Item -LiteralPath $file -Destination (Join-Path $outAbs 'selftest.json') -Force
        $st = [IO.File]::ReadAllText($file) | ConvertFrom-Json
        $report['selftest'] = [ordered]@{ ok = $st.ok; stage = $st.stage; unityVersion = $st.unityVersion; lines = $st.lines
            items = @($st.items | ForEach-Object { "$($_.item) $(if ($_.ok) { 'ok' } else { 'RED' }) $($_.sec)s $($_.name)" })
            failed = @($st.items | ForEach-Object { $i = $_.item; @($_.checks | Where-Object { -not $_.ok } | ForEach-Object { "${i}: $($_.check) ($($_.actual))" }) })
            final = $st.final; cleanupErrors = $st.cleanupErrors; error = $st.error }
        if (-not $st.ok) { Set-Failure "selftest is red: stage=$($st.stage) (selftest.json)"; return }
    }
    $state.phase = 'done'
}

try { New-Item -ItemType Directory -Force $outAbs | Out-Null; Get-ChildItem -LiteralPath $outAbs | Remove-Item -Recurse -Force } catch { }
try { Invoke-FreshClone } catch { Set-Failure "unexpected error: $($_.Exception.Message) at $($_.InvocationInfo.PositionMessage)" }
$green = $state.phase -eq 'done'

# ---- close the Editor (also after a failure), then check that nothing in the clone changed -----------------------------
if ($state.project -and (Test-Path -LiteralPath (Join-Path $state.project 'tools/quit.ps1')) -and -not $Keep) {
    $r = Invoke-CloneTool 'quit.ps1' @('-TimeoutSec', '60') -TimeoutSec 1000
    if (-not ($r.json -and $r.json.ok)) { $r = Invoke-CloneTool 'quit.ps1' @('-Force') -TimeoutSec 300 }
    $timings['quitSec'] = $r.sec
    if ($r.json) { $report['quit'] = $r.json }
    # Whatever quit.ps1 said (an older clone's quit.ps1 reports "no Editor" while a startup dialog waits): the Editor
    # open.ps1 started must be gone. One that has just exited can still be listed for a moment, with HasExited true.
    if ($state.pid) {
        $p = Get-Process -Id $state.pid -ErrorAction SilentlyContinue
        if ($p -and $p.ProcessName -eq 'Unity' -and -not $p.HasExited) { Stop-Process -Id $state.pid -Force; $report['killed'] = $state.pid }
    }
    if ($green -and -not ($r.json -and $r.json.ok -and $r.json.method -eq 'harness_quit')) { $state.phase = 'quit'; Set-Failure "the Editor did not close normally: $($r.err) $($r.out)"; $green = $false }
}
if ($state.project) {
    $log = Join-Path $state.project 'Logs/Editor.log'
    if (Test-Path -LiteralPath $log) {
        Copy-Item -LiteralPath $log -Destination (Join-Path $outAbs 'Editor.log') -Force
        $report['editorLog'] = [ordered]@{ path = (Join-Path $outAbs 'Editor.log').Replace('\', '/'); mb = [math]::Round((Get-Item -LiteralPath $log).Length / 1MB, 2)
            # O-7: the message that filled the shared Editor.log to 1.4 GB with two Editors open.
            accessVersionLines = @(Select-String -LiteralPath $log -SimpleMatch 'Access version should be odd').Count }
    }
}
if ($green -and -not $Keep) {
    # After the Editor has exited, so files it writes on exit count too (F-4: ProjectSettings modified by merely opening it).
    $expected = if ($state.versionChanged) { @('ProjectSettings/ProjectVersion.txt') } else { @() }
    $changed = @((Get-HarnessGitStatus $report.clone).GetEnumerator() | Where-Object { $expected -notcontains $_.Key } | ForEach-Object { "$($_.Value) $($_.Key)" } | Sort-Object)
    $report['gitStatus'] = $changed
    # Another Unity version may legitimately upgrade files; that is reported for P-1, not failed here.
    if ($changed.Count -gt 0 -and -not $state.versionChanged) { $state.phase = 'git'; Set-Failure "the clone's git status is not clean: $($changed -join '; ')"; $green = $false }
}

$report.ok = $green
if ($green) { $report.stage = 'done' }
if ($green -and -not $Keep) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $e = Remove-Tree $report.clone
    $timings['cleanupSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    if ($e) { $report['cleanupError'] = $e }
} elseif ($report.clone -and (Test-Path -LiteralPath $report.clone)) { $report['kept'] = $report.clone }

Save-HarnessReport $report $outAbs $clock $timings
exit $(if ($report.ok) { 0 } else { 1 })
