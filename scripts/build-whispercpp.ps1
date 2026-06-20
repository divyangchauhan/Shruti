[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
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
$buildPath = Join-Path $repositoryRoot "artifacts\whispercpp-native"

& $cmakePath -S $sourcePath -B $buildPath -G "Visual Studio 17 2022" -A x64
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $cmakePath --build $buildPath --config $Configuration
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $cmakePath --build $buildPath --config $Configuration --target RUN_TESTS
exit $LASTEXITCODE
