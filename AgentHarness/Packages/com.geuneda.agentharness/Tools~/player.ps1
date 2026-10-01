<#
.SYNOPSIS
  Player run (W8): the same scenario in a development build of the game - frame times without the Editor (G3-2) and the
  screen as the game draws it at the capture size (G3-8) - next to the Editor's play of it:
  Editor loop (compile, build, play -> <Out>/editor) -> development Player build (HarnessOut/player-build/<target>/,
  incremental) -> the Player, windowed at the capture size, plays the scenario on its own and quits -> its shots are
  compared with the Editor's, and its screen with its own shots -> <Out>/report.json (also printed).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/player.ps1
  powershell -ExecutionPolicy Bypass -File tools/player.ps1 -Scenario tools/scenarios/x.json
  powershell -ExecutionPolicy Bypass -File tools/player.ps1 -NoEditor      # no Editor play (still compiles and builds the scene): no comparisons
  powershell -ExecutionPolicy Bypass -File tools/player.ps1 -NoBuild       # the last Player build as it is (only the scenario changed)
  powershell -ExecutionPolicy Bypass -File tools/player.ps1 -PlayerArgs '-force-d3d11'
  powershell -ExecutionPolicy Bypass -File tools/player.ps1 -Debugging     # exact lines of a Player-only runtime error (unoptimized code)

  Exit code 0 = the Player was built and played the scenario without errors (no blank shots). How it differs from the
  Editor - frame times, events, pixels - is reported, not a failure. The Editor lock is held throughout: no other loop
  runs on this Editor while the Player measures frame times. The Player's window takes the focus for a few seconds.
#>
param(
    [string]$Scenario = 'tools/scenarios/default.json',
    [string]$Out = 'HarnessOut/player',
    [switch]$NoEditor,            # skip the Editor's play: no comparisons
    [switch]$NoBuild,             # start the last build of HarnessOut/player-build as it is
    [switch]$Paced,               # keep the project's vSync and frame cap (default: unpaced, frame times = the work)
    [switch]$Debugging,           # Script Debugging build: exact runtime error lines, slower (unoptimized) code
    [string[]]$PlayerArgs = @(),  # more Player command-line arguments (-force-d3d11, ...)
    [int]$TimeoutSec = 180,       # the Editor loop, and the Player at least this long
    [int]$BuildTimeoutSec = 1800
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
Set-StrictMode -Off   # reads optional fields of Editor replies and of result.json
$root = Get-HarnessProjectRoot
$work = Get-HarnessWorkRoot
$outAbs = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $work $Out })).Replace('\', '/')
$clock = [Diagnostics.Stopwatch]::StartNew()
$timings = [ordered]@{}
$report = [ordered]@{ ok = $false; stage = ''; scenario = $Scenario; editor = $null; player = $null; fps = $null; render = $null
    shots = @(); durationSec = 0; report = "$outAbs/report.json" }

if (Test-HarnessWorktree) {
    $report.stage = 'submit'
    $report['error'] = "This is an agent worktree ($work); the Editor runs on $root. Give this worktree an Editor of its own with tools/open.ps1 -Own, or run tools/player.ps1 in the Editor tree."
    Save-HarnessReport $report $outAbs $clock $timings
    exit 1
}

function Round([double]$v, [int]$d = 2) { [math]::Round($v, $d) }
function Step([string]$What) { [Console]::Error.WriteLine("player.ps1: $What ($([math]::Round($clock.Elapsed.TotalSeconds, 1)) s)") }
# Plain strings (Get-Content's lines carry PSDrive/PSProvider properties that ConvertTo-Json -Depth 20 expands for minutes).
function Tail([string]$Path, [int]$Lines = 30) { Read-HarnessLogTail $Path $Lines }

# A scenario file (absolute: the Player reads it) - an inline scenario is written next to the report.
Initialize-HarnessOut $outAbs
[void][IO.Directory]::CreateDirectory($outAbs)
$trimmed = $Scenario.Trim()
if ($trimmed.StartsWith('{')) {
    $scenarioAbs = "$outAbs/scenario.json"
    [IO.File]::WriteAllText($scenarioAbs, $trimmed, (New-Object Text.UTF8Encoding($false)))
} else {
    $scenarioAbs = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($trimmed)) { $trimmed } else { Join-Path $work $trimmed })).Replace('\', '/')
}

