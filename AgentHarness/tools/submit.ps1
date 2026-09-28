<#
.SYNOPSIS
  Submit module folders from an agent worktree to the shared Editor, as a transaction (G5-2).

  1. compile-check (csc, no Editor, no lock) of the modules and the in-project assemblies they reference, from the
     worktree's sources. Errors -> stage=compile, submit.phase=check, nothing is copied.
  2. Take the Editor lock. Mirror Assets/Game/<Module>/ (+ its folder .meta) of the worktree into the Editor tree and
     add new Assets/Game/Contracts/ files (add-only: a changed existing contract is refused). Every file it
     overwrites or deletes is backed up first, under a journal (Library/Harness/submit/pending.json).
  3. The normal loop (recompile, build, play, console) on the Editor tree.
  4. Green -> keep, and copy the .meta files Unity generated back into the worktree (commit them).
     Otherwise -> restore the backup and recompile, so the Editor tree is back where it was. -KeepOnFail keeps the
     files for non-compile failures; a compile failure is always reverted. If the submit dies half-way, the next lock
     holder (loop.ps1 / uc.ps1 / submit.ps1) rolls it back from the journal.
  So one agent's broken code never stays in the Editor tree and never blocks another agent's loop.

.EXAMPLE
  git worktree add ..\wt-foo -b agent/foo            # once per agent, from the checkout the Editor has open
  # work only in ..\wt-foo\AgentHarness\Assets\Game\Foo\, then from ..\wt-foo\AgentHarness:
  powershell -ExecutionPolicy Bypass -File tools/submit.ps1 -Module Foo
  -> the loop's report.json + "submit": {...} in HarnessOut/submit/ of the worktree. Exit 0 = green and kept.
