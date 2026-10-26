# Builds the helper exe, then the plugin DLL that embeds it. Usage: .\build.ps1 [-Release] [-GamePath "<Gorilla Tag folder>"]
param([switch]$Release, [string]$GamePath = $env:GamePath)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$csc = Join-Path (Split-Path $msbuild) 'Roslyn\csc.exe'
$ra = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$app = Join-Path $root 'app'
$out = Join-Path $root 'bin\helper'
New-Item -ItemType Directory -Force $out | Out-Null
$dbg = if ($Release) { '/debug-' } else { '/debug:pdbonly' }
$exe = Join-Path $out 'ValyxJarvis.exe'
& $csc /nologo /deterministic /langversion:7.3 /target:winexe /platform:anycpu /optimize+ $dbg "/out:$exe" `
    /r:System.Web.Extensions.dll /r:System.Net.Http.dll /r:System.Security.dll /r:System.Windows.Forms.dll /r:"$ra\System.Speech.dll" `
    (Join-Path $app 'Core.cs') (Join-Path $app 'Store.cs') (Join-Path $app 'Net.cs') (Join-Path $app 'Voice.cs') (Join-Path $app 'Assistant.cs') (Join-Path $app 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw "helper build failed" }
$cfg = if ($Release) { 'Release' } else { 'Debug' }
$extra = if ($Release) { @('/p:DebugType=none', '/p:CI=TRUE') } else { @() }
& $msbuild (Join-Path $root 'ValyxJarvis.csproj') /nologo /v:minimal /p:Configuration=$cfg /p:GamePath="$GamePath" @extra
if ($LASTEXITCODE -ne 0) { throw "plugin build failed" }
Get-Item (Join-Path $out 'ValyxJarvis.exe'), (Join-Path $root "bin\$cfg\ValyxJarvis.dll") | Select-Object Name, Length, LastWriteTime