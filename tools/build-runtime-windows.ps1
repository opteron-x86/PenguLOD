param([ValidateSet('1','17','vr')][string]$Line = '1')
. "$PSScriptRoot/windows-env.ps1"
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Install Visual Studio C++ build tools and the Windows SDK.' }
Import-Module (Join-Path $visualStudio 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $visualStudio -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'

$manifestDir = switch ($Line) { '1' { 'skse' } '17' { 'skse/cmake/manifest-17' } 'vr' { 'skse/cmake/manifest-vr' } }
$manifest = Get-Content "$manifestDir/vcpkg.json" -Raw | ConvertFrom-Json
$vcpkg = Join-Path $PenguLODRoot ".tools/runtime-vcpkg-$Line"
if (-not (Test-Path "$vcpkg/.git")) {
    git init $vcpkg
    git -C $vcpkg remote add origin https://github.com/microsoft/vcpkg.git
}
git -C $vcpkg fetch --depth 1 origin $manifest.'builtin-baseline'
git -C $vcpkg checkout --detach FETCH_HEAD
& "$vcpkg/bootstrap-vcpkg.bat" -disableMetrics
# Build from source so every package includes the matching CommonLib sources.
$env:VCPKG_BINARY_SOURCES = 'clear'
$installed = Join-Path $PenguLODRoot ".tools/runtime-deps-$Line"
$build = Join-Path $PenguLODRoot ".tools/runtime-build-$Line"
$runtimeLine = if ($Line -eq 'vr') { '14' } else { $Line }
$options = @("-DCMAKE_TOOLCHAIN_FILE=$vcpkg/scripts/buildsystems/vcpkg.cmake",
    "-DVCPKG_MANIFEST_DIR=$PenguLODRoot/$manifestDir", "-DVCPKG_INSTALLED_DIR=$installed",
    '-DVCPKG_TARGET_TRIPLET=x64-windows-static-md', '-DCMAKE_BUILD_TYPE=Release',
    "-DPENGULOD_RUNTIME_LINE=$runtimeLine")
if ($Line -ne '1') { $options += "-DVCPKG_OVERLAY_PORTS=$PenguLODRoot/skse/cmake/ports-$Line" }
cmake -S skse -B $build -G Ninja @options
cmake --build $build --parallel 3
$folder = switch ($Line) { '1' { '1.5.97-1.6.1170' } '17' { '1.7.x' } 'vr' { '1.4.15-VR' } }
$out = Join-Path $PenguLODRoot "dist/runtime/$folder"
New-Item -ItemType Directory -Force "$out/SKSE/Plugins", "$out/licenses", "$out/source" | Out-Null
Copy-Item "$build/PenguLOD.dll" "$out/SKSE/Plugins/"
foreach ($package in Get-ChildItem "$installed/x64-windows-static-md/share" -Directory) {
    if (Test-Path "$($package.FullName)/copyright") {
        Copy-Item "$($package.FullName)/copyright" "$out/licenses/$($package.Name).txt"
    }
}
Copy-Item "$installed/vcpkg/status" "$out/licenses/vcpkg-packages.txt"
$commonlib = @(Get-ChildItem "$vcpkg/buildtrees/commonlibsse-ng/src" -Directory | Where-Object { Test-Path "$($_.FullName)/CMakeLists.txt" })
if ($commonlib.Count -ne 1) { throw 'Expected exactly one matching CommonLib source tree for the source archive.' }
tar -czf "$out/source/CommonLibSSE-NG.tar.gz" -C $commonlib[0].FullName .
Write-Host "Built PenguLOD runtime for $folder into $out"
