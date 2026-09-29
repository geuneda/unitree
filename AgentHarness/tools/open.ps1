# AgentHarness entry point: runs Tools~/<this file's name> of the com.geuneda.agentharness package that this project
# uses, so the tools always match the installed package version. Every tools/*.ps1 entry point is this same file
# (written by the package's install.ps1; do not edit - update the package instead).
#   Package found in: Packages/com.geuneda.agentharness (embedded), a "file:" dependency in Packages/manifest.json, or
#   Library/PackageCache (git/registry). An agent worktree without Library/ uses the package of the Editor tree.
#   Before the first import there is no Library/PackageCache yet: open.ps1 then imports the project once in batch mode.
$ErrorActionPreference = 'Stop'
$AgentHarnessWork = Split-Path -Parent $PSScriptRoot
$AgentHarnessTool = Split-Path -Leaf $PSCommandPath

function Find-AgentHarnessTools([string]$Project) {
    if (-not $Project) { return $null }
    $pkg = 'com.geuneda.agentharness'
    $candidates = @(Join-Path $Project "Packages/$pkg")
    $manifest = Join-Path $Project 'Packages/manifest.json'
    if (Test-Path -LiteralPath $manifest) {
        $m = [regex]::Match([IO.File]::ReadAllText($manifest), '"com\.geuneda\.agentharness"\s*:\s*"file:([^"]+)"')
        if ($m.Success) {
            $p = $m.Groups[1].Value
            if (-not [IO.Path]::IsPathRooted($p)) { $p = Join-Path (Join-Path $Project 'Packages') $p }
            $candidates += $p
        }
    }
    $candidates += @(Get-ChildItem -LiteralPath (Join-Path $Project 'Library/PackageCache') -Directory -Filter "$pkg@*" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | ForEach-Object { $_.FullName })
    foreach ($c in $candidates) {
        $t = Join-Path $c 'Tools~'
        if (Test-Path -LiteralPath (Join-Path $t 'Harness.psm1')) { return $t }
    }
    $null
}

# The checkout the Editor has open, for an agent worktree (the main worktree at the same sub-path).
function Get-AgentHarnessEditorRoot([string]$Work) {
    if ($env:AGENTHARNESS_EDITOR_ROOT) { return $env:AGENTHARNESS_EDITOR_ROOT }
    try {
        $main = @(& git -C $Work worktree list --porcelain 2>$null) | Select-Object -First 1
        $prefix = & git -C $Work rev-parse --show-prefix 2>$null
        if ($main -like 'worktree *') { return [IO.Path]::GetFullPath([IO.Path]::Combine($main.Substring(9), "$prefix")) }
    } catch { }
    $null
}

function Exit-AgentHarness([string]$Message) {
    [ordered]@{ ok = $false; stage = 'editor'; error = $Message } | ConvertTo-Json
    exit 1
}

$tools = Find-AgentHarnessTools $AgentHarnessWork
if (-not $tools -and -not (Test-Path -LiteralPath (Join-Path $AgentHarnessWork 'Library'))) { $tools = Find-AgentHarnessTools (Get-AgentHarnessEditorRoot $AgentHarnessWork) }

if (-not $tools -and $AgentHarnessTool -eq 'open.ps1') {
    # First open of a checkout that uses the package from git: Unity has not downloaded it yet. Import once in batch
    # mode (that resolves the packages), then run the package's open.ps1 as usual.
    $version = $null
    for ($i = 0; $i -lt $args.Count - 1; $i++) { if ("$($args[$i])" -ieq '-UnityVersion') { $version = "$($args[$i + 1])" } }
    if (-not $version) {
        $pv = [regex]::Match([IO.File]::ReadAllText((Join-Path $AgentHarnessWork 'ProjectSettings/ProjectVersion.txt')), 'm_EditorVersion:\s*(\S+)')
        if ($pv.Success) { $version = $pv.Groups[1].Value }
    }
    $exe = $null
    try {
        $installed = (& unity editors --installed --format json --no-banner 2>$null | Out-String | ConvertFrom-Json).data
        $e = @($installed | Where-Object { $_.version -eq $version }) | Select-Object -First 1
        if ($e) { $exe = if ("$($e.location)" -like '*.app') { Join-Path $e.location 'Contents/MacOS/Unity' } else { "$($e.location)" } }
    } catch { }
    if (-not $exe) { Exit-AgentHarness "the AgentHarness package is not downloaded yet and Unity $version is not installed ('unity editors --installed'): unity install $version, or open.ps1 -UnityVersion <installed 6.x>" }
    $log = Join-Path $AgentHarnessWork 'Logs/Editor-bootstrap.log'
    [void](New-Item -ItemType Directory -Force (Split-Path -Parent $log))
    [Console]::Error.WriteLine("open.ps1: importing the project once in batch mode to download the AgentHarness package (log: $log)")
    $argLine = (@('-batchmode', '-quit', '-projectPath', $AgentHarnessWork, '-logFile', $log) | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $p = Start-Process -FilePath $exe -ArgumentList $argLine -PassThru
    $null = $p.Handle   # keeps ExitCode readable after the process ends
    if (-not $p.WaitForExit(1800 * 1000)) { try { $p.Kill() } catch { }; Exit-AgentHarness "the batch-mode import did not finish in 1800 s (log: $log)" }
    $tools = Find-AgentHarnessTools $AgentHarnessWork
    if (-not $tools) { Exit-AgentHarness "the batch-mode import (exit code $($p.ExitCode)) did not download com.geuneda.agentharness: see $log and Packages/manifest.json" }
}
if (-not $tools) {
    Exit-AgentHarness "the AgentHarness package (com.geuneda.agentharness) was not found for $AgentHarnessWork (Packages/, a file: dependency, or Library/PackageCache): run tools/open.ps1 once, or install it with the package's Tools~/install.ps1"
}

# The real scripts find this checkout (the work root) here; Harness.psm1 derives the Editor tree from it.
$env:AGENTHARNESS_WORK_ROOT = $AgentHarnessWork
& (Join-Path $tools $AgentHarnessTool) @args
exit $LASTEXITCODE