# Steps 1-6 (a function: a step that fails returns, and the report is still written).
function Invoke-PlayerRun {
    # ---- 1. The Editor: compile, build the scene, play (the comparison's other side) ----------------------------------
    Step 'Editor loop'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $edTimings = [ordered]@{}
    $ed = Invoke-HarnessLoop -Scenario $scenarioAbs -OutDir "$outAbs/editor" -NoPlay:$NoEditor -TimeoutSec $TimeoutSec -Timings $edTimings
    Add-HarnessRecovery $ed
    [void](Save-HarnessReport $ed "$outAbs/editor" $null $edTimings)
    $timings['editorSec'] = Round $sw.Elapsed.TotalSeconds
    $report.editor = [ordered]@{ ok = $ed.ok; stage = $ed.stage; played = -not $NoEditor; report = "$outAbs/editor/report.json" }
    if ($ed.unityVersion) { $report['unityVersion'] = $ed.unityVersion }
    if ($ed.stage -in @('editor', 'compile', 'build', 'shader', 'lint')) {
        # The Player would be built from the same broken code or scene.
        $report.stage = $ed.stage
        $report['error'] = "the Editor loop failed at stage '$($ed.stage)': $($ed.error) (see editor/report.json; compileErrors are copied here)"
        $report['compileErrors'] = @($ed.compileErrors)
        return
    }
    if (-not $NoEditor) {
        $report.editor['fps'] = $ed.fps
        $report.editor['render'] = $ed.render
        $report.editor['events'] = @($ed.play.events)
        $report.editor['mode'] = $ed.editor.mode
    }

    # ---- 2. What to build and how to start it ------------------------------------------------------------------------
    Step 'plan'
    $plan = Invoke-UnityCommand -Name 'harness_player_plan' -Params @{ scenario = $scenarioAbs } -TimeoutSec 30
    if (-not $plan.success -or -not $plan.result.ok) {
        $report.stage = 'playerBuild'
        $report['error'] = if ($plan.success) { $plan.result.error } else { "harness_player_plan failed: $($plan.error)" }
        if ($plan.success -and @($plan.result.urpStale).Count) { $report['urpStale'] = @($plan.result.urpStale) }   # URP assets of a newer Unity
        return
    }
    $p = $plan.result
    $exe = [string]$p.output
    $report.player = [ordered]@{ exe = $exe; target = $p.target; scenes = @($p.scenes); size = @([int]$p.width, [int]$p.height); scriptingBackend = $p.scriptingBackend }

    # ---- 3. Development build (Pipeline "build": queued, blocking the Editor's main thread; build_status answers off it) ----
    Step 'development Player build'
    if ($NoBuild) {
        if (-not (Test-Path -LiteralPath $exe)) { $report.stage = 'playerBuild'; $report['error'] = "-NoBuild: no Player at $exe (run without -NoBuild once)"; return }
        $report.player['build'] = [ordered]@{ skipped = $true }
    } else {
        $repo = $null
        $g = Invoke-HarnessGit $root @('rev-parse', '--show-toplevel')
        if ($g.code -eq 0) { $repo = $g.out.Trim() }
        $before = if ($repo) { Get-HarnessGitStatus $repo } else { $null }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $options = @('Development'); if ($Debugging) { $options += 'AllowDebugging' }
        # One build: the completed build_status result, or an error string.
        $build = {
            param([string[]]$Options)
            $req = @{ target = $p.target; outputPath = $exe; options = $Options; scenes = @($p.scenes); confirm = $true }
            $start = Invoke-UnityCommand -Name 'build' -Params $req -TimeoutSec 60
            if (-not $start.success) { return "build did not start: $($start.error)" }
            if ("$($start.result.status)" -ne 'queued') { return "build did not start: $($start.result | ConvertTo-Json -Compress -Depth 5)" }
            do {
                Start-Sleep -Milliseconds 500
                $s = Invoke-UnityCommand -Name 'build_status' -TimeoutSec 30
                if ($s.success -and "$($s.result.status)" -eq 'completed' -and "$($s.result.buildId)" -eq "$($start.result.buildId)") { return $s.result }
            } while ($sw.Elapsed.TotalSeconds -lt $BuildTimeoutSec)
            "the Player build did not finish in $BuildTimeoutSec s"
        }
        $st = & $build $options
        # Unity's incremental build can keep the player data of an earlier build: after a release build (no harness) the list of
        # script assemblies the Player loads (<Data>/ScriptingAssemblies.json) lacked Harness.Runtime although its DLL was there -
        # the Player then never ran the scenario (Fluid-Sim, 6.0). Checked; built once more without the build cache.
        $clean = $null
        $data = if ($exe -like '*.app') { Join-Path $exe 'Contents/Resources/Data' } else { [IO.Path]::ChangeExtension($exe, $null).TrimEnd('.') + '_Data' }
        $list = Join-Path $data 'ScriptingAssemblies.json'
        if ($st -isnot [string] -and "$($st.result)" -eq 'Succeeded' -and (Test-Path -LiteralPath $list) -and [IO.File]::ReadAllText($list) -notmatch '"Harness\.Runtime\.dll"') {
            $clean = 'the incremental build kept an earlier build''s ScriptingAssemblies.json without Harness.Runtime: built again with CleanBuildCache'
            $st = & $build ($options + 'CleanBuildCache')
        }
        $timings['buildSec'] = Round $sw.Elapsed.TotalSeconds
        if ($st -is [string]) {
            $report.stage = 'playerBuild'
            $report['error'] = $st
            return
        }
        $b = [ordered]@{ result = $st.result; sec = Round ([double]$st.buildTimeMs / 1000) 1; sizeMB = Round ([double]$st.totalSizeBytes / 1MB) 1; warnings = [int]$st.totalWarnings
            code = $(if ($Debugging) { 'debug (Script Debugging: exact lines, slower)' } else { 'release (optimized: runtime error lines can be a few lines off; the Editor loop has the exact line)' }) }
        if ($clean) { $b['cleanRebuild'] = $clean }
        # build_status keeps only the file of a "File.cs(line,col): error CS..." message (Pipeline's BuildIssue); the build
        # steps' messages have the whole text: file, line and module as the Editor loop's compileErrors.
        $texts = @(@($st.buildSteps) | Where-Object { $_ } | ForEach-Object { @($_.messages) } | Where-Object { $_ -and "$($_.type)" -eq 'Error' } | ForEach-Object { "$($_.content)" })
        if (-not $texts.Count) { $texts = @($st.errors | Where-Object { $_ } | ForEach-Object { if ($_.file) { "$($_.file): $($_.message)" } else { "$($_.message)" } }) }
        $errs = @($texts | Select-Object -Unique | ForEach-Object { ConvertFrom-CompilerString $_ })
        $compile = @($errs | Where-Object { $_.line -gt 0 -and $_.msg -match '^CS\d+' } | ForEach-Object { $_['kind'] = 'player'; $_ })
        if ($errs.Count) { $b['errors'] = $errs }
        $report.player['build'] = $b
        # Unity writes project settings while building a Player (URP runtime settings, the Input System's preloaded asset...),
        # whatever the harness does, and some only at exit: saved now, what the build changed in the working tree is reported
        # (put it back or commit it).
        [void](Wait-HarnessIdle -TimeoutSec 60)
        [void](Invoke-UnityCommand -Name 'harness_player_built' -TimeoutSec 60)
        if ($repo) {
            $after = Get-HarnessGitStatus $repo
            $rewrote = @($after.Keys | Where-Object { -not $before.ContainsKey($_) -or $before[$_] -ne $after[$_] } | Sort-Object)
            if ($rewrote.Count) { $report.player['buildRewrote'] = $rewrote }
        }
        if ("$($st.result)" -ne 'Succeeded') {
            $report.stage = 'playerBuild'
            $where = { param($e) if ($e.file) { "$($e.file):$($e.line) $($e.msg)" } else { $e.msg } }
            if ($compile.Count) {
                # The Editor compiled these scripts (its loop ran first): code the Player's target does not have, behind a runtime
                # check instead of #if - Handheld.Vibrate() under Application.isMobilePlatform in a mobile game (company project A).
                $report['compileErrors'] = $compile
                $report['error'] = "the scripts do not compile for the Player's target $($p.target), although the Editor compiled them: an API the target lacks " +
                    "(UnityEditor without #if UNITY_EDITOR, another platform's API such as Handheld without #if UNITY_ANDROID || UNITY_IOS - a runtime check is not enough): " +
                    (@($compile | Select-Object -First 5 | ForEach-Object { & $where $_ }) -join ' | ')
            } else {
                $report['error'] = "the development Player build $($st.result): $(@($errs | Select-Object -First 5 | ForEach-Object { & $where $_ }) -join ' | ')"
            }
            return
        }
    }

    # ---- 4. The Player plays the scenario (windowed at the capture size) and quits -----------------------------------
    Step 'Player'
    $playerOut = "$outAbs/player"
    if (Test-Path -LiteralPath $playerOut) { Get-ChildItem -LiteralPath $playerOut -File | Where-Object { $_.Extension -in @('.png', '.json') } | Remove-Item -Force }
    [void][IO.Directory]::CreateDirectory($playerOut)
    $log = "$outAbs/Player.log"
    $w = [int]$p.width; $h = [int]$p.height
    $a = @('-screen-fullscreen', '0', '-screen-width', "$w", '-screen-height', "$h", '-logFile', $log,
        '-harness-scenario', $scenarioAbs, '-harness-out', $playerOut, '-harness-size', "${w}x$h", '-harness-screen', '-harness-id', ([guid]::NewGuid().ToString('N').Substring(0, 12)))
    if ($p.config) { $a += @('-harness-config', [string]$p.config) }
    if ($Paced) { $a += '-harness-paced' }
    $a += @($PlayerArgs)
    $limit = [math]::Max([double]$TimeoutSec, [double]$p.durationSec + [double]$p.readyTimeoutSec + [double]$p.waitSec + 90)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # Started in the out folder: what the game writes to its working directory stays there (company project A: the Facebook
    # Game SDK's fbg.log, in the project root when the Player started there). A macOS Player is an .app bundle: its executable
    # is started directly (as the Editor is), which keeps the process to wait for and its exit code.
    # Its standard output goes to a file there: a macOS Player writes its start-up (memory setup) to stdout before it takes
    # -logFile, into this script's JSON.
    $run = $exe
    $streams = @{}
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { $streams = @{ RedirectStandardOutput = "$outAbs/Player.stdout.log"; RedirectStandardError = "$outAbs/Player.stderr.log" } }
    if ($exe -like '*.app') {
        $plist = [IO.File]::ReadAllText((Join-Path $exe 'Contents/Info.plist'))
        $m = [regex]::Match($plist, '<key>CFBundleExecutable</key>\s*<string>([^<]+)</string>')
        $run = Join-Path $exe "Contents/MacOS/$(if ($m.Success) { $m.Groups[1].Value } else { [IO.Path]::GetFileNameWithoutExtension($exe) })"
    }
    $proc = Start-Process -FilePath $run -ArgumentList ((@($a) | ForEach-Object { ConvertTo-HarnessArg $_ }) -join ' ') -WorkingDirectory $playerOut -PassThru @streams
    $null = $proc.Handle   # keeps ExitCode readable
    Step "Player started (pid $($proc.Id), $w x $h window)"
    $exited = $proc.WaitForExit([int]($limit * 1000))
    if (-not $exited) { try { $proc.Kill() } catch { }; [void]$proc.WaitForExit(10000) }
    $timings['playerSec'] = Round $sw.Elapsed.TotalSeconds
    Step $(if ($exited) { "Player exited ($($proc.ExitCode))" } else { 'Player killed (timeout)' })
    $report.player['exitCode'] = if ($exited) { $proc.ExitCode } else { $null }
    $report.player['log'] = $log
    if (-not $exited) {
        $report.stage = 'player'
        $report['error'] = "the Player did not finish in $([math]::Round($limit)) s (killed)"
        $report['logTail'] = Tail $log
        return
    }
    $resultPath = "$playerOut/result.json"
    if (-not (Test-Path -LiteralPath $resultPath)) {
        $report.stage = 'player'
        $report['error'] = "the Player exited ($($proc.ExitCode)) without a result.json: it crashed or never started the scenario (logTail, Player.log)"
        $report['logTail'] = Tail $log
        return
    }
    $r = [IO.File]::ReadAllText($resultPath) | ConvertFrom-Json

    # ---- 5. What the Player saw -------------------------------------------------------------------------------------
    Step 'result'
    $pi = $r.player
    foreach ($k in @('platform', 'development', 'graphicsDevice', 'fullScreenMode', 'vSyncCount', 'targetFrameRate')) { $report.player[$k] = $pi.$k }
    $report.player['screen'] = @([int]$pi.screenWidth, [int]$pi.screenHeight)
    $report.player['startupSec'] = Round $pi.startupSec
    if ([double]$pi.splashSec -gt 0) { $report.player['splashSec'] = Round $pi.splashSec }
    if ([int]$pi.screenWidth -ne $w -or [int]$pi.screenHeight -ne $h) {
        $report.player['sizeNote'] = "the window was ${w}x$h requested but $($pi.screenWidth)x$($pi.screenHeight) (the display refused it): the screen shots have that size"
    }
    $f = $r.fps
    $report.fps = [ordered]@{ avg = Round $f.avg 1; min = Round $f.min 1; avgMs = Round $f.avgMs; p95ms = Round $f.p95ms; p99ms = Round $f.p99ms; hitches = $f.hitches
        cpuMainAvgMs = Round $f.cpuMainAvgMs; samples = $f.samples; focused = $r.editorFocused }
    # The Player unpaces itself before the first scene (vSync 0, no frame cap); a game that sets its own afterwards wins.
    if (-not $Paced -and ([int]$pi.targetFrameRate -gt 0 -or [int]$pi.vSyncCount -gt 0)) {
        $report.fps['note'] = "the game paced the Player itself after the harness unpaced it (targetFrameRate $($pi.targetFrameRate), vSyncCount $($pi.vSyncCount)): " +
            'fps is that cap, not what a frame costs - compare frame times only below the cap, or take the pacing out of the game for the measurement'
    }
    $rs = $r.render
    if ($rs -and [int]$rs.samples -gt 0) { $report.render = [ordered]@{ batches = $(if ([double]$rs.batches -lt 0) { $null } else { Round $rs.batches 1 }); setPassCalls = Round $rs.setPassCalls 1; drawCalls = Round $rs.drawCalls 1; triangles = [math]::Round($rs.triangles); vertices = [math]::Round($rs.vertices) } }
    if (-not $NoEditor -and $ed.fps -and [double]$ed.fps.avg -gt 0) { $report['fpsVsEditor'] = Round ([double]$f.avg / [double]$ed.fps.avg) }
    $report['play'] = [ordered]@{ success = $r.success; error = $r.error; probeReady = $r.probeReady; readySec = Round $r.readySec; wallSec = Round $r.wallSec; gameSec = Round $r.gameSec
        frames = $r.frames; modules = @($r.modules); failedModules = @($r.failedModules); inputEventsApplied = $r.inputEventsApplied; inputBackends = @($r.inputBackends)
        events = @($r.events | ForEach-Object { [ordered]@{ name = $_.name; count = $_.count } }); activeScene = $r.activeScene
        uiClock = [ordered]@{ mode = $r.uiClock.mode; panels = $r.uiClock.panels; error = $r.uiClock.error } }
    if (@($r.inputHooks).Count) { $report.play['inputHooks'] = @($r.inputHooks) }
    if (@($r.waits).Count) { $report.play['waits'] = @($r.waits | ForEach-Object { [ordered]@{ type = $_.type; target = $_.target; t = Round $_.t 3; waitedSec = Round $_.waitedSec } }) }
    if (@($r.clicks).Count) { $report.play['clicks'] = @($r.clicks | ForEach-Object { [ordered]@{ target = $_.target; t = Round $_.t 3; x = Round $_.x 1; y = Round $_.y 1; via = $_.via } }) }
    if (-not $NoEditor -and $ed.play) {
        $key = { param($list) (@($list | ForEach-Object { "$($_.name)=$($_.count)" }) | Sort-Object) -join ',' }
        $report['eventsMatch'] = (& $key $ed.play.events) -eq (& $key $r.events)
    }

    # Runtime errors (the runner's own log capture): the project's known errors apart, modules from the file.
    $known = @()
    $cfgFile = Join-Path $work 'ProjectSettings/AgentHarness.json'
    if (Test-Path -LiteralPath $cfgFile) { $j = [IO.File]::ReadAllText($cfgFile) | ConvertFrom-Json; if ($j.knownErrors) { $known = @($j.knownErrors) } }
    $runtime = @(); $knownHits = @()
    foreach ($e in @($r.runtimeErrors)) {
        $row = [ordered]@{ type = $e.type; msg = ($e.message -split "`n")[0]; file = $e.file; line = $e.line; module = $(if ($e.file) { (Get-HarnessModuleOf $e.file).name } else { '' }); count = $e.count; stack = $e.stack }
        if (@($known | Where-Object { $e.message -match $_ }).Count) { $knownHits += $row } else { $runtime += $row }
    }
    $report['runtimeErrors'] = $runtime
    if ($knownHits.Count) { $report['knownErrors'] = $knownHits }

    $shotObjs = @($r.shots)
    $report.shots = @($shotObjs | Where-Object { $_.path } | ForEach-Object { $_.path })
    $byFile = @{}
    $report['shotStats'] = @($shotObjs | ForEach-Object {
        $s = [ordered]@{ name = $_.name; preset = $_.preset; t = Round $_.t 3; width = $_.width; height = $_.height; meanLuma = Round $_.meanLuma 1; stdLuma = Round $_.stdLuma 1
            blank = $_.blank; dark = [bool]$_.dark; magenta = [bool]$_.magenta }
        if (@($_.cameras).Count) { $s['cameras'] = @($_.cameras | Where-Object { $_ }) }
        if ($null -ne $_.ui) { $s['ui'] = @($_.ui | Where-Object { $_ }) }
        if ($_.uiError) { $s['uiError'] = $_.uiError }
        if ([int]$_.frames -gt 1) { $s['frames'] = [int]$_.frames; $s['sheet'] = $_.sheet; $s['motion'] = @($_.motion | ForEach-Object { Round $_ }) }
        if ($_.screen) { $s['screen'] = [ordered]@{ path = $_.screen; width = $_.screenWidth; height = $_.screenHeight } }
        elseif ($_.screenError) { $s['screen'] = [ordered]@{ error = $_.screenError } }
        $s['error'] = $_.error
        $s['hint'] = $_.hint
        if ($_.path) { $byFile[(Split-Path -Leaf $_.path)] = $s }
        $s
    })

    # ---- 6. Against the Editor, and the screen against the shot of the same frame -------------------------------------
    Step 'compare'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $pairs = @()
    # A development build draws "Development Build" in the bottom right corner of the screen: left out of screen comparisons.
    # ([math]::Min(1, ...) would take the int overload: 1.0.)
    $mark = $null
    if ($pi.development) {
        $mw = [math]::Min(1.0, 170.0 / $w); $mh = [math]::Min(1.0, 28.0 / $h)
        $mark = [ordered]@{ x = Round (1.0 - $mw) 4; y = Round (1.0 - $mh) 4; w = Round $mw 4; h = Round $mh 4 }
    }
    $edShots = @{}
    if (-not $NoEditor) { foreach ($e in @($ed.shots)) { if ($e) { $edShots[(Split-Path -Leaf $e)] = $e } } }
    foreach ($o in $shotObjs) {
        if (-not $o.path -or $o.error) { continue }
        $file = Split-Path -Leaf $o.path
        $stem = [IO.Path]::GetFileNameWithoutExtension($file)
        if ($o.preset -ne 'screen' -and $edShots.ContainsKey($file)) {
            $pairs += [ordered]@{ key = "$file|vsEditor"; name = $o.name; path = $o.path; against = $edShots[$file]; diff = "$stem.vsEditor.diff.png" }
        }
        if ($o.screen) {
            $pairs += [ordered]@{ key = "$file|screen.vsShot"; name = $o.name; path = $o.screen; against = $o.path; diff = "$stem.screen.vsShot.diff.png"; ignore = $mark }
            if ($edShots.ContainsKey($file)) { $pairs += [ordered]@{ key = "$file|screen.vsEditor"; name = $o.name; path = $o.screen; against = $edShots[$file]; diff = "$stem.screen.vsEditor.diff.png"; ignore = $mark } }
        }
    }
    # Two different renders (another camera, another process) dither differently: mean difference ~0.5 with no pixel changed,
    # the golden rule's limit. Same pixel rule, mean up to 1.
    $summary = [ordered]@{ sameMean = 1.0; vsEditor = [ordered]@{ same = 0; changed = 0; maxChangedRatio = 0.0; maxMeanDiff = 0.0 }
        screen = [ordered]@{ same = 0; changed = 0; maxChangedRatio = 0.0; maxMeanDiff = 0.0 } }
    if ($pairs.Count) {
        $items = @($pairs | ForEach-Object { $i = [ordered]@{ name = $_.name; path = $_.path; against = $_.against; diff = $_.diff }; if ($_.ignore) { $i['ignore'] = @($_.ignore) }; $i })
        $cmp = Invoke-UnityCommand -Name 'harness_compare' -Params @{ pairs = (ConvertTo-Json -InputObject $items -Depth 6 -Compress); out = "$outAbs/compare"; same_mean = 1.0 } -TimeoutSec 120
        if ($cmp.success -and $cmp.result.ok) {
            $res = @($cmp.result.results)
            for ($i = 0; $i -lt $pairs.Count; $i++) {
                $x = $res[$i]
                $file, $what = $pairs[$i].key.Split('|')
                $d = [ordered]@{ status = $x.status }
                if ($x.status -in @('same', 'changed')) { $d['meanDiff'] = Round $x.meanDiff 3; $d['changedRatio'] = Round $x.changedRatio 5; $d['ssim'] = Round $x.ssim 4; $d['maxDiff'] = [int]$x.maxDiff }
                if ($x.rect) { $d['rect'] = @($x.rect) }
                if ($x.diff) { $d['diff'] = $x.diff }
                if ($x.error) { $d['error'] = $x.error }
                $s = $byFile[$file]
                if ($what -eq 'vsEditor') { $s['vsEditor'] = $d; $bucket = $summary.vsEditor }
                else { $s.screen[$what.Substring(7)] = $d; $bucket = $summary.screen }
                if ($x.status -eq 'same') { $bucket.same++ } else { $bucket.changed++ }
                $bucket.maxChangedRatio = [math]::Max([double]$bucket.maxChangedRatio, [double](Round $x.changedRatio 5))
                $bucket.maxMeanDiff = [math]::Max([double]$bucket.maxMeanDiff, [double](Round $x.meanDiff 3))
            }
        } else { $summary['error'] = if ($cmp.success) { $cmp.result.error } else { $cmp.error } }
    }
    if ($mark) { $summary['screenIgnore'] = $mark }
    # Why a shot differs from the Editor's: the Player's own screen in that frame tells the game from the capture (G3-8).
    $game = @(); $capturePath = @(); $unknown = @()
    foreach ($s in @($report.shotStats)) {
        if (-not $s['vsEditor'] -or $s.vsEditor.status -ne 'changed') { continue }
        $label = "$($s.name) (t=$($s.t))"   # several shots can share a name ("main")
        if (-not $s['screen'] -or -not $s.screen['vsShot']) { $unknown += $label; continue }
        if ($s.screen.vsShot.status -eq 'same') { $s.vsEditor['cause'] = 'game'; $game += $label } else { $s.vsEditor['cause'] = 'capture'; $capturePath += $label }
    }
    $notes = @()
    if ($game.Count) {
        $notes += "$($game -join ', '): the Player's own screen is this capture (screen.vsShot same), so the difference is the game's in the Editor and " +
            'in the Player, not the capture: code under #if UNITY_EDITOR or Application.isEditor (Editor-only UI), platform #if, data only the Editor makes ' +
            '(OnValidate), UI laid out from Screen.width/height (the Editor''s Screen is the Game view, the Player''s the capture size), animations on real ' +
            'time (unscaled time, DOTween SetUpdate(true): a Player frame takes another wall time than an Editor frame), the first scene''s particles one ' +
            'step ahead (a Player simulates them once while it loads the scene) - see the vsEditor diff images'
    }
    if ($capturePath.Count) {
        $notes += "$($capturePath -join ', '): the capture is not the Player's own screen of that frame (screen.vsShot changed) - the harness's capture path " +
            '(canvases of stacked overlay cameras composited over the cameras, a perspective UI camera, render-to-texture UI, effects that need the previous ' +
            'frame such as TAA, text a script moves vertex by vertex): the .screen.png is what the game showed'
    }
    if ($unknown.Count) {
        $notes += "$($unknown -join ', '): no screen of that frame (a pose or camera shot) to tell the game from the capture. A Player simulates the " +
            'particle systems of its first scene one step (Time.fixedDeltaTime) further than play mode does (Unity steps them while it loads the scene): ' +
            'small changes around particles are expected. Anything else in the diff images is not.'
    }
    if ($notes.Count) { $summary['note'] = $notes -join ' | ' }
    $report['compare'] = $summary
    $timings['compareSec'] = Round $sw.Elapsed.TotalSeconds

    $bad = @($shotObjs | Where-Object { $_.blank -or $_.error }).Count
    $report.ok = [bool]$r.success -and $runtime.Count -eq 0 -and $bad -eq 0 -and $shotObjs.Count -gt 0 -and $proc.ExitCode -eq 0
    $report.stage = if ($report.ok) { 'done' } elseif (-not $r.success) { 'play' } elseif ($runtime.Count) { 'runtime' } elseif ($bad -or $shotObjs.Count -eq 0) { 'shots' } else { 'player' }
    if (-not $r.success) { $report['error'] = $r.error }
    elseif ($proc.ExitCode -ne 0 -and $report.stage -eq 'player') { $report['error'] = "the Player exited with $($proc.ExitCode)"; $report['logTail'] = Tail $log }
}

$timings['lockWaitSec'] = Enter-HarnessLock
try { $null = Invoke-PlayerRun }
finally { Exit-HarnessLock }
Save-HarnessReport $report $outAbs $clock $timings
exit $(if ($report.ok) { 0 } else { 1 })
