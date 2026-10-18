$ErrorActionPreference = 'Stop'
# A Windows PowerShell child launched from pwsh can inherit only pwsh module paths.
$env:PSModulePath = (Join-Path $PSHOME 'Modules') + [IO.Path]::PathSeparator + $env:PSModulePath
$root = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $root 'artifacts/speech-detection'
$modelPath = Join-Path $directory 'ggml-silero-v6.2.0.bin'
$expectedHash = '2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987'
$url = 'https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/ggml-silero-v6.2.0.bin'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
if ((Test-Path -LiteralPath $modelPath) -and (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash -eq $expectedHash) { return }
$downloadPath = Join-Path $directory ('download-' + [Guid]::NewGuid().ToString('N') + '.bin')
try {
    Invoke-WebRequest -Uri $url -OutFile $downloadPath -UseBasicParsing
    if ((Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'Speech detection model integrity check failed.'
    }
    Move-Item -LiteralPath $downloadPath -Destination $modelPath -Force
} finally {
    if (Test-Path -LiteralPath $downloadPath) { Remove-Item -LiteralPath $downloadPath -Force }
}
