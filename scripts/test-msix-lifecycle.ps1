param(
    [Parameter(Mandatory)]
    [string] $PackagePath,
    [string] $UpdatePackagePath,
    [string] $LocalDataRoot = (Join-Path $env:LOCALAPPDATA "Shruti"),
    [switch] $UninstallAfterTest,
    [switch] $ConfirmLifecycleTest
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath -ErrorAction Stop).Path
$resolvedUpdatePackagePath = if ([string]::IsNullOrWhiteSpace($UpdatePackagePath)) {
    $null
}
else {
    (Resolve-Path -LiteralPath $UpdatePackagePath -ErrorAction Stop).Path
}

if (-not $ConfirmLifecycleTest) {
    throw "This script installs and may uninstall an MSIX package for the current user. Re-run with -ConfirmLifecycleTest after reviewing the command."
}

function Find-WindowsSdkTool {
    param([string] $ToolName)

    $command = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $sdkBinRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $tool = Get-ChildItem $sdkBinRoot -Recurse -Filter $ToolName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\$ToolName$" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($tool) {
        return $tool.FullName
    }

    throw "$ToolName was not found. Install the Windows 10/11 SDK packaging tools."
}

function Read-PackageIdentity {
    param([string] $Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $manifestEntry = $archive.GetEntry("AppxManifest.xml")
        if (-not $manifestEntry) {
            throw "$Path does not contain AppxManifest.xml."
        }

        $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
        try {
            [xml] $manifest = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        return [pscustomobject]@{
            Name = [string] $manifest.Package.Identity.Name
            Publisher = [string] $manifest.Package.Identity.Publisher
            Version = [version] $manifest.Package.Identity.Version
            Architecture = [string] $manifest.Package.Identity.ProcessorArchitecture
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-TrustedPackageSignature {
    param(
        [string] $SignTool,
        [string] $Path
    )

    & $SignTool verify /pa $Path
    if ($LASTEXITCODE -ne 0) {
        throw "The package signature is missing, invalid, or not trusted: $Path"
    }
}

function Get-LocalDataSnapshot {
    param([string] $DataRoot)

    $snapshot = @{}
    if (-not (Test-Path -LiteralPath $DataRoot)) {
        return $snapshot
    }

    $resolvedDataRoot = (Resolve-Path -LiteralPath $DataRoot).Path.TrimEnd("\")
    $trackedDirectories = @("Models", "Settings", "Transcripts", "Recordings")
    foreach ($directoryName in $trackedDirectories) {
        $directoryPath = Join-Path $resolvedDataRoot $directoryName
        if (-not (Test-Path -LiteralPath $directoryPath)) {
            continue
        }

        foreach ($file in Get-ChildItem -LiteralPath $directoryPath -File -Recurse -Force) {
            $relativePath = $file.FullName.Substring($resolvedDataRoot.Length).TrimStart("\")
            $snapshot[$relativePath] = [pscustomobject]@{
                Length = $file.Length
                Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
        }
    }

    return $snapshot
}

function Assert-LocalDataPreserved {
    param(
        [hashtable] $Before,
        [string] $DataRoot,
        [string] $Phase
    )

    $after = Get-LocalDataSnapshot $DataRoot
    $missing = @()
    $changed = @()
    foreach ($relativePath in $Before.Keys) {
        if (-not $after.ContainsKey($relativePath)) {
            $missing += $relativePath
            continue
        }

        if ($Before[$relativePath].Length -ne $after[$relativePath].Length -or
            $Before[$relativePath].Sha256 -ne $after[$relativePath].Sha256) {
            $changed += $relativePath
        }
    }

    if ($missing.Count -gt 0 -or $changed.Count -gt 0) {
        throw "Local Shruti data was not preserved after $Phase. Missing: $($missing -join ', '). Changed: $($changed -join ', ')."
    }

    return [pscustomobject]@{
        Phase = $Phase
        PreservedFileCount = $Before.Count
    }
}

$runningProcess = Get-Process -Name "Shruti.App.WinUI" -ErrorAction SilentlyContinue
if ($runningProcess) {
    throw "Close Shruti, including its tray process, before running the MSIX lifecycle test."
}

$baseIdentity = Read-PackageIdentity $resolvedPackagePath
if ($baseIdentity.Name -ne "DivyangChauhan.Shruti") {
    throw "Unexpected package identity '$($baseIdentity.Name)'."
}

$updateIdentity = $null
if ($resolvedUpdatePackagePath) {
    $updateIdentity = Read-PackageIdentity $resolvedUpdatePackagePath
    if ($updateIdentity.Name -ne $baseIdentity.Name -or
        $updateIdentity.Publisher -ne $baseIdentity.Publisher -or
        $updateIdentity.Architecture -ne $baseIdentity.Architecture) {
        throw "The update package identity, publisher, and architecture must match the base package."
    }

    if ($updateIdentity.Version -le $baseIdentity.Version) {
        throw "Update package version $($updateIdentity.Version) must be greater than base version $($baseIdentity.Version)."
    }
}

$existingPackage = Get-AppxPackage -Name $baseIdentity.Name -ErrorAction SilentlyContinue
if ($existingPackage) {
    throw "Package '$($baseIdentity.Name)' is already installed for the current user. Use a clean test account or uninstall it before running this lifecycle test."
}

$signTool = Find-WindowsSdkTool "signtool.exe"
Assert-TrustedPackageSignature $signTool $resolvedPackagePath
if ($resolvedUpdatePackagePath) {
    Assert-TrustedPackageSignature $signTool $resolvedUpdatePackagePath
}

$dataSnapshot = Get-LocalDataSnapshot $LocalDataRoot
if ($dataSnapshot.Count -eq 0) {
    Write-Warning "No existing model, settings, transcript, or recording files were found. Lifecycle behavior will be tested, but data preservation cannot be proven with an empty snapshot."
}

$results = [System.Collections.Generic.List[object]]::new()
Add-AppxPackage -Path $resolvedPackagePath -ForceApplicationShutdown
$installedPackage = Get-AppxPackage -Name $baseIdentity.Name -ErrorAction Stop
if ([version] $installedPackage.Version -ne $baseIdentity.Version) {
    throw "Installed version '$($installedPackage.Version)' does not match base package version '$($baseIdentity.Version)'."
}

$results.Add([pscustomobject]@{
    Phase = "Install"
    Version = [string] $installedPackage.Version
    PackageFullName = $installedPackage.PackageFullName
})
$results.Add((Assert-LocalDataPreserved $dataSnapshot $LocalDataRoot "install"))

if ($resolvedUpdatePackagePath) {
    Add-AppxPackage -Path $resolvedUpdatePackagePath -ForceApplicationShutdown
    $installedPackage = Get-AppxPackage -Name $baseIdentity.Name -ErrorAction Stop
    if ([version] $installedPackage.Version -ne $updateIdentity.Version) {
        throw "Installed version '$($installedPackage.Version)' does not match update package version '$($updateIdentity.Version)'."
    }

    $results.Add([pscustomobject]@{
        Phase = "Update"
        Version = [string] $installedPackage.Version
        PackageFullName = $installedPackage.PackageFullName
    })
    $results.Add((Assert-LocalDataPreserved $dataSnapshot $LocalDataRoot "update"))
}

if ($UninstallAfterTest) {
    Remove-AppxPackage -Package $installedPackage.PackageFullName
    if (Get-AppxPackage -Name $baseIdentity.Name -ErrorAction SilentlyContinue) {
        throw "The package is still installed after Remove-AppxPackage."
    }

    $results.Add([pscustomobject]@{
        Phase = "Uninstall"
        Version = [string] $installedPackage.Version
        PackageFullName = $installedPackage.PackageFullName
    })
    $results.Add((Assert-LocalDataPreserved $dataSnapshot $LocalDataRoot "uninstall"))
}

$reportDirectory = Join-Path $root "artifacts\installer\validation"
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
$reportPath = Join-Path $reportDirectory "msix-lifecycle-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')).json"
$report = [pscustomobject]@{
    TestedAtUtc = [DateTime]::UtcNow.ToString("O")
    BasePackage = $resolvedPackagePath
    UpdatePackage = $resolvedUpdatePackagePath
    LocalDataRoot = $LocalDataRoot
    InitialPreservedFileCount = $dataSnapshot.Count
    Results = $results
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8

[pscustomobject]@{
    ReportPath = $reportPath
    InstalledVersion = if ($UninstallAfterTest) { $null } else { [string] $installedPackage.Version }
    Uninstalled = [bool] $UninstallAfterTest
    PreservedFileCount = $dataSnapshot.Count
}
