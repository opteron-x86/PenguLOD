. "$PSScriptRoot/windows-env.ps1"
$env:ANVILLOD_SCREENSHOTS = Join-Path $PenguLODRoot '.artifacts/screenshots'
foreach ($suite in @('Core', 'Desktop')) {
    dotnet test "tests/AnvilLOD.$suite.Tests/AnvilLOD.$suite.Tests.csproj" -c Release `
        --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false `
        --logger "trx;LogFileName=$suite-windows.trx" --results-directory .artifacts/test-results
}
