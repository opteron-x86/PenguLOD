# Build a game-runtime DLL without installing it into a game or mod manager.
param([ValidateSet('1','17','vr','all')][string]$Line = '1')
$lines = if ($Line -eq 'all') { @('1','17','vr') } else { @($Line) }
foreach ($which in $lines) { & "$PSScriptRoot/../tools/build-runtime-windows.ps1" -Line $which }
