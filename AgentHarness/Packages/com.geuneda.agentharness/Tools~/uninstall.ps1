<#
.SYNOPSIS
  Remove AgentHarness from a project it was attached to with install.ps1, leaving git status as it was before (P-2).
  - Packages/manifest.json: the com.geuneda.agentharness line, dependencies install.ps1 added with it ("installAdded"), and
    the project's own versions of dependencies it raised ("installReplaced"). Packages/packages-lock.json: restored byte for byte from
    what install.ps1 recorded (Library/AgentHarness/install.json) when the manifest is back to that state; otherwise the
    lock entries only the harness needed are dropped (Unity re-resolves the rest when it opens the project).
  - an embedded copy (Packages/com.geuneda.agentharness), tools/ entry points, tools/scenarios/default.json and
    tools/AgentHarness.md (when unchanged; -Force removes changed ones too), ProjectSettings/AgentHarness.json, the
    CLAUDE.md install.ps1 wrote (when unchanged), empty tools/ folders, HarnessOut/, Library/Harness, Library/AgentHarness.
  The Editor must be closed (tools/quit.ps1): an open Editor would re-resolve the packages while files go away.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/uninstall.ps1 -WhatIf
  powershell -ExecutionPolicy Bypass -File tools/uninstall.ps1
  powershell -ExecutionPolicy Bypass -File <package>/Tools~/uninstall.ps1 -Project C:\dev\MyGame
  Prints one JSON object (removed / modified / kept / warnings). Exit code 0 = removed (or, with -WhatIf, would remove).
