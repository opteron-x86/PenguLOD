param([string]$Package = 'dist/win-x64')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$Package = (Resolve-Path $Package).Path
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("PenguLOD smoke forêt " + [guid]::NewGuid())
New-Item -ItemType Directory $temporary | Out-Null
try {
    & "$Package/PenguLOD.exe" --version
    & "$Package/PenguLOD.exe" --help
    & "$Package/PenguLOD.exe" compute-info --report "$temporary/compute.json"
    & "$Package/backends/fasterngio/FasterNGIO.exe" --help
    # Launch the real Win32/Avalonia window, without touching an existing user's settings.
    $process = Start-Process "$Package/PenguLOD.Desktop.exe" -ArgumentList @('--settings', "`"$temporary/settings.json`"", '--smoke-test') -PassThru
    if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw 'Desktop startup timed out.' }
    if ($process.ExitCode -ne 0) { throw "Desktop startup failed: $($process.ExitCode)" }
} finally { Remove-Item $temporary -Recurse -Force }
