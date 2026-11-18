param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0",
    [string]$OpenUtauCorePath = $env:OPENUTAU_CORE_PATH
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$buildArgs = @(
    "build",
    "src/OpenUtau.Plugin.ConsonantLength/OpenUtau.Plugin.ConsonantLength.csproj",
    "-c", $Configuration
)
if ($OpenUtauCorePath) {
    $buildArgs += "-p:OpenUtauCorePath=$OpenUtauCorePath"
}
& dotnet $buildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

& dotnet run --project tests/ConsonantLength.Tests/ConsonantLength.Tests.csproj -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "tests failed" }

$dll = "src/OpenUtau.Plugin.ConsonantLength/bin/$Configuration/OpenUtau.Plugin.ConsonantLength.dll"
$dist = "dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item $dll $dist
Copy-Item README.md $dist
Copy-Item LICENSE $dist

$zip = Join-Path $dist "OpenUtau.Plugin.ConsonantLength-$Version.zip"
Compress-Archive -Path (Join-Path $dist "*") -DestinationPath $zip -Force
Write-Host ""
Write-Host "Done: $zip"
Write-Host "Copy OpenUtau.Plugin.ConsonantLength.dll into <OpenUtau>/Plugins/ and restart OpenUtau."
