param(
    [string] $Configuration = "Debug",
    [string] $Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

dotnet test "$root/tests/Shruti.Tests/Shruti.Tests.csproj" --configuration $Configuration -p:Platform=$Platform
