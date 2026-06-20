param(
    [string] $Configuration = "Debug",
    [string] $Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

dotnet restore "$root/Shruti.sln" --ignore-failed-sources
$buildProperties = @(
    "-p:Platform=$Platform",
    "-p:NoWarn=NU1801%3BNU1900"
)

dotnet build "$root/Shruti.sln" --configuration $Configuration --no-restore @buildProperties