#>
param(
    [Parameter(Mandatory)][string[]]$Module,
    [string]$Scenario = 'tools/scenarios/default.json',
    [string]$Out = 'HarnessOut/submit',
    [switch]$NoPlay,
    [switch]$KeepOnFail,
    [switch]$SkipCheck,   # skip step 1 (the transaction still protects the Editor tree; only for testing that)
    [int]$TimeoutSec = 180
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
$clock = [Diagnostics.Stopwatch]::StartNew()
$work = Get-HarnessWorkRoot
$root = Get-HarnessProjectRoot
# `powershell -File ... -Module A,B` passes "A,B" as one string.
$Module = @($Module | ForEach-Object { "$_" -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$outAbs = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $work $Out }
# The Editor reads the scenario: pass this checkout's file by absolute path (inline JSON as is).
$scenarioArg = $Scenario
if (-not $Scenario.TrimStart().StartsWith('{') -and -not [IO.Path]::IsPathRooted($Scenario)) {
    $local = Join-Path $work $Scenario
    if (Test-Path -LiteralPath $local) { $scenarioArg = $local.Replace('\', '/') }
}
$runId = (Get-Date).ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$timings = [ordered]@{}
$sub = [ordered]@{ runId = $runId; modules = $Module; workRoot = $work.Replace('\', '/'); editorRoot = $root.Replace('\', '/')
    phase = 'check'; synced = $false; kept = $false; reverted = $false; written = @(); deleted = @(); contractsAdded = @(); metaWrittenBack = @() }

function New-FailReport([string]$stage, [string]$message, [object[]]$compileErrors = @()) {
    [ordered]@{ ok = $false; stage = $stage; compileErrors = @($compileErrors); runtimeErrors = @(); fps = $null; shots = @(); durationSec = 0
        report = (Join-Path $outAbs 'report.json').Replace('\', '/'); error = $message }
}

function Complete-Submit([System.Collections.IDictionary]$report) {
    $report['submit'] = $sub
    Save-HarnessReport $report $outAbs $clock $timings
    exit $(if ($report.ok) { 0 } else { 1 })
}

# Project-relative files (forward slashes) under <base>/<rel>. Hidden files are ignored, as Unity does.
function Get-RelFiles([string]$base, [string]$rel) {
    $dir = [IO.Path]::Combine($base, $rel)
    if (-not [IO.Directory]::Exists($dir)) { return @() }
    @(Get-ChildItem -LiteralPath $dir -Recurse -File | ForEach-Object { $_.FullName.Substring($base.Length + 1).Replace('\', '/') })
}

# <rel> itself plus every folder below it, project-relative.
function Get-RelDirs([string]$base, [string]$rel) {
    $dir = [IO.Path]::Combine($base, $rel)
    if (-not [IO.Directory]::Exists($dir)) { return @() }
    @($rel) + @(Get-ChildItem -LiteralPath $dir -Recurse -Directory | ForEach-Object { $_.FullName.Substring($base.Length + 1).Replace('\', '/') })
}

function Test-SameFile([string]$a, [string]$b) {
    $fa = New-Object IO.FileInfo $a; $fb = New-Object IO.FileInfo $b
    if (-not $fb.Exists -or $fa.Length -ne $fb.Length) { return $false }
    [Convert]::ToBase64String([IO.File]::ReadAllBytes($a)) -eq [Convert]::ToBase64String([IO.File]::ReadAllBytes($b))
}

# Folders that do not exist in the Editor tree yet, topmost only (removing one on revert removes what is below it).
function Get-CreatedDirs([string[]]$dirs) {
    @($dirs | Where-Object { -not [IO.Directory]::Exists([IO.Path]::Combine($root, $_)) -and [IO.Directory]::Exists([IO.Path]::Combine($root, $_.Substring(0, $_.LastIndexOf('/')))) })
}

# ---- 0. Arguments ------------------------------------------------------------------------------------
if (-not (Test-HarnessWorktree)) {
    Complete-Submit (New-FailReport 'submit' "submit.ps1 runs from an agent worktree (a checkout without Library/, e.g. 'git worktree add'). This is the Editor tree itself ($root): edit here and run tools/loop.ps1.")
}
if ($Module.Count -eq 0) { Complete-Submit (New-FailReport 'submit' '-Module is required') }
foreach ($m in $Module) {
    if ($m -notmatch '^[A-Za-z0-9_.-]+$' -or $m -eq 'Contracts') {
        Complete-Submit (New-FailReport 'submit' "invalid module '$m': a folder name under Assets/Game (new Contracts files are submitted automatically with a module)")
    }
    if (-not [IO.Directory]::Exists((Join-Path $work "Assets\Game\$m"))) { Complete-Submit (New-FailReport 'submit' "no folder Assets/Game/$m in this worktree ($work)") }
}

# ---- 1. Compile-check in the worktree (no Editor, no lock) ----------------------------------------------
if (-not $SkipCheck) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $ccText = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'compile-check.ps1') -Module ($Module -join ',') -Backend csc | Out-String
    $timings['checkSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    $cc = $null
    try { $cc = $ccText | ConvertFrom-Json } catch { }
    if ($null -eq $cc) { Complete-Submit (New-FailReport 'compile' "compile-check did not return JSON: $ccText") }
    $sub['check'] = [ordered]@{ ok = $cc.ok; targets = @($cc.targets | ForEach-Object { "$($_.assembly): $(if ($_.ok) { 'ok' } else { 'FAILED' })" }) }
    if (-not $cc.ok) {
        $errs = @($cc.compileErrors | ForEach-Object { [ordered]@{ file = $_.file; line = $_.line; msg = $_.msg; module = $_.module } })
        Complete-Submit (New-FailReport 'compile' 'compile-check failed in the worktree; nothing was copied to the Editor tree' $errs)
    }
}

# ---- 2-4. Under the Editor lock: sync, loop, keep or revert ----------------------------------------------
$timings['lockWaitSec'] = Enter-HarnessLock
$report = $null
$journal = $null
try {
    $recovered = Get-HarnessLastRecovery
    if ($recovered) { $sub['recoveredSubmit'] = $recovered }

    # Plan against the Editor tree as it is now (other submits may have landed while we waited).
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $writes = New-Object System.Collections.ArrayList    # @{ rel; src }
    $deletes = New-Object System.Collections.ArrayList
    $created = New-Object System.Collections.ArrayList
    $staleDirs = New-Object System.Collections.ArrayList # Editor-tree folders the worktree no longer has
    foreach ($m in $Module) {
        $rel = "Assets/Game/$m"
        $srcFiles = @(Get-RelFiles $work $rel)
        if ([IO.File]::Exists([IO.Path]::Combine($work, "$rel.meta"))) { $srcFiles += "$rel.meta" }
        $srcDirs = @(Get-RelDirs $work $rel)
        $srcSet = @{}; foreach ($p in @($srcFiles) + @($srcDirs)) { $srcSet[$p] = $true }
        foreach ($f in $srcFiles) {
            $src = [IO.Path]::Combine($work, $f)
            if (-not (Test-SameFile $src ([IO.Path]::Combine($root, $f)))) { [void]$writes.Add(@{ rel = $f; src = $src }) }
        }
        $dstFiles = @(Get-RelFiles $root $rel)
        if ([IO.File]::Exists([IO.Path]::Combine($root, "$rel.meta"))) { $dstFiles += "$rel.meta" }
        foreach ($f in $dstFiles) {
            if ($srcSet.ContainsKey($f)) { continue }
            # A .meta that Unity generated here for a file the worktree has: keep it (copied back to the worktree when kept).
            if ($f.EndsWith('.meta') -and $srcSet.ContainsKey($f.Substring(0, $f.Length - 5))) { continue }
            [void]$deletes.Add($f)
        }
        foreach ($d in @(Get-CreatedDirs $srcDirs)) { [void]$created.Add($d) }
        foreach ($d in @(Get-RelDirs $root $rel)) { if (-not $srcSet.ContainsKey($d)) { [void]$staleDirs.Add($d) } }
    }
    # Contracts are add-only: new files are copied, a changed existing file is refused.
    $conflicts = @()
    foreach ($f in @(Get-RelFiles $work 'Assets/Game/Contracts')) {
        $src = [IO.Path]::Combine($work, $f); $dst = [IO.Path]::Combine($root, $f)
        if (-not [IO.File]::Exists($dst)) { [void]$writes.Add(@{ rel = $f; src = $src }); $sub.contractsAdded += $f }
        elseif (-not (Test-SameFile $src $dst)) { $conflicts += $f }
    }
    foreach ($d in @(Get-CreatedDirs @(Get-RelDirs $work 'Assets/Game/Contracts'))) { [void]$created.Add($d) }

    if ($conflicts.Count -gt 0) {
        $sub.contractsAdded = @()
        $report = New-FailReport 'submit' "Assets/Game/Contracts is add-only, but these files differ from the Editor tree: $($conflicts -join ', '). Add a new file instead of changing one, or update the worktree (git merge master)."
    } else {
        if ($writes.Count + $deletes.Count -gt 0) {
            $journal = Start-HarnessSubmit -RunId $runId -WorkRoot $work -Modules $Module -Writes $writes.ToArray() -Deletes @($deletes) -CreatedDirs @($created)
            foreach ($w in $writes) {
                $dst = [IO.Path]::Combine($root, $w.rel)
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dst))
                [IO.File]::Copy($w.src, $dst, $true)
                [IO.File]::SetLastWriteTimeUtc($dst, [DateTime]::UtcNow)   # never older than what Unity imported last
            }
            foreach ($f in $deletes) { [IO.File]::Delete([IO.Path]::Combine($root, $f)) }
            foreach ($d in @($staleDirs | Sort-Object Length -Descending)) {
                $abs = [IO.Path]::Combine($root, $d)
                if ([IO.Directory]::Exists($abs) -and @([IO.Directory]::GetFileSystemEntries($abs)).Count -eq 0) { [IO.Directory]::Delete($abs) }
            }
            $sub.synced = $true
            $sub.written = @($writes | ForEach-Object { $_.rel })
            $sub.deleted = @($deletes)
        }
        $timings['syncSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)

        $sub.phase = 'loop'
        $report = Invoke-HarnessLoop -Scenario $scenarioArg -OutDir $outAbs -NoPlay:$NoPlay -TimeoutSec $TimeoutSec -Timings $timings
        $mods = @()
        foreach ($e in @($report.compileErrors) + @($report.runtimeErrors) + @($report['lint'])) {
            if ($e -is [System.Collections.IDictionary] -and $e.module) { $mods += $e.module }
        }
        if ($report['build'] -and $report['build'].steps) { foreach ($s in @($report['build'].steps)) { if ($s.error -and $s.module) { $mods += $s.module } } }
        $sub['errorModules'] = @($mods | Sort-Object -Unique)

        $sub.phase = 'done'
        if ($null -eq $journal) {
            $sub['note'] = 'no changes: the Editor tree already has these files'
            $sub.kept = [bool]$report.ok
        } elseif ($report.ok -or ($KeepOnFail -and $report.stage -notin @('compile', 'editor'))) {
            Complete-HarnessSubmit $journal
            $journal = $null
            $sub.kept = $true
            # Copy the .meta files Unity generated for new files/folders back, so the agent commits stable GUIDs.
            $back = @()
            $cands = @($sub.contractsAdded | ForEach-Object { "$_.meta" }) + @($created | ForEach-Object { "$_.meta" })
            foreach ($m in $Module) { $cands += @(Get-RelFiles $root "Assets/Game/$m" | Where-Object { $_.EndsWith('.meta') }) + @("Assets/Game/$m.meta") }
            foreach ($f in @($cands | Sort-Object -Unique)) {
                $src = [IO.Path]::Combine($root, $f); $dst = [IO.Path]::Combine($work, $f)
                if ([IO.File]::Exists($src) -and -not [IO.File]::Exists($dst)) {
                    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dst))
                    [IO.File]::Copy($src, $dst)
                    $back += $f
                }
            }
            $sub.metaWrittenBack = $back
        } else {
            $sw = [Diagnostics.Stopwatch]::StartNew()
            Undo-HarnessSubmit $journal
            $journal = $null
            $sub.reverted = $true
            if ($report.stage -ne 'editor') {
                $rc = Invoke-HarnessRecompile -TimeoutSec $TimeoutSec
                $sub['restore'] = [ordered]@{ ok = $rc.ok; status = $rc.status; errors = @($rc.errors) }
            }
            $timings['restoreSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
        }
    }
} catch {
    $msg = "submit failed in phase '$($sub.phase)': $($_.Exception.Message)"
    if ($null -ne $journal) {
        try {
            Undo-HarnessSubmit $journal
            $sub.reverted = $true
            $rc = Invoke-HarnessRecompile -TimeoutSec $TimeoutSec
            $sub['restore'] = [ordered]@{ ok = $rc.ok; status = $rc.status; errors = @($rc.errors) }
        } catch { $msg += " | revert failed: $($_.Exception.Message) (the next loop.ps1/submit.ps1 retries it from the journal)" }
    }
    # Keep the loop's findings (compile errors, shots, ...) if it got that far.
    if ($null -eq $report) { $report = New-FailReport 'submit' $msg } else { $report['submitError'] = $msg }
} finally { Exit-HarnessLock }
Complete-Submit $report
