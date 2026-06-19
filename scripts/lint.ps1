param(
    [string] $Configuration = "Debug",
    [string] $Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "Shruti.sln"

dotnet restore $solution
dotnet format $solution --verify-no-changes --verbosity minimal --no-restore
dotnet build $solution --configuration $Configuration --no-restore -p:Platform=$Platform -warnaserror
