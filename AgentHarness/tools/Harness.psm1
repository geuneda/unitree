# Harness.psm1 - thin client for the Unity Pipeline HTTP server of THIS project's Editor.
# `unity command` spawns a CLI process per call (~0.8s) and PowerShell 5.1 mangles quoted args,
# so the loop talks to /api/exec directly (port + bearer token from the descriptor file).

Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Net.Http

# Two roots. WorkRoot = the checkout these tools live in (where an agent edits sources). ProjectRoot = the Editor
# project they drive (Library/, the Pipeline descriptor, the lock). They are the same folder unless this is an agent
# worktree (G5-2): a checkout without Library/ (e.g. `git worktree add`) drives the Editor of the main worktree at the
# same sub-path. AGENTHARNESS_EDITOR_ROOT overrides the detection (e.g. for a plain copy of the project).
function Resolve-HarnessEditorRoot([string]$WorkRoot) {
    if ($env:AGENTHARNESS_EDITOR_ROOT) { return [IO.Path]::GetFullPath($env:AGENTHARNESS_EDITOR_ROOT).TrimEnd('\', '/') }
    if (Test-Path -LiteralPath (Join-Path $WorkRoot 'Library')) { return $WorkRoot }
    try {
        $main = @(& git -C $WorkRoot worktree list --porcelain 2>$null) | Select-Object -First 1
        $prefix = & git -C $WorkRoot rev-parse --show-prefix 2>$null
        if ($main -like 'worktree *') {
            $candidate = [IO.Path]::GetFullPath([IO.Path]::Combine($main.Substring(9), "$prefix")).TrimEnd('\', '/')
            if (Test-Path -LiteralPath (Join-Path $candidate 'Library')) { return $candidate }
        }
    } catch { }
    $WorkRoot
}

$script:WorkRoot = Split-Path -Parent $PSScriptRoot
$script:ProjectRoot = Resolve-HarnessEditorRoot $script:WorkRoot
$script:Descriptor = Join-Path $script:ProjectRoot 'Library\Pipeline\.unity-pipeline-port'
$script:Client = $null

function Get-HarnessProjectRoot { $script:ProjectRoot }
function Get-HarnessWorkRoot { $script:WorkRoot }
function Test-HarnessWorktree { $script:WorkRoot -ne $script:ProjectRoot }

function Get-HarnessEndpoint {
    if (-not (Test-Path $script:Descriptor)) { return $null }
    try {
        $d = [System.IO.File]::ReadAllText($script:Descriptor) | ConvertFrom-Json
        return [pscustomobject]@{ Port = [int]$d.port; Token = [string]$d.evalToken; Pid = [int]$d.pid }
    } catch { return $null }
}

function Get-HttpClient {
    if ($null -eq $script:Client) {
        $h = New-Object System.Net.Http.HttpClientHandler
        $h.UseProxy = $false
        $script:Client = New-Object System.Net.Http.HttpClient($h)
        $script:Client.Timeout = [TimeSpan]::FromSeconds(600)
    }
    $script:Client
}

# Returns the parsed envelope ({success, result, error, ...}).
# On transport failure (Editor reloading / not running) returns
# @{ success=$false; unreachable=$true; error=<msg> } instead of throwing, so pollers can retry.
function Invoke-UnityCommand {
    param(
        [Parameter(Mandatory)][string]$Name,
        [hashtable]$Params = @{},
        [int]$TimeoutSec = 60
    )
    $ep = Get-HarnessEndpoint
    if ($null -eq $ep) {
        return [pscustomobject]@{ success = $false; unreachable = $true; error = "descriptor not found: $script:Descriptor (is the Editor open?)" }
    }
    $body = @{ command = $Name; parameters = $Params } | ConvertTo-Json -Depth 30 -Compress
    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "http://127.0.0.1:$($ep.Port)/api/exec")
    $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $ep.Token)
    $req.Content = New-Object System.Net.Http.StringContent($body, [System.Text.Encoding]::UTF8, 'application/json')
    $cts = New-Object System.Threading.CancellationTokenSource([TimeSpan]::FromSeconds($TimeoutSec))
    try {
        $resp = (Get-HttpClient).SendAsync($req, $cts.Token).GetAwaiter().GetResult()
        # The Pipeline server restarts with a new token around a domain reload; a request that read the old
        # descriptor gets 401 for a moment. Transient, like a refused connection: pollers retry.
        if ([int]$resp.StatusCode -eq 401) {
            return [pscustomobject]@{ success = $false; unreachable = $true; error = 'Unauthorized (Editor token rotated by a domain reload)' }
        }
        $bytes = $resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        try { $obj = $text | ConvertFrom-Json } catch {
            return [pscustomobject]@{ success = $false; unreachable = $false; error = "non-JSON reply (HTTP $([int]$resp.StatusCode)): $text" }
        }
        if ($null -eq $obj -or -not ($obj.PSObject.Properties.Name -contains 'success')) {
            return [pscustomobject]@{ success = $false; unreachable = $false; error = "unexpected reply (HTTP $([int]$resp.StatusCode)): $text" }
        }
        # Same shape as the transport-failure replies, so strict-mode callers can read these fields.
        foreach ($p in @('unreachable', 'error')) { if (-not ($obj.PSObject.Properties.Name -contains $p)) { $obj | Add-Member -NotePropertyName $p -NotePropertyValue $null } }
        if ([int]$resp.StatusCode -eq 503) { $obj | Add-Member -NotePropertyName busy -NotePropertyValue $true -Force }
        return $obj
    } catch {
        $msg = $_.Exception.GetBaseException().Message
        return [pscustomobject]@{ success = $false; unreachable = $true; error = $msg }
    } finally { $cts.Dispose(); $req.Dispose() }
}

# Poll until the Editor answers a cheap background command (e.g. after a domain reload).
function Wait-UnityReachable {
    param([int]$TimeoutSec = 120)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        $r = Invoke-UnityCommand -Name 'recompile_status' -TimeoutSec 5
        if ($r.success) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

# ---- Editor lock -------------------------------------------------------------------------------------
# One Editor, many agents: every Editor-mutating operation (recompile/build/play/capture/submit...) runs under a
# machine-wide mutex per project, so parallel agents queue instead of interleaving.
$script:Mutex = $null
$script:LastRecovery = $null
$script:ReadOnlyCommands = @('harness_ping', 'harness_console', 'harness_play_status', 'harness_stats', 'harness_lint', 'harness_shaders',
    'recompile_status', 'editor_status', 'console', 'console_status', 'get_scene_hierarchy', 'find_gameobjects',
    'list_open_scenes', 'get_component_properties', 'package_list', 'test_status', 'build_status')

function Test-HarnessReadOnly([string]$Command) { $script:ReadOnlyCommands -contains $Command }

# Returns the seconds spent waiting. Whoever takes the lock first also rolls back a submit that died while holding
# it (see Restore-HarnessPendingSubmit); what was restored is available from Get-HarnessLastRecovery.
function Enter-HarnessLock {
    param([int]$TimeoutSec = 900)
    if ($null -ne $script:Mutex) { return 0 }
    $id = [BitConverter]::ToString([Security.Cryptography.SHA1]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($script:ProjectRoot.ToLowerInvariant()))).Replace('-', '').Substring(0, 16)
    $m = New-Object System.Threading.Mutex($false, "Global\AgentHarnessEditor_$id")
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try { $got = $m.WaitOne([TimeSpan]::FromSeconds($TimeoutSec)) }
    catch [System.Threading.AbandonedMutexException] { $got = $true }  # previous holder died: we own it now
    if (-not $got) { $m.Dispose(); throw "Timed out after $TimeoutSec s waiting for the Editor lock (another agent is running the loop)" }
    $script:Mutex = $m
    $wait = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    $script:LastRecovery = Restore-HarnessPendingSubmit
    return $wait
}

function Exit-HarnessLock {
    if ($null -eq $script:Mutex) { return }
    try { $script:Mutex.ReleaseMutex() } catch { }
    $script:Mutex.Dispose()
    $script:Mutex = $null
}

function Get-HarnessLastRecovery { $script:LastRecovery }

# ---- Submit transactions (tools/submit.ps1, G5-2) ------------------------------------------------------
# submit.ps1 copies an agent worktree's module folders into the Editor tree while holding the lock. Before the first
# write it backs up every file it will overwrite or delete and writes a journal (Library/Harness/submit/pending.json).
# The journal is removed once the submit has kept or reverted its files. A journal seen by the next lock holder means
# that submit died half-way; it is rolled back, so a crashed submit cannot leave its code in the Editor tree.
$script:SubmitDir = Join-Path $script:ProjectRoot 'Library\Harness\submit'
$script:JournalPath = Join-Path $script:SubmitDir 'pending.json'

# Writes: @{ rel = 'Assets/Game/Foo/X.cs'; src = <absolute source file> }. Deletes: project-relative files.
# CreatedDirs: project-relative folders that do not exist yet (removed with their Unity .meta on revert).
function Start-HarnessSubmit {
    param([string]$RunId, [string]$WorkRoot, [string[]]$Modules, [object[]]$Writes = @(), [string[]]$Deletes = @(), [string[]]$CreatedDirs = @())
    $backup = Join-Path $script:SubmitDir $RunId
    $entries = @()
    foreach ($rel in @(@($Writes | ForEach-Object { $_.rel }) + @($Deletes))) {
        $abs = [IO.Path]::Combine($script:ProjectRoot, $rel)
        $existed = [IO.File]::Exists($abs)
        if ($existed) {
            $b = [IO.Path]::Combine($backup, $rel)
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($b))
            [IO.File]::Copy($abs, $b, $true)
        }
        $entries += [ordered]@{ path = $rel; existed = $existed }
    }
    $journal = [ordered]@{ runId = $RunId; workRoot = $WorkRoot; modules = @($Modules); pid = $PID; startedAt = (Get-Date).ToString('o')
        backup = $backup; entries = $entries; createdDirs = @($CreatedDirs) }
    [void][IO.Directory]::CreateDirectory($script:SubmitDir)
    $tmp = "$script:JournalPath.tmp"
    [IO.File]::WriteAllText($tmp, ($journal | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmp -Destination $script:JournalPath -Force
    $journal
}

# The submit is final (kept): drop the journal and the backup.
function Complete-HarnessSubmit {
    param([Parameter(Mandatory)]$Journal)
    if (Test-Path -LiteralPath $script:JournalPath) { Remove-Item -LiteralPath $script:JournalPath -Force }
    if (Test-Path -LiteralPath $Journal.backup) { Remove-Item -LiteralPath $Journal.backup -Recurse -Force }
}

# Put every journaled file back as it was before the submit. Files the submit created are deleted together with the
# .meta Unity generated for them; restored files get a fresh timestamp so the next refresh re-imports them.
function Undo-HarnessSubmit {
    param([Parameter(Mandatory)]$Journal)
    $root = $script:ProjectRoot
    $restored = @{}
    foreach ($e in @($Journal.entries)) { if ($e.existed) { $restored[$e.path] = $true } }
    foreach ($e in @($Journal.entries)) {
        if ($e.existed) { continue }
        foreach ($p in @($e.path, "$($e.path).meta")) {
            if ($restored.ContainsKey($p)) { continue }
            $abs = [IO.Path]::Combine($root, $p)
            if ([IO.File]::Exists($abs)) { [IO.File]::Delete($abs) }
        }
    }
    foreach ($e in @($Journal.entries)) {
        if (-not $e.existed) { continue }
        $abs = [IO.Path]::Combine($root, $e.path)
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($abs))
        [IO.File]::Copy([IO.Path]::Combine($Journal.backup, $e.path), $abs, $true)
        [IO.File]::SetLastWriteTimeUtc($abs, [DateTime]::UtcNow)
    }
    foreach ($d in @(@($Journal.createdDirs) | Where-Object { $_ } | Sort-Object Length -Descending)) {
        $abs = [IO.Path]::Combine($root, $d)
        if ([IO.Directory]::Exists($abs)) { [IO.Directory]::Delete($abs, $true) }
        if ([IO.File]::Exists("$abs.meta") -and -not $restored.ContainsKey("$d.meta")) { [IO.File]::Delete("$abs.meta") }
    }
    Complete-HarnessSubmit $Journal
}

# Roll back a submit that died while holding the lock. Call only while holding the lock (Enter-HarnessLock does).
function Restore-HarnessPendingSubmit {
    if (-not (Test-Path -LiteralPath $script:JournalPath)) { return $null }
    try {
        $j = [IO.File]::ReadAllText($script:JournalPath) | ConvertFrom-Json
        Undo-HarnessSubmit $j
        return [ordered]@{ runId = $j.runId; workRoot = $j.workRoot; modules = @($j.modules); startedAt = $j.startedAt; files = @($j.entries).Count }
    } catch {
        return [ordered]@{ error = "could not roll back the interrupted submit in $script:JournalPath : $($_.Exception.Message)" }
    }
}

function Get-HarnessCompileState {
    $c = Invoke-UnityCommand -Name 'harness_console' -Params @{ compile_only = $true } -TimeoutSec 5
    if ($c.success -and $c.result.ok -and ($c.result.PSObject.Properties.Name -contains 'compileGeneration')) { return $c.result }
    return $null
}

# Wait until the Editor answers on the main thread and is neither compiling nor importing.
function Wait-HarnessIdle {
    param([int]$TimeoutSec = 120)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        $p = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 5
        if ($p.success -and -not $p.result.isCompiling -and -not $p.result.isUpdating) { return $p.result }
        Start-Sleep -Milliseconds 150
    }
    return $null
}

