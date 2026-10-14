<#
  Packs VL.Interpole as a vvvv nuget.

  Run from anywhere:  ./nuget/pack.ps1   (optionally: ./nuget/pack.ps1 -Version 0.2.0)

  It (1) builds Release, (2) refreshes the staged payload under nuget/ (the folder that mirrors the
  final package layout 1:1), (3) regenerates nuget/VL.Interpole.vl from the dev VL.Interpole.vl —
  rewriting the dev DLL path to the in-package ./lib/<tfm>/ path while preserving every other edit
  (e.g. the forwarded-dependency flag) — and (4) packs the .nupkg into the repo root.

  Only VL.Interpole.nuspec and this script are source; everything else under nuget/ is generated
  (see nuget/.gitignore).
#>
param([string]$Version)

$ErrorActionPreference = 'Stop'
$stage = $PSScriptRoot                                   # the nuget/ folder
$root  = Split-Path $stage -Parent                       # repo root
$proj  = Join-Path $root 'VL.Interpole\VL.Interpole.csproj'
$tfm   = 'net8.0-windows7.0'
$built = Join-Path $root 'VL.Interpole\bin\Release\net8.0-windows'
$devDllPath = './VL.Interpole/bin/Release/net8.0-windows/VL.Interpole.dll'   # path used in the dev .vl
$libDllPath = "./lib/$tfm/VL.Interpole.dll"                                   # path inside the package

# 1. Build Release
dotnet build $proj -c Release

# 2. Refresh staged payload (lib + README)
$libDir = Join-Path $stage "lib\$tfm"
New-Item -ItemType Directory -Force -Path $libDir | Out-Null
Copy-Item (Join-Path $built 'VL.Interpole.dll') $libDir -Force
Copy-Item (Join-Path $built 'VL.Interpole.xml') $libDir -Force
Copy-Item (Join-Path $root  'README.md')        $stage  -Force

# 3. Regenerate the package .vl from the dev .vl (only the DLL path differs)
$vl = Get-Content (Join-Path $root 'VL.Interpole.vl') -Raw
$vl = $vl.Replace($devDllPath, $libDllPath)
if ($vl -notmatch [regex]::Escape($libDllPath)) {
    throw "Could not rewrite the DLL path in VL.Interpole.vl - check its PlatformDependency Location still reads '$devDllPath'."
}
Set-Content (Join-Path $stage 'VL.Interpole.vl') $vl -Encoding UTF8

# 4. Optionally bump the version in the nuspec
if ($Version) {
    $nuspecPath = Join-Path $stage 'VL.Interpole.nuspec'
    $nuspec = Get-Content $nuspecPath -Raw
    $nuspec = [regex]::Replace($nuspec, '<version>.*?</version>', "<version>$Version</version>")
    Set-Content $nuspecPath $nuspec -Encoding UTF8
}

# 5. Pack into the repo root
$nuspecFile = Join-Path $stage 'VL.Interpole.nuspec'
dotnet pack $proj -c Release --no-build `
    "-p:NuspecFile=$nuspecFile" `
    "-p:NuspecBasePath=$stage" `
    -o $root

Write-Host "`nDone. Push with:" -ForegroundColor Green
Write-Host "  dotnet nuget push VL.Interpole.<version>.nupkg --source https://api.nuget.org/v3/index.json --api-key YOUR_KEY"
