<#
.SYNOPSIS
  Compile-check game modules WITHOUT the Unity Editor (safe to run from many agents in parallel).

  Each module assembly (asmdef under Assets/Game/<Module>/, incl. <Module>/Builders) is compiled against the DLLs of
  the Editor's last successful compile. Source lists are re-globbed from disk, so files an agent just created are
  included. Every run uses its own temp dir.
  - Worktree aware: sources come from the checkout this script lives in (e.g. an agent worktree), compiler settings
    and dependency DLLs from the Editor project it drives (Harness.psm1 Resolve-HarnessEditorRoot).
  - Chained: assemblies checked in one run are compiled in dependency order, and later ones reference this run's
    output instead of the Editor's DLL. -Module also checks the in-project assemblies it references (Game.Contracts).
  - New assemblies the Editor has never compiled (no .rsp) are checked by csc with a response file synthesized from a
    template assembly of the same kind (Editor-only or not) plus the asmdef references ("synthesized": true).

  Backends:
    csc (default)  Unity's own compiler response file (Library/Bee/artifacts/*/<Asm>.rsp), run the way the Editor's
                   build graph (Library/Bee/*.dag.json) runs it: same dotnet, csc.dll and flags. Identical
                   flags/analyzers to the Editor, ~0.1-0.3 s per assembly, whatever the Unity version's layout.
    msbuild        Unity-generated <Asm>.csproj (tools/uc.ps1 harness_sync_csproj once), rewritten per run; needs
                   Visual Studio 2022 / Build Tools MSBuild (no .NET SDK needed). Cold start 10-75 s. Existing assemblies only.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Smoke
  powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Smoke,Stage -Backend msbuild
  -> JSON { ok, backend, compileErrors:[{file,line,msg,module,assembly}], targets:[...], durationSec }

.NOTES
  Staleness: assemblies outside the checked set (other modules; Harness unless -IncludeHarness) are the Editor's last
  compiled DLLs. Another module's public API change is only seen after the Editor compiled it (loop.ps1 / submit.ps1).
#>
param(
    [string[]]$Module,
    [ValidateSet('msbuild', 'csc')][string]$Backend = 'csc',
    [switch]$IncludeHarness
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Harness.psm1') -Force
$work = Get-HarnessWorkRoot      # sources
$root = Get-HarnessProjectRoot   # Library/, csproj, response files (their relative paths resolve from here)
Set-Location $root
$env:VSLANG = '1033'   # English compiler messages
# `powershell -File ... -Module A,B` passes "A,B" as one string.
$Module = @($Module | ForEach-Object { "$_" -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$total = [Diagnostics.Stopwatch]::StartNew()
$runId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$tmpRoot = Join-Path $root "Temp\compile-check\$runId"
New-Item -ItemType Directory -Force $tmpRoot | Out-Null

# The compiler exactly as the Editor runs it (P-1). Its build graph records every C# compile as
# '"<dotnet>" exec "<csc.dll>" /nostdlib /noconfig /shared "@<Asm>.rsp" "@<Asm>.rsp2"'. Where dotnet and csc.dll live
# differs between Unity versions (6.0-6.3: Data/NetCoreRuntime + Data/DotNetSdkRoslyn; 6.6: Data/DotNetSdk/...
# /Roslyn/bincore), so nothing about the install is assumed. No graph = the Editor never compiled the project, and
# there are no response files either.
function Get-EditorCompiler {
    $dags = @(Get-ChildItem -LiteralPath (Join-Path $root 'Library/Bee') -Filter '*.dag.json' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    $pattern = '"Action":\s*"\\"(?<dotnet>[^"\\]*(?:\\\\[^"\\]*)*)\\" exec \\"(?<csc>[^"\\]*(?:\\\\[^"\\]*)*csc\.dll)\\"(?<flags>(?: /\w+)*) '
    foreach ($dag in $dags) {
        $m = [regex]::Match([IO.File]::ReadAllText($dag.FullName), $pattern)
        if (-not $m.Success) { continue }
        # JSON escapes: \\ -> \
        return [pscustomobject]@{ dotnet = $m.Groups['dotnet'].Value -replace '\\\\', '\'; csc = $m.Groups['csc'].Value -replace '\\\\', '\'
            flags = @($m.Groups['flags'].Value -split ' ' | Where-Object { $_ }); graph = "Library/Bee/$($dag.Name)" }
    }
    throw "no C# compile in the Editor's build graph (Library/Bee/*.dag.json): open the project in the Editor once (tools/open.ps1)"
}

function Get-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $p = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($p) { return $p }
    }
    $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw 'MSBuild not found (install Visual Studio 2022 or Build Tools, or use -Backend csc)'
}

function ModuleOf([string]$path) {
    $p = $path.Replace('\', '/')
    if ($p -match 'Assets/Game/([^/]+)/') { return $Matches[1] }
    if ($p -match 'Assets/Harness/') { return 'Harness' }
    ''
}

function New-CheckError([string]$msg, $t) { [ordered]@{ file = ''; line = 0; msg = $msg; module = $t.module; assembly = $t.assembly } }

function Parse-Errors([string[]]$lines, [string]$assembly) {
    $seen = @{}
    foreach ($l in $lines) {
        if ($l -match '^\s*(?<file>[^\s].*?)\((?<line>\d+),(?<col>\d+)\):\s*error\s+(?<code>\w+):\s*(?<msg>.*?)(\s+\[[^\]]+\])?\s*$') {
            $file = $Matches.file.Replace('\', '/')
            $i = $file.IndexOf('/Assets/'); if ($i -ge 0) { $file = $file.Substring($i + 1) }
            $key = "$file|$($Matches.line)|$($Matches.code)"
            if ($seen.ContainsKey($key)) { continue }
            $seen[$key] = $true
            [ordered]@{ file = $file; line = [int]$Matches.line; msg = "$($Matches.code): $($Matches.msg)"; module = (ModuleOf $file); assembly = $assembly }
        }
    }
}

# ---- Targets: asmdefs + their source files (excluding nested asmdef folders) --------------------------
$projectNames = @{}   # every asmdef under Assets/ (for synthesized response files)
$guidName = @{}       # asmdef GUID -> name ("GUID:..." references)
foreach ($a in @(Get-ChildItem (Join-Path $work 'Assets') -Recurse -Filter *.asmdef)) {
    $n = (Get-Content $a.FullName -Raw | ConvertFrom-Json).name
    $projectNames[$n] = $true
    $meta = "$($a.FullName).meta"
    if (Test-Path -LiteralPath $meta) {
        $g = Select-String -LiteralPath $meta -Pattern '^guid:\s*(\w+)' | Select-Object -First 1
        if ($g) { $guidName[$g.Matches[0].Groups[1].Value] = $n }
    }
}
$asmdefs = @(Get-ChildItem (Join-Path $work 'Assets\Game') -Recurse -Filter *.asmdef)
if ($IncludeHarness) { $asmdefs += @(Get-ChildItem (Join-Path $work 'Assets\Harness') -Recurse -Filter *.asmdef) }
$all = @()
foreach ($a in $asmdefs) {
    $j = Get-Content $a.FullName -Raw | ConvertFrom-Json
    $dir = $a.DirectoryName
    $nested = @($asmdefs | Where-Object { $_.DirectoryName -ne $dir -and $_.DirectoryName.StartsWith($dir + '\') } | ForEach-Object { $_.DirectoryName + '\' })
    $sources = @(Get-ChildItem $dir -Recurse -Filter *.cs | Where-Object { $f = $_.FullName; -not ($nested | Where-Object { $f.StartsWith($_) }) } | ForEach-Object { $_.FullName })
    $refs = @(@($j.references) | Where-Object { $_ } | ForEach-Object { if ($_ -like 'GUID:*') { $guidName[$_.Substring(5)] } else { $_ } } | Where-Object { $_ })
    $all += [pscustomobject]@{ assembly = $j.name; module = (ModuleOf ($a.FullName.Substring($work.Length + 1))); sources = $sources; refs = $refs
        editorOnly = (@($j.includePlatforms) -contains 'Editor'); dependency = $false }
}
$byName = @{}; foreach ($t in $all) { $byName[$t.assembly] = $t }

# -Module: the module's assemblies plus the in-project assemblies they reference (so e.g. a Contracts addition in
# this checkout is visible to the module that uses it).
$picked = $all
if ($Module.Count -gt 0) {
    $sel = [ordered]@{}
    $queue = New-Object System.Collections.Queue
    foreach ($t in $all) { if ($t.module -in $Module) { $sel[$t.assembly] = $t; $queue.Enqueue($t) } }
    while ($queue.Count -gt 0) {
        $t = $queue.Dequeue()
        foreach ($r in $t.refs) {
            if ($byName.ContainsKey($r) -and -not $sel.Contains($r)) { $byName[$r].dependency = $true; $sel[$r] = $byName[$r]; $queue.Enqueue($byName[$r]) }
        }
    }
    $picked = @($sel.Values)
}

# Dependency order, so each assembly can reference the DLLs compiled before it in this run.
$pickedByName = @{}; foreach ($t in $picked) { $pickedByName[$t.assembly] = $t }
$targets = New-Object System.Collections.ArrayList
$visited = @{}
function Add-InOrder($t) {
    if ($visited.ContainsKey($t.assembly)) { return }
    $visited[$t.assembly] = $true
    foreach ($r in $t.refs) { if ($pickedByName.ContainsKey($r)) { Add-InOrder $pickedByName[$r] } }
    [void]$targets.Add($t)
}
foreach ($t in @($picked | Sort-Object assembly)) { Add-InOrder $t }

# ---- Backends: each returns @{ ok; errors; dll; note; synthesized } -------------------------------------
$built = @{}    # assembly -> DLL compiled in this run
$failed = @{}   # assemblies that failed or were skipped in this run

function Find-Rsp([string]$assembly) {
    Get-ChildItem (Join-Path $root 'Library\Bee\artifacts') -Recurse -Filter "$assembly.rsp" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

function Invoke-CscCheck($t) {
    $rsp = Find-Rsp $t.assembly
    $synth = $null -eq $rsp
    if ($synth) {
        # Never compiled by the Editor (new assembly): borrow the flags/defines/references of a Harness assembly of the same kind.
        $rsp = Find-Rsp $(if ($t.editorOnly) { 'Harness.Editor' } else { 'Harness.Runtime' })
        if (-not $rsp) { return @{ ok = $false; errors = @(New-CheckError "no compiler response file for $($t.assembly) (compile once in the Editor)" $t) } }
    }
    $dag = $rsp.DirectoryName.Substring($root.Length + 1).Replace('\', '/')
    $outDir = Join-Path $tmpRoot $t.assembly; New-Item -ItemType Directory -Force $outDir | Out-Null
    $dll = "$($outDir.Replace('\', '/'))/$($t.assembly).dll"
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($l in (Get-Content $rsp.FullName)) {
        if (-not $l -or $l -match '^-out:|^-refout:|^/?"?Assets/.*\.cs"?$|^".*\.cs"$') { continue }
        if ($l -match '^-r:"(?<p>[^"]+)"$') {
            $name = [IO.Path]::GetFileNameWithoutExtension($Matches.p) -replace '\.ref$', ''
            if ($built.ContainsKey($name)) { $l = "-r:`"$($built[$name])`"" }
            elseif ($synth -and $projectNames.ContainsKey($name)) { continue }   # the template's own project references
        }
        $lines.Add($l)
    }
    if ($synth) {
        foreach ($r in $t.refs) {
            $p = if ($built.ContainsKey($r)) { $built[$r] } elseif (Test-Path "$dag/$r.ref.dll") { "$dag/$r.ref.dll" } elseif (Test-Path "Library/ScriptAssemblies/$r.dll") { "Library/ScriptAssemblies/$r.dll" } else { $null }
            if ($p -and -not $lines.Contains("-r:`"$p`"")) { $lines.Add("-r:`"$p`"") }
        }
    }
    $lines.Add("-out:`"$dll`"")
    foreach ($s in $t.sources) { $lines.Add('"' + $s.Replace('\', '/') + '"') }
    $rspPath = Join-Path $outDir 'check.rsp'
    [IO.File]::WriteAllLines($rspPath, $lines, (New-Object Text.UTF8Encoding($false)))
    $out = & $script:Compiler.dotnet exec $script:Compiler.csc @($script:Compiler.flags) "@$rspPath" 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $errs = @(Parse-Errors $out $t.assembly)
    if ($code -ne 0 -and $errs.Count -eq 0) { $errs = @(New-CheckError (($out | Select-Object -First 5) -join ' | ') $t) }
    @{ ok = ($code -eq 0); errors = $errs; dll = $dll; synthesized = $synth }
}

function Invoke-MSBuildCheck($t) {
    $csproj = Join-Path $root "$($t.assembly).csproj"
    if (-not (Test-Path $csproj)) {
        $note = "missing $($t.assembly).csproj - run: tools/uc.ps1 harness_sync_csproj (new assembly: use -Backend csc)"
        return @{ ok = $false; note = $note; errors = @(New-CheckError $note $t) }
    }
    [xml]$x = Get-Content $csproj -Raw
    $ns = New-Object Xml.XmlNamespaceManager($x.NameTable); $ns.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
    foreach ($n in @($x.SelectNodes('//m:Compile', $ns))) { [void]$n.ParentNode.RemoveChild($n) }
    $ig = $x.CreateElement('ItemGroup', $x.Project.NamespaceURI); [void]$x.Project.AppendChild($ig)
    foreach ($s in $t.sources) { $c = $x.CreateElement('Compile', $x.Project.NamespaceURI); $c.SetAttribute('Include', $s); [void]$ig.AppendChild($c) }
    # Project references -> this run's DLL or the Editor's compiled DLL (no shared obj/ between parallel runs).
    foreach ($pr in @($x.SelectNodes('//m:ProjectReference', $ns))) {
        $refName = [IO.Path]::GetFileNameWithoutExtension($pr.GetAttribute('Include'))
        $dll = if ($built.ContainsKey($refName)) { $built[$refName] } else { "Library\ScriptAssemblies\$refName.dll" }
        if (-not (Test-Path $dll)) { continue }
        $r = $x.CreateElement('Reference', $x.Project.NamespaceURI); $r.SetAttribute('Include', $refName)
        $h = $x.CreateElement('HintPath', $x.Project.NamespaceURI); $h.InnerText = $dll; [void]$r.AppendChild($h)
        [void]$pr.ParentNode.AppendChild($r); [void]$pr.ParentNode.RemoveChild($pr)
    }
    $out = "Temp\compile-check\$runId\$($t.assembly)\"
    $pg = $x.CreateElement('PropertyGroup', $x.Project.NamespaceURI)
    foreach ($kv in @(@('OutputPath', "${out}bin\"), @('IntermediateOutputPath', "${out}obj\"), @('GenerateDocumentationFile', 'false'), @('ResolveAssemblyWarnOrErrorOnTargetArchitectureMismatch', 'None'))) {
        $e = $x.CreateElement($kv[0], $x.Project.NamespaceURI); $e.InnerText = $kv[1]; [void]$pg.AppendChild($e)
    }
    [void]$x.Project.AppendChild($pg)
    $tmpProj = Join-Path $root "$($t.assembly).cc-$runId.csproj"
    $x.Save($tmpProj)
    try {
        $lines = & $script:MSBuild $tmpProj -nologo -v:q -m -p:Configuration=Debug -p:PreferredUILang=en-US -clp:NoSummary 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    } finally { Remove-Item $tmpProj -Force -ErrorAction SilentlyContinue }
    $errs = @(Parse-Errors $lines $t.assembly)
    if ($code -ne 0 -and $errs.Count -eq 0) { $errs = @(New-CheckError (($lines | Where-Object { $_ -match 'error' }) -join ' | ') $t) }
    @{ ok = ($code -eq 0); errors = $errs; dll = (Join-Path $root "${out}bin\$($t.assembly).dll") }
}

$script:Compiler = $null
if ($Backend -eq 'msbuild') { $script:MSBuild = Get-MSBuild } else {
    $script:Compiler = Get-EditorCompiler
    foreach ($f in @($script:Compiler.dotnet, $script:Compiler.csc)) { if (-not (Test-Path -LiteralPath $f)) { throw "$f (from $($script:Compiler.graph)) not found: was the Editor that compiled this project uninstalled? Open the project again (tools/open.ps1)" } }
}

$results = @()
$allErrors = @()
if ($targets.Count -eq 0) {
    $allErrors += [ordered]@{ file = ''; line = 0; msg = "no assembly (asmdef) for module(s) '$($Module -join ',')' under $work\Assets\Game"; module = ''; assembly = '' }
}
foreach ($t in $targets) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $row = [ordered]@{ assembly = $t.assembly; module = $t.module }
    if ($t.dependency) { $row['dependency'] = $true }
    $badDeps = @($t.refs | Where-Object { $failed.ContainsKey($_) })
    if ($badDeps.Count -gt 0) {
        $failed[$t.assembly] = $true
        $row['ok'] = $false; $row['skipped'] = "dependency failed: $($badDeps -join ', ')"; $row['seconds'] = 0
        $results += $row
        continue
    }
    $r = if ($Backend -eq 'csc') { Invoke-CscCheck $t } else { Invoke-MSBuildCheck $t }
    if ($r.ok) { $built[$t.assembly] = $r.dll } else { $failed[$t.assembly] = $true }
    $allErrors += @($r.errors)
    $row['ok'] = $r.ok; $row['sources'] = @($t.sources).Count; $row['errors'] = @($r.errors).Count; $row['seconds'] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    if ($r.synthesized) { $row['synthesized'] = $true }
    if ($r.note) { $row['note'] = $r.note }
    $results += $row
}

Remove-Item $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
$report = [ordered]@{
    ok            = ($allErrors.Count -eq 0) -and ($results.Count -gt 0) -and (@($results | Where-Object { -not $_.ok }).Count -eq 0)
    backend       = $Backend
    compileErrors = @($allErrors)
    compiler      = if ($script:Compiler) { [ordered]@{ dotnet = $script:Compiler.dotnet.Replace('\', '/'); csc = $script:Compiler.csc; flags = @($script:Compiler.flags) } } else { $null }
    targets       = @($results)
    durationSec   = [math]::Round($total.Elapsed.TotalSeconds, 2)
}
if (Test-HarnessWorktree) { $report['sourceRoot'] = $work.Replace('\', '/'); $report['editorRoot'] = $root.Replace('\', '/') }
$report | ConvertTo-Json -Depth 6
exit $(if ($report.ok) { 0 } else { 1 })
