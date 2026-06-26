[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("None", "Vulkan", "CUDA")]
    [string]$GpuBackend = "None",
    [string]$BuildPath
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Add-VulkanSdkToPath {
    if ($GpuBackend -ne "Vulkan") {
        return
    }

    $vulkanSdk = $env:VULKAN_SDK
    if ([string]::IsNullOrWhiteSpace($vulkanSdk)) {
        $vulkanSdk = [Environment]::GetEnvironmentVariable("VULKAN_SDK", "Machine")
    }

    if ([string]::IsNullOrWhiteSpace($vulkanSdk)) {
        $vulkanSdk = [Environment]::GetEnvironmentVariable("VULKAN_SDK", "User")
    }

    if ([string]::IsNullOrWhiteSpace($vulkanSdk)) {
        return
    }

    $vulkanBin = Join-Path $vulkanSdk "Bin"
    if (Test-Path $vulkanBin) {
        $env:VULKAN_SDK = $vulkanSdk
        if (($env:Path -split ';') -notcontains $vulkanBin) {
            $env:Path = "$vulkanBin;$env:Path"
        }
    }
}

function Get-DefaultGpuBuildPath {
    $suffix = $GpuBackend.ToLowerInvariant()
    $driveRoot = [System.IO.Path]::GetPathRoot($repositoryRoot)
    $candidates = @(
        (Join-Path $driveRoot "shruti-native-$suffix"),
        (Join-Path ([System.IO.Path]::GetTempPath()) "s-$suffix")
    )

    foreach ($candidate in $candidates) {
        try {
            New-Item -ItemType Directory -Force -Path $candidate | Out-Null
            return $candidate
        }
        catch {
        }
    }

    return Join-Path $repositoryRoot "artifacts\whispercpp-native"
}

Add-VulkanSdkToPath
$cmake = Get-Command cmake -ErrorAction SilentlyContinue

if ($null -eq $cmake) {
    $portableCmake = Join-Path $env:LOCALAPPDATA "ShrutiToolchain\cmake-4.3.3-windows-x86_64\bin\cmake.exe"
    if (Test-Path $portableCmake) {
        $cmakePath = $portableCmake
    }
    else {
        throw "CMake is required. Install Kitware.CMake or place CMake in %LOCALAPPDATA%\ShrutiToolchain."
    }
}
else {
    $cmakePath = $cmake.Source
}

$sourcePath = Join-Path $repositoryRoot "src\Shruti.Transcription.WhisperCpp.Native"
$artifactBuildPath = Join-Path $repositoryRoot "artifacts\whispercpp-native"
if ([string]::IsNullOrWhiteSpace($BuildPath)) {
    $buildPath = if ($GpuBackend -eq "None") { $artifactBuildPath } else { Get-DefaultGpuBuildPath }
}
else {
    $buildPath = $BuildPath
}

& $cmakePath -S $sourcePath -B $buildPath -G "Visual Studio 17 2022" -A x64 "-DSHRUTI_WHISPER_GPU_BACKEND=$GpuBackend"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $cmakePath --build $buildPath --config $Configuration
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $cmakePath --build $buildPath --config $Configuration --target RUN_TESTS
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($buildPath -ne $artifactBuildPath) {
    $builtNativeLibrary = Join-Path $buildPath "$Configuration\shruti_whisper.dll"
    if (-not (Test-Path $builtNativeLibrary)) {
        throw "Native transcription library missing at $builtNativeLibrary."
    }

    $artifactConfigurationPath = Join-Path $artifactBuildPath $Configuration
    New-Item -ItemType Directory -Force -Path $artifactConfigurationPath | Out-Null
    Copy-Item -Path $builtNativeLibrary -Destination (Join-Path $artifactConfigurationPath "shruti_whisper.dll") -Force

    $builtSymbols = Join-Path $buildPath "$Configuration\shruti_whisper.pdb"
    if (Test-Path $builtSymbols) {
        Copy-Item -Path $builtSymbols -Destination (Join-Path $artifactConfigurationPath "shruti_whisper.pdb") -Force
    }
}

exit 0
