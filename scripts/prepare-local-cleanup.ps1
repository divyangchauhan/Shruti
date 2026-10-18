[CmdletBinding()]
param([string]$Destination = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\local-cleanup'),
    [ValidateSet('Cpu', 'Vulkan')][string]$Backend = 'Cpu')
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$runtimeName = if ($Backend -eq 'Cpu') { 'llama-b11574-bin-win-cpu-x64.zip' } else { 'llama-b11574-bin-win-vulkan-x64.zip' }
$runtimeHash = if ($Backend -eq 'Cpu') { '3f185fa804d8f4af359d917e21cb19916e7d579459322820bb078a4b6705736f' } else { '73387123e22bd2a01a66194f02bd899189542188e8ca87d542b463d75bedc164' }
function Get-VerifiedArtifact([string]$Url, [string]$Path, [string]$Hash) {
    if ((Test-Path -LiteralPath $Path) -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Hash) { return }
    $temporary = "$Path.download"
    & curl.exe --fail --location --retry 3 --continue-at - --output $temporary $Url
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $Url" }
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $Hash) { throw "SHA256 mismatch: $temporary" }
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}
Get-VerifiedArtifact "https://github.com/ggml-org/llama.cpp/releases/download/b11574/$runtimeName" (Join-Path $Destination $runtimeName) $runtimeHash
$runtimeDirectory = Join-Path $Destination $Backend
Expand-Archive -LiteralPath (Join-Path $Destination $runtimeName) -DestinationPath $runtimeDirectory -Force
Invoke-WebRequest 'https://raw.githubusercontent.com/ggml-org/llama.cpp/8bb3cf0d2/LICENSE' -OutFile (Join-Path $runtimeDirectory 'llama-LICENSE.txt')
Get-VerifiedArtifact 'https://huggingface.co/zanish-labs/qwen3.5-2b-q4_k_m-gguf/resolve/56c1090cf58b925e3bf23eb8731781e4d9c9029c/qwen3.5-2b-q4_k_m.gguf' (Join-Path $Destination 'qwen3.5-2b-q4_k_m.gguf') '92b61f139fbae199718c0c283df25cb619b1cc57e71faaae5a9a6b06a1842dda'
Invoke-WebRequest 'https://huggingface.co/zanish-labs/qwen3.5-2b-q4_k_m-gguf/raw/56c1090cf58b925e3bf23eb8731781e4d9c9029c/LICENSE' -OutFile (Join-Path $Destination 'Qwen-LICENSE.txt')
$server = Get-ChildItem -LiteralPath $runtimeDirectory -Recurse -Filter 'llama-server.exe' | Select-Object -First 1
if ($null -eq $server) { throw 'The pinned runtime archive does not contain llama-server.exe.' }
Write-Output "Runtime: $($server.FullName)"
Write-Output "Model: $(Join-Path $Destination 'qwen3.5-2b-q4_k_m.gguf')"
