param(
    [string] $Configuration = "Release",
    [string] $Platform = "x64",
    [string] $Version = "0.1.0.0",
    [ValidateSet("None", "Vulkan", "CUDA")]
    [string] $GpuBackend = "None",
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
    "--self-contained", "true",
    "-p:Platform=$Platform",
    "-p:WindowsPackageType=None",
    "-p:WindowsAppSDKSelfContained=true",
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
$manifest = $manifest.Replace(
    "__PACKAGE_PUBLISHER__",
    [System.Security.SecurityElement]::Escape($effectivePublisher))
Set-Content -Path (Join-Path $stageDirectory "AppxManifest.xml") -Value $manifest -Encoding utf8

New-PackageLogo (Join-Path $assetDirectory "StoreLogo.png") 50 50
New-PackageLogo (Join-Path $assetDirectory "Square44x44Logo.png") 44 44
New-PackageLogo (Join-Path $assetDirectory "Square150x150Logo.png") 150 150
New-PackageLogo (Join-Path $assetDirectory "Wide310x150Logo.png") 310 150

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
    RuntimeMode = "SelfContained"
    Publisher = $effectivePublisher
    Signed = $isSigned
}