# Import changed files, compile, and wait until the new domain has loaded. Returns
# @{ ok; status; failed; errors; compileErrors; seconds; reloaded }.
# Does not trust the Pipeline status file alone (it can report a stale failure right after a failed compile,
# or stay 'triggered' when a compile/reload was already in flight). Instead: wait for the compile *generation*
# that started after the trigger, or - failing that - for the Editor to settle, then read the native flag.
function Invoke-HarnessRecompile {
    param([int]$TimeoutSec = 180)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $before = Wait-HarnessIdle -TimeoutSec $TimeoutSec
    if ($null -eq $before) {
        return [pscustomobject]@{ ok = $false; status = 'unreachable'; failed = $true; errors = @('Editor not reachable/idle'); compileErrors = @(); seconds = $sw.Elapsed.TotalSeconds; reloaded = $false }
    }
    $reloadsBefore = [int]$before.domainReloads
    $cs0 = Get-HarnessCompileState
    $gen0 = if ($cs0) { [int]$cs0.compileGeneration } else { -1 }

    $trigger = Invoke-UnityCommand -Name 'recompile' -TimeoutSec 120
    if (-not $trigger.success -and -not $trigger.unreachable) {
        return [pscustomobject]@{ ok = $false; status = 'trigger_failed'; failed = $true; errors = @($trigger.error); compileErrors = @(); seconds = $sw.Elapsed.TotalSeconds; reloaded = $false }
    }
    $trigStatus = if ($trigger.success) { [string]$trigger.result.status } else { 'compiling' }
    if ($trigStatus -eq 'up_to_date') {
        return [pscustomobject]@{ ok = $true; status = 'up_to_date'; failed = $false; errors = @(); compileErrors = @(); seconds = $sw.Elapsed.TotalSeconds; reloaded = $false }
    }

    $done = $null
    $settled = $null
    $idleSince = $null
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        if ($gen0 -ge 0) {
            $cs = Get-HarnessCompileState
            if ($cs -and [int]$cs.compileGeneration -gt $gen0 -and $cs.compileFinished) { $done = $cs; break }
        }
        $p = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 5
        if ($p.success -and -not $p.result.isCompiling -and -not $p.result.isUpdating) {
            if ($null -eq $idleSince) { $idleSince = $sw.Elapsed.TotalSeconds }
            elseif ($sw.Elapsed.TotalSeconds - $idleSince -gt 2.0) { $settled = $p.result; break }
        } else { $idleSince = $null }
        Start-Sleep -Milliseconds 150
    }
    if ($null -eq $done -and $null -eq $settled) {
        return [pscustomobject]@{ ok = $false; status = 'timeout'; failed = $true; errors = @("recompile did not finish in $TimeoutSec s"); compileErrors = @(); seconds = $sw.Elapsed.TotalSeconds; reloaded = $false }
    }

    $compileErrors = @()
    if ($done) {
        $compileErrors = @($done.compileErrors)
        $failed = $compileErrors.Count -gt 0
        $statusName = 'completed'
    } else {
        # Settled without a new generation we could see: trust the Editor's native compile-failure flag.
        $failed = [bool]$settled.compileFailed
        $statusName = 'settled'
        if ($failed) { $cs = Get-HarnessCompileState; if ($cs) { $compileErrors = @($cs.compileErrors) } }
    }
    $errors = @($compileErrors | ForEach-Object { "$($_.file)($($_.line)): $($_.code): $($_.msg)" })

    $reloaded = $false
    if (-not $failed -and $statusName -eq 'completed') {
        # A successful compile is followed by a domain reload; wait for the new domain. If the old domain stays
        # idle and answering for a while, nothing was reloaded (no assembly actually changed).
        $idleSince = $null
        while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
            $p = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 5
            if ($p.success -and -not $p.result.isCompiling -and -not $p.result.isUpdating) {
                if ([int]$p.result.domainReloads -gt $reloadsBefore) { $reloaded = $true; break }
                if ($null -eq $idleSince) { $idleSince = $sw.Elapsed.TotalSeconds }
                elseif ($sw.Elapsed.TotalSeconds - $idleSince -gt 3) { break }
            } else { $idleSince = $null }
            Start-Sleep -Milliseconds 150
        }
    } elseif ($statusName -eq 'settled') {
        $reloaded = [int]$settled.domainReloads -gt $reloadsBefore
    }
    [pscustomobject]@{ ok = -not $failed; status = $statusName; failed = $failed; errors = $errors; compileErrors = $compileErrors; seconds = $sw.Elapsed.TotalSeconds; reloaded = $reloaded }
}

