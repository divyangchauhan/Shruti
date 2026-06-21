param(
    [string] $Configuration = "Debug",
    [string] $Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "Shruti.sln"

function Invoke-DotnetCommand {
    param([string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Invoke-DotnetCommand @("restore", $solution, "--ignore-failed-sources")
Invoke-DotnetCommand @("format", $solution, "--verify-no-changes", "--verbosity", "minimal", "--no-restore")
$buildProperties = @(
    "-p:Platform=$Platform",
    "-p:NoWarn=NU1801%3BNU1900"
)

$buildArguments = @("build", $solution, "--configuration", $Configuration, "--no-restore", "-warnaserror") + $buildProperties
Invoke-DotnetCommand $buildArguments
