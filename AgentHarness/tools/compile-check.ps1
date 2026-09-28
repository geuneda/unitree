<#
.SYNOPSIS
  Compile-check game modules WITHOUT the Unity Editor (safe to run from many agents in parallel).

  Each module assembly (asmdef under Assets/Game/<Module>/, incl. <Module>/Builders) is compiled alone
  against the DLLs of its dependencies from the Editor's last successful compile. Source lists are
  re-globbed from disk, so files an agent just created are included. Every run uses its own temp dir.

  Backends:
    msbuild (default) -Unity-generated <Asm>.csproj (tools/uc.ps1 harness_sync_csproj once), rewritten
                        per run; needs Visual Studio 2022 / Build Tools MSBuild (no .NET SDK needed).
    csc               -Unity's own compiler response file (Library/Bee/artifacts/*/<Asm>.rsp) +
                        the Roslyn bundled with the Editor install. Identical flags/analyzers to the Editor.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Module Smoke
  powershell -ExecutionPolicy Bypass -File tools/compile-check.ps1 -Backend csc
  -> JSON { ok, backend, compileErrors:[{file,line,msg,module,assembly}], targets:[...], durationSec }

.NOTES
  Staleness: dependencies (other modules, Harness) are the Editor's last compiled DLLs. A change to another
  module's public API is only seen after that module is compiled by the Editor (loop.ps1).
