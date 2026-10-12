[CmdletBinding()]
param([string]$Destination = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\local-cleanup'))
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$archive = Join-Path $Destination 'llama-b11574-bin-win-cpu-x64.zip'
$sha = '3f185fa804d8f4af359d917e21cb19916e7d579459322820bb078a4b6705736f'
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash -ne $sha) {
    & curl.exe --fail --location --retry 3 --output "$archive.download" 'https://github.com/ggml-org/llama.cpp/releases/download/b11574/llama-b11574-bin-win-cpu-x64.zip'
    if ($LASTEXITCODE -ne 0) { throw 'Cleanup runtime download failed.' }
    if ((Get-FileHash -LiteralPath "$archive.download").Hash -ne $sha) { throw 'Cleanup runtime checksum mismatch.' }
    Move-Item -LiteralPath "$archive.download" -Destination $archive -Force
}
$runtime = Join-Path $Destination 'Cpu'
Expand-Archive -LiteralPath $archive -DestinationPath $runtime -Force
Invoke-WebRequest 'https://raw.githubusercontent.com/ggml-org/llama.cpp/8bb3cf0d2/LICENSE' -OutFile (Join-Path $runtime 'llama-LICENSE.txt')
if (!(Test-Path -LiteralPath (Join-Path $runtime 'llama-server.exe'))) { throw 'Cleanup runtime is incomplete.' }
Write-Output "Prepared pinned CPU cleanup runtime at $runtime."