# ---- The loop (tools/loop.ps1, tools/submit.ps1) -------------------------------------------------------------
# recompile -> (stop on compile errors) -> lint + harness_build + harness_shaders -> harness_play (or edit-mode capture)
# -> harness_console + harness_stats. Returns the report (see CLAUDE.md "report.json"); laps go into $Timings.
# The caller holds the Editor lock and saves the report with Save-HarnessReport.

# Fallback parser for recompile_status strings: "Assets\X.cs(12,5): error CS0103: msg"
function ConvertFrom-CompilerString([string]$s) {
    if ($s -match '^(?<file>.*?)\((?<line>\d+),(?<col>\d+)\):\s*error\s+(?<code>\w+):\s*(?<msg>.*)$') {
        $file = $Matches.file.Replace('\', '/')
        $module = if ($file -match 'Assets/Game/([^/]+)/') { $Matches[1] } elseif ($file -like 'Assets/Harness/*') { 'Harness' } else { '' }
        return [ordered]@{ file = $file; line = [int]$Matches.line; msg = "$($Matches.code): $($Matches.msg)"; module = $module }
    }
    [ordered]@{ file = ''; line = 0; msg = $s; module = '' }
}

function Invoke-HarnessLoop {
    param(
        [string]$Scenario = 'tools/scenarios/default.json',
        [Parameter(Mandatory)][string]$OutDir,
        [switch]$NoPlay,
        [switch]$NoCompile,
        [int]$TimeoutSec = 180,
        [System.Collections.IDictionary]$Timings = [ordered]@{}
    )
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Off   # reads optional fields of Editor replies (error, file, ...) that may be absent
    $report = [ordered]@{
        ok            = $false
        stage         = ''
        compileErrors = @()
        runtimeErrors = @()
        fps           = $null
        shots         = @()
        durationSec   = 0
        report        = (Join-Path $OutDir 'report.json').Replace('\', '/')
    }

    # ---- 0. Editor reachable, not playing -----------------------------------------------------------
    $ping = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 10
    # The previous lock holder may have just triggered a domain reload: if the Editor process is alive, wait it out.
    $ep = Get-HarnessEndpoint
    if (-not $ping.success -and $ping.unreachable -and $ep -and (Get-Process -Id $ep.Pid -ErrorAction SilentlyContinue)) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while (-not $ping.success -and $sw.Elapsed.TotalSeconds -lt 60) {
            Start-Sleep -Milliseconds 250
            $ping = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 10
        }
    }
    if (-not $ping.success) {
        $report.stage = 'editor'
        $report['error'] = "Editor not reachable: $($ping.error). Open it with 'unity open $script:ProjectRoot' and wait for 'unity status' = ready. If it is open, it may be in Safe Mode (compile errors at startup): run 'unity pipeline list'."
        return $report
    }
    # Keep the Editor ticking while it is not the foreground app (otherwise compile/play stall). Idempotent.
    Invoke-UnityCommand -Name 'set_autotick' -Params @{ enable = $true } -TimeoutSec 10 | Out-Null
    if ($ping.result.isPlaying -or $ping.result.willChangePlaymode) {
        Invoke-UnityCommand -Name 'editor_stop' | Out-Null
        $sw = [Diagnostics.Stopwatch]::StartNew()
        do { Start-Sleep -Milliseconds 200; $ping = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 5 } while ((-not $ping.success -or $ping.result.isPlaying) -and $sw.Elapsed.TotalSeconds -lt 30)
    }
    $mark = [int]$ping.result.mark

    # ---- 1. Recompile (stop immediately on errors) ----------------------------------------------------
    if (-not $NoCompile) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $rc = Invoke-HarnessRecompile -TimeoutSec $TimeoutSec
        $Timings['compileSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
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
            return $report
        }
    }

    # ---- 2. Lint (reported, fails the loop at the end) + build -------------------------------------------
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
    $Timings['buildSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    $report['build'] = if ($build.success) {
        [ordered]@{ ok = $build.result.ok; fingerprint = $build.result.fingerprint; gameObjects = $build.result.gameObjects; durationMs = $build.result.durationMs; steps = @($build.result.steps | ForEach-Object { [ordered]@{ type = $_.type; module = $_.module; ms = $_.ms; error = $_.error; file = $_.file; line = $_.line } }); warnings = @($build.result.warnings) }
    } else { [ordered]@{ ok = $false; error = $build.error } }
    $report['lint'] = $lintIssues
    if (-not $build.success -or -not $build.result.ok) {
        $report.stage = 'build'
        $report['error'] = if ($build.success) { $build.result.error } else { $build.error }
        $con = Invoke-UnityCommand -Name 'harness_console' -Params @{ since = $mark } -TimeoutSec 10
        if ($con.success) { $report.runtimeErrors = @($con.result.runtimeErrors | ForEach-Object { [ordered]@{ type = $_.type; msg = $_.message; file = $_.file; line = $_.line; module = $_.module; count = $_.count } }) }
        return $report
    }

    # ---- 3. Play scenario (or edit-mode capture) ------------------------------------------------------
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $playResult = $null
    if ($NoPlay) {
        $cap = Invoke-UnityCommand -Name 'harness_capture' -Params @{ preset = 'all'; out = $OutDir } -TimeoutSec 60
        if ($cap.success) { $shotObjs = @($cap.result.shots) } else { $shotObjs = @(); $report['error'] = $cap.error }
    } else {
        $start = Invoke-UnityCommand -Name 'harness_play' -Params @{ scenario = $Scenario; out = $OutDir } -TimeoutSec 30
        if (-not $start.success -or -not $start.result.ok) {
            $report.stage = 'play'
            $report['error'] = if ($start.success) { $start.result.error } else { $start.error }
            return $report
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
            return $report
        }
        $playResult = $state.result
        $shotObjs = @(); if ($playResult) { $shotObjs = @($playResult.shots) }
        if ($state.state -eq 'failed') { $report['playError'] = $state.error }
    }
    $Timings['playSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)

    # ---- 4. Console + stats ---------------------------------------------------------------------------
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $con = Invoke-UnityCommand -Name 'harness_console' -Params @{ since = $mark } -TimeoutSec 10
    $stats = if ($NoPlay) { $null } else { Invoke-UnityCommand -Name 'harness_stats' -TimeoutSec 10 }
    $Timings['collectSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)

    $runtimeErrors = @()
    $warningCount = 0
    if ($con.success) {
        $runtimeErrors = @($con.result.runtimeErrors | ForEach-Object { [ordered]@{ type = $_.type; msg = $_.message; file = $_.file; line = $_.line; module = $_.module; count = $_.count; stack = $_.stack } })
        $warningCount = [int]$con.result.counts.warning
        # Errors from inside the Editor/packages (no Assets/ frame). Visible, but they do not fail the loop.
        $report['editorErrors'] = @($con.result.editorErrors | ForEach-Object { [ordered]@{ type = $_.type; msg = ($_.message -split "`n")[0]; count = $_.count; stack = $_.stack } })
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
    $report
}

# Fill durationSec/timings, write <OutDir>/report.json (UTF-8, no BOM) and return the JSON text.
function Save-HarnessReport {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$Report,
        [Parameter(Mandatory)][string]$OutDir,
        [Diagnostics.Stopwatch]$Clock,
        [System.Collections.IDictionary]$Timings
    )
    if ($Clock) { $Report['durationSec'] = [math]::Round($Clock.Elapsed.TotalSeconds, 2) }
    if ($null -ne $Timings) { $Report['timings'] = $Timings }
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    $json = $Report | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText((Join-Path $OutDir 'report.json'), $json, (New-Object Text.UTF8Encoding($false)))
    $json
}

Export-ModuleMember -Function Get-HarnessProjectRoot, Get-HarnessWorkRoot, Test-HarnessWorktree, Get-HarnessEndpoint, Invoke-UnityCommand,
    Wait-UnityReachable, Invoke-HarnessRecompile, Get-HarnessCompileState, Wait-HarnessIdle, Enter-HarnessLock, Exit-HarnessLock,
    Test-HarnessReadOnly, Get-HarnessLastRecovery, Start-HarnessSubmit, Complete-HarnessSubmit, Undo-HarnessSubmit,
    Invoke-HarnessLoop, Save-HarnessReport
