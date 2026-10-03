# Builds the files to share with other people:
#   dist\vibeRacingOverlays-Setup-<version>.exe      installer (per-user, no admin rights, no .NET install needed)
#   dist\vibeRacingOverlays-<version>-portable.zip   single exe, runs without installing
# Usage:  .\build-release.ps1 [-Version 1.0.1]
param([string]$Version = "1.0.0")

$ErrorActionPreference = 'Stop'
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } else { 'C:\Program Files\dotnet\dotnet.exe' }
$root = $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$dist = Join-Path $root 'dist'
if (Test-Path $artifacts) { [IO.Directory]::Delete($artifacts, $true) }
New-Item -ItemType Directory -Force $artifacts, $dist | Out-Null

Write-Host "1/3 Publishing the app (self-contained single file, win-x64)..."
& $dotnet publish (Join-Path $root 'src\vibeRacingOverlays.App') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:ReleaseBuild=true -p:Version=$Version -o (Join-Path $artifacts 'app') -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "App publish failed" }
$appExe = Join-Path $artifacts 'app\vibeRacingOverlays.exe'

Write-Host "2/3 Building the uninstaller and the installer..."
$setupProj = Join-Path $root 'src\vibeRacingOverlays.Setup'
& $dotnet build $setupProj -c Release -p:Version=$Version -o (Join-Path $artifacts 'uninstaller') -v q -nologo --no-incremental
if ($LASTEXITCODE -ne 0) { throw "Uninstaller build failed" }
$uninstallerExe = Join-Path $artifacts 'uninstall.exe'
Copy-Item (Join-Path $artifacts 'uninstaller\vibeRacingOverlays-Setup.exe') $uninstallerExe
# the uninstaller must not contain the app (stale build output would make every install twice as large)
if ((Get-Item $uninstallerExe).Length -gt 1MB) { throw "Uninstaller unexpectedly contains the app payload" }
& $dotnet build $setupProj -c Release -p:PayloadPath="$appExe" -p:UninstallerPath="$uninstallerExe" -p:Version=$Version `
    -o (Join-Path $artifacts 'setup') -v q -nologo --no-incremental
if ($LASTEXITCODE -ne 0) { throw "Setup build failed" }

Write-Host "3/3 Packaging..."
$setupOut = Join-Path $dist "vibeRacingOverlays-Setup-$Version.exe"
Copy-Item (Join-Path $artifacts 'setup\vibeRacingOverlays-Setup.exe') $setupOut -Force

$portableDir = Join-Path $artifacts 'portable'
New-Item -ItemType Directory -Force $portableDir | Out-Null
Copy-Item $appExe $portableDir
Copy-Item (Join-Path $root 'dist-readme.txt') (Join-Path $portableDir 'README.txt')
# licences of the bundled widget fonts (SIL OFL: they go with every copy; the installer's exe carries them in the font files)
Copy-Item (Join-Path $root 'src\vibeRacingOverlays.App\Fonts\FONT-LICENSES.txt') $portableDir
$zip = Join-Path $dist "vibeRacingOverlays-$Version-portable.zip"
if (Test-Path $zip) { [IO.File]::Delete($zip) }
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zip

Get-ChildItem $dist | Where-Object { $_.Name -like "*$Version*" } | ForEach-Object { "{0,-45} {1,6:N1} MB" -f $_.Name, ($_.Length / 1MB) }
