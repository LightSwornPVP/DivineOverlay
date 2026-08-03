param(
    [string]$VintageStoryInstall = "C:\Program Files\Vintage Story",
    [string]$DotNetRoot = ""
)

$ErrorActionPreference = "Stop"

$buildArguments = @{
    VintageStoryInstall = $VintageStoryInstall
}
if (![string]::IsNullOrWhiteSpace($DotNetRoot)) {
    $buildArguments.DotNetRoot = $DotNetRoot
}

& (Join-Path $PSScriptRoot "Divine\build.ps1") @buildArguments

$dist = Join-Path $PSScriptRoot "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

$divinePackage = Join-Path $dist "Divine-0.2.25.zip"
$overlayPackage = Join-Path $dist "DivineOverlay-0.2.25.zip"
$bundlePackage = Join-Path $dist "Divine-and-DivineOverlay-0.2.25.zip"

foreach ($path in @($divinePackage, $overlayPackage, $bundlePackage)) {
    if (Test-Path $path) {
        Remove-Item $path -Force
    }
}

Copy-Item (Join-Path $PSScriptRoot "Divine\Divine.zip") $divinePackage
Copy-Item (Join-Path $PSScriptRoot "Divine\DivineOverlay.zip") $overlayPackage

$bundleStaging = Join-Path $dist "bundle"
if (Test-Path $bundleStaging) {
    Remove-Item $bundleStaging -Recurse -Force
}
New-Item -ItemType Directory -Force $bundleStaging | Out-Null
Copy-Item $divinePackage (Join-Path $bundleStaging "Divine.zip")
Copy-Item $overlayPackage (Join-Path $bundleStaging "DivineOverlay.zip")
Compress-Archive -Path (Join-Path $bundleStaging "*") -DestinationPath $bundlePackage -CompressionLevel Optimal
Remove-Item $bundleStaging -Recurse -Force

Write-Host "Built separate packages:"
Write-Host "  $divinePackage"
Write-Host "  $overlayPackage"
Write-Host "Built one-download bundle:"
Write-Host "  $bundlePackage"
