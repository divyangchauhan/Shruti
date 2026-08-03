param(
    [Parameter(Mandatory)]
    [string] $PackagePath,
    [string] $ExpectedVersion,
    [string] $ExpectedPublisher,
    [ValidateSet("SelfContained")]
    [string] $RuntimeMode = "SelfContained",
    [switch] $RequireSignature
)

$ErrorActionPreference = "Stop"
$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath -ErrorAction Stop).Path

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

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackagePath)
try {
    $entryNames = @($archive.Entries | ForEach-Object { $_.FullName.Replace("\", "/") })
    $manifestEntry = $archive.GetEntry("AppxManifest.xml")
    if (-not $manifestEntry) {
        throw "The package does not contain AppxManifest.xml."
    }

    $manifestReader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try {
        [xml] $manifest = $manifestReader.ReadToEnd()
    }
    finally {
        $manifestReader.Dispose()
    }

    $identity = $manifest.Package.Identity
    if (-not $identity) {
        throw "The package manifest does not contain an Identity element."
    }

    if ($identity.Name -ne "DivyangChauhan.Shruti") {
        throw "Unexpected package identity '$($identity.Name)'."
    }

    if ($identity.ProcessorArchitecture -ne "x64") {
        throw "Unexpected package architecture '$($identity.ProcessorArchitecture)'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and
        $identity.Version -ne $ExpectedVersion) {
        throw "Package version '$($identity.Version)' does not match expected version '$ExpectedVersion'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisher) -and
        -not [string]::Equals(
            $identity.Publisher,
            $ExpectedPublisher,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Package publisher '$($identity.Publisher)' does not match expected publisher '$ExpectedPublisher'."
    }

    $requiredEntries = @(
        "AppxBlockMap.xml",
        "[Content_Types].xml",
        "resources.pri",
        "Assets/StoreLogo.png",
        "Assets/Square44x44Logo.png",
        "Assets/Square150x150Logo.png",
        "Assets/Wide310x150Logo.png",
        "VFS/ProgramFilesX64/Shruti/Shruti.App.WinUI.exe",
        "VFS/ProgramFilesX64/Shruti/Shruti.App.WinUI.pri",
        "VFS/ProgramFilesX64/Shruti/shruti_whisper.dll"
    )
    foreach ($requiredEntry in $requiredEntries) {
        if ($entryNames -notcontains $requiredEntry) {
            throw "The package is missing required entry '$requiredEntry'."
        }
    }

    $pdbEntries = @($entryNames | Where-Object { $_.EndsWith(".pdb", [StringComparison]::OrdinalIgnoreCase) })
    if ($pdbEntries.Count -gt 0) {
        throw "The package contains PDB files: $($pdbEntries -join ', ')."
    }

    if ($RuntimeMode -eq "SelfContained") {
        $requiredRuntimeEntries = @(
            "VFS/ProgramFilesX64/Shruti/coreclr.dll",
            "VFS/ProgramFilesX64/Shruti/hostfxr.dll",
            "VFS/ProgramFilesX64/Shruti/System.Private.CoreLib.dll",
            "VFS/ProgramFilesX64/Shruti/Microsoft.UI.Xaml.dll"
        )
        foreach ($requiredRuntimeEntry in $requiredRuntimeEntries) {
            if ($entryNames -notcontains $requiredRuntimeEntry) {
                throw "The self-contained package is missing runtime entry '$requiredRuntimeEntry'."
            }
        }
    }

    $hasSignature = $entryNames -contains "AppxSignature.p7x"
    if ($RequireSignature -and -not $hasSignature) {
        throw "The package is unsigned."
    }
}
finally {
    $archive.Dispose()
}

if ($RequireSignature) {
    $signTool = Find-WindowsSdkTool "signtool.exe"
    & $signTool verify /pa /v $resolvedPackagePath
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool could not verify the MSIX signature as trusted."
    }
}

[pscustomobject]@{
    PackagePath = $resolvedPackagePath
    IdentityName = $identity.Name
    Publisher = $identity.Publisher
    Version = $identity.Version
    Architecture = $identity.ProcessorArchitecture
    RuntimeMode = $RuntimeMode
    Signed = $hasSignature
    FileCount = $entryNames.Count
}
