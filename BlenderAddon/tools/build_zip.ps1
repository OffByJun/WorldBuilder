# Builds the distributable Blender extension zip.
# Usage:  powershell -File build_zip.ps1 [-Output <path>]
param(
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot   # BlenderAddon/

$manifest = Get-Content (Join-Path $root "blender_manifest.toml") -Raw
if ($manifest -match '(?m)^version\s*=\s*"([^"]+)"') { $version = $Matches[1] } else { $version = "dev" }
if (-not $Output) { $Output = Join-Path (Split-Path -Parent $root) "WorldBuilder-BlenderAddon-v$version.zip" }

$stage = Join-Path $env:TEMP ("wb_addon_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stage | Out-Null

robocopy $root $stage /E /XD __pycache__ /XF "*.meta" "*.pyc" /NFL /NDL /NJH /NJS | Out-Null
if (Test-Path $Output) { Remove-Item $Output -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $Output -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

Write-Host "Built: $Output"
