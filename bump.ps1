# Set the independent PenguLOD release version. The game plugin keeps UPSTREAM_VERSION.
# Example: .\bump.ps1 0.1.0-alpha.2
param([Parameter(Mandatory=$true)][string]$Version)
& python3 (Join-Path $PSScriptRoot "tools/version.py") $Version
if ($LASTEXITCODE -ne 0) { throw "Version update failed." }
