# uc.ps1 - call one Editor command and print the JSON envelope.
#   tools/uc.ps1 harness_build
#   tools/uc.ps1 harness_capture '{"preset":"overview","out":"HarnessOut/manual"}'
#   tools/uc.ps1 eval_file '{"file":"AgentScripts/probe.cs"}'   # C# body, no usings
# Parameters are ONE JSON object (avoids PowerShell 5.1 native-arg quoting bugs).
# From an agent worktree this talks to the Editor tree's Editor; relative paths in the JSON resolve there.
param(
    [Parameter(Mandatory, Position = 0)][string]$Command,
    [Parameter(Position = 1)][string]$Json = '{}',
    [int]$TimeoutSec = 120
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force

$params = @{}
$parsed = $Json | ConvertFrom-Json
if ($null -ne $parsed) { foreach ($p in $parsed.PSObject.Properties) { $params[$p.Name] = $p.Value } }

# Editor-mutating commands take the same lock as loop.ps1 (read-only ones don't wait).
if (-not (Test-HarnessReadOnly $Command)) {
    [void](Enter-HarnessLock)
    $rec = Get-HarnessLastRecovery
    if ($rec) { [Console]::Error.WriteLine("uc.ps1: rolled back an interrupted submit: $($rec | ConvertTo-Json -Compress)") }
    $rec = Get-HarnessLastLandRecovery
    if ($rec) { [Console]::Error.WriteLine("uc.ps1: undid an interrupted land: $($rec | ConvertTo-Json -Compress)") }
}
try { $r = Invoke-UnityCommand -Name $Command -Params $params -TimeoutSec $TimeoutSec }
finally { Exit-HarnessLock }
$r | ConvertTo-Json -Depth 30
if (-not $r.success) { exit 1 }
