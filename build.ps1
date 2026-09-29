$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'build/Build.csproj'
Push-Location $PSScriptRoot
try {
    & dotnet run --project $project --configuration Release --disable-build-servers -- @args
    $buildExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
exit $buildExitCode
