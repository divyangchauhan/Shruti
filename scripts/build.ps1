param(
    [string] $Configuration = "Debug",
    [string] $Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

& "$PSScriptRoot\build-whispercpp.ps1" -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "The default CPU/GPU transcription runtime build failed with exit code $LASTEXITCODE."
}

& "$PSScriptRoot\build-openvino-genai.ps1" -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "The default CPU/GPU/NPU transcription runtime build failed with exit code $LASTEXITCODE."
}

function Invoke-DotnetCommand {
    param([string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Invoke-DotnetCommand @("restore", "$root/Shruti.sln", "--ignore-failed-sources")
$buildProperties = @(
    "-p:Platform=$Platform",
    "-p:NoWarn=NU1801%3BNU1900"
)

$buildArguments = @("build", "$root/Shruti.sln", "--configuration", $Configuration, "--no-restore") + $buildProperties
Invoke-DotnetCommand $buildArguments
