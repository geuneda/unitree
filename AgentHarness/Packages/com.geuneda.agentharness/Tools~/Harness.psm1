# Harness.psm1 - thin client for the Unity Pipeline HTTP server of THIS project's Editor.
# `unity command` spawns a CLI process per call (~0.8s) and PowerShell 5.1 mangles quoted args,
# so the loop talks to /api/exec directly (port + bearer token from the descriptor file).

Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Net.Http

# Two roots. WorkRoot = the checkout whose tools/*.ps1 entry point ran (where an agent edits sources; the entry point
# passes it in AGENTHARNESS_WORK_ROOT, since these scripts live in the package, not in the project). ProjectRoot = the
# Editor project they drive (Library/, the Pipeline descriptor, the lock). They are the same folder unless this is an
# agent worktree (G5-2): a checkout without Library/ (e.g. `git worktree add`) drives the Editor of the main worktree at
# the same sub-path. AGENTHARNESS_EDITOR_ROOT overrides the detection (e.g. for a plain copy of the project).
# A worktree that has its own Library/ (tools/open.ps1 -Own, W7) drives its own Editor; submit.ps1 and land.ps1 still
# integrate into the main worktree (Use-HarnessIntegrationRoot).
function Find-HarnessProjectAbove([string]$Dir) {
    $d = $Dir
    while ($d) {
        if ((Test-Path -LiteralPath (Join-Path $d 'Assets')) -and (Test-Path -LiteralPath (Join-Path $d 'ProjectSettings'))) { return $d.TrimEnd('\', '/') }
        $d = Split-Path -Parent $d
    }
    throw "no Unity project above $Dir (run the tools through the project's tools/*.ps1 entry points)"
}

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

# The checkout submit/land integrate into: the main worktree at the same sub-path when this is a linked worktree (with or
# without its own Editor), else this checkout. AGENTHARNESS_EDITOR_ROOT overrides it like the Editor root.
function Get-HarnessIntegrationRoot {
    if ($env:AGENTHARNESS_EDITOR_ROOT) { return [IO.Path]::GetFullPath($env:AGENTHARNESS_EDITOR_ROOT).TrimEnd('\', '/') }
    try {
        $main = @(& git -C $script:WorkRoot worktree list --porcelain 2>$null) | Select-Object -First 1
        $prefix = & git -C $script:WorkRoot rev-parse --show-prefix 2>$null
        if ($main -like 'worktree *') {
            $candidate = [IO.Path]::GetFullPath([IO.Path]::Combine($main.Substring(9), "$prefix")).TrimEnd('\', '/')
            if (Test-Path -LiteralPath (Join-Path $candidate 'ProjectSettings')) { return $candidate }
        }
    } catch { }
    $script:WorkRoot
}

# The Editor project the functions below drive, and the paths that hang off it (Pipeline descriptor, submit/land journals).
function Set-HarnessEditorRoot([string]$Root) {
    if ($null -ne $script:Mutex) { throw "Set-HarnessEditorRoot while holding the lock of $script:ProjectRoot" }
    $script:ProjectRoot = $Root
    $script:Descriptor = Join-Path $Root 'Library/Pipeline/.unity-pipeline-port'
    $script:SubmitDir = Join-Path $Root 'Library/Harness/submit'
    $script:JournalPath = Join-Path $script:SubmitDir 'pending.json'
    $script:OwnersPath = Join-Path $script:SubmitDir 'owners.json'
    $script:ContractOwnersPath = Join-Path $script:SubmitDir 'contracts.json'
    $script:LandDir = Join-Path $Root 'Library/Harness/land'
    $script:LandJournalPath = Join-Path $script:LandDir 'pending.json'
    $script:RepoRoot = $null
}

# submit.ps1 / land.ps1: from a worktree with its own Editor too, they put the work into the shared Editor tree.
function Use-HarnessIntegrationRoot { Set-HarnessEditorRoot (Get-HarnessIntegrationRoot) }

$script:Mutex = $null
$script:WorkRoot = if ($env:AGENTHARNESS_WORK_ROOT) { [IO.Path]::GetFullPath($env:AGENTHARNESS_WORK_ROOT).TrimEnd('\', '/') } else { Find-HarnessProjectAbove $PSScriptRoot }
Set-HarnessEditorRoot (Resolve-HarnessEditorRoot $script:WorkRoot)
$script:Client = $null

function Get-HarnessProjectRoot { $script:ProjectRoot }
function Get-HarnessWorkRoot { $script:WorkRoot }
function Test-HarnessWorktree { $script:WorkRoot -ne $script:ProjectRoot }
# An agent worktree running its own Editor (its own Library/, open.ps1 -Own).
function Test-HarnessOwnEditor { ($script:ProjectRoot -eq $script:WorkRoot) -and ((Get-HarnessIntegrationRoot) -ne $script:WorkRoot) }

# ---- ProjectSettings/AgentHarness.json (the same file the Editor reads: Harness.HarnessConfig) ---------------------
# Missing file or field = the defaults of an attached project. Modules: every folder under a moduleRoots entry, and
# every modules[] entry {name, path}.
function Get-HarnessConfig([string]$Root = $script:WorkRoot) {
    $c = [ordered]@{ setup = 'attach'; moduleRoots = @(); modules = @(); contracts = ''; generatedRoot = 'Assets/AgentHarness/Generated'
        buildScene = 'Assets/AgentHarness/Main.unity'; playScene = 'first'; goldenRoot = 'golden'; fromFile = $false }
    $f = Join-Path $Root 'ProjectSettings/AgentHarness.json'
    if (Test-Path -LiteralPath $f) {
        $j = [IO.File]::ReadAllText($f) | ConvertFrom-Json
        foreach ($k in @('setup', 'moduleRoots', 'modules', 'contracts', 'generatedRoot', 'buildScene', 'playScene', 'goldenRoot')) {
            if ($j.PSObject.Properties.Name -contains $k -and $null -ne $j.$k) { $c[$k] = $j.$k }
        }
        $c.fromFile = $true
    }
    $norm = { param($p) if ($p) { "$p".Trim().Replace('\', '/').TrimEnd('/') } else { '' } }
    $c.moduleRoots = @(@($c.moduleRoots) | ForEach-Object { & $norm $_ } | Where-Object { $_ })
    $c.modules = @(@($c.modules) | Where-Object { $_ -and $_.name -and $_.path } | ForEach-Object { [pscustomobject]@{ name = "$($_.name)".Trim(); path = (& $norm $_.path) } })
    foreach ($k in @('contracts', 'generatedRoot', 'buildScene', 'goldenRoot')) { $c[$k] = & $norm $c[$k] }
    if (-not $c.goldenRoot) { $c.goldenRoot = 'golden' }
    $c.setup = "$($c.setup)".Trim().ToLowerInvariant()
    [pscustomobject]$c
}

# The module a project-relative path belongs to: @{ name; folder; fromRoot } (name '' = none). Mirrors HarnessConfig.ModuleOf.
function Get-HarnessModuleOf([string]$Path, $Config = (Get-HarnessConfig)) {
    $p = $Path.Replace('\', '/')
    if ($p -match '^(?:Packages/com\.geuneda\.agentharness|Assets/Harness)/') { return [pscustomobject]@{ name = 'Harness'; folder = 'Packages/com.geuneda.agentharness'; fromRoot = $false } }
    $best = $null
    foreach ($m in @($Config.modules)) {
        if (($p -eq $m.path -or $p.StartsWith("$($m.path)/")) -and (-not $best -or $m.path.Length -gt $best.path.Length)) { $best = $m }
    }
    if ($best) { return [pscustomobject]@{ name = $best.name; folder = $best.path; fromRoot = $false } }
    foreach ($r in @($Config.moduleRoots)) {
        if (-not $p.StartsWith("$r/")) { continue }
        $rest = $p.Substring($r.Length + 1)
        $slash = $rest.IndexOf('/')
        $name = if ($slash -gt 0) { $rest.Substring(0, $slash) } elseif ($rest.EndsWith('.meta')) { $rest.Substring(0, $rest.Length - 5) } else { '' }
        if (-not $name) { break }
        return [pscustomobject]@{ name = $name; folder = "$r/$name"; fromRoot = $true }
    }
    [pscustomobject]@{ name = ''; folder = ''; fromRoot = $false }
}

# The folder (project-relative) of module <Name> in the checkout <Root>: a modules[] entry, else <moduleRoot>/<Name> that
# exists there (else the first module root's, for a module that does not exist yet). $null = not a module name.
function Get-HarnessModuleFolder([string]$Name, [string]$Root = $script:WorkRoot, $Config = (Get-HarnessConfig $Root)) {
    foreach ($m in @($Config.modules)) { if ($m.name -eq $Name) { return $m.path } }
    foreach ($r in @($Config.moduleRoots)) { if (Test-Path -LiteralPath (Join-Path $Root "$r/$Name")) { return "$r/$Name" } }
    if (@($Config.moduleRoots).Count -gt 0) { return "$(@($Config.moduleRoots)[0])/$Name" }
    $null
}

# HarnessOut/ ignores itself (no .gitignore entry needed in a project the harness is attached to).
function Initialize-HarnessOut([string]$Dir) {
    $full = [IO.Path]::GetFullPath($Dir)
    $out = [IO.Path]::GetFullPath((Join-Path $script:WorkRoot 'HarnessOut'))
    if (-not $full.StartsWith($out, [StringComparison]::OrdinalIgnoreCase)) { return }
    [void][IO.Directory]::CreateDirectory($out)
    $gi = Join-Path $out '.gitignore'
    if (-not (Test-Path -LiteralPath $gi)) { [IO.File]::WriteAllText($gi, "# AgentHarness output (captures, reports): never committed`n*`n", (New-Object Text.UTF8Encoding($false))) }
}

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

# ---- Editor process (tools/open.ps1, tools/quit.ps1, tools/fresh-clone-test.ps1) ---------------------------------
# The Editor serving this project, from the Pipeline descriptor. $null when none is running or the descriptor is stale
# (left by a killed Editor: its pid is gone or now belongs to another program, possibly another Editor).
function Get-HarnessEditorProcess {
    $ep = Get-HarnessEndpoint
    if ($null -eq $ep -or $ep.Pid -le 0) { return $null }
    $p = Get-Process -Id $ep.Pid -ErrorAction SilentlyContinue
    if (-not $p -or $p.ProcessName -ne 'Unity') { return $null }
    # The descriptor is written after its Editor started; a process that started later reuses a dead Editor's pid.
    try { if ($p.StartTime -gt (Get-Item -LiteralPath $script:Descriptor).LastWriteTime) { return $null } } catch { }
    $p
}

# The Editor tools/open.ps1 started for this project (Logs/harness-editor.json), while it runs. Known from the start,
# before the Editor writes its lock file or the Pipeline descriptor (e.g. while a startup dialog waits for a person).
function Get-HarnessLaunchedEditor {
    $f = Join-Path $script:ProjectRoot 'Logs/harness-editor.json'
    if (-not (Test-Path -LiteralPath $f)) { return $null }
    try { $j = [IO.File]::ReadAllText($f) | ConvertFrom-Json } catch { return $null }
    $p = Get-Process -Id ([int]$j.pid) -ErrorAction SilentlyContinue
    if (-not $p -or $p.ProcessName -ne 'Unity' -or $p.HasExited) { return $null }
    # A later process that reuses the pid of an Editor that has exited is not it.
    try { if ([math]::Abs(($p.StartTime.ToUniversalTime() - [DateTime]::Parse($j.startedAt).ToUniversalTime()).TotalSeconds) -gt 2) { return $null } } catch { return $null }
    $p
}

# Titles of a process's visible top-level windows: how a dialog shown before the Pipeline server is up (software
# terms, "Enter Safe Mode?", package errors) is seen from outside. Process.MainWindowTitle misses some of them (the
# Safe Mode prompt has no main window). Windows only for now (P-3); elsewhere an empty list.
function Get-HarnessWindowTitles([int]$ProcessId) {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return @() }
    if (-not ('AgentHarnessWindows' -as [type])) {
        Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class AgentHarnessWindows {
    delegate bool EnumProc(IntPtr hwnd, IntPtr arg);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr arg);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    public static string[] Titles(int processId) {
        var titles = new List<string>();
        EnumWindows((hwnd, arg) => {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == processId && IsWindowVisible(hwnd)) {
                var text = new StringBuilder(512);
                if (GetWindowText(hwnd, text, text.Capacity) > 0) titles.Add(text.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return titles.ToArray();
    }
}
'@
    }
    @([AgentHarnessWindows]::Titles($ProcessId))
}

function Save-HarnessLaunchedEditor([Diagnostics.Process]$Process) {
    $f = Join-Path $script:ProjectRoot 'Logs/harness-editor.json'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($f))
    $j = [ordered]@{ pid = $Process.Id; startedAt = $Process.StartTime.ToUniversalTime().ToString('o') }
    [IO.File]::WriteAllText($f, ($j | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
}

# True while an Editor has this project open, also before its Pipeline server is up (first import, Safe Mode):
# the Editor holds Temp/UnityLockfile exclusively.
function Test-HarnessProjectOpen {
    $f = Join-Path $script:ProjectRoot 'Temp/UnityLockfile'
    if (-not (Test-Path -LiteralPath $f)) { return $false }
    try { $s = [IO.File]::Open($f, 'Open', 'Read', 'None'); $s.Dispose(); return $false } catch { return $true }
}

function Get-HarnessProjectVersion([string]$Root = $script:ProjectRoot) {
    $m = [regex]::Match([IO.File]::ReadAllText((Join-Path $Root 'ProjectSettings/ProjectVersion.txt')), 'm_EditorVersion:\s*(\S+)')
    if ($m.Success) { $m.Groups[1].Value } else { $null }
}

# Installed Editors ({version, location}) from 'unity editors --installed': Hub installs and registered locations.
function Get-HarnessInstalledEditors {
    $r = Invoke-HarnessProcess 'unity' @('editors', '--installed', '--format', 'json', '--no-banner') -TimeoutSec 60
    if ($r.code -ne 0) { throw "unity editors --installed failed ($($r.code)): $($r.err.Trim())" }
    @(($r.out | ConvertFrom-Json).data | ForEach-Object { [pscustomobject]@{ version = [string]$_.version; location = [string]$_.location } })
}

# The Editor executable of an installed version, or $null. On macOS the CLI reports the .app bundle.
function Find-HarnessEditorExe([string]$Version) {
    $e = @(Get-HarnessInstalledEditors | Where-Object { $_.version -eq $Version }) | Select-Object -First 1
    if (-not $e) { return $null }
    if ($e.location -like '*.app') { return (Join-Path $e.location 'Contents/MacOS/Unity') }
    $e.location
}

# Last lines of a log that an Editor may still be writing.
function Read-HarnessLogTail([string]$Path, [int]$Lines = 40) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $s = New-Object IO.FileStream($Path, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        [void]$s.Seek([math]::Max(0, $s.Length - 65536), 'Begin')
        $text = (New-Object IO.StreamReader($s, [Text.Encoding]::UTF8)).ReadToEnd()
    } finally { $s.Dispose() }
    @($text -split "`r?`n" | Where-Object { $_ -ne '' } | Select-Object -Last $Lines)
}

# Command line of an Editor started by the tools (W7, G2-2). Every one gets -automated (EditorUtility.DisplayDialog*
# answer their default at once instead of blocking the main thread; the window layout is not saved on exit) unless
# -Interactive (a person uses that Editor), and -debugCodeOptimization (Debug from the start: exact exception lines and the
# stable fingerprint without the extra recompile HarnessCodeOptimization would do). -Headless: -batchmode without -quit, a
# resident Editor without a window that renders with the GPU; -ignoreCompilerErrors lets it start on the last good
# assemblies when the scripts do not compile (batch mode would exit instead; a windowed -automated Editor enters Safe Mode).
function Get-HarnessEditorArguments {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Log, [switch]$Headless, [switch]$Interactive)
    $a = @()
    if ($Headless) { $a += @('-batchmode', '-ignoreCompilerErrors') }
    $a += @('-projectPath', $Root, '-logFile', $Log, '-debugCodeOptimization')
    if (-not $Interactive) { $a += '-automated' }
    $a
}

# The C# compiler errors of the latest compile in an Editor log (Safe Mode or a headless start: nothing else has them).
function Get-HarnessLogCompileErrors([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $s = New-Object IO.FileStream($Path, 'Open', 'Read', 'ReadWrite, Delete')
    try { $text = (New-Object IO.StreamReader($s, [Text.Encoding]::UTF8)).ReadToEnd() } finally { $s.Dispose() }
    # Each compile run logs its errors after starting the build program; keep the last run's.
    $start = $text.LastIndexOf('bee_backend')
    if ($start -gt 0) { $text = $text.Substring($start) }
    $seen = @{}
    # (The same errors also come as ##utp JSON lines: a file name with no quote in it skips those.)
    @([regex]::Matches($text, '(?m)^(?<file>[^\r\n("]+\.cs)\((?<line>\d+),(?<col>\d+)\): error (?<code>CS\d+): (?<msg>[^\r\n]*)') | ForEach-Object {
        $file = $_.Groups['file'].Value.Replace('\', '/')
        $key = "$file|$($_.Groups['line'].Value)|$($_.Groups['code'].Value)|$($_.Groups['msg'].Value)"
        if (-not $seen.ContainsKey($key)) {
            $seen[$key] = $true
            [ordered]@{ file = $file; line = [int]$_.Groups['line'].Value; msg = "$($_.Groups['code'].Value): $($_.Groups['msg'].Value.Trim())"; module = (Get-HarnessModuleOf $file).name }
        }
    })
}

# A windowed Editor in Safe Mode (compile errors at startup; -automated enters it without asking): no Pipeline server,
# the window title says so.
function Test-HarnessSafeMode([int]$ProcessId) {
    if ($ProcessId -le 0) { return $false }
    [bool](@(Get-HarnessWindowTitles $ProcessId) | Where-Object { $_ -match 'SAFE MODE' })
}

# Seed an agent worktree's Library/ with a copy of the Editor tree's (open.ps1 -Own): the imported assets, the package
# cache and the compiled scripts come along, so its Editor does not import the project again (it recompiles the scripts once:
# their paths changed). Not copied: what belongs to the running Editor or the Editor tree (the Pipeline descriptor, the
# harness state with the submit/land journals, lock and pid files). Copied to a temporary folder, then renamed, so a
# half-done copy never looks like a Library. Returns @{ files; mb; sec }.
function Copy-HarnessLibrary {
    param([Parameter(Mandatory)][string]$From, [Parameter(Mandatory)][string]$To)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $src = [IO.Path]::GetFullPath($From).TrimEnd('\', '/')
    $dst = [IO.Path]::GetFullPath($To).TrimEnd('\', '/')
    $tmp = "$dst.seed"
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
    $skipDirs = @('Pipeline', 'Harness', 'AgentHarness', 'TempArtifacts')
    $skipFiles = @('EditorInstance.json', 'ilpp.pid', 'ArtifactDB-lock', 'SourceAssetDB-lock')
    $robocopy = Get-Command robocopy -ErrorAction SilentlyContinue
    if ($robocopy) {
        # Windows: multi-threaded (1.9 GB, 27k files in ~7 s here). Exit codes below 8 are success.
        $rcArgs = @($src, $tmp, '/E', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/MT:16', '/R:2', '/W:1', '/XD') + @($skipDirs | ForEach-Object { Join-Path $src $_ }) + @('/XF') + $skipFiles
        $r = Invoke-HarnessProcess $robocopy.Source $rcArgs
        if ($r.code -ge 8) { throw "copying $src failed (robocopy $($r.code)): $($r.out.Trim()) $($r.err.Trim())" }
    } else {
        foreach ($f in [IO.Directory]::EnumerateFiles($src, '*', [IO.SearchOption]::AllDirectories)) {
            $rel = $f.Substring($src.Length + 1)
            $top = $rel.Split([char[]]@('\', '/'))[0]
            if ($skipDirs -contains $top -or $skipFiles -contains $rel) { continue }
            $target = [IO.Path]::Combine($tmp, $rel)
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
            [IO.File]::Copy($f, $target, $true)
        }
    }
    [IO.Directory]::Move($tmp, $dst)
    $files = @([IO.Directory]::EnumerateFiles($dst, '*', [IO.SearchOption]::AllDirectories))
    $bytes = 0; foreach ($f in $files) { $bytes += (New-Object IO.FileInfo $f).Length }
    [ordered]@{ from = $src.Replace('\', '/'); files = $files.Count; mb = [math]::Round($bytes / 1MB); sec = [math]::Round($sw.Elapsed.TotalSeconds, 1) }
}

# ---- Editor lock -------------------------------------------------------------------------------------
# One Editor, many agents: every Editor-mutating operation (recompile/build/play/capture/submit...) runs under a
# machine-wide mutex per project, so parallel agents queue instead of interleaving.
$script:LastRecovery = $null
$script:LastLandRecovery = $null
$script:ReadOnlyCommands = @('harness_ping', 'harness_console', 'harness_play_status', 'harness_stats', 'harness_lint', 'harness_shaders',
    'recompile_status', 'editor_status', 'console', 'console_status', 'get_scene_hierarchy', 'find_gameobjects',
    'list_open_scenes', 'get_component_properties', 'package_list', 'test_status', 'build_status', 'harness_player_plan', 'harness_contracts')

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
    $script:LastLandRecovery = Restore-HarnessPendingLand
    return $wait
}

function Exit-HarnessLock {
    if ($null -eq $script:Mutex) { return }
    try { $script:Mutex.ReleaseMutex() } catch { }
    $script:Mutex.Dispose()
    $script:Mutex = $null
}

function Get-HarnessLastRecovery { $script:LastRecovery }
function Get-HarnessLastLandRecovery { $script:LastLandRecovery }

# recoveredSubmit / recoveredLand fields for a report, when this lock holder rolled one back.
function Add-HarnessRecovery([System.Collections.IDictionary]$Report) {
    if ($script:LastRecovery) { $Report['recoveredSubmit'] = $script:LastRecovery }
    if ($script:LastLandRecovery) { $Report['recoveredLand'] = $script:LastLandRecovery }
}

# ---- Submit transactions (tools/submit.ps1, G5-2) ------------------------------------------------------
# submit.ps1 copies an agent worktree's module folders into the Editor tree while holding the lock. Before the first
# write it backs up every file it will overwrite or delete and writes a journal (Library/Harness/submit/pending.json).
# The journal is removed once the submit has kept or reverted its files. A journal seen by the next lock holder means
# that submit died half-way; it is rolled back, so a crashed submit cannot leave its code in the Editor tree.
# ($script:SubmitDir, $script:JournalPath: Set-HarnessEditorRoot.)

# Writes: @{ rel = 'Assets/Game/Foo/X.cs'; src = <absolute source file> }. Deletes: project-relative files.
# CreatedDirs: project-relative folders that do not exist yet (removed with their Unity .meta on revert).
# -Guard: files the submit does not write but its loop may (ProjectSettings the settings steps own, G1-5): backed up too,
# so a reverted or interrupted submit puts them back.
function Start-HarnessSubmit {
    param([string]$RunId, [string]$WorkRoot, [string[]]$Modules, [object[]]$Writes = @(), [string[]]$Deletes = @(), [string[]]$CreatedDirs = @(), [string[]]$Guard = @())
    $backup = Join-Path $script:SubmitDir $RunId
    $entries = @()
    $paths = @(@($Writes | ForEach-Object { $_.rel }) + @($Deletes))
    $guarded = @($Guard | Where-Object { $paths -notcontains $_ })
    foreach ($rel in @($paths + $guarded)) {
        $abs = [IO.Path]::Combine($script:ProjectRoot, $rel)
        $existed = [IO.File]::Exists($abs)
        if ($existed) {
            $b = [IO.Path]::Combine($backup, $rel)
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($b))
            [IO.File]::Copy($abs, $b, $true)
        }
        $entries += [ordered]@{ path = $rel; existed = $existed; guard = $guarded -contains $rel }
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

# A journal entry of a -Guard file (in memory an ordered dictionary, read back from the journal file an object).
function Test-HarnessGuardEntry($Entry) {
    if ($Entry -is [System.Collections.IDictionary]) { return [bool]$Entry['guard'] }
    ($Entry.PSObject.Properties.Name -contains 'guard') -and [bool]$Entry.guard
}

# Put every journaled file back as it was before the submit. Files the submit created are deleted together with the
# .meta Unity generated for them; restored files get a fresh timestamp so the next refresh re-imports them. A guarded file
# (-Guard) is only written when its content changed (a ProjectSettings file Unity sees change is loaded again).
function Undo-HarnessSubmit {
    param([Parameter(Mandatory)]$Journal)
    $root = $script:ProjectRoot
    $restored = @{}
    foreach ($e in @($Journal.entries)) { if ($e.existed) { $restored[$e.path] = $true } }
    foreach ($e in @($Journal.entries)) {
        if ($e.existed -or (Test-HarnessGuardEntry $e)) { continue }
        foreach ($p in @($e.path, "$($e.path).meta")) {
            if ($restored.ContainsKey($p)) { continue }
            $abs = [IO.Path]::Combine($root, $p)
            if ([IO.File]::Exists($abs)) { [IO.File]::Delete($abs) }
        }
    }
    foreach ($e in @($Journal.entries)) {
        if (-not $e.existed) { continue }
        $abs = [IO.Path]::Combine($root, $e.path)
        $b = [IO.Path]::Combine($Journal.backup, $e.path)
        if ((Test-HarnessGuardEntry $e) -and [IO.File]::Exists($abs) -and
            [Convert]::ToBase64String([IO.File]::ReadAllBytes($abs)) -eq [Convert]::ToBase64String([IO.File]::ReadAllBytes($b))) { continue }
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($abs))
        [IO.File]::Copy($b, $abs, $true)
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

# ---- Child processes (git, unity CLI, PowerShell scripts) ------------------------------------------------------
# Programs run through Process, not the call operator: UTF-8 output (non-ASCII paths), stderr captured without
# PowerShell 5.1 turning it into errors, exit code returned. -Environment: name -> value ($null removes the variable).
# -TimeoutSec > 0 kills the program when it runs longer (timedOut = $true).
function ConvertTo-HarnessArg([string]$a) {
    if ($a -ne '' -and $a -notmatch '[\s"]') { return $a }
    '"' + (($a -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}

function Invoke-HarnessProcess {
    param([Parameter(Mandatory, Position = 0)][string]$File, [Parameter(Position = 1)][string[]]$Arguments = @(),
        [hashtable]$Environment = @{}, [int]$TimeoutSec = 0)
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    $psi.Arguments = (@($Arguments) | ForEach-Object { ConvertTo-HarnessArg $_ }) -join ' '
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $utf8 = New-Object Text.UTF8Encoding($false)
    $psi.StandardOutputEncoding = $utf8
    $psi.StandardErrorEncoding = $utf8
    foreach ($k in $Environment.Keys) {
        if ($null -eq $Environment[$k]) { $psi.EnvironmentVariables.Remove($k) } else { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] }
    }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($psi)
    $p.StandardInput.Close()   # nothing to read (.NET may have written the console encoding's BOM to it already)
    $out = $p.StandardOutput.ReadToEndAsync()
    $err = $p.StandardError.ReadToEndAsync()
    $timedOut = $false
    if ($TimeoutSec -gt 0 -and -not $p.WaitForExit($TimeoutSec * 1000)) {
        $timedOut = $true
        try { $p.Kill() } catch { }
    }
    $p.WaitForExit()
    # A grandchild that inherited the pipes would keep them open: do not wait for it.
    $stdout = if ($out.Wait(10000)) { $out.Result } else { '' }
    $stderr = if ($err.Wait(10000)) { $err.Result } else { '' }
    $r = [pscustomobject]@{ code = $p.ExitCode; out = $stdout; err = $stderr; timedOut = $timedOut; sec = [math]::Round($sw.Elapsed.TotalSeconds, 2) }
    $p.Dispose()
    $r
}

# The PowerShell running this script (powershell.exe or pwsh), for running other tools/*.ps1 as child processes.
function Get-HarnessPowerShell { [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName }

# ---- git (tools/land.ps1, submit ownership) -------------------------------------------------------------------
# Paths come back unquoted (core.quotePath=false).
function Invoke-HarnessGit {
    param([Parameter(Mandatory, Position = 0)][string]$Dir, [Parameter(Mandatory, Position = 1)][string[]]$Arguments, [switch]$Check)
    $p = Invoke-HarnessProcess 'git' (@('-C', $Dir, '-c', 'core.quotePath=false') + $Arguments) -Environment @{ GIT_TERMINAL_PROMPT = '0'; GIT_MERGE_AUTOEDIT = 'no'; LC_ALL = 'C' }
    $r = [pscustomobject]@{ code = $p.code; out = $p.out; err = $p.err.Trim() }
    if ($Check -and $r.code -ne 0) { throw "git $($Arguments -join ' ') failed ($($r.code)): $($r.err) $($r.out.Trim())" }
    $r
}

# Tokens of NUL-terminated (-z) output.
function Split-HarnessZ([string]$s) { @($s.Split([char]0) | Where-Object { $_ -ne '' }) }

# Uncommitted state of a repository: repo-relative path -> XY ('??' untracked, ' M', ' D', 'M ', ...).
function Get-HarnessGitStatus([string]$Repo) {
    $h = @{}
    foreach ($t in @(Split-HarnessZ (Invoke-HarnessGit $Repo @('status', '--porcelain=v1', '-z', '--untracked-files=all', '--no-renames') -Check).out)) {
        $h[$t.Substring(3)] = $t.Substring(0, 2)
    }
    $h
}

function Test-HarnessSamePath([string]$a, [string]$b) {
    if (-not $a -or -not $b) { return $false }
    [IO.Path]::GetFullPath($a).TrimEnd('\', '/') -ieq [IO.Path]::GetFullPath($b).TrimEnd('\', '/')
}

# Top level of the Editor tree's repository (what land.ps1 merges into; forward slashes). Cached until Set-HarnessEditorRoot.
function Get-HarnessRepoRoot {
    if ($null -eq $script:RepoRoot) { $script:RepoRoot = (Invoke-HarnessGit $script:ProjectRoot @('rev-parse', '--show-toplevel') -Check).out.Trim() }
    $script:RepoRoot
}

# A file for --pathspec-from-file --pathspec-file-nul (no command-line length limit, no glob magic).
function Write-HarnessPathspec([string]$File, [string[]]$Paths) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($File))
    $text = (@($Paths | ForEach-Object { ":(literal)$_" }) -join [char]0) + [char]0
    [IO.File]::WriteAllText($File, $text, (New-Object Text.UTF8Encoding($false)))
}

# Blob ids of working-tree files as git would store them (eol-normalized), repo-relative path -> id or $null (absent).
function Get-HarnessContentIds([string]$Repo, [string[]]$Paths) {
    $h = @{}
    foreach ($p in $Paths) { $h[$p] = $null }
    $exist = @($Paths | Where-Object { [IO.File]::Exists([IO.Path]::Combine($Repo, $_)) })
    $i = 0
    while ($i -lt $exist.Count) {
        # Arguments, in chunks under the Windows command-line limit.
        $chunk = @(); $len = 0
        while ($i -lt $exist.Count -and ($chunk.Count -eq 0 -or $len + $exist[$i].Length -lt 16000)) { $chunk += $exist[$i]; $len += $exist[$i].Length + 3; $i++ }
        $ids = @((Invoke-HarnessGit $Repo (@('hash-object', '--') + $chunk) -Check).out -split "`n" | Where-Object { $_ })
        for ($k = 0; $k -lt $chunk.Count; $k++) { $h[$chunk[$k]] = $ids[$k].Trim() }
    }
    $h
}

# ---- Module owners: one module = one agent (G5-5) ----------------------------------------------------------------
# submit.ps1 records which worktree last kept each module in the Editor tree; land.ps1 releases the modules of the
# branch it landed. A module whose Editor-tree copy still has un-landed changes from another live worktree is refused.
# ($script:OwnersPath: Set-HarnessEditorRoot.)

# module -> @{ workRoot; branch; runId; at }
function Get-HarnessOwners {
    $h = @{}
    if (Test-Path -LiteralPath $script:OwnersPath) {
        try { $o = [IO.File]::ReadAllText($script:OwnersPath) | ConvertFrom-Json; foreach ($p in $o.PSObject.Properties) { $h[$p.Name] = $p.Value } } catch { }
    }
    $h
}

function Save-HarnessOwners([hashtable]$Owners) {
    $o = [ordered]@{}
    foreach ($k in @($Owners.Keys | Sort-Object)) { $o[$k] = $Owners[$k] }
    [void][IO.Directory]::CreateDirectory($script:SubmitDir)
    $tmp = "$script:OwnersPath.tmp"
    [IO.File]::WriteAllText($tmp, ($o | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmp -Destination $script:OwnersPath -Force
}

# Editor-tree files under the module's folder (and its folder .meta) that differ from HEAD = submitted, not landed.
function Get-HarnessModuleChanges([string]$Module) {
    $folder = Get-HarnessModuleFolder $Module $script:ProjectRoot
    if (-not $folder) { return @() }
    $r = Invoke-HarnessGit $script:ProjectRoot @('status', '--porcelain=v1', '-z', '--untracked-files=all', '--no-renames', '--', $folder, "$folder.meta") -Check
    @(Split-HarnessZ $r.out | ForEach-Object { $_.Substring(3) })
}

# ---- Contracts: the event types modules share (G5-4) ------------------------------------------------------------
# Parallel agents meet in the contracts folder, so submit.ps1 and land.ps1 check it before anything is copied or merged:
# a landed type never changes (new types are added: add-only per type, not per file), no type name is declared twice
# (play.events counts events by type name), and a contracts file submitted from a worktree and not landed yet is that
# worktree's until it lands (Library/Harness/submit/contracts.json, released by land.ps1). harness_lint checks that each
# module's events live in its own <Module>Events.cs. ($script:ContractOwnersPath: Set-HarnessEditorRoot.)

# project-relative path -> @{ workRoot; branch; runId; at }
function Get-HarnessContractOwners {
    $h = @{}
    if (Test-Path -LiteralPath $script:ContractOwnersPath) {
        try { $o = [IO.File]::ReadAllText($script:ContractOwnersPath) | ConvertFrom-Json; foreach ($p in $o.PSObject.Properties) { $h[$p.Name] = $p.Value } } catch { }
    }
    $h
}

function Save-HarnessContractOwners([hashtable]$Owners) {
    $o = [ordered]@{}
    foreach ($k in @($Owners.Keys | Sort-Object)) { $o[$k] = $Owners[$k] }
    [void][IO.Directory]::CreateDirectory($script:SubmitDir)
    $tmp = "$script:ContractOwnersPath.tmp"
    [IO.File]::WriteAllText($tmp, ($o | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmp -Destination $script:ContractOwnersPath -Force
}

# Files under <RelDir> in commit <Rev> of the checkout <Dir>, relative to <Dir> -> blob id (none: an empty table).
function Get-HarnessTreeFiles([string]$Dir, [string]$Rev, [string]$RelDir) {
    $h = @{}
    $r = Invoke-HarnessGit $Dir @('ls-tree', '-r', '-z', $Rev, '--', $RelDir)
    if ($r.code -ne 0) { return $h }
    foreach ($e in @(Split-HarnessZ $r.out)) { $t = $e.IndexOf("`t"); $h[$e.Substring($t + 1)] = $e.Substring(0, $t).Split(' ')[2] }
    $h
}

function Get-HarnessBlobText([string]$Dir, [string]$Id) { (Invoke-HarnessGit $Dir @('cat-file', 'blob', $Id) -Check).out }

# Top-level types that C# sources declare, read by the Editor with Roslyn (harness_contracts; nothing is compiled).
# Sources: hashtables @{ id; path } (absolute, or from the Editor project) or @{ id; text }. Returns id -> @(types {name, ns, full,
# kind, line, hash}).
function Get-HarnessDeclaredTypes([object[]]$Sources) {
    $h = @{}
    if (@($Sources).Count -eq 0) { return $h }
    $json = ConvertTo-Json -InputObject @($Sources | ForEach-Object { [ordered]@{ id = "$($_['id'])"; path = "$($_['path'])"; text = "$($_['text'])" } }) -Depth 3 -Compress
    $r = Invoke-UnityCommand -Name 'harness_contracts' -Params @{ sources = $json } -TimeoutSec 60
    if (-not $r.success) { throw "harness_contracts failed: $($r.error)" }
    foreach ($s in @($r.result.sources)) {
        if ($s.error) { throw "harness_contracts: $($s.id): $($s.error)" }
        $h["$($s.id)"] = @($s.types | Where-Object { $_ })
    }
    $h
}

# Add-only and unique names for a planned state of the contracts folder, from the sources:
#   Landed: path -> text of the landed version of each .cs the plan changes (if it has one)
#   After:  path -> @{ path } (a file) or @{ text } of each .cs the plan writes, $null for one it deletes
#   Rest:   path -> @{ path } or @{ text } of the other .cs files there will be
# changed: a landed type the plan removes or changes. conflicts: a type the plan adds whose name is declared elsewhere too.
function Test-HarnessContracts {
    param([hashtable]$Landed = @{}, [hashtable]$After = @{}, [hashtable]$Rest = @{})
    $src = @()
    foreach ($p in $Landed.Keys) { $src += @{ id = "L|$p"; text = $Landed[$p] } }
    foreach ($p in $After.Keys) { if ($null -ne $After[$p]) { $src += @{ id = "A|$p"; path = $After[$p]['path']; text = $After[$p]['text'] } } }
    foreach ($p in $Rest.Keys) { $src += @{ id = "R|$p"; path = $Rest[$p]['path']; text = $Rest[$p]['text'] } }
    $types = Get-HarnessDeclaredTypes $src
    $of = { param($id) if ($types.ContainsKey($id)) { @($types[$id]) } else { @() } }
    $changed = @(); $entries = @()
    foreach ($p in @($After.Keys | Sort-Object)) {
        $a = & $of "A|$p"
        $l = & $of "L|$p"
        foreach ($t in $l) {
            $n = @($a | Where-Object { $_.full -ceq $t.full })
            if ($n.Count -eq 0) { $changed += [ordered]@{ path = $p; type = $t.full; change = 'removed'; line = $t.line } }
            elseif ($n[0].hash -ne $t.hash) { $changed += [ordered]@{ path = $p; type = $t.full; change = 'changed'; line = $n[0].line } }
        }
        foreach ($t in $a) { $entries += [pscustomobject]@{ path = $p; t = $t; new = @($l | Where-Object { $_.full -ceq $t.full }).Count -eq 0 } }
    }
    foreach ($p in @($Rest.Keys | Sort-Object)) { foreach ($t in (& $of "R|$p")) { $entries += [pscustomobject]@{ path = $p; t = $t; new = $false } } }
    $conflicts = @()
    foreach ($g in @($entries | Group-Object -CaseSensitive { $_.t.name } | Where-Object { $_.Count -gt 1 })) {
        foreach ($e in @($g.Group | Where-Object { $_.new })) {
            $o = @($g.Group | Where-Object { -not [object]::ReferenceEquals($_, $e) })[0]
            $conflicts += [ordered]@{ type = $e.t.name; full = $e.t.full; path = $e.path; line = $e.t.line; other = $o.path; otherLine = $o.t.line; otherFull = $o.t.full }
        }
    }
    [pscustomobject]@{ changed = @($changed); conflicts = @($conflicts) }
}

# One line per finding of Test-HarnessContracts, for a refusal.
function Format-HarnessContractChanges($Changed) { (@($Changed | ForEach-Object { "$($_.type) ($($_.change), $($_.path):$($_.line))" }) -join '; ') }
function Format-HarnessContractConflicts($Conflicts) {
    (@($Conflicts | ForEach-Object { "$($_.full) ($($_.path):$($_.line)) - $($_.type) is declared in $($_.other):$($_.otherLine)$(if ($_['otherOwner']) { " ($($_['otherOwner']))" })" }) -join '; ')
}

# ---- Land transactions (tools/land.ps1, G5-5) -----------------------------------------------------------------
# land.ps1 merges an agent branch into the Editor tree's branch under the lock. The Editor tree holds uncommitted
# copies of what was submitted, which make git refuse the merge, so it stashes exactly the paths the merge touches
# (plus leftovers in the merged modules), merges, and runs the loop. A journal (Library/Harness/land/pending.json)
# lives from before the stash until the land is kept or undone; the next lock holder undoes a land that died.
# ($script:LandDir, $script:LandJournalPath: Set-HarnessEditorRoot.)

function Save-HarnessLandJournal {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Journal)
    [void][IO.Directory]::CreateDirectory($script:LandDir)
    $tmp = "$script:LandJournalPath.tmp"
    [IO.File]::WriteAllText($tmp, ($Journal | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmp -Destination $script:LandJournalPath -Force
}

function Complete-HarnessLand {
    if (Test-Path -LiteralPath $script:LandJournalPath) { Remove-Item -LiteralPath $script:LandJournalPath -Force }
}

# refs/stash is shared by every worktree of the repository: an agent's 'git stash pop' could take the land's entry.
# So land.ps1 moves its stash out of the list right away, to a private ref (the journal also records the sha).
function Get-HarnessLandStashRef([string]$RunId) { "refs/agentharness/land/$RunId" }

# The land's entry while it is still in the stash list (@{ sha; ref = 'stash@{n}' }), found by its message.
function Find-HarnessLandStash([string]$Repo, [string]$RunId) {
    $list = Invoke-HarnessGit $Repo @('stash', 'list', '--format=%H%x00%gd%x00%gs')
    if ($list.code -ne 0) { return $null }
    foreach ($line in @($list.out -split "`n")) {
        $f = $line.Split([char]0)
        if ($f.Count -ge 3 -and $f[2] -like "*agentharness-land $RunId *") { return [pscustomobject]@{ sha = $f[0]; ref = $f[1] } }
    }
    $null
}

# HEAD is what the land made: a fast-forward to the branch, or a merge commit (preHead, branch).
function Test-HarnessLandMerge([string]$Repo, [string]$Head, $Journal) {
    if ($Head -eq $Journal.branchSha) { return $true }
    $ids = @((Invoke-HarnessGit $Repo @('rev-list', '--parents', '-n', '1', $Head)).out.Trim() -split ' ')
    $ids.Count -eq 3 -and $ids[1] -eq $Journal.preHead -and $ids[2] -eq $Journal.branchSha
}

# Undo a land: abort a half-done merge, reset HEAD to the pre-land commit touching only the merged paths (other
# uncommitted work in the tree stays), drop .meta files Unity generated for what the merge added, re-apply the stash.
# Returns the steps taken. Throws when the tree is not in a state it can undo safely.
function Undo-HarnessLand {
    param([Parameter(Mandatory)]$Journal)
    $repo = $Journal.repo
    $steps = @()
    if ((Invoke-HarnessGit $repo @('rev-parse', '-q', '--verify', 'MERGE_HEAD')).code -eq 0) {
        [void](Invoke-HarnessGit $repo @('merge', '--abort') -Check)
        $steps += 'merge --abort'
    }
    $head = (Invoke-HarnessGit $repo @('rev-parse', 'HEAD') -Check).out.Trim()
    if ($head -ne $Journal.preHead) {
        if (-not (Test-HarnessLandMerge $repo $head $Journal)) {
            throw "HEAD is $head, neither the pre-land commit $($Journal.preHead) nor its merge with $($Journal.branchSha); not resetting (inspect by hand)"
        }
        # Edits to merged files made during the land (Unity rewriting a .meta, ...) go away with the merge.
        $status = Get-HarnessGitStatus $repo
        $dirty = @(@($Journal.paths) | Where-Object { $status.ContainsKey($_) })
        foreach ($p in @($dirty | Where-Object { $status[$_] -eq '??' })) { [IO.File]::Delete([IO.Path]::Combine($repo, $p)) }
        $tracked = @($dirty | Where-Object { $status[$_] -ne '??' })
        if ($tracked.Count -gt 0) {
            $ps = Join-Path $script:LandDir "$($Journal.runId).undo.paths"
            Write-HarnessPathspec $ps $tracked
            [void](Invoke-HarnessGit $repo @('restore', '--source=HEAD', '--staged', '--worktree', "--pathspec-from-file=$ps", '--pathspec-file-nul') -Check)
            Remove-Item -LiteralPath $ps -Force
        }
        [void](Invoke-HarnessGit $repo @('reset', '--keep', $Journal.preHead) -Check)
        $steps += "reset --keep $($Journal.preHead)"
    }
    # .meta files Unity generated during the land for files/folders the merge added (those are gone again).
    $status = Get-HarnessGitStatus $repo
    foreach ($p in @(@($Journal.added) + @(@($Journal.newDirs) | Sort-Object Length -Descending))) {
        if (-not $p) { continue }
        $abs = [IO.Path]::Combine($repo, $p)
        if ([IO.Directory]::Exists($abs) -and @([IO.Directory]::GetFileSystemEntries($abs)).Count -eq 0) { [IO.Directory]::Delete($abs) }
        if (-not [IO.File]::Exists($abs) -and -not [IO.Directory]::Exists($abs) -and $status.ContainsKey("$p.meta") -and $status["$p.meta"] -eq '??') {
            [IO.File]::Delete("$abs.meta")
        }
    }
    $listed = Find-HarnessLandStash $repo $Journal.runId   # still listed: the land died right after 'stash push'
    $sha = if ($Journal.stashSha) { $Journal.stashSha } elseif ($listed) { $listed.sha } else { $null }
    if ($sha) {
        # git will not re-create a stashed untracked file that exists again: remove identical copies, refuse others.
        $u = Invoke-HarnessGit $repo @('ls-tree', '-r', '-z', '--name-only', "$sha^3")
        if ($u.code -eq 0) {
            foreach ($p in @(Split-HarnessZ $u.out)) {
                $abs = [IO.Path]::Combine($repo, $p)
                if (-not [IO.File]::Exists($abs)) { continue }
                $now = (Invoke-HarnessGit $repo @('hash-object', '--', $p) -Check).out.Trim()
                $was = (Invoke-HarnessGit $repo @('rev-parse', "$sha^3:$p") -Check).out.Trim()
                if ($now -ne $was) { throw "$p exists and differs from its stashed copy; stash $sha was not applied (git stash apply $sha by hand)" }
                [IO.File]::Delete($abs)
            }
        }
        [void](Invoke-HarnessGit $repo @('stash', 'apply', $sha) -Check)
        if ($listed) { [void](Invoke-HarnessGit $repo @('stash', 'drop', $listed.ref) -Check) }
        [void](Invoke-HarnessGit $repo @('update-ref', '-d', (Get-HarnessLandStashRef $Journal.runId)))
        $steps += "stash apply $sha"
    }
    Complete-HarnessLand
    $steps
}

# Undo a land that died while holding the lock. Call only while holding the lock (Enter-HarnessLock does).
function Restore-HarnessPendingLand {
    if (-not (Test-Path -LiteralPath $script:LandJournalPath)) { return $null }
    $j = $null
    try {
        $j = [IO.File]::ReadAllText($script:LandJournalPath) | ConvertFrom-Json
        $steps = Undo-HarnessLand $j
        return [ordered]@{ runId = $j.runId; branch = $j.branch; startedAt = $j.startedAt; steps = @($steps) }
    } catch {
        # Park the journal instead of failing every later lock holder on it.
        $parked = Join-Path $script:LandDir ('failed-' + $(if ($j) { $j.runId } else { 'unknown' }) + '.json')
        try { Move-Item -LiteralPath $script:LandJournalPath -Destination $parked -Force } catch { }
        return [ordered]@{ error = "could not undo the interrupted land: $($_.Exception.Message). Journal moved to $parked" }
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

# A command's result says a domain reload was requested (settings.reloadRequested: a settings step switched the render
# pipeline). Wait until the Editor has reloaded (domainReloads past the reported value) and is idle; $true when it has
# or nothing was requested.
function Wait-HarnessReload {
    param($Result, [int]$TimeoutSec = 120)
    Set-StrictMode -Off   # any command's reply: settings is usually absent
    if ($null -eq $Result -or $null -eq $Result.settings -or -not $Result.settings.reloadRequested) { return $true }
    $before = [int]$Result.settings.domainReloads
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        $p = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 5
        if ($p.success -and [int]$p.result.domainReloads -gt $before -and -not $p.result.isCompiling -and -not $p.result.isUpdating) { return $true }
        Start-Sleep -Milliseconds 150
    }
    return $false
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
        $msg = "$($Matches.code): $($Matches.msg)"
        $line = [int]$Matches.line
        return [ordered]@{ file = $file; line = $line; msg = $msg; module = (Get-HarnessModuleOf $file).name }
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
        [System.Collections.IDictionary]$Timings = [ordered]@{},
        [string]$GoldenRoot,        # golden images (G3-4); default: goldenRoot of the config, under the work root
        [switch]$UpdateGolden,      # write this loop's shots as the golden images (a green loop only)
        [switch]$Hot                # G2-1: when only [CodeReload] method bodies changed, reload them and skip compile + build
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
    # The previous lock holder may have just triggered a domain reload, or the Editor has just started and still answers
    # 503 "Server Busy" although 'unity status' says ready: if the Editor process is alive, wait it out.
    $ep = Get-HarnessEndpoint
    if (-not $ping.success -and ($ping.unreachable -or $ping.busy) -and $ep -and (Get-Process -Id $ep.Pid -ErrorAction SilentlyContinue)) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        # A fresh Editor opened without tools/open.ps1 (no -debugCodeOptimization) also recompiles once for Debug code
        # optimization (HarnessCodeOptimization): ~30 s measured.
        while (-not $ping.success -and $sw.Elapsed.TotalSeconds -lt 120) {
            Start-Sleep -Milliseconds 250
            $ping = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 10
        }
        $Timings['editorWaitSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    }
    if (-not $ping.success) {
        $report.stage = 'editor'
        $launched = Get-HarnessLaunchedEditor
        if ($launched -and (Test-HarnessSafeMode $launched.Id)) {
            # A windowed Editor started on scripts that do not compile: -automated enters Safe Mode without asking.
            $report['safeMode'] = $true
            $report.compileErrors = @(Get-HarnessLogCompileErrors (Join-Path $script:ProjectRoot 'Logs/Editor.log'))
            $report['error'] = "The Editor is in Safe Mode: the scripts did not compile when it started (compileErrors, from its log). Fix them, then tools/quit.ps1 -Force and tools/open.ps1 (or open.ps1 -Headless, which starts on the last good assemblies)."
        } else {
            $own = if (Test-HarnessOwnEditor) { ' This worktree has its own Editor (Library/): tools/open.ps1 -Own starts it.' } else { '' }
            $report['error'] = "Editor not reachable: $($ping.error). Open it with tools/open.ps1 (waits until it answers).$own If it is open, it may be in Safe Mode (compile errors at startup): tools/open.ps1 reports it."
        }
        return $report
    }
    # Which Editor ran the loop (W7): a headless one (-batchmode) has no Game view, so nothing renders the game between
    # captures - fps is the game's update alone and there are no render counts; "screen" captures are refused.
    $report['editor'] = [ordered]@{ pid = $ping.result.pid; mode = $(if ($ping.result.editorMode) { $ping.result.editorMode } else { 'window' }); automated = [bool]$ping.result.automated }
    if (Test-HarnessOwnEditor) { $report.editor['own'] = $true }
    # Keep the Editor ticking while it is not the foreground app (otherwise compile/play stall). Idempotent.
    Invoke-UnityCommand -Name 'set_autotick' -Params @{ enable = $true } -TimeoutSec 10 | Out-Null
    if ($ping.result.isPlaying -or $ping.result.willChangePlaymode) {
        Invoke-UnityCommand -Name 'editor_stop' | Out-Null
        $sw = [Diagnostics.Stopwatch]::StartNew()
        do { Start-Sleep -Milliseconds 200; $ping = Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 5 } while ((-not $ping.success -or $ping.result.isPlaying) -and $sw.Elapsed.TotalSeconds -lt 30)
    }
    $mark = [int]$ping.result.mark
    if ($ping.result.unityVersion) { $report['unityVersion'] = $ping.result.unityVersion }

    # ---- 1a. Hot (G2-1): only [CodeReload] method bodies changed since the last compile -> reload them in place ----------
    # (the Pipeline interpreter; no compile, no domain reload) and play the same scenario. Anything else runs the full loop.
    $hotApplied = $false
    if ($Hot) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $h = Invoke-UnityCommand -Name 'harness_hot' -Params @{ mode = 'apply' } -TimeoutSec 120
        $Timings['hotSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
        if ($h.success -and $h.result.ok -and $h.result.hot) {
            $hotApplied = $true
            $report['hot'] = [ordered]@{ applied = $true; reloaded = @($h.result.applied | ForEach-Object {
                $e = [ordered]@{ file = $_.file; methods = @($_.methods); ms = $_.reloadMs }
                # G2-5: methods added since the compile, compiled by the Pipeline with the reloaded bodies that call them
                if ($_.PSObject.Properties['newMethods'] -and @($_.newMethods).Count) { $e['newMethods'] = @($_.newMethods) }
                $e }) }
            if ([int]$h.result.overridesCleared -gt 0) { $report.hot['overridesCleared'] = [int]$h.result.overridesCleared }
        } else {
            $why = if (-not $h.success) { "harness_hot failed: $($h.error)" } elseif (-not $h.result.ok) { $h.result.error } else { $h.result.reason }
            $report['hot'] = [ordered]@{ applied = $false; fallback = $why }
            if ($h.success -and @($h.result.changes).Count) { $report.hot['changes'] = @($h.result.changes) }
        }
    }

    # ---- 1. Recompile (stop immediately on errors) ----------------------------------------------------
    if (-not $NoCompile -and -not $hotApplied) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        # What is about to be compiled: the baseline of a later hot loop (kept only when the compile succeeds). Also clears
        # the overrides of earlier hot loops, so this loop runs the compiled code. Not with Domain Reload on entering play
        # mode (no hot loop there: the reload would drop reloaded methods).
        $snapshot = -not $ping.result.domainReloadOnPlay
        $prep = if ($snapshot) { Invoke-UnityCommand -Name 'harness_hot' -Params @{ mode = 'prepare' } -TimeoutSec 60 }
        $rc = Invoke-HarnessRecompile -TimeoutSec $TimeoutSec
        $commit = if ($snapshot -and $rc.ok) { Invoke-UnityCommand -Name 'harness_hot' -Params @{ mode = 'commit' } -TimeoutSec 60 }
        $Timings['compileSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
        # The snapshot's own time (the Editor's; the commit call also waits out the Editor's work after a domain reload).
        if ($prep -and $prep.success -and $prep.result.ok) { $Timings['snapshotSec'] = [math]::Round(([double]$prep.result.ms + $(if ($commit -and $commit.success -and $commit.result.ok) { [double]$commit.result.ms } else { 0 })) / 1000, 3) }
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
    # A hot loop skips them: its only changes are method bodies, which no builder, shader or lint rule reads.
    $lintIssues = @()
    $shaderErrors = @()
    if ($hotApplied) {
        $report['build'] = [ordered]@{ ok = $true; skipped = $true; note = 'hot loop: the scene of the last full loop (builders unchanged since)' }
        $report['lint'] = $lintIssues
    } else {
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
            $b = [ordered]@{ ok = $build.result.ok; fingerprint = $build.result.fingerprint; gameObjects = $build.result.gameObjects; durationMs = $build.result.durationMs; steps = @($build.result.steps | ForEach-Object { [ordered]@{ type = $_.type; module = $_.module; ms = $_.ms; error = $_.error; file = $_.file; line = $_.line } }); warnings = @($build.result.warnings) }
            # Render settings from code (ISettingsStep): their assets, what this build rewrote, the active pipeline.
            if ($build.result.settings) { $b['settings'] = $build.result.settings }
            if ($build.result.phases) { $b['phases'] = $build.result.phases }   # where the build's time went (ms)
            # No build steps (an attached project): nothing was built; the fingerprint is the play scene's.
            if ($build.result.skipped) { $b['skipped'] = $true; $b['scene'] = $build.result.scene; $b['fingerprintOf'] = $build.result.fingerprintOf }
            $b
        } else { [ordered]@{ ok = $false; error = $build.error } }
        $report['lint'] = $lintIssues
        if (-not $build.success -or -not $build.result.ok) {
            $report.stage = 'build'
            $report['error'] = if ($build.success) { $build.result.error } else { $build.error }
            $con = Invoke-UnityCommand -Name 'harness_console' -Params @{ since = $mark } -TimeoutSec 10
            if ($con.success) { $report.runtimeErrors = @($con.result.runtimeErrors | ForEach-Object { [ordered]@{ type = $_.type; msg = $_.message; file = $_.file; line = $_.line; module = $_.module; count = $_.count } }) }
            return $report
        }

        # A settings step switched the render pipeline (e.g. its assets were deleted): the build asked for a domain reload,
        # which must be over before the play (see SettingsContext.Run).
        if ($build.result.settings -and $build.result.settings.reloadRequested) {
            $sw = [Diagnostics.Stopwatch]::StartNew()
            $reloaded = Wait-HarnessReload $build.result
            $Timings['reloadSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
            if (-not $reloaded) {
                $report.stage = 'editor'
                $report['error'] = "the domain reload requested after the render pipeline switch ($($build.result.settings.switched)) did not finish"
                return $report
            }
        }
    }

    # ---- 3. Play scenario (or edit-mode capture) ------------------------------------------------------
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $playResult = $null
    $state = $null
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
        # A scenario that waits for scenes/targets may run longer than the default: the Editor's own timeout (+ margin) counts.
        $waitSec = $TimeoutSec
        if ($start.result.PSObject.Properties.Name -contains 'timeoutSec') { $waitSec = [math]::Max($TimeoutSec, [double]$start.result.timeoutSec + 30) }
        while ($sw.Elapsed.TotalSeconds -lt $waitSec) {
            Start-Sleep -Milliseconds 100   # harness_play_status is served off the main thread: polling does not slow the game
            $st = Invoke-UnityCommand -Name 'harness_play_status' -TimeoutSec 5
            if ($st.success -and $st.result.id -eq $start.result.id -and $st.result.state -in @('done', 'failed')) { $state = $st.result; break }
        }
        if ($null -eq $state) {
            Invoke-UnityCommand -Name 'editor_stop' | Out-Null
            $report.stage = 'play'
            $report['error'] = "play did not finish within $([math]::Round($waitSec)) s"
            return $report
        }
        $playResult = $state.result
        $shotObjs = @(); if ($playResult) { $shotObjs = @($playResult.shots) }
        if ($state.state -eq 'failed') { $report['playError'] = $state.error }
        # Entering play mode (a domain reload when the project keeps Domain Reload on): request -> scenario runner started.
        try { $Timings['playEnterSec'] = [math]::Round(([DateTime]::Parse($state.startedAt) - [DateTime]::Parse($state.requestedAt)).TotalSeconds, 2) } catch { }
        if ($start.result.scene) { $report['scene'] = $start.result.scene }
    }
    $Timings['playSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)

    # ---- 4. Console + stats ---------------------------------------------------------------------------
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # Errors after the scenario finished were logged while play mode exited (teardown): reported, not failing.
    $conParams = @{ since = $mark }
    if ($state -and $state.PSObject.Properties.Name -contains 'finishedSeq' -and [int]$state.finishedSeq -gt 0) { $conParams['until'] = [int]$state.finishedSeq }
    $con = Invoke-UnityCommand -Name 'harness_console' -Params $conParams -TimeoutSec 10
    $stats = if ($NoPlay) { $null } else { Invoke-UnityCommand -Name 'harness_stats' -TimeoutSec 10 }
    # G2-5: what the reloaded methods cost in the interpreter during the play (their calls and time)
    $calls = if ($hotApplied -and -not $NoPlay -and @($report.hot.reloaded).Count) { Invoke-UnityCommand -Name 'harness_hot' -Params @{ mode = 'calls' } -TimeoutSec 10 }
    $Timings['collectSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)

    $runtimeErrors = @()
    $warningCount = 0
    if ($con.success) {
        $runtimeErrors = @($con.result.runtimeErrors | ForEach-Object { [ordered]@{ type = $_.type; msg = $_.message; file = $_.file; line = $_.line; module = $_.module; count = $_.count; stack = $_.stack } })
        $warningCount = [int]$con.result.counts.warning
        # Errors from inside the Editor/packages (no Assets/ frame). Visible, but they do not fail the loop.
        $report['editorErrors'] = @($con.result.editorErrors | ForEach-Object { [ordered]@{ type = $_.type; msg = ($_.message -split "`n")[0]; count = $_.count; stack = $_.stack } })
        # knownErrors (config regexes: the project's known noise) and teardownErrors (after the scenario): visible, not failing.
        $has = @($con.result.PSObject.Properties.Name)
        if ($has -contains 'knownErrors' -and @($con.result.knownErrors).Count) { $report['knownErrors'] = @($con.result.knownErrors | ForEach-Object { [ordered]@{ type = $_.type; msg = ($_.message -split "`n")[0]; file = $_.file; line = $_.line; count = $_.count } }) }
        if ($has -contains 'teardownErrors' -and @($con.result.teardownErrors).Count) { $report['teardownErrors'] = @($con.result.teardownErrors | ForEach-Object { [ordered]@{ type = $_.type; msg = ($_.message -split "`n")[0]; file = $_.file; line = $_.line; module = $_.module; count = $_.count } }) }
        if ($has -contains 'knownErrorsConfigError' -and $con.result.knownErrorsConfigError) { $report['configError'] = $con.result.knownErrorsConfigError }
    }
    $report.runtimeErrors = $runtimeErrors
    # A reloaded method that throws is caught by the Pipeline, logged without the throwing line and its original body runs
    # instead. Run the full loop: compiled, the error comes with its exact line (and the method runs once per call).
    $hotError = @($runtimeErrors | Where-Object { "$($_.msg)" -like 'CodeReload:*' } | Select-Object -First 1)
    if ($hotApplied -and $hotError.Count) {
        $Timings['hotPlaySec'] = $Timings['playSec']
        $full = Invoke-HarnessLoop -Scenario $Scenario -OutDir $OutDir -NoPlay:$NoPlay -TimeoutSec $TimeoutSec -Timings $Timings -GoldenRoot $GoldenRoot -UpdateGolden:$UpdateGolden
        $full['hot'] = [ordered]@{ applied = $false; reloaded = $report.hot.reloaded
            fallback = "a reloaded method failed while playing ($(($hotError[0].msg -split "`n")[0])): the full loop below has its exact line" }
        return $full
    }
    $report['warningCount'] = $warningCount
    $report.shots = @($shotObjs | Where-Object { $_.path } | ForEach-Object { $_.path })
    $statsByPath = @{}
    $report['shotStats'] = @($shotObjs | ForEach-Object {
        # Fields a result of an older package version does not have are left out.
        $has = @($_.PSObject.Properties.Name)
        $s = [ordered]@{ name = $_.name; preset = $_.preset; t = $_.t; width = $_.width; height = $_.height; meanLuma = [math]::Round($_.meanLuma, 1); stdLuma = [math]::Round($_.stdLuma, 1); blank = $_.blank; dark = [bool]$_.dark }
        if ($has -contains 'magenta') { $s['magenta'] = [bool]$_.magenta; $s['magentaRatio'] = [math]::Round([double]$_.magentaRatio, 4) }
        if ($has -contains 'cameras' -and @($_.cameras).Count) { $s['cameras'] = @($_.cameras | Where-Object { $_ }) }
        if ($has -contains 'ui') { $s['ui'] = @($_.ui | Where-Object { $_ }) }
        if ($has -contains 'uiError' -and $_.uiError) { $s['uiError'] = $_.uiError }
        if ($has -contains 'shadersCompiling' -and $_.shadersCompiling) { $s['shadersCompiling'] = $true }
        if ($has -contains 'frames' -and [int]$_.frames -gt 1) {
            $s['frames'] = [int]$_.frames; $s['every'] = [int]$_.every; $s['sheet'] = $_.sheet
            $s['times'] = @($_.times | ForEach-Object { [math]::Round([double]$_, 3) }); $s['motion'] = @($_.motion | ForEach-Object { [math]::Round([double]$_, 2) })
        }
        $s['error'] = $_.error
        $s['hint'] = $(if ($has -contains 'hint') { $_.hint } else { $null })
        if ($_.path) { $statsByPath[[string]$_.path] = $s }
        $s
    })
    if ($stats -and $stats.success -and $stats.result.ok) {
        $f = $stats.result.fps
        $report.fps = [ordered]@{ avg = [math]::Round($f.avg, 1); min = [math]::Round($f.min, 1); p95ms = [math]::Round($f.p95ms, 2); p99ms = [math]::Round($f.p99ms, 2); hitches = $f.hitches; cpuMainAvgMs = [math]::Round($f.cpuMainAvgMs, 2); samples = $f.samples; editorFocused = $stats.result.editorFocused }
        if ($report.editor.mode -eq 'headless') {
            $report.fps['note'] = 'headless Editor: nothing renders the game between captures, so these frames are its update alone (not comparable with a windowed Editor)'
            $report['render'] = $null
        } else {
            $r = $stats.result.render
            # batches -1: this Unity has no Render "Batches Count" (6.6)
            $report['render'] = [ordered]@{ batches = $(if ([double]$r.batches -lt 0) { $null } else { [math]::Round($r.batches, 1) }); setPassCalls = [math]::Round($r.setPassCalls, 1); drawCalls = [math]::Round($r.drawCalls, 1); triangles = [math]::Round($r.triangles); vertices = [math]::Round($r.vertices) }
        }
    }
    # G2-5: the reloaded methods ran in the interpreter, slower than compiled. Their calls and time during the play, and their
    # share of a frame at this loop's fps: what separates this fps from a full loop's.
    if ($calls) {
        $it = [ordered]@{}
        if ($calls.success -and $calls.result.ok) {
            $it['methods'] = @($calls.result.methods | ForEach-Object {
                [ordered]@{ method = $_.method; calls = [int64]$_.calls; frames = [int]$_.frames; ms = [math]::Round([double]$_.ms, 2)
                    msPerCall = $(if ([int64]$_.calls -gt 0) { [math]::Round([double]$_.ms / [double]$_.calls, 4) } else { 0 }) } })
            # Each method's time over the frames it ran in, summed (a Tick runs once a frame).
            $perFrame = 0.0
            foreach ($m in @($calls.result.methods)) { if ([int]$m.frames -gt 0) { $perFrame += [double]$m.ms / [double]$m.frames } }
            $it['msPerFrame'] = [math]::Round($perFrame, 3)
            if ($report.fps -and [double]$report.fps.avg -gt 0) { $it['frameShare'] = [math]::Round($perFrame * [double]$report.fps.avg / 1000.0, 4) }
            if ($calls.result.error) { $it['error'] = $calls.result.error }
        } else { $it['error'] = "harness_hot calls failed: $(if ($calls.success) { $calls.result.error } else { $calls.error })" }
        $report.hot['interpreted'] = $it
    }
    if ($playResult) {
        $report['play'] = [ordered]@{ success = $playResult.success; error = $playResult.error; probeReady = $playResult.probeReady; readySec = [math]::Round($playResult.readySec, 2); wallSec = [math]::Round($playResult.wallSec, 2); gameSec = [math]::Round($playResult.gameSec, 2); frames = $playResult.frames; modules = @($playResult.modules); failedModules = @($playResult.failedModules); inputEventsApplied = $playResult.inputEventsApplied; events = @($playResult.events | ForEach-Object { [ordered]@{ name = $_.name; count = $_.count } }) }
        # Fields a result.json of an older package version does not have are left out.
        $has = @($playResult.PSObject.Properties.Name)
        if ($has -contains 'inputBackends') { $report.play['inputBackends'] = @($playResult.inputBackends) }
        if ($has -contains 'inputHooks' -and @($playResult.inputHooks).Count) { $report.play['inputHooks'] = @($playResult.inputHooks) }
        if ($has -contains 'isolatedDevices' -and @($playResult.isolatedDevices).Count) { $report.play['isolatedDevices'] = @($playResult.isolatedDevices | ForEach-Object { $d = [ordered]@{ name = $_.name; presses = $_.presses }; if ($_.PSObject.Properties['background'] -and $_.background) { $d['background'] = $true }; $d }) }
        if ($has -contains 'activeScene') { $report.play['activeScene'] = $playResult.activeScene }
        if ($has -contains 'uiFocusError' -and $playResult.uiFocusError) { $report.play['uiFocusError'] = $playResult.uiFocusError }
        if ($has -contains 'uiClock' -and $playResult.uiClock) { $report.play['uiClock'] = [ordered]@{ mode = $playResult.uiClock.mode; panels = $playResult.uiClock.panels; scope = $playResult.uiClock.scope; error = $playResult.uiClock.error } }
        if ($has -contains 'scenes') { $report.play['scenes'] = @($playResult.scenes | ForEach-Object { [ordered]@{ name = $_.name; mode = $_.mode; t = [math]::Round($_.t, 3); wallSec = [math]::Round($_.wallSec, 2) } }) }
        if ($has -contains 'waits' -and @($playResult.waits).Count) { $report.play['waits'] = @($playResult.waits | ForEach-Object { [ordered]@{ type = $_.type; target = $_.target; t = [math]::Round($_.t, 3); waitedSec = [math]::Round($_.waitedSec, 2); frames = $_.frames } }) }
        if ($has -contains 'clicks' -and @($playResult.clicks).Count) { $report.play['clicks'] = @($playResult.clicks | ForEach-Object { [ordered]@{ target = $_.target; t = [math]::Round($_.t, 3); x = [math]::Round($_.x, 1); y = [math]::Round($_.y, 1); via = $_.via } }) }
    }

    $blank = @($shotObjs | Where-Object { $_.blank -or $_.error }).Count
    $playOk = $NoPlay -or ($playResult -and $playResult.success)
    $lintCount = @($lintIssues).Count
    $report.ok = ($runtimeErrors.Count -eq 0) -and $playOk -and ($blank -eq 0) -and ($shotObjs.Count -gt 0) -and ($lintCount -eq 0) -and ($shaderErrors.Count -eq 0)
    $report.stage = if ($report.ok) { 'done' } elseif ($shaderErrors.Count -gt 0) { 'shader' } elseif (-not $playOk) { 'play' } elseif ($runtimeErrors.Count -gt 0) { 'runtime' } elseif ($lintCount -gt 0) { 'lint' } else { 'shots' }

    # ---- 5. Golden images (G3-4): how the shots differ from the golden ones - reported, never failing ---------------
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $g = Invoke-HarnessGolden -Shots $shotObjs -StatsByPath $statsByPath -Key $(if ($NoPlay) { 'capture' } elseif ($playResult -and $playResult.scenario) { "$($playResult.scenario)" } else { 'default' }) `
        -OutDir $OutDir -GoldenRoot $GoldenRoot -Update:$UpdateGolden -LoopOk:$report.ok
    if ($g) { $report['golden'] = $g; $Timings['goldenSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2) }
    $report
}

# Golden images of a loop's shots (harness_golden): per shot golden {status, meanDiff, changedRatio, ssim, rect, diff} into its
# shotStats entry, and the summary. Nothing when there is no golden root yet (and no -Update). "screen" shots (Game view size)
# and captures with "golden": false are left out.
function Invoke-HarnessGolden {
    param($Shots, [hashtable]$StatsByPath, [string]$Key, [string]$OutDir, [string]$GoldenRoot, [switch]$Update, [bool]$LoopOk)
    $root = if (-not $GoldenRoot) { Join-Path $script:WorkRoot (Get-HarnessConfig).goldenRoot } elseif ([IO.Path]::IsPathRooted($GoldenRoot)) { $GoldenRoot } else { Join-Path $script:WorkRoot $GoldenRoot }
    $root = [IO.Path]::GetFullPath($root).Replace('\', '/')
    if (-not $Update -and -not (Test-Path -LiteralPath $root)) { return $null }
    $g = [ordered]@{ root = $root; key = $Key }
    if ($Update -and -not $LoopOk) { $g['error'] = 'not updated: the loop is not green'; return $g }
    $items = @(@($Shots) | Where-Object { $_.path -and -not $_.error -and $_.preset -ne 'screen' -and -not ($_.PSObject.Properties.Name -contains 'golden' -and $_.golden -eq $false) } | ForEach-Object {
        $i = [ordered]@{ path = [string]$_.path; name = [string]$_.name }
        if ($_.PSObject.Properties.Name -contains 'ignore' -and @($_.ignore).Count) { $i['ignore'] = @($_.ignore | ForEach-Object { [ordered]@{ x = $_.x; y = $_.y; w = $_.w; h = $_.h } }) }
        $i
    })
    if ($items.Count -eq 0) { $g['note'] = 'no shots to compare'; return $g }
    $res = Invoke-UnityCommand -Name 'harness_golden' -TimeoutSec 120 -Params @{ shots = (ConvertTo-Json -InputObject $items -Depth 6 -Compress); golden = $root; key = $Key; out = $OutDir; update = [bool]$Update }
    if (-not $res.success -or -not $res.result.ok) { $g['error'] = $(if ($res.success) { "$($res.result.error)$(@($res.result.results | Where-Object { $_.error } | ForEach-Object { " $($_.name): $($_.error)" }) -join ';')" } else { $res.error }); return $g }
    $r = $res.result
    $g['version'] = $r.version
    if ($Update) {
        $g['dir'] = $r.dir
        $g['updated'] = @($r.results | ForEach-Object { Split-Path -Leaf $_.golden })
        if (@($r.removed).Count) { $g['removed'] = @($r.removed) }
        return $g
    }
    if ($r.from -and $r.from -ne $r.version) { $g['from'] = $r.from }   # goldens of another patch of this major.minor
    if ($r.dir) { $g['dir'] = $r.dir }
    $g['same'] = [int]$r.same; $g['changed'] = [int]$r.changed; $g['missing'] = [int]$r.missing
    if ([int]$r.errors) { $g['errors'] = [int]$r.errors }
    foreach ($x in @($r.results)) {
        $s = $StatsByPath[[string]$x.path]
        if (-not $s) { continue }
        $d = [ordered]@{ status = $x.status }
        if ($x.status -in @('same', 'changed')) { $d['meanDiff'] = [math]::Round([double]$x.meanDiff, 3); $d['changedRatio'] = [math]::Round([double]$x.changedRatio, 5); $d['ssim'] = [math]::Round([double]$x.ssim, 4); $d['maxDiff'] = [int]$x.maxDiff }
        if ($x.rect) { $d['rect'] = @($x.rect) }
        if ($x.diff) { $d['diff'] = $x.diff }
        if ($x.error) { $d['error'] = $x.error }
        $s['golden'] = $d
    }
    if ($g.missing -gt 0) { $g['hint'] = "no golden image for $($g.missing) shot(s) in $root/$($r.version)/$Key - when the shots look right, tools/loop.ps1 -UpdateGolden makes them the golden images" }
    $g
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
    Initialize-HarnessOut $OutDir
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    $json = $Report | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText((Join-Path $OutDir 'report.json'), $json, (New-Object Text.UTF8Encoding($false)))
    $json
}

Export-ModuleMember -Function Get-HarnessProjectRoot, Get-HarnessWorkRoot, Test-HarnessWorktree, Get-HarnessEndpoint, Invoke-UnityCommand,
    Get-HarnessIntegrationRoot, Set-HarnessEditorRoot, Use-HarnessIntegrationRoot, Test-HarnessOwnEditor, Get-HarnessEditorArguments,
    Get-HarnessLogCompileErrors, Test-HarnessSafeMode, Copy-HarnessLibrary,
    Get-HarnessConfig, Get-HarnessModuleOf, Get-HarnessModuleFolder, Initialize-HarnessOut, Find-HarnessProjectAbove,
    Wait-UnityReachable, Get-HarnessEditorProcess, Get-HarnessLaunchedEditor, Save-HarnessLaunchedEditor, Get-HarnessWindowTitles, Test-HarnessProjectOpen,
    Get-HarnessProjectVersion, Get-HarnessInstalledEditors,
    Find-HarnessEditorExe, Read-HarnessLogTail, Invoke-HarnessProcess, Get-HarnessPowerShell, ConvertTo-HarnessArg,
    Invoke-HarnessRecompile, Get-HarnessCompileState, Wait-HarnessIdle, Wait-HarnessReload, Enter-HarnessLock, Exit-HarnessLock, Test-HarnessReadOnly,
    Get-HarnessLastRecovery, Get-HarnessLastLandRecovery, Add-HarnessRecovery, Start-HarnessSubmit, Complete-HarnessSubmit,
    Undo-HarnessSubmit, Invoke-HarnessLoop, Save-HarnessReport, Invoke-HarnessGit, Split-HarnessZ, Get-HarnessGitStatus,
    Test-HarnessSamePath, Get-HarnessRepoRoot, Write-HarnessPathspec, Get-HarnessContentIds, Get-HarnessOwners, Save-HarnessOwners,
    Get-HarnessModuleChanges, Save-HarnessLandJournal, Complete-HarnessLand, Get-HarnessLandStashRef, Find-HarnessLandStash,
    Undo-HarnessLand, Get-HarnessContractOwners, Save-HarnessContractOwners, Get-HarnessTreeFiles, Get-HarnessBlobText,
    Get-HarnessDeclaredTypes, Test-HarnessContracts, Format-HarnessContractChanges, Format-HarnessContractConflicts
