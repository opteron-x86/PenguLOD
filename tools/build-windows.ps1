param([switch]$ReuseBackend)
. "$PSScriptRoot/windows-env.ps1"
if (-not $ReuseBackend) { & "$PSScriptRoot/build-native-windows.ps1" }
foreach ($required in @('.tools/fasterngio-runtime-win-x64/FasterNGIO.exe', '.tools/fasterngio-runtime-win-x64/FasterNGIO-source.tar.gz', '.tools/texture-compute/win-x64/pengulod_compute.dll')) {
    if (-not (Test-Path $required)) { throw "Missing native build: $required" }
}
$version = (Get-Content VERSION -Raw).Trim()
$package = Join-Path $PenguLODRoot 'dist/win-x64'
if (Test-Path $package) { Remove-Item $package -Recurse -Force }
New-Item -ItemType Directory -Force $package | Out-Null
foreach ($app in @('Cli', 'Desktop')) {
    dotnet publish "src/AnvilLOD.$app/AnvilLOD.$app.csproj" -c Release -r win-x64 --self-contained true `
        -o $package --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false
}
Copy-Item LICENSE.txt, VERSION, UPSTREAM_VERSION $package
Copy-Item assets/pengulod-256.png "$package/pengulod.png"
Copy-Item packaging/README-WINDOWS.md $package
New-Item -ItemType Directory -Force "$package/docs" | Out-Null
foreach ($guide in @('USER-GUIDE', 'GRASS', 'TEXTURES', 'TERRAIN', 'TREES', 'MATERIALS', 'SEASONS', 'ATLASES', 'PIPELINE', 'CLI', 'DIAGNOSTICS', 'TROUBLESHOOTING', 'COMPUTE', 'PACKAGING', 'PROVENANCE', 'ROADMAP')) {
    Copy-Item "docs/$guide.md" "$package/docs/"
}
$env:PENGULOD_PACKAGE_DIR = $package
$env:PENGULOD_RUNTIME = 'win-x64'
$env:ANVILLOD_SDK_DIR = Split-Path (Get-Command dotnet).Source -Parent
python tools/collect-licenses.py
# Retain licenses for the compute helper's native dependencies as well.
Copy-Item .tools/fasterngio-runtime-win-x64/licenses "$package/licenses/native" -Recurse
& "$package/PenguLOD.exe" --version
& "$package/PenguLOD.Desktop.exe" --version
python tools/package-windows.py
