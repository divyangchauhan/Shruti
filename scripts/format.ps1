param(
    [switch] $Verify
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "Shruti.sln"

if ($Verify) {
    dotnet format $solution --verify-no-changes --verbosity minimal
    exit
}

dotnet format $solution --verbosity minimal
