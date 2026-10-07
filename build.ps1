<#
.SYNOPSIS
    Builds, tests and packages PhraseFlow.

.DESCRIPTION
    Produces in .\dist:
      PhraseFlow-<version>-win-x64.zip        self-contained single-file exe (no .NET install needed)
      PhraseFlow-<version>-win-arm64.zip      self-contained single-file exe for Windows on ARM
      PhraseFlow-<version>-framework-dependent.zip  small build that needs the .NET 10 Desktop Runtime

.EXAMPLE
    ./build.ps1                # test + package everything
    ./build.ps1 -SkipTests -Runtimes win-x64
#>
param(
    [string[]] $Runtimes = @('win-x64', 'win-arm64'),
    [switch] $SkipTests,
    [switch] $SkipFrameworkDependent
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$project = 'src/PhraseFlow.App/PhraseFlow.App.csproj'
[xml]$props = Get-Content 'Directory.Build.props'
$version = $props.Project.PropertyGroup.Version
$dist = Join-Path $PSScriptRoot 'dist'
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $dist | Out-Null

if (-not $SkipTests) {
    Write-Host '==> Running unit tests' -ForegroundColor Cyan
    dotnet test --project tests/PhraseFlow.Core.Tests/PhraseFlow.Core.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
}

foreach ($rid in $Runtimes) {
    Write-Host "==> Publishing self-contained $rid" -ForegroundColor Cyan
    $out = Join-Path $dist "publish-$rid"
    dotnet publish $project -c Release -r $rid --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -o $out
    if ($LASTEXITCODE -ne 0) { throw "Publishing $rid failed." }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath (Join-Path $dist "PhraseFlow-$version-$rid.zip")
}

if (-not $SkipFrameworkDependent) {
    Write-Host '==> Publishing framework-dependent build' -ForegroundColor Cyan
    $out = Join-Path $dist 'publish-framework-dependent'
    dotnet publish $project -c Release --no-self-contained -p:PublishSingleFile=false -p:DebugType=none -o $out
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the framework-dependent build failed.' }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath (Join-Path $dist "PhraseFlow-$version-framework-dependent.zip")
}

Write-Host '==> Done' -ForegroundColor Green
Get-ChildItem $dist -Filter *.zip | Select-Object Name, @{ Name = 'Size (MB)'; Expression = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
