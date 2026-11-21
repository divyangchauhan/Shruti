[CmdletBinding()]
param([string]$Destination = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\local-cleanup'),
    [ValidateSet('Cpu', 'Vulkan')][string]$Backend = 'Vulkan')
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$release = 'b11574'
$name = "llama-$release-bin-win-$($Backend.ToLowerInvariant())-x64.zip"
$archive = Join-Path $Destination $name
$sha = if ($Backend -eq 'Vulkan') { '73387123e22bd2a01a66194f02bd899189542188e8ca87d542b463d75bedc164' }
       else { '3f185fa804d8f4af359d917e21cb19916e7d579459322820bb078a4b6705736f' }
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash -ne $sha) {
    & curl.exe --fail --location --retry 3 --output "$archive.download" "https://github.com/ggml-org/llama.cpp/releases/download/$release/$name"
    if ($LASTEXITCODE -ne 0) { throw 'Cleanup runtime download failed.' }
    if ((Get-FileHash -LiteralPath "$archive.download").Hash -ne $sha) { throw 'Cleanup runtime checksum mismatch.' }
    Move-Item -LiteralPath "$archive.download" -Destination $archive -Force
}
$runtime = Join-Path $Destination $Backend
Expand-Archive -LiteralPath $archive -DestinationPath $runtime -Force
Invoke-WebRequest 'https://raw.githubusercontent.com/ggml-org/llama.cpp/8bb3cf0d2/LICENSE' -OutFile (Join-Path $runtime 'llama-LICENSE.txt')
if (!(Test-Path -LiteralPath (Join-Path $runtime 'llama-server.exe'))) { throw 'Cleanup runtime is incomplete.' }
Write-Output "Prepared pinned $Backend cleanup runtime at $runtime."