#>
param(
    [string]$Project = $env:AGENTHARNESS_WORK_ROOT,
    [switch]$WhatIf,
    [switch]$Force,        # remove edited scenario/guide files too, and allow a project with "setup": "harness"
    [switch]$KeepOutput    # keep HarnessOut/ (captures and reports)
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$pkgName = 'com.geuneda.agentharness'
$templates = Join-Path $PSScriptRoot 'templates'
$entryNames = @('open.ps1', 'quit.ps1', 'loop.ps1', 'uc.ps1', 'submit.ps1', 'land.ps1', 'compile-check.ps1', 'uninstall.ps1')
$utf8 = New-Object Text.UTF8Encoding($false)
$result = [ordered]@{ ok = $false; whatIf = [bool]$WhatIf; project = $null; removed = @(); modified = @(); kept = @(); warnings = @(); removedDependencies = @() }

function Finish([int]$Code) {
    $result | ConvertTo-Json -Depth 6
    exit $Code
}
function Fail([string]$Message) { $result['error'] = $Message; Finish 1 }
# File text with LF line ends: a package from git is checked out with the machine's line ends (CRLF on Windows).
function Read-Text([string]$Path) { [IO.File]::ReadAllText($Path).Replace("`r`n", "`n") }

if (-not $Project) { Fail '-Project is required (or run tools/uninstall.ps1 of the project)' }
$root = [IO.Path]::GetFullPath($Project).TrimEnd('\', '/')
$result.project = $root.Replace('\', '/')
if (-not (Test-Path -LiteralPath (Join-Path $root 'Assets'))) { Fail "$root is not a Unity project" }
$configPath = Join-Path $root 'ProjectSettings/AgentHarness.json'
if ((Test-Path -LiteralPath $configPath) -and -not $Force) {
    $cfg = [IO.File]::ReadAllText($configPath) | ConvertFrom-Json
    if ($cfg.PSObject.Properties.Name -contains 'setup' -and "$($cfg.setup)" -eq 'harness') { Fail 'this project is set up as a harness project ("setup": "harness"), not attached to an existing one; -Force removes the harness anyway' }
}
$lockFile = Join-Path $root 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $lockFile) {
    try { $s = [IO.File]::Open($lockFile, 'Open', 'Read', 'None'); $s.Dispose() }
    catch { Fail 'an Editor has the project open: close it first (powershell -ExecutionPolicy Bypass -File tools/quit.ps1)' }
}

$actions = New-Object System.Collections.ArrayList
function Add-Action([string]$Rel, [string]$Kind, [scriptblock]$Do) { [void]$actions.Add([pscustomobject]@{ path = $Rel; kind = $Kind; do = $Do }) }

# ---- 1. Packages/manifest.json and packages-lock.json -------------------------------------------------------------
# Remove the dependency line exactly as install.ps1 inserted it (and the comma it added to the previous line).
function Remove-ManifestDependency([string]$Text, [string]$Name) {
    $m = [regex]::Match($Text, '(?<prev>,?)(?<nl>\r?\n)(?<line>[ \t]*"' + [regex]::Escape($Name) + '"[ \t]*:[ \t]*"[^"]*")(?<comma>,?)')
    if (-not $m.Success) { return $Text }
    if ($m.Groups['comma'].Value) {
        $from = $m.Groups['nl'].Index; $to = $m.Groups['comma'].Index + 1
    } else {
        $from = $m.Index; $to = $m.Groups['line'].Index + $m.Groups['line'].Length
    }
    $Text.Substring(0, $from) + $Text.Substring($to)
}

# Drop lock entries no remaining root (a manifest dependency or an embedded package) reaches.
function Remove-UnreachableLockEntries([string]$Lock, [string[]]$Roots) {
    $nl = if ($Lock.Contains("`r`n")) { "`r`n" } else { "`n" }
    $blocks = @([regex]::Matches($Lock, '(?m)^    "(?<name>[^"]+)": \{\r?\n(?<body>(?:.*\r?\n)*?)    \},?\r?\n'))
    if ($blocks.Count -eq 0) { return $Lock }
    $deps = @{}
    $embedded = @()
    foreach ($b in $blocks) {
        $body = $b.Groups['body'].Value
        $d = [regex]::Match($body, '"dependencies": \{(?<list>[^}]*)\}')
        $deps[$b.Groups['name'].Value] = @(if ($d.Success) { [regex]::Matches($d.Groups['list'].Value, '"(?<n>[^"]+)":') | ForEach-Object { $_.Groups['n'].Value } })
        if ($body -match '"source": "embedded"') { $embedded += $b.Groups['name'].Value }
    }
    $keep = @{}
    $queue = New-Object System.Collections.Queue
    foreach ($r in @($Roots) + @($embedded | Where-Object { $_ -ne $pkgName })) { if ($deps.ContainsKey($r) -and -not $keep.ContainsKey($r)) { $keep[$r] = $true; $queue.Enqueue($r) } }
    while ($queue.Count -gt 0) {
        foreach ($d in $deps[$queue.Dequeue()]) { if ($deps.ContainsKey($d) -and -not $keep.ContainsKey($d)) { $keep[$d] = $true; $queue.Enqueue($d) } }
    }
    $kept = @($blocks | Where-Object { $keep.ContainsKey($_.Groups['name'].Value) })
    if ($kept.Count -eq $blocks.Count) { return $Lock }
    $first = $blocks[0].Index
    $last = $blocks[$blocks.Count - 1]
    $parts = @()
    for ($i = 0; $i -lt $kept.Count; $i++) {
        $parts += '    "' + $kept[$i].Groups['name'].Value + '": {' + $nl + $kept[$i].Groups['body'].Value + '    }' + $(if ($i -lt $kept.Count - 1) { ',' } else { '' }) + $nl
    }
    $Lock.Substring(0, $first) + ($parts -join '') + $Lock.Substring($last.Index + $last.Length)
}

$manifestPath = Join-Path $root 'Packages/manifest.json'
$lockPath = Join-Path $root 'Packages/packages-lock.json'
$statePath = Join-Path $root 'Library/AgentHarness/install.json'
$manifest = [IO.File]::ReadAllText($manifestPath)
$newManifest = Remove-ManifestDependency $manifest $pkgName
$state = $null
if (Test-Path -LiteralPath $statePath) { try { $state = [IO.File]::ReadAllText($statePath) | ConvertFrom-Json } catch { } }
# Dependencies install.ps1 added besides the harness (installAdded in the config and in its record).
$added = @()
if (Test-Path -LiteralPath $configPath) {
    try { $c = [IO.File]::ReadAllText($configPath) | ConvertFrom-Json; if ($c.PSObject.Properties.Name -contains 'installAdded') { $added += @($c.installAdded) } } catch { }
}
if ($state -and $state.PSObject.Properties.Name -contains 'installAdded') { $added += @($state.installAdded) }
foreach ($a in @($added | Where-Object { $_ } | Sort-Object -Unique)) { $newManifest = Remove-ManifestDependency $newManifest $a; $result['removedDependencies'] += @($a) }
# Dependencies install.ps1 raised to the harness's version (installReplaced): back to the project's own version, while
# the manifest still has the version install wrote (a later change by the project is kept).
$replaced = @()
if (Test-Path -LiteralPath $configPath) {
    try { $c = [IO.File]::ReadAllText($configPath) | ConvertFrom-Json; if ($c.PSObject.Properties.Name -contains 'installReplaced') { $replaced += @($c.installReplaced) } } catch { }
}
if ($state -and $state.PSObject.Properties.Name -contains 'installReplaced') { $replaced += @($state.installReplaced) }
$result['restoredDependencies'] = @()
$doneReplaced = @{}
foreach ($r in @($replaced | Where-Object { $_ -and $_.name })) {
    if ($doneReplaced.ContainsKey($r.name)) { continue }
    $doneReplaced[$r.name] = $true
    $m = [regex]::Match($newManifest, '(?m)^(?<pre>[ \t]*"' + [regex]::Escape($r.name) + '"[ \t]*:[ \t]*")(?<val>[^"]*)"')
    if (-not $m.Success) { continue }
    if ($m.Groups['val'].Value -ne $r.to) { $result.warnings += "$($r.name) is $($m.Groups['val'].Value) now (install.ps1 set $($r.to)): kept"; continue }
    $newManifest = $newManifest.Substring(0, $m.Groups['val'].Index) + $r.from + $newManifest.Substring($m.Groups['val'].Index + $m.Groups['val'].Length)
    $result['restoredDependencies'] += @("$($r.name)@$($r.from)")
}
if ($newManifest -ne $manifest) { Add-Action 'Packages/manifest.json' 'modify' { [IO.File]::WriteAllText($manifestPath, $newManifest, $utf8) }.GetNewClosure() }
if (Test-Path -LiteralPath $lockPath) {
    $lock = [IO.File]::ReadAllText($lockPath)
    if ($state -and $state.manifestBefore -eq $newManifest -and $null -ne $state.lockBefore) {
        if ($lock -ne $state.lockBefore) {
            $before = [string]$state.lockBefore
            Add-Action 'Packages/packages-lock.json' 'modify' { [IO.File]::WriteAllText($lockPath, $before, $utf8) }.GetNewClosure()
            $result['lock'] = 'restored as recorded by install.ps1'
        }
    } else {
        $roots = @([regex]::Matches(([regex]::Match($newManifest, '"dependencies"\s*:\s*\{(?<b>[^}]*)\}').Groups['b'].Value), '"(?<n>[^"]+)"\s*:') | ForEach-Object { $_.Groups['n'].Value })
        $newLock = Remove-UnreachableLockEntries $lock $roots
        # Dependencies put back in the manifest: their lock entry back to that version too (Unity re-checks it on open).
        foreach ($d in @($result['restoredDependencies'])) {
            $name, $from = $d.Split('@', 2)
            $to = @($replaced | Where-Object { $_.name -eq $name } | Select-Object -First 1).to
            $newLock = [regex]::Replace($newLock, '(?m)(^    "' + [regex]::Escape($name) + '": \{\r?\n      "version": ")' + [regex]::Escape($to) + '"', '${1}' + $from + '"')
        }
        if ($newLock -ne $lock) { Add-Action 'Packages/packages-lock.json' 'modify' { [IO.File]::WriteAllText($lockPath, $newLock, $utf8) }.GetNewClosure() }
        $result['lock'] = 'no install record for this manifest (installed on another machine): entries only the harness needed removed, raised versions put back; Unity re-checks the lock when it opens the project'
    }
}
$embedDir = Join-Path $root "Packages/$pkgName"
if (Test-Path -LiteralPath (Join-Path $embedDir 'package.json')) {
    Add-Action "Packages/$pkgName" 'remove' { Remove-Item -LiteralPath $embedDir -Recurse -Force; if (Test-Path -LiteralPath "$embedDir.meta") { Remove-Item -LiteralPath "$embedDir.meta" -Force } }.GetNewClosure()
}

# ---- 2. Files install.ps1 created ------------------------------------------------------------------------------------
$entry = [IO.File]::ReadAllText((Join-Path $templates 'entry.ps1'))
foreach ($n in $entryNames) {
    $rel = "tools/$n"; $f = Join-Path $root $rel
    if (-not (Test-Path -LiteralPath $f)) { continue }
    if ([IO.File]::ReadAllText($f).Contains('AgentHarness entry point')) { Add-Action $rel 'remove' { Remove-Item -LiteralPath $f -Force }.GetNewClosure() }
    else { Add-Action $rel 'keep' $null; $result.warnings += "$rel is not an AgentHarness entry point: kept" }
}
foreach ($t in @(@{ rel = 'tools/scenarios/default.json'; src = 'default-scenario.json' }, @{ rel = 'tools/AgentHarness.md'; src = 'AgentHarness.md' })) {
    $f = Join-Path $root $t.rel
    if (-not (Test-Path -LiteralPath $f)) { continue }
    $same = (Read-Text $f) -eq (Read-Text (Join-Path $templates $t.src))
    if ($same -or $Force) { Add-Action $t.rel 'remove' { Remove-Item -LiteralPath $f -Force }.GetNewClosure() }
    else { Add-Action $t.rel 'keep' $null; $result.warnings += "$($t.rel) was changed after install: kept (-Force removes it)" }
}
if (Test-Path -LiteralPath $configPath) { Add-Action 'ProjectSettings/AgentHarness.json' 'remove' { Remove-Item -LiteralPath $configPath -Force }.GetNewClosure() }
$claude = Join-Path $root 'CLAUDE.md'
if (Test-Path -LiteralPath $claude) {
    $stub = "# $(Split-Path -Leaf $root)`n`nThis Unity project has the AgentHarness attached: read tools/AgentHarness.md before working here`n" +
        "(tools/loop.ps1 = recompile + play + screenshots + console as JSON).`n"
    if ((Read-Text $claude) -eq $stub) { Add-Action 'CLAUDE.md' 'remove' { Remove-Item -LiteralPath $claude -Force }.GetNewClosure() }
}
foreach ($d in @('tools/scenarios', 'tools')) {
    $abs = Join-Path $root $d
    Add-Action $d 'remove-if-empty' { if ((Test-Path -LiteralPath $abs) -and @(Get-ChildItem -LiteralPath $abs -Force).Count -eq 0) { Remove-Item -LiteralPath $abs -Force } }.GetNewClosure()
}
# Ignored by git (HarnessOut/.gitignore, Library/), removed so nothing of the harness is left behind.
$scratch = @('Library/Harness', 'Library/AgentHarness')
if (-not $KeepOutput) { $scratch = @('HarnessOut') + $scratch }
foreach ($d in $scratch) {
    $abs = Join-Path $root $d
    if (Test-Path -LiteralPath $abs) { Add-Action $d 'remove' { Remove-Item -LiteralPath $abs -Recurse -Force }.GetNewClosure() }
}
# The legacy input shim (install.ps1 -InputShim) is the game's once code reads input through it.
$shim = Join-Path $root 'Assets/AgentHarness/HarnessInput.cs'
if (Test-Path -LiteralPath $shim) {
    $same = (Read-Text $shim) -eq (Read-Text (Join-Path $templates 'HarnessInput.cs'))
    $users = @(if ($same) { Get-ChildItem -LiteralPath (Join-Path $root 'Assets') -Recurse -Filter *.cs -File | Where-Object { $_.FullName -ne $shim -and [IO.File]::ReadAllText($_.FullName).Contains('HarnessInput.') } | ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') } })
    if ($same -and $users.Count -eq 0) {
        $shimDir = Split-Path -Parent $shim
        Add-Action 'Assets/AgentHarness/HarnessInput.cs' 'remove' {
            Remove-Item -LiteralPath $shim -Force
            if (Test-Path -LiteralPath "$shim.meta") { Remove-Item -LiteralPath "$shim.meta" -Force }
            if (@(Get-ChildItem -LiteralPath $shimDir -Force).Count -eq 0) { Remove-Item -LiteralPath $shimDir -Force; if (Test-Path -LiteralPath "$shimDir.meta") { Remove-Item -LiteralPath "$shimDir.meta" -Force } }
        }.GetNewClosure()
    } else {
        Add-Action 'Assets/AgentHarness/HarnessInput.cs' 'keep' $null
        $result.warnings += "Assets/AgentHarness/HarnessInput.cs is kept: $(if ($same) { "game code uses it ($($users[0])$(if ($users.Count -gt 1) { ", +$($users.Count - 1)" }))" } else { 'it was changed after install' })"
    }
}
$generated = Join-Path $root 'Assets/AgentHarness'
if ((Test-Path -LiteralPath $generated) -and @(Get-ChildItem -LiteralPath $generated -Force | Where-Object { $_.Name -notin @('HarnessInput.cs', 'HarnessInput.cs.meta') }).Count -gt 0) { $result.warnings += 'Assets/AgentHarness (scenes/assets harness_build generated) is kept: delete it in the Editor if you do not need it' }

# ---- 3. Apply ------------------------------------------------------------------------------------------------------
foreach ($a in $actions) {
    if ($a.kind -eq 'remove-if-empty') {
        if (-not $WhatIf) { & $a.do }
        continue
    }
    switch ($a.kind) { 'remove' { $result.removed += $a.path } 'modify' { $result.modified += $a.path } 'keep' { $result.kept += $a.path } }
    if (-not $WhatIf -and $a.do) { & $a.do }
}
$result.ok = $true
Finish 0
