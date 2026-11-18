param(
    [string]$Configuration = "Release",
    [string]$Version = "1.1.0",
    [string]$OpenUtauCorePath = $env:OPENUTAU_CORE_PATH
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$plugins = @(
    "src/OpenUtau.Plugin.ConsonantLength/OpenUtau.Plugin.ConsonantLength.csproj",
    "src/OpenUtau.Plugin.CVVCExpression/OpenUtau.Plugin.CVVCExpression.csproj"
)

foreach ($project in $plugins) {
    $buildArgs = @("build", $project, "-c", $Configuration)
    if ($OpenUtauCorePath) {
        $buildArgs += "-p:OpenUtauCorePath=$OpenUtauCorePath"
    }
    & dotnet $buildArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed: $project" }
}

& dotnet run --project tests/ConsonantLength.Tests/ConsonantLength.Tests.csproj -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "consonant length tests failed" }

& dotnet run --project tests/ExpressionEditor.Tests/ExpressionEditor.Tests.csproj -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "expression editor tests failed" }

$dist = "dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
foreach ($dll in @(
    "src/OpenUtau.Plugin.ConsonantLength/bin/$Configuration/OpenUtau.Plugin.ConsonantLength.dll",
    "src/OpenUtau.Plugin.CVVCExpression/bin/$Configuration/OpenUtau.Plugin.CVVCExpression.dll"
)) {
    if (-not (Test-Path $dll)) { throw "missing $dll" }
    Copy-Item $dll $dist
}
Copy-Item README.md $dist
Copy-Item LICENSE $dist

$zip = Join-Path $dist "OpenUtau-Plugins-$Version.zip"
Compress-Archive -Path (Join-Path $dist "*") -DestinationPath $zip -Force
Write-Host ""
Write-Host "Done: $zip"
Write-Host "Copy both DLLs into <OpenUtau>/Plugins/ and restart OpenUtau."
