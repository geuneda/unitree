# Harness.psm1 - thin client for the Unity Pipeline HTTP server of THIS project's Editor.
# `unity command` spawns a CLI process per call (~0.8s) and PowerShell 5.1 mangles quoted args,
# so the loop talks to /api/exec directly (port + bearer token from the descriptor file).

Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Net.Http

$script:ProjectRoot = Split-Path -Parent $PSScriptRoot
$script:Descriptor = Join-Path $script:ProjectRoot 'Library\Pipeline\.unity-pipeline-port'
$script:Client = $null

function Get-HarnessProjectRoot { $script:ProjectRoot }

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
        $bytes = $resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        try { $obj = $text | ConvertFrom-Json } catch {
            return [pscustomobject]@{ success = $false; unreachable = $false; error = "non-JSON reply (HTTP $([int]$resp.StatusCode)): $text" }
        }
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
# One Editor, many agents: every Editor-mutating operation (recompile/build/play/capture/...) runs under a
# machine-wide mutex per project, so parallel agents queue instead of interleaving.
$script:Mutex = $null
$script:ReadOnlyCommands = @('harness_ping', 'harness_console', 'harness_play_status', 'harness_stats', 'harness_lint', 'harness_shaders',
    'recompile_status', 'editor_status', 'console', 'console_status', 'get_scene_hierarchy', 'find_gameobjects',
    'list_open_scenes', 'get_component_properties', 'package_list', 'test_status', 'build_status')

function Test-HarnessReadOnly([string]$Command) { $script:ReadOnlyCommands -contains $Command }

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
    return [math]::Round($sw.Elapsed.TotalSeconds, 2)
}

function Exit-HarnessLock {
    if ($null -eq $script:Mutex) { return }
    try { $script:Mutex.ReleaseMutex() } catch { }
    $script:Mutex.Dispose()
    $script:Mutex = $null
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
Export-ModuleMember -Function Get-HarnessProjectRoot, Get-HarnessEndpoint, Invoke-UnityCommand, Wait-UnityReachable, Invoke-HarnessRecompile, Get-HarnessCompileState, Wait-HarnessIdle, Enter-HarnessLock, Exit-HarnessLock, Test-HarnessReadOnly
