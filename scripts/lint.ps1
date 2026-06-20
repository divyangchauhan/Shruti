param(
    [string] $Configuration = "Debug",
    [string] $Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root "Shruti.sln"

dotnet restore $solution --ignore-failed-sources
dotnet format $solution --verify-no-changes --verbosity minimal --no-restore
$buildProperties = @(
    "-p:Platform=$Platform",
    "-p:NoWarn=NU1801%3BNU1900"
)

dotnet build $solution --configuration $Configuration --no-restore -warnaserror @buildProperties
