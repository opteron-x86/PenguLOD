# Shared environment for native Windows builds; PowerShell 7.4 or newer.
#Requires -Version 7.4
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if (-not $IsWindows) { throw 'Run this script on Windows x64.' }
$PenguLODRoot = Split-Path $PSScriptRoot -Parent
Set-Location $PenguLODRoot
$env:DOTNET_CLI_HOME = Join-Path $PenguLODRoot '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $PenguLODRoot '.tools/nuget'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:PYTHONUTF8 = '1'
New-Item -ItemType Directory -Force .tools, .artifacts | Out-Null
