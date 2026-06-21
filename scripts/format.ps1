param(
    [switch] $Verify
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

if ($Verify) {
    Invoke-DotnetCommand @("format", $solution, "--verify-no-changes", "--verbosity", "minimal")
    return
}

Invoke-DotnetCommand @("format", $solution, "--verbosity", "minimal")
