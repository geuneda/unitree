<#
.SYNOPSIS
  Land an agent branch: merge it into the branch the Editor tree has checked out, as a transaction (G5-5).

  Submitted files sit in the Editor tree as uncommitted copies, so a plain 'git merge agent/foo' there is refused
  ("untracked working tree files would be overwritten"). land.ps1 does the whole procedure under the Editor lock:
  1. No lock: the branch exists, and the worktree that has it checked out has nothing uncommitted (only commits land;
     commit the .meta files submit.ps1 copied back).
  2. Editor lock. Refused (stage=land) before anything is touched when the Editor tree is detached, mid-merge or has
     staged changes; when the merge would conflict (git merge-tree, in the object store only); when files or folders
     the branch adds under Assets/ have no committed .meta (a fresh clone would give them new GUIDs); when a module
     it touches has un-landed changes submitted from another live worktree (-Takeover overrides); when it changes the
     contracts folder badly (G5-4): a contracts file another live worktree submitted and has not landed (land.contractOwner),
     a landed type changed or removed (land.contractChanged: add-only per type), a type name the contracts already declare,
     un-landed submits included (land.contractConflicts); or when it would replace any other uncommitted change that
     differs from what lands (land.foreign; e.g. an edit made directly in the Editor tree). So the only uncommitted files
     a land replaces are identical ones and this branch's own (superseded) submits.
  3. Journal (Library/Harness/land/pending.json), then 'git stash' exactly the uncommitted paths the merge touches,
     plus leftovers in the modules it touches (files submitted earlier and deleted since), moved out of the stash list
     shared by all worktrees to refs/agentharness/land/<runId>. Then 'git merge'.
  4. The normal loop on the Editor tree. Green -> keep the merge, drop the stash, release the branch's module
     and contracts-file ownership. Red -> undo: 'reset --keep' to the pre-land commit (only the merged paths; other uncommitted work
     stays), re-apply the stash, recompile. -KeepOnFail keeps non-compile failures. If land dies half-way, the next
     lock holder (loop/uc/submit/land) undoes it from the journal (report: recoveredLand).

.EXAMPLE
  # from the agent worktree, once submit.ps1 was green and everything is committed:
  powershell -ExecutionPolicy Bypass -File tools/land.ps1
  # or from the Editor tree:
  powershell -ExecutionPolicy Bypass -File tools/land.ps1 -Branch agent/foo
  -> the loop's report.json + "land": {...} in HarnessOut/land/. Exit 0 = merged and green.
