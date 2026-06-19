param(
    [string] $Configuration = "Debug",
    [string] $Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

dotnet restore "$root/Shruti.sln"
dotnet build "$root/Shruti.sln" --configuration $Configuration --no-restore -p:Platform=$Platform
