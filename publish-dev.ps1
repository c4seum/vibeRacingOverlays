# Builds the local test version (DEV build) into .\publish and keeps a "vibeRacingOverlays DEV" Start menu shortcut.
# The DEV build has its own settings folder (%APPDATA%\vibeRacingOverlays-dev), so it never touches the
# layouts of an installed release. A running DEV app (from .\publish) is asked to exit (it saves first) and
# started again afterwards.
$ErrorActionPreference = 'Stop'
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } else { 'C:\Program Files\dotnet\dotnet.exe' }
$root = $PSScriptRoot
$publish = Join-Path $root 'publish'
$exe = Join-Path $publish 'vibeRacingOverlays.exe'

# closing the window only hides the app to the tray: use the app's exit request (App.ExitEventName), else close its window
$running = @(Get-Process vibeRacingOverlays -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
foreach ($p in $running) {
    $asked = $false
    try { $ev = [Threading.EventWaitHandle]::OpenExisting("Local\vibeRacingOverlays-exit-$($p.Id)"); [void]$ev.Set(); $ev.Dispose(); $asked = $true } catch { }
    if (-not $asked) { [void]$p.CloseMainWindow() }
    if (-not $p.WaitForExit(15000)) { throw "The DEV app (process $($p.Id)) didn't exit; close it and run this again" }
    Write-Host "Closed the running DEV app"
}

& $dotnet publish (Join-Path $root 'src\vibeRacingOverlays.App') -c Release -o $publish -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

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
if ($running.Count -gt 0) { Start-Process $exe -WorkingDirectory $publish; Write-Host "Started the DEV app again" }
