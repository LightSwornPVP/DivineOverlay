param(
    [string]$VintageStoryInstall = "C:\Program Files\Vintage Story"
)

$ErrorActionPreference = "Stop"

$sdkRoot = Join-Path $env:ProgramFiles "dotnet\sdk"
$sdk = Get-ChildItem $sdkRoot -Directory |
    Where-Object { $_.Name -like "10.*" } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1
$compiler = if ($null -ne $sdk) { Get-Item (Join-Path $sdk.FullName "Roslyn\bincore\csc.dll") } else { $null }

if ($null -eq $compiler) {
    throw "Could not find the .NET 10 C# compiler. Install the .NET 10 SDK, then try again."
}

$runtimeRoot = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.NETCore.App"
$runtime = Get-ChildItem $runtimeRoot -Directory |
    Where-Object { $_.Name -like "10.*" } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1

if ($null -eq $runtime) {
    throw "Could not find the .NET 10 runtime. Install the .NET 10 runtime or SDK, then try again."
}

$apiDll = Join-Path $VintageStoryInstall "VintagestoryAPI.dll"
$libDll = Join-Path $VintageStoryInstall "VintagestoryLib.dll"
$extraDlls = @(
    (Join-Path $VintageStoryInstall "Mods\VSSurvivalMod.dll"),
    (Join-Path $VintageStoryInstall "Mods\VSEssentials.dll"),
    (Join-Path $VintageStoryInstall "Mods\VSCreativeMod.dll"),
    (Join-Path $VintageStoryInstall "Lib\0Harmony.dll"),
    (Join-Path $VintageStoryInstall "Lib\cairo-sharp.dll"),
    (Join-Path $VintageStoryInstall "Lib\Newtonsoft.Json.dll"),
    (Join-Path $VintageStoryInstall "Lib\protobuf-net.dll")
)

$localRefs = Join-Path $PSScriptRoot "refs"
function Use-LocalRefIfPresent([string]$path) {
    $local = Join-Path $localRefs (Split-Path $path -Leaf)
    if (Test-Path $local) {
        return $local
    }

    return $path
}

$apiDll = Use-LocalRefIfPresent $apiDll
$libDll = Use-LocalRefIfPresent $libDll
$extraDlls = $extraDlls | ForEach-Object { Use-LocalRefIfPresent $_ }

if (!(Test-Path $apiDll)) {
    throw "Could not find VintagestoryAPI.dll in: $VintageStoryInstall"
}

if (!(Test-Path $libDll)) {
    throw "Could not find VintagestoryLib.dll in: $VintageStoryInstall"
}

$releaseDir = Join-Path $PSScriptRoot "bin\Release"
New-Item -ItemType Directory -Force $releaseDir | Out-Null

$references = @()
$references += Get-ChildItem $runtime.FullName -Filter *.dll | Where-Object {
    try {
        [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null
        $true
    } catch {
        $false
    }
} | ForEach-Object { "/r:$($_.FullName)" }
$references += "/r:$apiDll"
$references += "/r:$libDll"
foreach ($dll in $extraDlls) {
    if (Test-Path $dll) {
        $references += "/r:$dll"
    }
}

$sources = @()
$sources += Get-ChildItem (Join-Path $PSScriptRoot "src") -Filter *.cs | ForEach-Object { $_.FullName }
$chestOrganizerSource = Join-Path $PSScriptRoot "vendor\ChestOrganizer"
if (Test-Path $chestOrganizerSource) {
    $sources += Get-ChildItem $chestOrganizerSource -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
}
$outputDll = Join-Path $releaseDir "Divine.dll"

dotnet $compiler.FullName -nologo -target:library -langversion:latest -nullable:enable -nowarn:8600,8601,8602,8603,8604,8618,8625,8632,8767 -out:$outputDll @references @sources
if ($LASTEXITCODE -ne 0) {
    throw "Compile failed."
}

$package = Join-Path $PSScriptRoot "Divine.zip"
if (Test-Path $package) {
    Remove-Item $package
}

$staging = Join-Path $PSScriptRoot "package"
if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}

New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item (Join-Path $PSScriptRoot "modinfo.json") $staging
Copy-Item $outputDll $staging
Copy-Item (Join-Path $PSScriptRoot "modicon.png") $staging
if (Test-Path (Join-Path $PSScriptRoot "assets")) {
    Copy-Item (Join-Path $PSScriptRoot "assets") $staging -Recurse
}
$chestOrganizerAssets = Join-Path $PSScriptRoot "vendor\ChestOrganizer\assets"
if (Test-Path $chestOrganizerAssets) {
    New-Item -ItemType Directory -Force (Join-Path $staging "assets") | Out-Null
    Copy-Item (Join-Path $chestOrganizerAssets "*") (Join-Path $staging "assets") -Recurse -Force
}
if (Test-Path (Join-Path $PSScriptRoot "vendor\ChestOrganizer-LICENSE.txt")) {
    Copy-Item (Join-Path $PSScriptRoot "vendor\ChestOrganizer-LICENSE.txt") $staging
}
Copy-Item (Join-Path $PSScriptRoot "README.md") $staging
if (Test-Path (Join-Path $PSScriptRoot "CHANGELOG.md")) {
    Copy-Item (Join-Path $PSScriptRoot "CHANGELOG.md") $staging
}

Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $package
Write-Host "Built $package"