#>
param(
    [string[]]$Module,
    [ValidateSet('msbuild', 'csc')][string]$Backend = 'msbuild',
    [switch]$IncludeHarness
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$env:VSLANG = '1033'   # English compiler messages
$total = [Diagnostics.Stopwatch]::StartNew()
$runId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$tmpRoot = Join-Path $root "Temp\compile-check\$runId"
New-Item -ItemType Directory -Force $tmpRoot | Out-Null

function Get-EditorPath {
    $ver = (Get-Content (Join-Path $root 'ProjectSettings\ProjectVersion.txt') | Select-String 'm_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
    $candidates = @("C:\Program Files\Unity\Hub\Editor\$ver\Editor", "C:\Program Files\Unity\Hub\Editor\$ver-x86_64\Editor")
    foreach ($c in $candidates) { if (Test-Path "$c\Unity.exe") { return $c } }
    $json = & unity editors --installed --format json --no-banner 2>$null | ConvertFrom-Json
    $e = $json.data | Where-Object { $_.version -eq $ver } | Select-Object -First 1
    if ($e) { return (Split-Path -Parent $e.location) }
    throw "Unity $ver not found"
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
$asmdefs = @(Get-ChildItem (Join-Path $root 'Assets\Game') -Recurse -Filter *.asmdef)
if ($IncludeHarness) { $asmdefs += @(Get-ChildItem (Join-Path $root 'Assets\Harness') -Recurse -Filter *.asmdef) }
$targets = @()
foreach ($a in $asmdefs) {
    $name = (Get-Content $a.FullName -Raw | ConvertFrom-Json).name
    $dir = $a.DirectoryName
    $mod = ModuleOf ($a.FullName.Substring($root.Length + 1))
    if ($Module -and $mod -notin $Module) { continue }
    $nested = @($asmdefs | Where-Object { $_.DirectoryName -ne $dir -and $_.DirectoryName.StartsWith($dir + '\') } | ForEach-Object { $_.DirectoryName + '\' })
    $sources = @(Get-ChildItem $dir -Recurse -Filter *.cs | Where-Object { $f = $_.FullName; -not ($nested | Where-Object { $f.StartsWith($_) }) } |
        ForEach-Object { $_.FullName.Substring($root.Length + 1) })
    $targets += [pscustomobject]@{ assembly = $name; module = $mod; sources = $sources }
}

$results = @()
$allErrors = @()

if ($Backend -eq 'msbuild') {
    $msbuild = Get-MSBuild
    foreach ($t in $targets) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $csproj = Join-Path $root "$($t.assembly).csproj"
        if (-not (Test-Path $csproj)) {
            $results += [ordered]@{ assembly = $t.assembly; ok = $false; seconds = 0; note = "missing $($t.assembly).csproj - run: tools/uc.ps1 harness_sync_csproj (Editor must be open once)" }
            $allErrors += [ordered]@{ file = ''; line = 0; msg = "missing $($t.assembly).csproj (run harness_sync_csproj)"; module = $t.module; assembly = $t.assembly }
            continue
        }
        [xml]$x = Get-Content $csproj -Raw
        $ns = New-Object Xml.XmlNamespaceManager($x.NameTable); $ns.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
        foreach ($n in @($x.SelectNodes('//m:Compile', $ns))) { [void]$n.ParentNode.RemoveChild($n) }
        $ig = $x.CreateElement('ItemGroup', $x.Project.NamespaceURI); [void]$x.Project.AppendChild($ig)
        foreach ($s in $t.sources) { $c = $x.CreateElement('Compile', $x.Project.NamespaceURI); $c.SetAttribute('Include', $s); [void]$ig.AppendChild($c) }
        # Project references -> the Editor's compiled DLLs (no shared obj/ between parallel runs).
        foreach ($pr in @($x.SelectNodes('//m:ProjectReference', $ns))) {
            $refName = [IO.Path]::GetFileNameWithoutExtension($pr.GetAttribute('Include'))
            $dll = "Library\ScriptAssemblies\$refName.dll"
            if (-not (Test-Path (Join-Path $root $dll))) { continue }
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
            $lines = & $msbuild $tmpProj -nologo -v:q -m -p:Configuration=Debug -p:PreferredUILang=en-US -clp:NoSummary 2>&1 | ForEach-Object { "$_" }
            $code = $LASTEXITCODE
        } finally { Remove-Item $tmpProj -Force -ErrorAction SilentlyContinue }
        $errs = @(Parse-Errors $lines $t.assembly)
        if ($code -ne 0 -and $errs.Count -eq 0) { $errs = @([ordered]@{ file = ''; line = 0; msg = (($lines | Where-Object { $_ -match 'error' }) -join ' | '); module = $t.module; assembly = $t.assembly }) }
        $allErrors += $errs
        $results += [ordered]@{ assembly = $t.assembly; module = $t.module; ok = ($code -eq 0); sources = $t.sources.Count; errors = $errs.Count; seconds = [math]::Round($sw.Elapsed.TotalSeconds, 2) }
    }
} else {
    $editor = Get-EditorPath
    $dotnet = Join-Path $editor 'Data\NetCoreRuntime\dotnet.exe'
    $csc = Join-Path $editor 'Data\DotNetSdkRoslyn\csc.dll'
    foreach ($t in $targets) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $rsp = Get-ChildItem (Join-Path $root 'Library\Bee\artifacts') -Recurse -Filter "$($t.assembly).rsp" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $rsp) {
            $results += [ordered]@{ assembly = $t.assembly; ok = $false; seconds = 0; note = 'no Bee .rsp yet - the Editor has never compiled this assembly' }
            $allErrors += [ordered]@{ file = ''; line = 0; msg = "no compiler response file for $($t.assembly) (compile once in the Editor)"; module = $t.module; assembly = $t.assembly }
            continue
        }
        $outDir = Join-Path $tmpRoot $t.assembly; New-Item -ItemType Directory -Force $outDir | Out-Null
        $keep = Get-Content $rsp.FullName | Where-Object { $_ -and $_ -notmatch '^-out:|^-refout:|^/?"?Assets/.*\.cs"?$|^".*\.cs"$' }
        $newRsp = @($keep) + @("-out:`"$($outDir.Replace('\','/'))/$($t.assembly).dll`"") + @($t.sources | ForEach-Object { '"' + $_.Replace('\', '/') + '"' })
        $rspPath = Join-Path $outDir 'check.rsp'
        [IO.File]::WriteAllLines($rspPath, $newRsp, (New-Object Text.UTF8Encoding($false)))
        $lines = & $dotnet exec $csc /noconfig /shared "@$rspPath" 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
        $errs = @(Parse-Errors $lines $t.assembly)
        if ($code -ne 0 -and $errs.Count -eq 0) { $errs = @([ordered]@{ file = ''; line = 0; msg = (($lines | Select-Object -First 5) -join ' | '); module = $t.module; assembly = $t.assembly }) }
        $allErrors += $errs
        $results += [ordered]@{ assembly = $t.assembly; module = $t.module; ok = ($code -eq 0); sources = $t.sources.Count; errors = $errs.Count; seconds = [math]::Round($sw.Elapsed.TotalSeconds, 2) }
    }
}

Remove-Item $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
$report = [ordered]@{
    ok            = ($allErrors.Count -eq 0) -and ($results.Count -gt 0)
    backend       = $Backend
    compileErrors = @($allErrors)
    targets       = @($results)
    durationSec   = [math]::Round($total.Elapsed.TotalSeconds, 2)
}
$report | ConvertTo-Json -Depth 6
exit $(if ($report.ok) { 0 } else { 1 })
