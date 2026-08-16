param(
    [string] $Configuration = "Release",
    [string] $Platform = "x64",
    [string] $Version = "0.1.0.0",
    [ValidateSet("None", "Vulkan", "CUDA")]
    [string] $GpuBackend = "Vulkan",
    [switch] $SkipNativeBuild,
    [string] $Publisher,
    [string] $CertificatePath,
    [string] $CertificateThumbprint,
    [ValidateSet("CurrentUser", "LocalMachine")]
    [string] $CertificateStoreLocation = "CurrentUser",
    [string] $CertificatePasswordEnvironmentVariable = "SHRUTI_SIGNING_CERT_PASSWORD",
    [string] $TimestampUrl
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$packageSource = Join-Path $root "src\Shruti.App.Package"
$manifestTemplate = Join-Path $packageSource "Package.appxmanifest"
$appBuildOutputRoot = Join-Path $root "src\Shruti.App.WinUI\bin\$Platform\$Configuration"
$publishDirectory = Join-Path $root "artifacts\installer\publish\$Configuration\$Platform"
$stageDirectory = Join-Path $root "artifacts\installer\stage\$Configuration\$Platform"
$outputDirectory = Join-Path $root "artifacts\installer\output"
$priConfigPath = Join-Path $root "artifacts\installer\priconfig.xml"
$nativeBuildDirectory = Join-Path $root "artifacts\whispercpp-native"
$nativeLibraryPath = Join-Path $nativeBuildDirectory "$Configuration\shruti_whisper.dll"
$openVinoNativeDirectory = Join-Path $root "artifacts\openvino-genai\$Configuration"
$openVinoGenAiLibraryPath = Join-Path $openVinoNativeDirectory "openvino_genai_c.dll"
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

function Get-CodeSigningCertificate {
    if (-not [string]::IsNullOrWhiteSpace($CertificatePath) -and
        -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "Specify either CertificatePath or CertificateThumbprint, not both."
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
        $resolvedPath = (Resolve-Path -LiteralPath $CertificatePath -ErrorAction Stop).Path
        $password = [Environment]::GetEnvironmentVariable($CertificatePasswordEnvironmentVariable)
        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $resolvedPath,
            $password,
            $flags)

        return [pscustomobject]@{
            Certificate = $certificate
            Path = $resolvedPath
            Password = $password
            Thumbprint = $null
            StoreLocation = $null
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $normalizedThumbprint = $CertificateThumbprint.Replace(" ", "")
        $storeLocationValue = [System.Security.Cryptography.X509Certificates.StoreLocation]::$CertificateStoreLocation
        $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
            [System.Security.Cryptography.X509Certificates.StoreName]::My,
            $storeLocationValue)
        try {
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
            $matches = $store.Certificates.Find(
                [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $normalizedThumbprint,
                $false)
            if ($matches.Count -eq 0) {
                throw "No certificate with thumbprint $normalizedThumbprint was found in $CertificateStoreLocation\My."
            }

            $certificate = $matches[0]
            return [pscustomobject]@{
                Certificate = $certificate
                Path = $null
                Password = $null
                Thumbprint = $normalizedThumbprint
                StoreLocation = $CertificateStoreLocation
            }
        }
        finally {
            $store.Close()
        }
    }

    return $null
}

function Assert-CodeSigningCertificate {
    param([System.Security.Cryptography.X509Certificates.X509Certificate2] $Certificate)

    if (-not $Certificate.HasPrivateKey) {
        throw "The signing certificate does not have an accessible private key."
    }

    $now = [DateTimeOffset]::Now
    if ($now -lt $Certificate.NotBefore -or $now -gt $Certificate.NotAfter) {
        throw "The signing certificate is not valid at the current time."
    }

    $enhancedKeyUsage = $Certificate.Extensions |
        Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
        Select-Object -First 1
    if ($enhancedKeyUsage) {
        $supportsCodeSigning = $enhancedKeyUsage.EnhancedKeyUsages |
            Where-Object { $_.Value -eq "1.3.6.1.5.5.7.3.3" } |
            Select-Object -First 1
        if (-not $supportsCodeSigning) {
            throw "The signing certificate does not allow Code Signing enhanced key usage."
        }
    }
}

function Invoke-PackageSigning {
    param(
        [string] $SignTool,
        [string] $Package,
        [pscustomobject] $SigningCertificate
    )

    $arguments = @("sign", "/fd", "SHA256")
    if ($SigningCertificate.Path) {
        $arguments += @("/f", $SigningCertificate.Path)
        if (-not [string]::IsNullOrEmpty($SigningCertificate.Password)) {
            $arguments += @("/p", $SigningCertificate.Password)
        }
    }
    else {
        if ($SigningCertificate.StoreLocation -eq "LocalMachine") {
            $arguments += "/sm"
        }

        $arguments += @("/sha1", $SigningCertificate.Thumbprint)
    }

    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $arguments += @("/tr", $TimestampUrl, "/td", "SHA256")
    }

    $arguments += $Package
    & $SignTool @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed to sign the MSIX package with exit code $LASTEXITCODE."
    }

    & $SignTool verify /pa /v $Package
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool could not verify the signed MSIX package as trusted."
    }
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
    $accentBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(222, 110, 30))
    $waveBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $glyphSize = [Math]::Min($Width, $Height) * 0.82
    $glyphX = ($Width - $glyphSize) / 2
    $glyphY = ($Height - $glyphSize) / 2
    $cornerRadius = $glyphSize * 0.22
    $cornerDiameter = $cornerRadius * 2
    $glyphPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $glyphPath.AddArc($glyphX, $glyphY, $cornerDiameter, $cornerDiameter, 180, 90)
        $glyphPath.AddArc($glyphX + $glyphSize - $cornerDiameter, $glyphY, $cornerDiameter, $cornerDiameter, 270, 90)
        $glyphPath.AddArc($glyphX + $glyphSize - $cornerDiameter, $glyphY + $glyphSize - $cornerDiameter, $cornerDiameter, $cornerDiameter, 0, 90)
        $glyphPath.AddArc($glyphX, $glyphY + $glyphSize - $cornerDiameter, $cornerDiameter, $cornerDiameter, 90, 90)
        $glyphPath.CloseFigure()
        $graphics.FillPath($accentBrush, $glyphPath)

        $barWidth = [Math]::Max(1.5, $glyphSize * 0.065)
        $barGap = $glyphSize * 0.075
        $barHeights = @(0.25, 0.48, 0.70, 0.43, 0.22)
        $waveWidth = ($barWidth * $barHeights.Count) + ($barGap * ($barHeights.Count - 1))
        $waveX = ($Width - $waveWidth) / 2
        for ($index = 0; $index -lt $barHeights.Count; $index++) {
            $barHeight = $glyphSize * $barHeights[$index]
            $barX = $waveX + ($index * ($barWidth + $barGap))
            $barY = ($Height - $barHeight) / 2
            $graphics.FillRectangle($waveBrush, $barX, $barY, $barWidth, $barHeight)
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $glyphPath.Dispose()
        $waveBrush.Dispose()
        $accentBrush.Dispose()
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

$signingCertificate = Get-CodeSigningCertificate
if ($signingCertificate) {
    Assert-CodeSigningCertificate $signingCertificate.Certificate
    $certificatePublisher = $signingCertificate.Certificate.Subject
    if (-not [string]::IsNullOrWhiteSpace($Publisher) -and
        -not [string]::Equals($Publisher, $certificatePublisher, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Publisher '$Publisher' does not match signing certificate subject '$certificatePublisher'."
    }

    $effectivePublisher = $certificatePublisher
}
else {
    $effectivePublisher = if ([string]::IsNullOrWhiteSpace($Publisher)) {
        "CN=Shruti Dev"
    }
    else {
        $Publisher
    }
}

if (-not $SkipNativeBuild) {
    & (Join-Path $root "scripts\build-whispercpp.ps1") -Configuration $Configuration -GpuBackend $GpuBackend
    if ($LASTEXITCODE -ne 0) {
        throw "scripts\build-whispercpp.ps1 failed with exit code $LASTEXITCODE."
    }

    & (Join-Path $root "scripts\build-openvino-genai.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "scripts\build-openvino-genai.ps1 failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path $nativeLibraryPath)) {
    throw "Native transcription library missing at $nativeLibraryPath. Run this script without -SkipNativeBuild."
}
if (-not (Test-Path $openVinoGenAiLibraryPath)) {
    throw "OpenVINO GenAI runtime missing at $openVinoNativeDirectory. Run this script without -SkipNativeBuild."
}

Remove-Item $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $stageDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDirectory, $stageDirectory, $outputDirectory | Out-Null

Invoke-CheckedCommand "dotnet" @(
    "publish", (Join-Path $root "src\Shruti.App.WinUI\Shruti.App.WinUI.csproj"),
    "--configuration", $Configuration,
    "--runtime", "win-x64",
    "--self-contained", "true",
    "-p:Platform=$Platform",
    "-p:WindowsPackageType=None",
    "-p:WindowsAppSDKSelfContained=true",
    "-o", $publishDirectory
)

$appResourceIndex = Get-ChildItem -LiteralPath $appBuildOutputRoot -Recurse -Filter "Shruti.App.WinUI.pri" |
    Where-Object { $_.FullName -match "\\win-x64\\Shruti\.App\.WinUI\.pri$" } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if (-not $appResourceIndex) {
    throw "The WinUI resource index was not found under $appBuildOutputRoot."
}

$appDirectory = Join-Path $stageDirectory "VFS\ProgramFilesX64\Shruti"
$assetDirectory = Join-Path $stageDirectory "Assets"
New-Item -ItemType Directory -Force -Path $appDirectory, $assetDirectory | Out-Null
Copy-Item -Path (Join-Path $publishDirectory "*") -Destination $appDirectory -Recurse -Force
Copy-Item -LiteralPath $appResourceIndex.FullName -Destination $appDirectory -Force
Get-ChildItem $appDirectory -Recurse -Filter "*.pdb" | Remove-Item -Force

$packagedNativeLibrary = Join-Path $appDirectory "shruti_whisper.dll"
if (-not (Test-Path $packagedNativeLibrary)) {
    throw "Published package layout does not include shruti_whisper.dll."
}
$packagedOpenVinoLibrary = Join-Path $appDirectory "openvino_genai_c.dll"
if (-not (Test-Path $packagedOpenVinoLibrary)) {
    throw "Published package layout does not include openvino_genai_c.dll."
}

$manifest = Get-Content $manifestTemplate -Raw
$manifest = $manifest.Replace("__PACKAGE_VERSION__", $Version)
$manifest = $manifest.Replace(
    "__PACKAGE_PUBLISHER__",
    [System.Security.SecurityElement]::Escape($effectivePublisher))
Set-Content -Path (Join-Path $stageDirectory "AppxManifest.xml") -Value $manifest -Encoding utf8

New-PackageLogo (Join-Path $assetDirectory "StoreLogo.png") 50 50
New-PackageLogo (Join-Path $assetDirectory "Square44x44Logo.png") 44 44
New-PackageLogo (Join-Path $assetDirectory "Square150x150Logo.png") 150 150
New-PackageLogo (Join-Path $assetDirectory "Wide310x150Logo.png") 310 150

$makePri = Find-WindowsSdkTool "makepri.exe"
Remove-Item $priConfigPath -Force -ErrorAction SilentlyContinue
Invoke-CheckedCommand $makePri @(
    "createconfig",
    "/cf", $priConfigPath,
    "/dq", "en-US"
)
[xml] $priConfig = Get-Content -LiteralPath $priConfigPath -Raw
if ($priConfig.resources.packaging) {
    $null = $priConfig.resources.RemoveChild($priConfig.resources.packaging)
}
$priConfig.Save($priConfigPath)
Invoke-CheckedCommand $makePri @(
    "new",
    "/pr", $stageDirectory,
    "/cf", $priConfigPath,
    "/of", (Join-Path $stageDirectory "resources.pri"),
    "/in", "DivyangChauhan.Shruti",
    "/o"
)

$makeAppx = Find-WindowsSdkTool "makeappx.exe"
Remove-Item $packagePath -Force -ErrorAction SilentlyContinue
Invoke-CheckedCommand $makeAppx @("pack", "/d", $stageDirectory, "/p", $packagePath, "/overwrite")

$isSigned = $null -ne $signingCertificate
if ($isSigned) {
    $signTool = Find-WindowsSdkTool "signtool.exe"
    Invoke-PackageSigning $signTool $packagePath $signingCertificate
}

$verificationArguments = @{
    PackagePath = $packagePath
    ExpectedVersion = $Version
    ExpectedPublisher = $effectivePublisher
    RuntimeMode = "SelfContained"
}
if ($isSigned) {
    $verificationArguments.RequireSignature = $true
}

& (Join-Path $root "scripts\verify-msix-package.ps1") @verificationArguments
if ($LASTEXITCODE -ne 0) {
    throw "scripts\verify-msix-package.ps1 failed with exit code $LASTEXITCODE."
}

[pscustomobject]@{
    PackagePath = $packagePath
    StageDirectory = $stageDirectory
    IncludesNativeLibrary = (Test-Path $packagedNativeLibrary)
    IncludesNpuRuntime = (Test-Path $packagedOpenVinoLibrary)
    RuntimeMode = "SelfContained"
    Publisher = $effectivePublisher
    Signed = $isSigned
}
