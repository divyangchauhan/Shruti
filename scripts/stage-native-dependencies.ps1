param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$destinationPath = (Resolve-Path -LiteralPath $Destination).Path
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio installer discovery tool is missing.' }
$vsRoot = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsRoot) { throw 'Visual C++ build tools with redistributable runtimes are required.' }
$redist = Get-ChildItem (Join-Path $vsRoot 'VC\Redist\MSVC') -Directory |
    Where-Object Name -match '^\d+\.\d+\.\d+$' |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $redist) { throw 'Visual C++ redistributable directory is missing.' }
foreach ($component in @('Microsoft.VC143.CRT', 'Microsoft.VC143.OpenMP')) {
    $componentPath = Join-Path $redist.FullName "x64\$component"
    if (-not (Test-Path -LiteralPath $componentPath)) { throw "Missing redistributable component: $componentPath" }
    Copy-Item -Path (Join-Path $componentPath '*.dll') -Destination $destinationPath -Force
}

# Pin the official runtime archive rather than copying a developer machine's driver files.
$version = '1.4.350.0'
$expectedHash = '23CE69F32CEF3E2799617E2B1776CD0C71030D23A91F8375821CC40D76B185B9'
$cache = Join-Path $root 'artifacts\native-runtime-dependencies'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archivePath = Join-Path $cache "VulkanRT-X64-$version-Components.zip"
if (-not (Test-Path -LiteralPath $archivePath)) {
    Invoke-WebRequest -Uri "https://sdk.lunarg.com/sdk/download/$version/windows/VulkanRT-X64-$version-Components.zip" -OutFile $archivePath
}
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Vulkan runtime archive checksum mismatch.'
}
$extractPath = Join-Path $cache "vulkan-$version"
Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force
$runtimeRoot = Join-Path $extractPath "VulkanRT-X64-$version-Components"
Copy-Item -LiteralPath (Join-Path $runtimeRoot 'x64\vulkan-1.dll') -Destination $destinationPath -Force
$license = Get-ChildItem -LiteralPath $runtimeRoot -File -Filter 'VulkanRT-License*' | Select-Object -First 1
if (-not $license) { throw 'Vulkan runtime license is missing from the archive.' }
Copy-Item -LiteralPath $license.FullName -Destination $destinationPath -Force
foreach ($name in @('vulkan-1.dll','msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll','vcomp140.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $destinationPath $name))) { throw "Missing native dependency: $name" }
}