#>
param(
    [string]$Branch,      # default: the branch of the worktree this runs from
    [string]$Scenario = 'tools/scenarios/default.json',
    [string]$Out = 'HarnessOut/land',
    [switch]$NoPlay,
    [switch]$KeepOnFail,
    [switch]$Takeover,    # land over another worktree's un-landed changes to a module this branch touches
    [int]$TimeoutSec = 180
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
Use-HarnessIntegrationRoot   # the Editor tree, also from a worktree with its own Editor (open.ps1 -Own)
$clock = [Diagnostics.Stopwatch]::StartNew()
$work = Get-HarnessWorkRoot
$root = Get-HarnessProjectRoot
$outAbs = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $work $Out }
# The Editor reads the scenario: pass this checkout's file by absolute path (inline JSON as is).
$scenarioArg = $Scenario
if (-not $Scenario.TrimStart().StartsWith('{') -and -not [IO.Path]::IsPathRooted($Scenario)) {
    $local = Join-Path $work $Scenario
    if (Test-Path -LiteralPath $local) { $scenarioArg = $local.Replace('\', '/') }
}
$runId = (Get-Date).ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$timings = [ordered]@{}
if ($Branch -like 'refs/heads/*') { $Branch = $Branch.Substring(11) }
$land = [ordered]@{ runId = $runId; branch = $Branch; branchSha = $null; into = $null; worktree = $null; phase = 'check'
    head = [ordered]@{ before = $null; after = $null }; merged = $false; fastForward = $false; kept = $false; reverted = $false
    modules = @(); files = @() }
$script:Journal = $null
# Contracts paths (repo-relative) whose uncommitted Editor-tree copies are this branch's submits (or taken over): replaceable.
$script:ContractsMine = New-Object 'System.Collections.Generic.HashSet[string]'
$script:ContractTakeovers = @()   # project-relative

function New-FailReport([string]$stage, [string]$message) {
    [ordered]@{ ok = $false; stage = $stage; compileErrors = @(); runtimeErrors = @(); fps = $null; shots = @(); durationSec = 0
        report = (Join-Path $outAbs 'report.json').Replace('\', '/'); error = $message }
}

function Complete-Land([System.Collections.IDictionary]$report) {
    $report['land'] = $land
    Add-HarnessRecovery $report
    Save-HarnessReport $report $outAbs $clock $timings
    exit $(if ($report.ok) { 0 } else { 1 })
}

# Unity ignores these (no .meta): hidden names, '~' suffix, cvs, .tmp.
function Test-UnityHidden([string]$rel) {
    foreach ($s in $rel.Split('/')) { if ($s.StartsWith('.') -or $s.EndsWith('~') -or $s -ieq 'cvs' -or $s.EndsWith('.tmp')) { return $true } }
    $false
}

# ---- 0. Branch, and the worktree that has it (no lock) ---------------------------------------------------
$repo = $null
try { $repo = Get-HarnessRepoRoot } catch { Complete-Land (New-FailReport 'land' "the Editor tree ($root) is not a git checkout: $($_.Exception.Message)") }
function RepoGit([string[]]$a, [switch]$Check) { Invoke-HarnessGit $repo $a -Check:$Check }
$prefix = (Invoke-HarnessGit $root @('rev-parse', '--show-prefix') -Check).out.Trim()   # e.g. AgentHarness/
# Modules: ProjectSettings/AgentHarness.json of the Editor tree (folders under moduleRoots, and modules[] folders).
$cfg = Get-HarnessConfig $root
$contractsModule = if ($cfg.contracts) { (Get-HarnessModuleOf "$($cfg.contracts)/x.cs" $cfg).name } else { '' }
# Module of a repo-relative path ('' = none).
function Get-RepoPathModule([string]$p) {
    if ($prefix -and -not $p.StartsWith($prefix)) { return '' }
    (Get-HarnessModuleOf $p.Substring($prefix.Length) $cfg).name
}

if (-not $Branch) {
    if (-not (Test-HarnessWorktree)) { Complete-Land (New-FailReport 'land' "-Branch is required when run from the Editor tree (from an agent worktree it defaults to that worktree's branch)") }
    $g = Invoke-HarnessGit $work @('symbolic-ref', '-q', '--short', 'HEAD')
    if ($g.code -ne 0) { Complete-Land (New-FailReport 'land' "this worktree ($work) has a detached HEAD: pass -Branch") }
    $Branch = $g.out.Trim()
    $land.branch = $Branch
}
$g = RepoGit @('rev-parse', '-q', '--verify', "$Branch^{commit}")
if ($g.code -ne 0) { Complete-Land (New-FailReport 'land' "no branch or commit '$Branch'") }
$branchSha = $g.out.Trim()
$land.branchSha = $branchSha

$holder = $null
$cur = $null
foreach ($line in @((RepoGit @('worktree', 'list', '--porcelain') -Check).out -split "`n") + @('')) {
    $line = $line.TrimEnd("`r")
    if ($line -like 'worktree *') { $cur = @{ path = $line.Substring(9); branch = $null } }
    elseif ($line -like 'branch *' -and $cur) { $cur.branch = $line.Substring(7) }
    elseif ($line -eq '' -and $cur) { if ($cur.branch -eq "refs/heads/$Branch") { $holder = $cur }; $cur = $null }
}
$branchWork = $null
if ($holder) {
    if (Test-HarnessSamePath $holder.path $repo) { Complete-Land (New-FailReport 'land' "'$Branch' is the branch the Editor tree has checked out; there is nothing to land") }
    $land.worktree = $holder.path
    $branchWork = [IO.Path]::Combine($holder.path, $prefix)
    $dirty = @(Split-HarnessZ (Invoke-HarnessGit $holder.path @('status', '--porcelain=v1', '-z', '--untracked-files=all') -Check).out | ForEach-Object { $_.Substring(3) })
    if ($dirty.Count -gt 0) {
        $land['uncommitted'] = @($dirty | Select-Object -First 30)
        Complete-Land (New-FailReport 'land' "the worktree of '$Branch' ($($holder.path)) has $($dirty.Count) uncommitted files (land.uncommitted), and only commits land. Commit them - including the .meta files submit.ps1 copied back - or stash them.")
    }
}

# An owners.json entry that is this branch's (its submits).
function Test-OwnedHere($o) {
    $o -and ($o.branch -eq $Branch -or ($branchWork -and (Test-HarnessSamePath $o.workRoot $branchWork)))
}

function Test-InModule([string]$p, [string]$m) {
    $folder = Get-HarnessModuleFolder $m $root $cfg
    $folder -and ($p.StartsWith("$prefix$folder/") -or $p -eq "$prefix$folder.meta")
}

# Repo-relative path -> blob id, for every file in a tree.
function Get-TreeIds([string]$tree) {
    $h = @{}
    foreach ($e in @(Split-HarnessZ (RepoGit @('ls-tree', '-r', '-z', $tree) -Check).out)) {
        $t = $e.IndexOf("`t")
        $h[$e.Substring($t + 1)] = $e.Substring(0, $t).Split(' ')[2]
    }
    $h
}

# Release this branch's modules (and ones it took over) whose Editor-tree copies are committed now. A module that still
# has uncommitted changes (submitted, but not in the branch) stays owned and is reported in land.stillPending.
function Clear-BranchOwners([string[]]$TakenOver = @()) {
    $owners = Get-HarnessOwners
    $released = @(); $pending = @()
    foreach ($m in @($owners.Keys | Where-Object { (Test-OwnedHere $owners[$_]) -or $TakenOver -contains $_ } | Sort-Object)) {
        if (@(Get-HarnessModuleChanges $m).Count -gt 0) { $pending += $m; continue }
        $owners.Remove($m)
        $released += $m
    }
    if ($released.Count -gt 0) { Save-HarnessOwners $owners }
    # The same for its contracts files (G5-4): released once the Editor tree's copy is the committed one.
    $co = Get-HarnessContractOwners
    $cReleased = @()
    $status = $null
    foreach ($k in @($co.Keys | Where-Object { (Test-OwnedHere $co[$_]) -or $script:ContractTakeovers -contains $_ } | Sort-Object)) {
        if ($null -eq $status) { $status = Get-HarnessGitStatus $repo }
        if ($status.ContainsKey("$prefix$k")) { $pending += $k; continue }
        $co.Remove($k)
        $cReleased += $k
    }
    if ($cReleased.Count -gt 0) { Save-HarnessContractOwners $co; $land['releasedContracts'] = $cReleased }
    if ($pending.Count -gt 0) {
        $land['stillPending'] = $pending
        $land['warning'] = "the Editor tree still has uncommitted changes from this branch's submits that are not in the branch (modules / contracts files: $($pending -join ', ')). Commit and land them, or submit again to sync them."
    }
    $released
}

# ---- 1-4. Under the Editor lock --------------------------------------------------------------------------
function Invoke-Land {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $g = RepoGit @('symbolic-ref', '-q', '--short', 'HEAD')
    if ($g.code -ne 0) { return New-FailReport 'land' "the Editor tree ($repo) has a detached HEAD; check out the branch to land into" }
    $land.into = $g.out.Trim()
    foreach ($ref in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'REBASE_HEAD')) {
        if ((RepoGit @('rev-parse', '-q', '--verify', $ref)).code -eq 0) { return New-FailReport 'land' "a git operation is in progress in the Editor tree ($ref exists); finish or abort it first" }
    }
    foreach ($d in @('rebase-merge', 'rebase-apply')) {
        $gp = (RepoGit @('rev-parse', '--git-path', $d) -Check).out.Trim()
        if (-not [IO.Path]::IsPathRooted($gp)) { $gp = Join-Path $repo $gp }
        if (Test-Path -LiteralPath $gp) { return New-FailReport 'land' 'a rebase is in progress in the Editor tree; finish or abort it first' }
    }
    if ((RepoGit @('diff', '--cached', '--quiet')).code -ne 0) { return New-FailReport 'land' 'the Editor tree has staged changes, so git would refuse the merge; commit or unstage them first' }
    $preHead = (RepoGit @('rev-parse', 'HEAD') -Check).out.Trim()
    $land.head.before = $preHead
    if ((RepoGit @('merge-base', '--is-ancestor', $branchSha, $preHead)).code -eq 0) {
        $land.head.after = $preHead
        $land['note'] = "already landed: $Branch is contained in $($land.into)"
        $land['releasedOwners'] = @(Clear-BranchOwners)
        $land.phase = 'done'
        $timings['checkSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
        $r = New-FailReport 'done' ''
        $r.ok = $true
        $r.Remove('error')
        return $r
    }

    # Merge in the object store first: a conflict is refused before the Editor tree is touched.
    $mt = RepoGit @('merge-tree', '--write-tree', '--name-only', '--no-messages', '-z', $preHead, $branchSha)
    if ($mt.code -gt 1) { throw "git merge-tree failed ($($mt.code)): $($mt.err)" }
    $tok = @(Split-HarnessZ $mt.out)
    if ($mt.code -eq 1) {
        $land['conflicts'] = @($tok | Select-Object -Skip 1 | Sort-Object -Unique)
        return New-FailReport 'land' "merging $Branch into $($land.into) conflicts in $(@($land.conflicts).Count) files (land.conflicts); nothing was touched. In the worktree: git merge $($land.into), resolve, commit, submit.ps1 again, then land."
    }
    $mergedTree = $tok[0]
    $paths = @(Split-HarnessZ (RepoGit @('diff', '--name-only', '-z', '--no-renames', $preHead, $mergedTree) -Check).out)
    $added = @(Split-HarnessZ (RepoGit @('diff', '--name-only', '-z', '--no-renames', '--diff-filter=A', $preHead, $mergedTree) -Check).out)
    $land.files = $paths

    # Every file and folder the merge adds under Assets/ needs its .meta in the merge (stable GUIDs for everyone).
    $assets = "${prefix}Assets/"
    $mergedIds = Get-TreeIds $mergedTree
    $baseIds = Get-TreeIds $preHead
    $baseDirs = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($f in $baseIds.Keys) {
        $d = $f
        while (($i = $d.LastIndexOf('/')) -gt 0) { $d = $d.Substring(0, $i); if (-not $baseDirs.Add($d)) { break } }
    }
    $missing = New-Object System.Collections.ArrayList
    $newDirs = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($p in $added) {
        if (-not $p.StartsWith($assets) -or $p.EndsWith('.meta') -or (Test-UnityHidden $p.Substring($assets.Length))) { continue }
        if (-not $mergedIds.ContainsKey("$p.meta")) { [void]$missing.Add("$p.meta") }
        $d = $p.Substring(0, $p.LastIndexOf('/'))
        while ($d.StartsWith($assets) -and -not $baseDirs.Contains($d)) {
            if ($newDirs.Add($d) -and -not $mergedIds.ContainsKey("$d.meta")) { [void]$missing.Add("$d.meta") }
            $d = $d.Substring(0, $d.LastIndexOf('/'))
        }
    }
    if ($missing.Count -gt 0) {
        $land['missingMeta'] = @($missing | Sort-Object -Unique)
        return New-FailReport 'land' "$(@($land.missingMeta).Count) files/folders the branch adds under Assets/ have no committed .meta (land.missingMeta); a fresh clone would give them new GUIDs. submit.ps1 copies the .meta files Unity generated into the worktree: commit them and land again."
    }

    # Modules the merge touches, and their owners: never land over another live worktree's un-landed submit.
    $land.modules = @($paths | ForEach-Object { Get-RepoPathModule $_ } | Where-Object { $_ -and $_ -ne $contractsModule -and $_ -ne 'Harness' } | Sort-Object -Unique)
    $owners = Get-HarnessOwners
    $takeovers = @()
    foreach ($m in $land.modules) {
        $o = $owners[$m]
        if (-not $o -or (Test-OwnedHere $o) -or -not (Test-Path -LiteralPath $o.workRoot)) { continue }
        $pending = @(Get-HarnessModuleChanges $m)
        if ($pending.Count -eq 0) { continue }
        $info = [ordered]@{ module = $m; workRoot = $o.workRoot; branch = $o.branch; at = $o.at; pendingFiles = $pending.Count }
        if (-not $Takeover) {
            $land['owner'] = $info
            return New-FailReport 'land' "module $m ($(Get-HarnessModuleFolder $m $root $cfg)) has un-landed changes ($($pending.Count) files) submitted from another worktree: $($o.workRoot) (branch $($o.branch), $($o.at)); landing $Branch would replace them. Land that branch first, or pass -Takeover if that work is abandoned."
        }
        $takeovers += $info
    }
    if ($takeovers.Count -gt 0) { $land['takeover'] = $takeovers }
    # Uncommitted files in these modules may be replaced: this branch's own (superseded) submits, or taken over.
    $mine = @($land.modules | Where-Object { (Test-OwnedHere $owners[$_]) -or @($takeovers | Where-Object module -eq $_).Count })

    # What to move out of the way: uncommitted paths the merge writes, and anything uncommitted in its modules.
    $status = Get-HarnessGitStatus $repo
    $pathSet = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($p in $paths) { [void]$pathSet.Add($p) }

    # The contracts files the merge changes (G5-4): not over another live worktree's un-landed submit of one, no landed type
    # changed (add-only per type), and no type name twice with the contracts the Editor tree will have (un-landed ones included).
    if ($cfg.contracts) {
        $cPaths = @($paths | Where-Object { $_.StartsWith("$prefix$($cfg.contracts)/") } | Sort-Object)
        $cOwners = Get-HarnessContractOwners
        foreach ($cp in $cPaths) {
            $o = $cOwners[$cp.Substring($prefix.Length)]
            if (-not $o -or -not $status.ContainsKey($cp)) { continue }
            if (Test-OwnedHere $o) { [void]$script:ContractsMine.Add($cp); continue }
            if (-not (Test-Path -LiteralPath $o.workRoot)) { continue }
            $info = [ordered]@{ path = $cp.Substring($prefix.Length); workRoot = $o.workRoot; branch = $o.branch; at = $o.at }
            if (-not $Takeover) {
                $land['contractOwner'] = $info
                return New-FailReport 'land' "$($info.path) has un-landed changes submitted from another worktree: $($o.workRoot) (branch $($o.branch), $($o.at)); landing $Branch would replace them. Land that branch first, or pass -Takeover if that work is abandoned."
            }
            $script:ContractTakeovers += $info.path
            [void]$script:ContractsMine.Add($cp)
        }
        $csPaths = @($cPaths | Where-Object { $_.EndsWith('.cs') })
        if ($csPaths.Count -gt 0) {
            $L = @{}; $A = @{}; $R = @{}
            foreach ($cp in $csPaths) {
                $rel = $cp.Substring($prefix.Length)
                if ($baseIds.ContainsKey($cp)) { $L[$rel] = Get-HarnessBlobText $repo $baseIds[$cp] }
                $A[$rel] = if ($mergedIds.ContainsKey($cp)) { @{ text = (Get-HarnessBlobText $repo $mergedIds[$cp]) } } else { $null }
            }
            # The other .cs files there after the land: the Editor tree's as they are (submitted, un-landed ones included).
            $cDir = Join-Path $root $cfg.contracts
            if (Test-Path -LiteralPath $cDir) {
                foreach ($f in @(Get-ChildItem -LiteralPath $cDir -Recurse -File -Filter '*.cs')) {
                    $rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/')
                    if (-not $A.ContainsKey($rel)) { $R[$rel] = @{ path = $rel } }
                }
            }
            $check = Test-HarnessContracts -Landed $L -After $A -Rest $R
            if ($check.changed.Count -gt 0) {
                $land['contractChanged'] = $check.changed
                return New-FailReport 'land' "$Branch changes landed contracts types: $(Format-HarnessContractChanges $check.changed). The contracts are add-only: a type that landed never changes (other modules use it); add a new type instead."
            }
            if ($check.conflicts.Count -gt 0) {
                foreach ($c in $check.conflicts) {
                    $o = $cOwners[$c.other]
                    $c['otherOwner'] = if ($status.ContainsKey("$prefix$($c.other)") -and $o) { "submitted from $($o.workRoot), not landed" } elseif ($status.ContainsKey("$prefix$($c.other)")) { 'uncommitted in the Editor tree' } else { 'landed' }
                }
                $land['contractConflicts'] = $check.conflicts
                return New-FailReport 'land' "$Branch declares contracts type names that are already declared: $(Format-HarnessContractConflicts $check.conflicts). play.events counts events by type name: rename one (in the worktree: commit, submit.ps1, land again)."
            }
        }
    }
    $stashPaths = @($status.Keys | Where-Object {
            $p = $_
            if ($pathSet.Contains($p)) { return $true }
            foreach ($m in $land.modules) { if (Test-InModule $p $m) { return $true } }
            $false
        } | Sort-Object)
    $before = Get-HarnessContentIds $repo $stashPaths
    # Anything else the land would replace is somebody's uncommitted work (e.g. an edit made in the Editor tree):
    # never moved away silently.
    $foreign = @($stashPaths | Where-Object {
            $p = $_
            $landed = if ($pathSet.Contains($p)) { $mergedIds[$p] } else { $baseIds[$p] }
            $before[$p] -ne $landed -and -not @($mine | Where-Object { Test-InModule $p $_ }).Count -and -not $script:ContractsMine.Contains($p)
        })
    if ($foreign.Count -gt 0) {
        $land['foreign'] = $foreign
        return New-FailReport 'land' "the Editor tree has uncommitted changes that are not this branch's submits and differ from what would land (land.foreign). Landing would replace them; nothing was touched. Commit or stash them first (git stash push -- <paths>), or submit them from their worktree."
    }
    $timings['checkSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)

    # ---- stash + merge, under a journal ----
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $land.phase = 'stash'
    $script:Journal = [ordered]@{ runId = $runId; repo = $repo; branch = $Branch; branchSha = $branchSha; preHead = $preHead; mergedTree = $mergedTree
        mergedHead = $null; paths = $paths; added = $added; newDirs = @($newDirs); stashSha = $null; stashPaths = $stashPaths; pid = $PID
        startedAt = (Get-Date).ToString('o') }
    Save-HarnessLandJournal $script:Journal
    $land['stash'] = [ordered]@{ sha = $null; paths = $stashPaths; dropped = $false; kept = $false; differs = @() }
    if ($stashPaths.Count -gt 0) {
        $ps = Join-Path $root "Library\Harness\land\$runId.paths"
        Write-HarnessPathspec $ps $stashPaths
        [void](RepoGit @('stash', 'push', '--include-untracked', '-m', "agentharness-land $runId $Branch", "--pathspec-from-file=$ps", '--pathspec-file-nul') -Check)
        Remove-Item -LiteralPath $ps -Force
        $s = Find-HarnessLandStash $repo $runId
        if (-not $s) { throw 'git stash push did not create a stash entry' }
        $script:Journal.stashSha = $s.sha
        Save-HarnessLandJournal $script:Journal
        $land.stash.sha = $s.sha
        # Out of the stash list shared by all worktrees (an agent's 'git stash pop' must not take it).
        [void](RepoGit @('update-ref', (Get-HarnessLandStashRef $runId), $s.sha) -Check)
        [void](RepoGit @('stash', 'drop', $s.ref) -Check)
    }
    $land.phase = 'merge'
    $mg = RepoGit @('merge', '--no-edit', '--no-autostash', '-m', "Merge branch '$Branch'", $branchSha)
    if ($mg.code -ne 0) { throw "git merge failed: $($mg.err) $($mg.out.Trim())" }
    $newHead = (RepoGit @('rev-parse', 'HEAD') -Check).out.Trim()
    $script:Journal.mergedHead = $newHead
    Save-HarnessLandJournal $script:Journal
    $land.head.after = $newHead
    $land.merged = $true
    $land.fastForward = $newHead -eq $branchSha
    $timings['mergeSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)

    # ---- the loop on the merged Editor tree ----
    $land.phase = 'loop'
    $report = Invoke-HarnessLoop -Scenario $scenarioArg -OutDir $outAbs -NoPlay:$NoPlay -TimeoutSec $TimeoutSec -Timings $timings
    $mods = @()
    foreach ($e in @($report.compileErrors) + @($report.runtimeErrors) + @($report['lint'])) {
        if ($e -is [System.Collections.IDictionary] -and $e.module) { $mods += $e.module }
    }
    if ($report['build'] -and $report['build'].steps) { foreach ($s in @($report['build'].steps)) { if ($s.error -and $s.module) { $mods += $s.module } } }
    $land['errorModules'] = @($mods | Sort-Object -Unique)

    $land.phase = 'done'
    if ($report.ok -or ($KeepOnFail -and $report.stage -notin @('compile', 'editor'))) {
        # Stashed copies are what landed, or this branch's superseded submits (checked before merging). Anything else
        # that differs now changed during the land (e.g. rewritten on import): keep the stash for a human then.
        $after = Get-HarnessContentIds $repo $stashPaths
        $differs = @($stashPaths | Where-Object { $after[$_] -ne $before[$_] })
        $unexpected = @($differs | Where-Object { $p = $_; -not @($mine | Where-Object { Test-InModule $p $_ }).Count -and -not $script:ContractsMine.Contains($p) })
        $land.stash.differs = $differs
        if ($land.stash.sha) {
            if ($unexpected.Count -eq 0) {
                $land.stash.dropped = $true
            } else {
                [void](RepoGit @('stash', 'store', '-m', "agentharness-land $runId $Branch (pre-land copies)", $land.stash.sha) -Check)
                $land.stash.kept = $true
                $land['warning'] = "files changed during the land and differ from their pre-land copies: $($unexpected -join ', '). The pre-land copies are kept in the stash list: git stash show --include-untracked -p $($land.stash.sha)"
            }
            [void](RepoGit @('update-ref', '-d', (Get-HarnessLandStashRef $runId)))
        }
        Complete-HarnessLand
        $script:Journal = $null
        $land.kept = $true
        $land['releasedOwners'] = @(Clear-BranchOwners @($takeovers | ForEach-Object { $_.module }))
    } else {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $land['undo'] = @(Undo-HarnessLand $script:Journal)
        $script:Journal = $null
        $land.reverted = $true
        $land.head.after = $preHead
        if ($report.stage -ne 'editor') {
            $rc = Invoke-HarnessRecompile -TimeoutSec $TimeoutSec
            $land['restore'] = [ordered]@{ ok = $rc.ok; status = $rc.status; errors = @($rc.errors) }
        }
        $timings['restoreSec'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    }
    $report
}

$timings['lockWaitSec'] = Enter-HarnessLock
$report = $null
try {
    $report = Invoke-Land
} catch {
    $msg = "land failed in phase '$($land.phase)': $($_.Exception.Message)"
    if ($null -ne $script:Journal) {
        try {
            $land['undo'] = @(Undo-HarnessLand $script:Journal)
            $script:Journal = $null
            $land.reverted = $true
            if ((Invoke-UnityCommand -Name 'harness_ping' -TimeoutSec 5).success) {
                $rc = Invoke-HarnessRecompile -TimeoutSec $TimeoutSec
                $land['restore'] = [ordered]@{ ok = $rc.ok; status = $rc.status; errors = @($rc.errors) }
            }
        } catch { $msg += " | undo failed: $($_.Exception.Message) (the next loop/submit/land retries it from the journal)" }
    }
    $report = New-FailReport 'land' $msg
} finally { Exit-HarnessLock }
Complete-Land $report
