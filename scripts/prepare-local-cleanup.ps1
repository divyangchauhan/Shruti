[CmdletBinding()]
param([string]$Destination = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\local-cleanup'),
    [ValidateSet('Cpu', 'Vulkan')][string]$Backend = 'Cpu')
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
function Get-VerifiedArtifact([string]$Url, [string]$Path, [string]$Hash) {
    if ((Test-Path -LiteralPath $Path) -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Hash) { return }
    $temporary = "$Path.download"
    & curl.exe --fail --location --retry 3 --continue-at - --output $temporary $Url
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $Url" }
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $Hash) { throw "SHA256 mismatch: $temporary" }
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}
& (Join-Path $PSScriptRoot 'prepare-local-cleanup-runtime.ps1') -Destination $Destination -Backend $Backend
$runtimeDirectory = Join-Path $Destination $Backend
Get-VerifiedArtifact 'https://huggingface.co/zanish-labs/qwen3.5-2b-q4_k_m-gguf/resolve/56c1090cf58b925e3bf23eb8731781e4d9c9029c/qwen3.5-2b-q4_k_m.gguf' (Join-Path $Destination 'qwen3.5-2b-q4_k_m.gguf') '92b61f139fbae199718c0c283df25cb619b1cc57e71faaae5a9a6b06a1842dda'
Invoke-WebRequest 'https://huggingface.co/zanish-labs/qwen3.5-2b-q4_k_m-gguf/raw/56c1090cf58b925e3bf23eb8731781e4d9c9029c/LICENSE' -OutFile (Join-Path $Destination 'Qwen-LICENSE.txt')
$server = Get-ChildItem -LiteralPath $runtimeDirectory -Recurse -Filter 'llama-server.exe' | Select-Object -First 1
if ($null -eq $server) { throw 'The pinned runtime archive does not contain llama-server.exe.' }
Write-Output "Runtime: $($server.FullName)"
Write-Output "Model: $(Join-Path $Destination 'qwen3.5-2b-q4_k_m.gguf')"
