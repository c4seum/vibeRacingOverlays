# Builds the local test version (DEV build) into .\publish and keeps a "vibeRacingOverlays DEV" Start menu shortcut.
# The DEV build has its own settings folder (%APPDATA%\vibeRacingOverlays-dev), so it never touches the
# layouts of an installed release. Close the DEV app before running this.
$ErrorActionPreference = 'Stop'
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } else { 'C:\Program Files\dotnet\dotnet.exe' }
$root = $PSScriptRoot
$publish = Join-Path $root 'publish'

& $dotnet publish (Join-Path $root 'src\vibeRacingOverlays.App') -c Release -o $publish -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed (is the DEV app still running?)" }
$exe = Join-Path $publish 'vibeRacingOverlays.exe'

$programs = [Environment]::GetFolderPath('Programs')
$sh = New-Object -ComObject WScript.Shell
# older setups used "vibeRacingOverlays.lnk" for this build; that name belongs to the installed release now
$legacy = Join-Path $programs 'vibeRacingOverlays.lnk'
if ((Test-Path $legacy) -and ($sh.CreateShortcut($legacy).TargetPath -eq $exe)) { [IO.File]::Delete($legacy) }

$lnk = $sh.CreateShortcut((Join-Path $programs 'vibeRacingOverlays DEV.lnk'))
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $publish
$lnk.IconLocation = "$exe,0"
$lnk.Description = 'vibeRacingOverlays DEV (local test build)'
$lnk.Save()
Write-Host "DEV build published to $publish (Start menu: vibeRacingOverlays DEV)"
