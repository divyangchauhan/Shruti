param(
    [string] $Configuration = "Release",
    [string] $Platform = "x64",
    [string] $Version = "0.1.0.0",
    [switch] $SkipNativeBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$packageSource = Join-Path $root "src\Shruti.App.Package"
$manifestTemplate = Join-Path $packageSource "Package.appxmanifest"
$publishDirectory = Join-Path $root "artifacts\installer\publish\$Configuration\$Platform"
$stageDirectory = Join-Path $root "artifacts\installer\stage\$Configuration\$Platform"
$outputDirectory = Join-Path $root "artifacts\installer\output"
$nativeBuildDirectory = Join-Path $root "artifacts\whispercpp-native"
$nativeLibraryPath = Join-Path $nativeBuildDirectory "$Configuration\shruti_whisper.dll"
$packagePath = Join-Path $outputDirectory "Shruti-$Version-$Platform.msix"

function Invoke-CheckedCommand {
    param(
        [string] $FilePath,
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Find-WindowsSdkTool {
    param([string] $ToolName)

    $command = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $sdkBinRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $tool = Get-ChildItem $sdkBinRoot -Recurse -Filter $ToolName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\$Platform\\$ToolName$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($tool) {
        return $tool.FullName
    }

    throw "$ToolName was not found. Install the Windows 10/11 SDK packaging tools."
}

function New-PackageLogo {
    param(
        [string] $Path,
        [int] $Width,
        [int] $Height
    )

    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(17, 24, 39))
    $accentBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(94, 234, 212))
    $fontSize = [Math]::Max(18, [Math]::Floor([Math]::Min($Width, $Height) * 0.48))
    $font = [System.Drawing.Font]::new("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.FillRectangle($brush, 0, 0, $Width, $Height)
        $format = [System.Drawing.StringFormat]::new()
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $graphics.DrawString("S", $font, $accentBrush, [System.Drawing.RectangleF]::new(0, 0, $Width, $Height), $format)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $font.Dispose()
        $accentBrush.Dispose()
        $brush.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must use MSIX four-part numeric format, for example 0.1.0.0."
}

if ($Platform -ne "x64") {
    throw "The Shruti installer currently supports x64 packages only."
}

if (-not (Test-Path $manifestTemplate)) {
    throw "Package manifest template not found at $manifestTemplate."
}

if (-not $SkipNativeBuild) {
    Invoke-CheckedCommand "cmake" @(
        "-S", (Join-Path $root "src\Shruti.Transcription.WhisperCpp.Native"),
        "-B", $nativeBuildDirectory,
        "-G", "Visual Studio 17 2022",
        "-A", $Platform
    )
    Invoke-CheckedCommand "cmake" @("--build", $nativeBuildDirectory, "--config", $Configuration)
}

if (-not (Test-Path $nativeLibraryPath)) {
    throw "Native transcription library missing at $nativeLibraryPath. Run this script without -SkipNativeBuild."
}

Remove-Item $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $stageDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDirectory, $stageDirectory, $outputDirectory | Out-Null

Invoke-CheckedCommand "dotnet" @(
    "publish", (Join-Path $root "src\Shruti.App.WinUI\Shruti.App.WinUI.csproj"),
    "--configuration", $Configuration,
    "--runtime", "win-x64",
    "--self-contained", "false",
    "-p:Platform=$Platform",
    "-p:WindowsPackageType=None",
    "-o", $publishDirectory
)

$appDirectory = Join-Path $stageDirectory "VFS\ProgramFilesX64\Shruti"
$assetDirectory = Join-Path $stageDirectory "Assets"
New-Item -ItemType Directory -Force -Path $appDirectory, $assetDirectory | Out-Null
Copy-Item -Path (Join-Path $publishDirectory "*") -Destination $appDirectory -Recurse -Force
Get-ChildItem $appDirectory -Recurse -Filter "*.pdb" | Remove-Item -Force

$packagedNativeLibrary = Join-Path $appDirectory "shruti_whisper.dll"
if (-not (Test-Path $packagedNativeLibrary)) {
    throw "Published package layout does not include shruti_whisper.dll."
}

$manifest = Get-Content $manifestTemplate -Raw
$manifest = $manifest.Replace("__PACKAGE_VERSION__", $Version)
Set-Content -Path (Join-Path $stageDirectory "AppxManifest.xml") -Value $manifest -Encoding utf8

New-PackageLogo (Join-Path $assetDirectory "StoreLogo.png") 50 50
New-PackageLogo (Join-Path $assetDirectory "Square44x44Logo.png") 44 44
New-PackageLogo (Join-Path $assetDirectory "Square150x150Logo.png") 150 150
New-PackageLogo (Join-Path $assetDirectory "Wide310x150Logo.png") 310 150

$makeAppx = Find-WindowsSdkTool "makeappx.exe"
Remove-Item $packagePath -Force -ErrorAction SilentlyContinue
Invoke-CheckedCommand $makeAppx @("pack", "/d", $stageDirectory, "/p", $packagePath, "/overwrite")

[pscustomobject]@{
    PackagePath = $packagePath
    StageDirectory = $stageDirectory
    IncludesNativeLibrary = (Test-Path $packagedNativeLibrary)
}
