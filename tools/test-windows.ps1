. "$PSScriptRoot/windows-env.ps1"
$env:PENGULOD_SCREENSHOTS = Join-Path $PenguLODRoot '.artifacts/screenshots'
foreach ($suite in @('Core', 'Desktop')) {
    dotnet test "tests/PenguLOD.$suite.Tests/PenguLOD.$suite.Tests.csproj" -c Release `
        --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false `
        --logger "trx;LogFileName=$suite-windows.trx" --results-directory .artifacts/test-results
}
