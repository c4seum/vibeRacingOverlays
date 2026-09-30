# Prints the CHANGELOG.md section of one version (used as the GitHub release notes by release.yml).
# Usage:  .\tools\changelog-section.ps1 -Version 1.1.0 [-Out notes.md]
param([Parameter(Mandatory = $true)][string]$Version, [string]$Out)

$ErrorActionPreference = 'Stop'
$changelog = Join-Path $PSScriptRoot '..\CHANGELOG.md'
$lines = [IO.File]::ReadAllLines($changelog)
$section = New-Object System.Collections.Generic.List[string]
$inside = $false
foreach ($line in $lines) {
    if ($line -match '^## \[') {
        if ($inside) { break }
        $inside = $line -match ('^## \[' + [regex]::Escape($Version) + '\]')
        continue
    }
    if ($line -match '^\[[^\]]+\]: ') { if ($inside) { break } else { continue } }   # link references at the end
    if ($inside) { $section.Add($line) }
}
$text = ($section -join "`n").Trim()
if (-not $text) { Write-Warning "No CHANGELOG.md section for $Version" }
if ($Out) { [IO.File]::WriteAllText($Out, $text + "`n", (New-Object Text.UTF8Encoding $false)) } else { $text }
