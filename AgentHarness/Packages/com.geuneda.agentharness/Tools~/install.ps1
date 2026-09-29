<#
.SYNOPSIS
  Attach AgentHarness to an existing Unity project (P-2). Adds only new things; changes no existing file except the one
  line of the package dependency in Packages/manifest.json (and Unity then updates Packages/packages-lock.json).
    - the package: a dependency on com.geuneda.agentharness (git URL by default), or a copy in Packages/ (-Source embed)
    - tools/<name>.ps1 entry points (open, loop, quit, uc, submit, land, compile-check, uninstall) that run the package's
      Tools~ scripts, tools/scenarios/default.json, tools/AgentHarness.md (how to use it, for agents)
    - ProjectSettings/AgentHarness.json (which scene the loop plays, module folders, "setup": "attach")
    - CLAUDE.md pointing at tools/AgentHarness.md, only when the project has neither CLAUDE.md nor AGENTS.md
  Nothing in Assets/ or ProjectSettings/*.asset is touched; harness_setup changes project settings only when asked.
  One more dependency is added when needed: com.unity.inputsystem, if the project's Active Input Handling is 'Input System
  Package' or 'Both' but the package is not installed (com.unity.pipeline 0.8.0-exp.1, the harness's Editor connection,
  then fails to compile). It is listed in "installAdded" of the config, removed by uninstall, and left out of release builds.
  tools/uninstall.ps1 removes exactly this again. Run from the harness repository (or a copy of the package).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File <unitree>/AgentHarness/Packages/com.geuneda.agentharness/Tools~/install.ps1 -Project C:\dev\MyGame -WhatIf
  powershell -ExecutionPolicy Bypass -File .../install.ps1 -Project C:\dev\MyGame                      # git URL, play the first Build Settings scene
  powershell -ExecutionPolicy Bypass -File .../install.ps1 -Project C:\dev\MyGame -Scene Assets/Scenes/Level1.unity -Module Gameplay=Assets/Scripts
  powershell -ExecutionPolicy Bypass -File .../install.ps1 -Project C:\dev\MyGame -Source local       # file: dependency on this package folder (harness development)
  powershell -ExecutionPolicy Bypass -File .../install.ps1 -Project C:\dev\MyGame -Source embed       # copy the package into Packages/ (vendored)

  Prints one JSON object (created / modified / kept / warnings / next). Exit code 0 = installed (or, with -WhatIf, would install).
  Then: tools/open.ps1 -> tools/uc.ps1 harness_setup (recommendations only) -> tools/loop.ps1.
#>
param(
    [Parameter(Mandatory)][string]$Project,
    # git (default: GitHub URL of this repository, -Ref), local (file: path of this package folder), embed (copy into
    # Packages/), or a UPM dependency string of your own (a git URL or file:...).
    [string]$Source = 'git',
    [string]$Ref = 'master',
    [string]$Scene,                 # playScene: a scene path; default "first" (the first enabled scene of the Build Settings)
    [string[]]$Module = @(),        # modules[] entries: Name=Assets/Path (a folder of existing code = one module)
    [ValidateSet('attach', 'harness')][string]$Setup = 'attach',
    [string]$InputSystemVersion = '1.19.0',   # added only when the Active Input Handling needs it (see below)
    [switch]$WhatIf,
    [switch]$Force                  # replace existing tools/*.ps1 files that are not AgentHarness entry points
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$pkgName = 'com.geuneda.agentharness'
$pkgDir = Split-Path -Parent $PSScriptRoot
$templates = Join-Path $PSScriptRoot 'templates'
$repoUrl = 'https://github.com/geuneda/unitree.git'
$repoPath = '/AgentHarness/Packages/com.geuneda.agentharness'
$entryNames = @('open.ps1', 'quit.ps1', 'loop.ps1', 'uc.ps1', 'submit.ps1', 'land.ps1', 'compile-check.ps1', 'uninstall.ps1')
$utf8 = New-Object Text.UTF8Encoding($false)
$result = [ordered]@{ ok = $false; whatIf = [bool]$WhatIf; project = $null; unityVersion = $null; source = $Source; dependency = $null
    created = @(); modified = @(); kept = @(); warnings = @(); next = @() }

function Finish([int]$Code) {
    $result | ConvertTo-Json -Depth 6
    exit $Code
}
function Fail([string]$Message) { $result['error'] = $Message; Finish 1 }
# File text with LF line ends: a package from git is checked out with the machine's line ends (CRLF on Windows).
function Read-Text([string]$Path) { [IO.File]::ReadAllText($Path).Replace("`r`n", "`n") }

# ---- 0. The project ------------------------------------------------------------------------------------------
$root = [IO.Path]::GetFullPath($Project).TrimEnd('\', '/')
$result.project = $root.Replace('\', '/')
if (-not (Test-Path -LiteralPath (Join-Path $root 'Assets')) -or -not (Test-Path -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt'))) {
    Fail "$root is not a Unity project (no Assets/ or ProjectSettings/ProjectVersion.txt)"
}
if ([IO.Path]::GetFullPath($root) -ieq [IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent $pkgDir)))) {
    Fail "$root is the project this package comes from"
}
$version = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'ProjectSettings/ProjectVersion.txt')), 'm_EditorVersion:\s*(\S+)').Groups[1].Value
$result.unityVersion = $version
$major = 0
[void][int]::TryParse(($version -split '\.')[0], [ref]$major)
if ($major -lt 6000) {
    $result.warnings += "the project is on Unity $version; the harness needs Unity 6.0 LTS or newer (open it with tools/open.ps1 -UnityVersion <installed 6.x> to upgrade)"
}
if ($root.Length -gt 60) { $result.warnings += "the project path is $($root.Length) characters: Unity package paths under Library/ can then exceed the Windows 260-character limit (keep it at 60 or less)" }
if ($root.StartsWith([IO.Path]::GetTempPath().TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { $result.warnings += 'the project is under %TEMP%: Windows application control may block Burst DLLs there' }
if (Test-Path -LiteralPath (Join-Path $root 'Temp/UnityLockfile')) {
    try { $s = [IO.File]::Open((Join-Path $root 'Temp/UnityLockfile'), 'Open', 'Read', 'None'); $s.Dispose() }
    catch { $result.warnings += 'an Editor has the project open: it picks up the new package on its next refresh (or close it and use tools/open.ps1)' }
}

# ---- 1. The package dependency --------------------------------------------------------------------------------
$manifestPath = Join-Path $root 'Packages/manifest.json'
$manifest = [IO.File]::ReadAllText($manifestPath)
$embedDir = Join-Path $root "Packages/$pkgName"
switch ($Source) {
    'git' { $dep = "$repoUrl`?path=$repoPath#$Ref" }
    'local' { $dep = 'file:' + $pkgDir.Replace('\', '/') }
    'embed' { $dep = $null }
    default { $dep = $Source }
}
$result.dependency = $dep

# Text-preserving edit of the "dependencies" object (Unity's own layout: one "name": "value" per line, sorted).
function Set-ManifestDependency([string]$Text, [string]$Name, [string]$Value) {
    $nl = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $existing = [regex]::Match($Text, '(?m)^(?<pre>[ \t]*"' + [regex]::Escape($Name) + '"[ \t]*:[ \t]*")(?<val>[^"]*)(?<post>".*)$')
    if ($existing.Success) {
        if ($existing.Groups['val'].Value -eq $Value) { return $Text }
        return $Text.Substring(0, $existing.Groups['val'].Index) + $Value + $Text.Substring($existing.Groups['val'].Index + $existing.Groups['val'].Length)
    }
    $open = [regex]::Match($Text, '"dependencies"\s*:\s*\{')
    if (-not $open.Success) { throw 'Packages/manifest.json has no "dependencies" object' }
    $start = $open.Index + $open.Length
    $end = $Text.IndexOf('}', $start)
    $body = $Text.Substring($start, $end - $start)
    $entries = @([regex]::Matches($body, '(?m)^(?<indent>[ \t]*)"(?<name>[^"]+)"[ \t]*:[ \t]*"[^"]*"(?<comma>,?)[ \t]*\r?$'))
    if ($entries.Count -eq 0) {
        return $Text.Substring(0, $start) + $nl + '    "' + $Name + '": "' + $Value + '"' + $nl + '  ' + $Text.Substring($end)
    }
    $indent = $entries[0].Groups['indent'].Value
    $line = $indent + '"' + $Name + '": "' + $Value + '"'
    $after = $null
    foreach ($e in $entries) { if ([string]::CompareOrdinal($e.Groups['name'].Value, $Name) -gt 0) { $after = $e; break } }
    if ($after) {
        $at = $start + $after.Index
        return $Text.Substring(0, $at) + $line + ',' + $nl + $Text.Substring($at)
    }
    $last = $entries[$entries.Count - 1]
    $lastEnd = $start + $last.Groups['comma'].Index   # where the last entry's (missing) comma goes
    return $Text.Substring(0, $lastEnd) + ',' + $nl + $line + $Text.Substring($lastEnd)
}

$plan = New-Object System.Collections.ArrayList   # @{ path; kind = create|modify|keep; write = scriptblock }
function Add-Step([string]$Rel, [string]$Kind, [scriptblock]$Write) { [void]$plan.Add([pscustomobject]@{ path = $Rel; kind = $Kind; write = $Write }) }

$newManifest = $manifest
if ($dep) {
    $newManifest = Set-ManifestDependency $newManifest $pkgName $dep
    if (Test-Path -LiteralPath $embedDir) { $result.warnings += "Packages/$pkgName exists (an embedded copy): Unity uses it instead of the dependency" }
} else {
    if ([regex]::IsMatch($manifest, '"' + [regex]::Escape($pkgName) + '"\s*:')) { Fail "Packages/manifest.json already depends on $pkgName; remove that line to embed a copy" }
    if (Test-Path -LiteralPath $embedDir) { Add-Step "Packages/$pkgName" 'keep' $null }
    else {
        Add-Step "Packages/$pkgName" 'create' {
            Copy-Item -LiteralPath $pkgDir -Destination $embedDir -Recurse
        }.GetNewClosure()
    }
}
# com.unity.pipeline 0.8.0-exp.1 compiles its input commands under ENABLE_INPUT_SYSTEM (Active Input Handling 'Input
# System Package' = 1 or 'Both' = 2) without checking that com.unity.inputsystem is installed: add the package then.
# (With 'Input Manager (Old)' = 0 it is not needed, and installing it would make the Input System ask to switch backends.)
$installAdded = @()
$playerSettings = Join-Path $root 'ProjectSettings/ProjectSettings.asset'
$handler = 0
if (Test-Path -LiteralPath $playerSettings) {
    $h = [regex]::Match([IO.File]::ReadAllText($playerSettings), 'activeInputHandler:\s*(\d)')
    if ($h.Success) { $handler = [int]$h.Groups[1].Value }
}
$lockText = if (Test-Path -LiteralPath (Join-Path $root 'Packages/packages-lock.json')) { [IO.File]::ReadAllText((Join-Path $root 'Packages/packages-lock.json')) } else { '' }
$hasInputSystem = [regex]::IsMatch($manifest, '"com\.unity\.inputsystem"\s*:') -or $lockText.Contains('"com.unity.inputsystem": {')
if ($handler -ne 0 -and -not $hasInputSystem) {
    $newManifest = Set-ManifestDependency $newManifest 'com.unity.inputsystem' $InputSystemVersion
    $installAdded += 'com.unity.inputsystem'
    $result.warnings += "Active Input Handling is '$(if ($handler -eq 1) { 'Input System Package' } else { 'Both' })' but com.unity.inputsystem is not installed: adding com.unity.inputsystem $InputSystemVersion (com.unity.pipeline needs it then). uninstall.ps1 removes it; release builds leave it out"
}
$result['installAdded'] = $installAdded
if ($newManifest -ne $manifest) { Add-Step 'Packages/manifest.json' 'modify' { [IO.File]::WriteAllText($manifestPath, $newManifest, $utf8) }.GetNewClosure() }
else { Add-Step 'Packages/manifest.json' 'keep' $null }

# ---- 2. Entry points, scenario, guide ------------------------------------------------------------------------------
$entry = Read-Text (Join-Path $templates 'entry.ps1')
foreach ($n in $entryNames) {
    $rel = "tools/$n"
    $dst = Join-Path $root $rel
    if (Test-Path -LiteralPath $dst) {
        $cur = Read-Text $dst
        if ($cur -eq $entry) { Add-Step $rel 'keep' $null; continue }
        if (-not $cur.Contains('AgentHarness entry point') -and -not $Force) { Fail "$rel exists and is not an AgentHarness entry point (-Force replaces it)" }
        Add-Step $rel 'modify' { [IO.File]::WriteAllText($dst, $entry, $utf8) }.GetNewClosure()
    } else {
        Add-Step $rel 'create' { [void][IO.Directory]::CreateDirectory((Split-Path -Parent $dst)); [IO.File]::WriteAllText($dst, $entry, $utf8) }.GetNewClosure()
    }
}
foreach ($t in @(@{ rel = 'tools/scenarios/default.json'; src = 'default-scenario.json' }, @{ rel = 'tools/AgentHarness.md'; src = 'AgentHarness.md' })) {
    $dst = Join-Path $root $t.rel
    $src = Join-Path $templates $t.src
    if (Test-Path -LiteralPath $dst) { Add-Step $t.rel 'keep' $null; continue }
    $text = Read-Text $src
    Add-Step $t.rel 'create' { [void][IO.Directory]::CreateDirectory((Split-Path -Parent $dst)); [IO.File]::WriteAllText($dst, $text, $utf8) }.GetNewClosure()
}

# ---- 3. ProjectSettings/AgentHarness.json ---------------------------------------------------------------------------
$configRel = 'ProjectSettings/AgentHarness.json'
$configPath = Join-Path $root $configRel
if (Test-Path -LiteralPath $configPath) {
    Add-Step $configRel 'keep' $null
} else {
    $playScene = 'first'
    if ($Scene) {
        $playScene = $Scene.Replace('\', '/')
        if ($playScene -notin @('first', 'build') -and -not (Test-Path -LiteralPath (Join-Path $root $playScene))) { Fail "-Scene ${Scene}: no such file in the project" }
    } elseif ($Setup -eq 'attach') {
        # "first" needs an enabled scene in the Build Settings.
        $bs = Join-Path $root 'ProjectSettings/EditorBuildSettings.asset'
        $enabled = @(if (Test-Path -LiteralPath $bs) { [regex]::Matches([IO.File]::ReadAllText($bs), '(?m)^\s*- enabled: 1\s*\r?\n\s*path: (.+?)\s*$') | ForEach-Object { $_.Groups[1].Value } })
        if ($enabled.Count -eq 0) {
            $scenes = @(Get-ChildItem -LiteralPath (Join-Path $root 'Assets') -Recurse -Filter *.unity -File -ErrorAction SilentlyContinue | Select-Object -First 12 |
                ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') })
            $result['scenes'] = $scenes
            Fail "the Build Settings have no enabled scene, so the loop would not know which scene to play: pass -Scene <path> (scenes found: see 'scenes')"
        }
        $result['playScene'] = "first = $($enabled[0])"
    }
    $mods = @()
    foreach ($m in @($Module | ForEach-Object { "$_" -split ',' } | Where-Object { $_.Trim() })) {
        $kv = $m.Split('=', 2)
        if ($kv.Count -ne 2 -or -not $kv[0].Trim() -or -not $kv[1].Trim()) { Fail "-Module '$m': use Name=Assets/Path" }
        $path = $kv[1].Trim().Replace('\', '/').TrimEnd('/')
        if (-not (Test-Path -LiteralPath (Join-Path $root $path))) { Fail "-Module '$m': no folder $path" }
        $mods += [ordered]@{ name = $kv[0].Trim(); path = $path }
    }
    if ($Setup -eq 'harness' -and (Test-Path -LiteralPath (Join-Path $root 'Assets/Scenes/Main.unity'))) {
        $result.warnings += "-Setup harness is for a new project: harness_build will overwrite Assets/Scenes/Main.unity, which exists here (use the default -Setup attach for an existing project)"
    }
    $cfg = if ($Setup -eq 'harness') {
        [ordered]@{ setup = 'harness'; moduleRoots = @('Assets/Game'); modules = $mods; contracts = 'Assets/Game/Contracts'; generatedRoot = 'Assets/Generated'
            buildScene = 'Assets/Scenes/Main.unity'; playScene = $(if ($Scene) { $playScene } else { 'build' }); installAdded = $installAdded }
    } else {
        [ordered]@{ setup = 'attach'; moduleRoots = @(); modules = $mods; contracts = ''; generatedRoot = 'Assets/AgentHarness/Generated'
            buildScene = 'Assets/AgentHarness/Main.unity'; playScene = $playScene; installAdded = $installAdded }
    }
    # Unity-style JSON (2-space indent): PowerShell 5.1's ConvertTo-Json indents oddly, so write it by hand.
    $lines = @('{')
    $keys = @($cfg.Keys)
    for ($i = 0; $i -lt $keys.Count; $i++) {
        $k = $keys[$i]; $v = $cfg[$k]
        $comma = if ($i -lt $keys.Count - 1) { ',' } else { '' }
        if ($k -eq 'modules') {
            if (@($v).Count -eq 0) { $lines += "  `"modules`": []$comma" }
            else {
                $lines += '  "modules": ['
                $items = @($v)
                for ($j = 0; $j -lt $items.Count; $j++) { $lines += '    { "name": "' + $items[$j].name + '", "path": "' + $items[$j].path + '" }' + $(if ($j -lt $items.Count - 1) { ',' } else { '' }) }
                $lines += "  ]$comma"
            }
        } elseif ($k -eq 'moduleRoots' -or $k -eq 'installAdded') {
            $lines += '  "' + $k + '": [' + ((@($v) | ForEach-Object { '"' + $_ + '"' }) -join ', ') + "]$comma"
        } else {
            $lines += '  "' + $k + '": "' + $v + '"' + $comma
        }
    }
    $lines += '}'
    $configText = ($lines -join "`n") + "`n"
    $result['config'] = $cfg
    Add-Step $configRel 'create' { [IO.File]::WriteAllText($configPath, $configText, $utf8) }.GetNewClosure()
}

# ---- 4. A pointer for agents -------------------------------------------------------------------------------------
$agentDocs = @('CLAUDE.md', 'AGENTS.md') | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) }
if (@($agentDocs).Count -eq 0) {
    $claude = Join-Path $root 'CLAUDE.md'
    $claudeText = "# $(Split-Path -Leaf $root)`n`nThis Unity project has the AgentHarness attached: read tools/AgentHarness.md before working here`n" +
        "(tools/loop.ps1 = recompile + play + screenshots + console as JSON).`n"
    Add-Step 'CLAUDE.md' 'create' { [IO.File]::WriteAllText($claude, $claudeText, $utf8) }.GetNewClosure()
} else {
    $result.warnings += "add a line to $(@($agentDocs)[0]) so agents find the harness, e.g. 'AgentHarness: read tools/AgentHarness.md (tools/loop.ps1 = recompile + play + screenshots + console as JSON)'"
}

# ---- 5. Apply -----------------------------------------------------------------------------------------------------
# What uninstall.ps1 needs to put packages-lock.json back byte for byte: kept under Library/ (never committed).
$state = Join-Path $root 'Library/AgentHarness/install.json'
$lockPath = Join-Path $root 'Packages/packages-lock.json'
foreach ($s in $plan) {
    switch ($s.kind) { 'create' { $result.created += $s.path } 'modify' { $result.modified += $s.path } 'keep' { $result.kept += $s.path } }
}
if (-not $WhatIf) {
    if (-not (Test-Path -LiteralPath $state)) {
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $state))
        $record = [ordered]@{ installedAt = (Get-Date).ToString('o'); source = $Source; dependency = $dep; installAdded = @($installAdded); manifestBefore = $manifest
            lockBefore = $(if (Test-Path -LiteralPath $lockPath) { [IO.File]::ReadAllText($lockPath) } else { $null }); created = @($result.created) }
        [IO.File]::WriteAllText($state, ($record | ConvertTo-Json -Depth 4), $utf8)
    }
    foreach ($s in $plan) { if ($s.write) { & $s.write } }
}
$result.next = @('powershell -ExecutionPolicy Bypass -File tools/open.ps1', "& ./tools/uc.ps1 harness_setup   # recommendations only in an attached project", 'powershell -ExecutionPolicy Bypass -File tools/loop.ps1')
$result.ok = $true
Finish 0
