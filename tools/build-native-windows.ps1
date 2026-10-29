. "$PSScriptRoot/windows-env.ps1"

# Import the installed compiler instead of depending on a mutable setup action.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Install Visual Studio 2026 C++ build tools and the Windows SDK.' }
Import-Module (Join-Path $visualStudio 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $visualStudio -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'

$manifest = Get-Content tools/windows/vcpkg.json -Raw | ConvertFrom-Json
$vcpkg = Join-Path $PenguLODRoot '.tools/vcpkg'
if (-not (Test-Path "$vcpkg/.git")) {
    git init $vcpkg
    git -C $vcpkg remote add origin https://github.com/microsoft/vcpkg.git
}
git -C $vcpkg fetch --depth 1 origin $manifest.'builtin-baseline'
git -C $vcpkg checkout --detach FETCH_HEAD
& "$vcpkg/bootstrap-vcpkg.bat" -disableMetrics
$installed = Join-Path $PenguLODRoot '.tools/windows-deps'
& "$vcpkg/vcpkg.exe" install --triplet x64-windows-static --host-triplet x64-windows-static `
    "--x-manifest-root=$PenguLODRoot/tools/windows" "--x-install-root=$installed" --disable-metrics
$cmakeDependencies = @(
    "-DCMAKE_TOOLCHAIN_FILE=$vcpkg/scripts/buildsystems/vcpkg.cmake",
    '-DVCPKG_MANIFEST_MODE=OFF', "-DVCPKG_INSTALLED_DIR=$installed",
    '-DVCPKG_TARGET_TRIPLET=x64-windows-static', '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded',
    '-DCMAKE_BUILD_TYPE=Release'
)
python tools/fasterngio/prepare.py --platform win-x64
cmake -S .tools/FasterNGIO-win -B .tools/fasterngio-win-build -G Ninja @cmakeDependencies `
    -DFASTERNGIO_ENABLE_GPU=OFF -DFASTERNGIO_HEADLESS=ON -DBASICTELEMETRY_ENABLE_TRACY=OFF -DFASTERNGIO_BUILD_TESTS=ON
cmake --build .tools/fasterngio-win-build --parallel 4
ctest --test-dir .tools/fasterngio-win-build --output-on-failure --parallel 4 --output-junit "$PenguLODRoot/.artifacts/native-windows.xml"
cmake --install .tools/fasterngio-win-build --component FasterNGIO --prefix "$PenguLODRoot/.tools/fasterngio-runtime-win-x64"
python tools/fasterngio/package.py --platform win-x64

cmake -S native/texture-compute -B .tools/texture-compute-win-build -G Ninja @cmakeDependencies
cmake --build .tools/texture-compute-win-build --parallel 4
New-Item -ItemType Directory -Force .tools/texture-compute/win-x64 | Out-Null
Copy-Item .tools/texture-compute-win-build/pengulod_compute.dll .tools/texture-compute/win-x64/
