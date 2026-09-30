<#
.SYNOPSIS
  Verify attaching the harness to an existing project end to end (P-2): install -> open -> harness_setup -> loop x N
  -> release build -> quit -> uninstall, on a git checkout of that project whose working tree is clean. Green when:
  - install.ps1 and every step succeed, harness_setup changes nothing (an attached project);
  - after install and the loops, git status lists only what install.ps1 reported (created/modified) plus
    Packages/packages-lock.json (Unity resolves the new dependency);
  - every loop is green on the project's own scene, with the same build fingerprint and play events;
  - with -Player, tools/player.ps1 (W8) plays the loops' scenario in a development build of the project: green;
  - the release (non-development) Player build has no Harness.* assembly (and reports what else of the harness's
    dependencies it holds; the build's own rewrites of project settings are reported as buildRewrote and put back);
  - after uninstall.ps1, git status is empty again.
  Every step runs the project's own tools/*.ps1 in a child PowerShell. The report goes to HarnessOut/attach-test/<name>/
  of this checkout (report.json, install.json, loop<N>.json, the last loop's shots/, uninstall.json).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/attach-test.ps1 -Project ..\..\ah-p2\bagel -Module Game=Assets/Game,UI=Assets/UI
  powershell -ExecutionPolicy Bypass -File tools/attach-test.ps1 -Project ..\..\ah-p2\fluid -Scene "Assets/Scenes/Fluid Particles.unity"
  powershell -ExecutionPolicy Bypass -File tools/attach-test.ps1 -Project <path> -Source git -Ref master     # the published package
#>
param(
    [Parameter(Mandatory)][string]$Project,
    [string]$Source = 'local',      # install.ps1 -Source: local (this package folder), git, embed, or a UPM dependency string
    [string]$Ref = 'master',
    [string]$Scene,
    [string[]]$Module = @(),
    [string]$UnityVersion,          # open.ps1 -UnityVersion (default: the project's ProjectVersion.txt)
    [int]$Loops = 3,
    [string]$Scenario,              # loop.ps1 -Scenario (a path; relative = this checkout): e.g. one that waits for the game's boot
    [string[]]$KnownErrors = @(),   # install.ps1 -KnownErrors (errors the project logs anyway, e.g. an SDK library it does not commit)
    [switch]$InputShim,             # install.ps1 -InputShim
    [switch]$NoBuild,
    [switch]$Player,                # also tools/player.ps1 (development Player build + run, W8) after the loops
    [string]$BuildDir,              # default: <project>-harness-build next to the project
    [string]$Out = 'HarnessOut/attach-test'
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
$clock = [Diagnostics.Stopwatch]::StartNew()
$proj = [IO.Path]::GetFullPath($Project).TrimEnd('\', '/')
$name = Split-Path -Leaf $proj
$outAbs = if ([IO.Path]::IsPathRooted($Out)) { Join-Path $Out $name } else { Join-Path (Join-Path (Get-HarnessWorkRoot) $Out) $name }
if (Test-Path -LiteralPath $outAbs) { Remove-Item -LiteralPath $outAbs -Recurse -Force }
[void](New-Item -ItemType Directory -Force $outAbs)
if (-not $BuildDir) { $BuildDir = "$proj-harness-build" }
$ps = Get-HarnessPowerShell
$childEnv = @{ AGENTHARNESS_EDITOR_ROOT = $null; AGENTHARNESS_WORK_ROOT = $null }
$report = [ordered]@{ ok = $false; stage = 'prepare'; project = $proj.Replace('\', '/'); source = $Source; head = $null; unityVersion = $null
    steps = [ordered]@{}; installed = $null; loops = @(); build = $null; unexpected = @(); buildRewrote = @(); finalStatus = $null; durationSec = 0 }

function Save-Report {
    $report.durationSec = [math]::Round($clock.Elapsed.TotalSeconds, 2)
    Initialize-HarnessOut $outAbs
    $json = $report | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText((Join-Path $outAbs 'report.json'), $json, (New-Object Text.UTF8Encoding($false)))
    $json
}
function Fail([string]$Message) {
    $report['error'] = $Message
    # Leave nothing running: close the project's Editor if the harness is still installed.
    if (Test-Path -LiteralPath (Join-Path $proj 'tools/quit.ps1')) { [void](Invoke-Tool 'quit.ps1' @('-Force') 120) }
    Save-Report
    exit 1
}
function Step([string]$Name, [scriptblock]$Body) {
    $report.stage = $Name
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $r = & $Body
    $report.steps[$Name] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    $r
}
# A tools/*.ps1 of the project, or a script by absolute path; parses stdout as JSON when it is JSON.
function Invoke-Tool([string]$Tool, [string[]]$Arguments = @(), [int]$TimeoutSec = 900) {
    $file = if ([IO.Path]::IsPathRooted($Tool)) { $Tool } else { Join-Path $proj "tools/$Tool" }
    $r = Invoke-HarnessProcess $ps (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $file) + $Arguments) -Environment $childEnv -TimeoutSec $TimeoutSec
    $json = $null
    try { $json = $r.out | ConvertFrom-Json } catch { }
    [pscustomobject]@{ code = $r.code; json = $json; out = $r.out; err = $r.err.Trim(); timedOut = $r.timedOut }
}
function Get-Status { @((Invoke-HarnessGit $proj @('status', '--porcelain=v1', '-z', '--untracked-files=all', '--no-renames') -Check).out.Split([char]0) | Where-Object { $_ } | ForEach-Object { $_.Substring(3) }) }

# ---- prepare: a clean git checkout -------------------------------------------------------------------------------
$g = Invoke-HarnessGit $proj @('rev-parse', 'HEAD')
if ($g.code -ne 0) { Fail "$proj is not a git checkout" }
$report.head = $g.out.Trim()
$before = @(Get-Status)
if ($before.Count -gt 0) { Fail "the working tree of $proj is not clean (commit the Unity upgrade churn first): $($before -join ', ')" }

# ---- install -------------------------------------------------------------------------------------------------------
$installArgs = @('-Project', $proj, '-Source', $Source, '-Ref', $Ref)
if ($Scene) { $installArgs += @('-Scene', $Scene) }
if ($Module.Count) { $installArgs += @('-Module', ($Module -join ',')) }
if ($KnownErrors.Count) { $installArgs += @('-KnownErrors'); $installArgs += $KnownErrors }
if ($InputShim) { $installArgs += @('-InputShim') }
$loopArgs = @()
if ($Scenario) {
    $sc = if ([IO.Path]::IsPathRooted($Scenario)) { $Scenario } else { Join-Path (Get-HarnessWorkRoot) $Scenario }
    if (-not (Test-Path -LiteralPath $sc)) { Fail "-Scenario ${Scenario}: no such file" }
    $loopArgs = @('-Scenario', ([IO.Path]::GetFullPath($sc)).Replace('\', '/'))
    $report['scenario'] = $loopArgs[1]
}
$inst = Step 'install' { Invoke-Tool (Join-Path $PSScriptRoot 'install.ps1') $installArgs 120 }
if ($inst.json) { [IO.File]::WriteAllText((Join-Path $outAbs 'install.json'), $inst.out, (New-Object Text.UTF8Encoding($false))) }
if (-not $inst.json -or -not $inst.json.ok) { Fail "install.ps1 failed: $(if ($inst.json) { $inst.json.error } else { $inst.out + $inst.err })" }
$allowed = @{}
foreach ($p in @($inst.json.created) + @($inst.json.modified) + @('Packages/packages-lock.json')) { $allowed[$p] = $true }
function Get-Unexpected {
    # Untracked folders are listed file by file; a created folder (tools/, an embedded package) covers what is below it.
    @(Get-Status | Where-Object { $p = $_; -not $allowed.ContainsKey($p) -and -not @($allowed.Keys | Where-Object { $p.StartsWith("$_/") }).Count })
}

# ---- open, setup, loops --------------------------------------------------------------------------------------------
$openArgs = @(); if ($UnityVersion) { $openArgs += @('-UnityVersion', $UnityVersion) }
$open = Step 'open' { Invoke-Tool 'open.ps1' $openArgs 1800 }
if (-not $open.json -or -not $open.json.ok) { Fail "open.ps1 failed: $(if ($open.json) { ($open.json | ConvertTo-Json -Depth 5 -Compress) } else { $open.out + $open.err })" }
$report.unityVersion = $open.json.version
if ($open.json.PSObject.Properties.Name -contains 'deprecatedPackages') { $report['deprecatedPackages'] = @($open.json.deprecatedPackages) }
$setup = Step 'setup' { Invoke-Tool 'uc.ps1' @('harness_setup') 300 }
if (-not $setup.json -or -not $setup.json.success -or -not $setup.json.result.ok) { Fail "harness_setup failed: $($setup.out)" }
$report['setup'] = [ordered]@{ changed = @($setup.json.result.changed); issues = @($setup.json.result.issues); recommendations = @($setup.json.result.recommendations | ForEach-Object { $_.setting }) }
if (@($setup.json.result.changed | Where-Object { $_ -notlike 'CompilationPipeline.codeOptimization*' }).Count -gt 0) { Fail "harness_setup changed project settings in an attached project: $($setup.json.result.changed -join ', ')" }

$lastScene = $null
for ($i = 1; $i -le $Loops; $i++) {
    $loop = Step "loop$i" { Invoke-Tool 'loop.ps1' $loopArgs 900 }
    $j = $loop.json
    if ($j) { [IO.File]::WriteAllText((Join-Path $outAbs "loop$i.json"), $loop.out, (New-Object Text.UTF8Encoding($false))) }
    $row = [ordered]@{ ok = [bool]($j -and $j.ok); stage = $(if ($j) { $j.stage } else { 'none' }); sec = $(if ($j) { $j.durationSec } else { 0 }) }
    if ($j) {
        $row['fingerprint'] = $j.build.fingerprint
        $row['events'] = (@($j.play.events | ForEach-Object { "$($_.name)=$($_.count)" }) -join ',')
        $row['shots'] = @($j.shotStats | ForEach-Object { "$($_.name):$([math]::Round($_.meanLuma, 1))$(if ($_.blank) { ' BLANK' })$(if ($_.dark) { ' DARK' })" })
        $row['fps'] = $j.fps.avg
        $row['playEnterSec'] = $j.timings.playEnterSec
        if ($j.PSObject.Properties.Name -contains 'knownErrors') { $row['knownErrors'] = @($j.knownErrors).Count }
        if ($j.PSObject.Properties.Name -contains 'teardownErrors') { $row['teardownErrors'] = @($j.teardownErrors).Count }
        if ($j.play -and $j.play.PSObject.Properties.Name -contains 'waits') { $row['waits'] = @($j.play.waits | ForEach-Object { "$($_.target):$($_.waitedSec)s" }) }
        if ($j.PSObject.Properties.Name -contains 'scene') { $lastScene = $j.scene }
    }
    $report.loops += , $row
    if (-not $row.ok) { Fail "loop $i is red (stage=$($row.stage)): see loop$i.json" }
    if ($i -eq $Loops -and $j.shots) {
        $shots = Join-Path $outAbs 'shots'; [void](New-Item -ItemType Directory -Force $shots)
        foreach ($s in @($j.shots)) { if (Test-Path -LiteralPath $s) { Copy-Item -LiteralPath $s -Destination $shots } }
    }
}
$fps = @($report.loops | ForEach-Object { $_.fingerprint } | Sort-Object -Unique)
$evs = @($report.loops | ForEach-Object { $_.events } | Sort-Object -Unique)
if ($fps.Count -ne 1) { $report.stage = 'determinism'; Fail "build fingerprint differs between loops: $($fps -join ', ')" }
if ($evs.Count -ne 1) { $report.stage = 'determinism'; Fail "play events differ between loops: $($evs -join ' | ')" }
$report.installed = @(Get-Status)
$report.unexpected = @(Get-Unexpected)
if ($report.unexpected.Count -gt 0) { $report.stage = 'status'; Fail "files changed that install.ps1 did not report: $($report.unexpected -join ', ')" }

# ---- Player run (W8): the loops' scenario in a development build of the project -----------------------------------
if ($Player) {
    $pl = Step 'player' { Invoke-Tool 'player.ps1' $loopArgs 3600 }
    $j = $pl.json
    if ($j) { [IO.File]::WriteAllText((Join-Path $outAbs 'player.json'), $pl.out, (New-Object Text.UTF8Encoding($false))) }
    $report['player'] = [ordered]@{ ok = [bool]($j -and $j.ok); stage = $(if ($j) { $j.stage } else { 'none' }) }
    if ($j) {
        $report.player['fps'] = $j.fps.avg; $report.player['p95ms'] = $j.fps.p95ms; $report.player['editorFps'] = $j.editor.fps.avg; $report.player['fpsVsEditor'] = $j.fpsVsEditor
        $report.player['buildSec'] = $j.player.build.sec; $report.player['startupSec'] = $j.player.startupSec; $report.player['screen'] = (@($j.player.screen) -join 'x')
        $report.player['graphicsDevice'] = $j.player.graphicsDevice; $report.player['eventsMatch'] = $j.eventsMatch; $report.player['compare'] = $j.compare
        if ($j.player.buildRewrote) { $report.player['buildRewrote'] = @($j.player.buildRewrote) }
        $pe = @($j.runtimeErrors | Where-Object { $_ })
        if ($pe.Count) { $report.player['runtimeErrors'] = @($pe | ForEach-Object { "$($_.msg) @ $($_.file):$($_.line)" }) }
        if ($j.error) { $report.player['error'] = "$($j.error)" }
        $pshots = Join-Path $outAbs 'shots-player'; [void](New-Item -ItemType Directory -Force $pshots)
        foreach ($s in @($j.shots) + @($j.shotStats | ForEach-Object { if ($_.screen) { $_.screen.path } })) { if ($s -and (Test-Path -LiteralPath $s)) { Copy-Item -LiteralPath $s -Destination $pshots } }
    }
    if (-not $report.player.ok) { Fail "player.ps1 is red (stage=$($report.player.stage)): see player.json $(if ($j) { $j.error })" }
}

# ---- release build -------------------------------------------------------------------------------------------------
if (-not $NoBuild) {
    $exe = Join-Path $BuildDir "$name.exe"
    if (Test-Path -LiteralPath $BuildDir) { Remove-Item -LiteralPath $BuildDir -Recurse -Force }
    $req = [ordered]@{ target = 'StandaloneWindows64'; outputPath = $exe.Replace('\', '/'); confirm = $true }
    if ($lastScene) { $req['scenes'] = @($lastScene) }
    $b = Step 'build' {
        $start = Invoke-Tool 'uc.ps1' @('build', ($req | ConvertTo-Json -Compress)) 120
        if (-not $start.json -or -not $start.json.success) { return [pscustomobject]@{ error = "build did not start: $($start.out)" } }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        do { Start-Sleep 5; $st = Invoke-Tool 'uc.ps1' @('build_status') 60 } while ((-not $st.json -or $st.json.result.status -ne 'completed') -and $sw.Elapsed.TotalSeconds -lt 1800)
        $st.json.result
    }
    if ($b.PSObject.Properties.Name -contains 'error' -and $b.error -is [string]) { Fail $b.error }
    $managed = @(Get-ChildItem -LiteralPath $BuildDir -Recurse -Filter *.dll -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '_Data[\\/]Managed[\\/]' } | ForEach-Object { $_.Name } | Sort-Object)
    $report.build = [ordered]@{ result = $b.result; sec = [math]::Round($b.buildTimeMs / 1000, 1); outputPath = $exe.Replace('\', '/'); managed = $managed.Count
        harness = @($managed | Where-Object { $_ -like 'Harness.*' }); pipeline = @($managed | Where-Object { $_ -like 'Unity.Pipeline*' -or $_ -like 'UnityPipeline.*' }) }
    if ($b.result -ne 'Succeeded') { $report.stage = 'build'; Fail "the release build did not succeed: $($b.result) $(@($b.errors | ForEach-Object { $_.message }) -join ' | ')" }
    if ($report.build.harness.Count -gt 0) { $report.stage = 'build'; Fail "the release build holds harness assemblies: $($report.build.harness -join ', ')" }
}

# ---- quit, uninstall ---------------------------------------------------------------------------------------------------
$q = Step 'quit' { Invoke-Tool 'quit.ps1' @() 180 }
if (-not $q.json -or -not $q.json.ok) { Fail "quit.ps1 failed: $($q.out)" }
# Unity rewrites project settings while building a Player, with or without the harness (URP assets, ProjectSettings,
# the Input System's preloaded assets), and saves them again when it exits. The tree was checked clean of anything but
# the install just before the build, so what else changed now is the build's: report it and put it back (Editor closed).
$rewrote = @(Get-Unexpected)
if ($rewrote.Count -gt 0 -and $NoBuild -and -not $Player) { $report.unexpected = $rewrote; $report.stage = 'status'; Fail "files changed that install.ps1 did not report: $($rewrote -join ', ')" }
$report.buildRewrote = $rewrote
foreach ($p in $rewrote) {
    if ((Invoke-HarnessGit $proj @('ls-files', '--error-unmatch', '--', $p)).code -eq 0) { [void](Invoke-HarnessGit $proj @('checkout', '--', $p) -Check) }
    else { Remove-Item -LiteralPath (Join-Path $proj $p) -Force }
}
$u = Step 'uninstall' { Invoke-Tool 'uninstall.ps1' @() 120 }
if ($u.json) { [IO.File]::WriteAllText((Join-Path $outAbs 'uninstall.json'), $u.out, (New-Object Text.UTF8Encoding($false))) }
if (-not $u.json -or -not $u.json.ok) { Fail "uninstall.ps1 failed: $($u.out) $($u.err)" }
$report.finalStatus = @(Get-Status)
if ($report.finalStatus.Count -gt 0) { $report.stage = 'final'; Fail "git status is not clean after uninstall: $($report.finalStatus -join ', ')" }
$report.stage = 'done'
$report.ok = $true
Save-Report
exit 0