$blockOverlayZip = Join-Path $PSScriptRoot "vendor\blockoverlay-4.11.6.zip"
$repositoryOverlayRoot = Split-Path $PSScriptRoot -Parent
$useRepositoryOverlay = (Test-Path (Join-Path $repositoryOverlayRoot "BlockOverlay.dll")) `
    -and (Test-Path (Join-Path $repositoryOverlayRoot "modinfo.json")) `
    -and (Test-Path (Join-Path $repositoryOverlayRoot "assets\blocksoverlay"))
if ($useRepositoryOverlay -or (Test-Path $blockOverlayZip)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $overlayPackage = Join-Path $PSScriptRoot "DivineOverlay.zip"
    if (Test-Path $overlayPackage) {
        Remove-Item $overlayPackage
    }

    $overlayStaging = Join-Path $PSScriptRoot "package-divineoverlay"
    if (Test-Path $overlayStaging) {
        Remove-Item $overlayStaging -Recurse -Force
    }

    New-Item -ItemType Directory -Force $overlayStaging | Out-Null
    if ($useRepositoryOverlay) {
        Copy-Item (Join-Path $repositoryOverlayRoot "BlockOverlay.dll") $overlayStaging
        Copy-Item (Join-Path $repositoryOverlayRoot "modinfo.json") $overlayStaging
        Copy-Item (Join-Path $repositoryOverlayRoot "modicon.png") $overlayStaging
        Copy-Item (Join-Path $repositoryOverlayRoot "assets") $overlayStaging -Recurse
        Copy-Item (Join-Path $repositoryOverlayRoot "README.md") $overlayStaging
    } else {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($blockOverlayZip)
        try {
            foreach ($entry in $archive.Entries) {
                if ([string]::IsNullOrWhiteSpace($entry.Name)) {
                    continue
                }

                $relative = $entry.FullName -replace '^blockoverlay-4\.11\.6/', ''
                if ([string]::IsNullOrWhiteSpace($relative)) {
                    continue
                }

                $target = Join-Path $overlayStaging $relative
                $targetDir = Split-Path $target -Parent
                New-Item -ItemType Directory -Force $targetDir | Out-Null
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            }
        } finally {
            $archive.Dispose()
        }
    }

    $overlayModInfo = Join-Path $overlayStaging "modinfo.json"
    if (Test-Path $overlayModInfo) {
        $info = Get-Content $overlayModInfo -Raw | ConvertFrom-Json
        $info.name = "Divine Overlay"
        $info.description = "Divine Overlay targets blocks and entities on the HUD. Based on Block Overlay by Xel."
        $info.authors = @("TheSinnerMan")
        if ($null -eq $info.contributors) {
            $info | Add-Member -NotePropertyName contributors -NotePropertyValue @("Xel (upstream Block Overlay)")
        }
        $info | ConvertTo-Json -Depth 10 | Set-Content -Path $overlayModInfo -Encoding UTF8
    }

    $blockOverlayLang = Join-Path $overlayStaging "assets\blocksoverlay\lang"
    $englishLang = Join-Path $blockOverlayLang "en.json"
    if (Test-Path $englishLang) {
        $text = Get-Content $englishLang -Raw
        $text = $text.Replace("Select targets to overlay", "Select Divine Overlay targets")
        $text = $text.Replace("Show blocks overlay", "Show Divine Overlay")
        $text = $text.Replace("Show block selector", "Open Divine Overlay selector")
        Set-Content -Path $englishLang -Value $text -Encoding UTF8
    }

    $russianLang = Join-Path $blockOverlayLang "ru.json"
    if (Test-Path $russianLang) {
        $text = Get-Content $russianLang -Raw
        $text = $text.Replace("Block Overlay", "Divine Overlay")
        $text = $text.Replace("block overlay", "Divine Overlay")
        Set-Content -Path $russianLang -Value $text -Encoding UTF8
    }

    if (!$useRepositoryOverlay -and (Test-Path (Join-Path $PSScriptRoot "vendor\DivineOverlay-README.md"))) {
        Copy-Item (Join-Path $PSScriptRoot "vendor\DivineOverlay-README.md") (Join-Path $overlayStaging "README.md")
    }

    Compress-Archive -Path (Join-Path $overlayStaging "*") -DestinationPath $overlayPackage
    Write-Host "Built $overlayPackage"
}
