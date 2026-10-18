[CmdletBinding()]
param([string]$Version = '1.0.8.0', [switch]$SkipNativeBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts\portable\Shruti'
$channelOutput = Join-Path $root 'artifacts\channels'
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}
if (!$SkipNativeBuild) {
    & (Join-Path $PSScriptRoot 'build-whispercpp.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Native speech build failed.' }
    & (Join-Path $PSScriptRoot 'build-openvino-genai.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'OpenVINO runtime build failed.' }
}
$sourceCommit = & git -C $root rev-parse HEAD
& (Join-Path $PSScriptRoot 'prepare-local-cleanup-runtime.ps1')
if (Test-Path -LiteralPath $output) {
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    $resolvedOutput = [IO.Path]::GetFullPath($output)
    if (!$resolvedOutput.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish directory is outside artifacts.' }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $output, $channelOutput | Out-Null
Invoke-Checked 'dotnet' @('publish', (Join-Path $root 'src\Shruti.App.WinUI\Shruti.App.WinUI.csproj'),
    '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'true', '-p:Platform=x64',
    '-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=true', "-p:Version=$Version",
    "-p:AssemblyVersion=$Version", "-p:FileVersion=$Version", "-p:InformationalVersion=$Version+$sourceCommit", '-o', $output)
& (Join-Path $PSScriptRoot 'stage-native-dependencies.ps1') -Destination $output
Copy-Item -LiteralPath (Join-Path $root 'artifacts\local-cleanup\Cpu') -Destination (Join-Path $output 'CleanupRuntime') -Recurse -Force
foreach ($dependency in @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'vcomp140.dll')) {
    Copy-Item -LiteralPath (Join-Path $output $dependency) -Destination (Join-Path $output 'CleanupRuntime') -Force
}
Invoke-Checked 'powershell.exe' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-native-package-runtime.ps1'), '-NativeDirectory', $output)
Get-ChildItem -LiteralPath $output -Recurse -Filter '*.pdb' | Remove-Item -Force
foreach ($file in @('LICENSE', 'PRIVACY.md')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $output -Force }
$licenses = Join-Path $output 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
Invoke-WebRequest 'https://raw.githubusercontent.com/ggml-org/whisper.cpp/5ed76e9a079962f1c85cfce44edd325c27ef1f97/LICENSE' -OutFile (Join-Path $licenses 'whisper.cpp-MIT.txt')
$toolchainRoot = Join-Path $env:LOCALAPPDATA 'ShrutiToolchain'
$openVinoRoot = Join-Path $toolchainRoot 'openvino-2026.3\openvino_toolkit_windows_2026.3.0.22451.bd8d6542e3c_x86_64'
Copy-Item -LiteralPath (Join-Path $openVinoRoot 'docs\licensing') -Destination (Join-Path $licenses 'OpenVINO') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $toolchainRoot 'openvino.genai-2026.3\LICENSE') -Destination (Join-Path $licenses 'OpenVINO-GenAI-Apache2.txt') -Force
Copy-Item -LiteralPath (Join-Path $openVinoRoot 'runtime\3rdparty\tbb\TBB-LICENSE') -Destination (Join-Path $licenses 'TBB-Apache2.txt') -Force
$requiredFiles = @('Shruti.App.WinUI.exe', 'Shruti.App.WinUI.pri', 'Shruti.App.WinUI.dll', 'shruti_whisper.dll',
    'openvino_genai_c.dll', 'vulkan-1.dll', 'vcruntime140.dll', 'msvcp140.dll', 'coreclr.dll', 'Microsoft.UI.Xaml.dll',
    'SpeechDetection\ggml-silero-v6.2.0.bin', 'SpeechDetection\silero-vad-LICENSE.txt',
    'CleanupRuntime\llama-server.exe', 'CleanupRuntime\llama-LICENSE.txt')
foreach ($file in $requiredFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $output $file))) { throw "Portable package is missing $file." }
}
$fileVersion = (Get-Item -LiteralPath (Join-Path $output 'Shruti.App.WinUI.exe')).VersionInfo.FileVersion
if ($fileVersion -ne $Version) { throw "Executable version $fileVersion does not match $Version." }
[ordered]@{ version = $Version; sourceCommit = $sourceCommit; architecture = 'x64'; distribution = 'portable'; signed = $false } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'release-info.json') -Encoding utf8
@'
Shruti portable edition. Start Shruti.App.WinUI.exe, or use your package manager's shortcut.
Settings and models use %LOCALAPPDATA%\Shruti. A model download is required before dictation.
Closing the window keeps Shruti in the tray. Choose Exit Shruti to stop it.
This ZIP is not digitally signed. Microsoft Store installation uses Microsoft's signing and update system.
Transcription history, dictionary entries, and snippets are saved locally. Manage or delete them in the app.
Optional AI cleanup requires a separate model download and runs locally.
'@ | Set-Content -LiteralPath (Join-Path $output 'PORTABLE.txt') -Encoding utf8
$zip = Join-Path $channelOutput "Shruti-$Version-x64.zip"
Compress-Archive -LiteralPath $output -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath (Join-Path $channelOutput 'SHA256SUMS.txt') -Encoding ascii
[pscustomobject]@{ Package = $zip; Version = $Version; SourceCommit = $sourceCommit; Sha256 = $hash }
