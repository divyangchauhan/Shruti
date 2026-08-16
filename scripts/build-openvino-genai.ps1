[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ToolchainRoot = (Join-Path $env:LOCALAPPDATA "ShrutiToolchain")
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$openVinoVersion = "2026.3.0.22451.bd8d6542e3c"
$genAiTag = "2026.3.0.0"
$archiveName = "openvino_toolkit_windows_${openVinoVersion}_x86_64.zip"
$archiveUrl = "https://storage.openvinotoolkit.org/repositories/openvino/packages/2026.3/windows/$archiveName"
$archiveSha256 = "4b26374eb342c3e0e4488b230cf8a16b6327e22b1ef12e45e5533cead06a66e3"
$archivePath = Join-Path $ToolchainRoot $archiveName
$openVinoParent = Join-Path $ToolchainRoot "openvino-2026.3"
$openVinoRoot = Join-Path $openVinoParent "openvino_toolkit_windows_${openVinoVersion}_x86_64"
$genAiSource = Join-Path $ToolchainRoot "openvino.genai-2026.3"
$genAiBuild = Join-Path $ToolchainRoot "openvino.genai-build-2026.3"
$artifactPath = Join-Path $repositoryRoot "artifacts\openvino-genai\$Configuration"

New-Item -ItemType Directory -Force -Path $ToolchainRoot | Out-Null
if (-not (Test-Path $openVinoRoot)) {
    if (-not (Test-Path $archivePath)) {
        Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath
    }

    $actualHash = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $archiveSha256) {
        throw "OpenVINO archive integrity check failed. Expected $archiveSha256, received $actualHash."
    }

    New-Item -ItemType Directory -Force -Path $openVinoParent | Out-Null
    Expand-Archive -Path $archivePath -DestinationPath $openVinoParent -Force
}

if (-not (Test-Path (Join-Path $genAiSource ".git"))) {
    & git clone --depth 1 --branch $genAiTag https://github.com/openvinotoolkit/openvino.genai.git $genAiSource
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

& git -C $genAiSource submodule update --init --recursive --depth 1
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$openVinoCmake = Join-Path $openVinoRoot "runtime\cmake"
& cmake -S $genAiSource -B $genAiBuild -G "Visual Studio 17 2022" -A x64 `
    "-DOpenVINO_DIR=$openVinoCmake" `
    -DENABLE_PYTHON=OFF `
    -DENABLE_JS=OFF `
    -DENABLE_SAMPLES=OFF `
    -DENABLE_TESTS=OFF `
    -DENABLE_TOOLS=OFF `
    -DENABLE_GGUF=ON `
    -DENABLE_XGRAMMAR=OFF `
    -DENABLE_MISAKI_CPP=OFF
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& cmake --build $genAiBuild --config $Configuration --target openvino_genai_c --parallel
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force -Path $artifactPath | Out-Null
$openVinoRuntime = Join-Path $openVinoRoot "runtime\bin\intel64\$Configuration"
Copy-Item (Join-Path $openVinoRuntime "*.dll") $artifactPath -Force
$runtimeCache = Join-Path $openVinoRuntime "cache.json"
if (Test-Path $runtimeCache) {
    Copy-Item $runtimeCache $artifactPath -Force
}

$tbbPattern = if ($Configuration -eq "Debug") { "*_debug.dll" } else { "*.dll" }
Get-ChildItem (Join-Path $openVinoRoot "runtime\3rdparty\tbb\bin") -Filter $tbbPattern -File |
    Where-Object { $Configuration -eq "Debug" -or $_.Name -notlike "*_debug.dll" } |
    Copy-Item -Destination $artifactPath -Force

$genAiLibraries = @(
    (Join-Path $genAiBuild "openvino_genai\openvino_genai.dll"),
    (Join-Path $genAiBuild "openvino_genai\openvino_tokenizers.dll"),
    (Join-Path $genAiBuild "src\c\$Configuration\openvino_genai_c.dll")
)
$genAiLibraries | Copy-Item -Destination $artifactPath -Force

foreach ($requiredLibrary in @("openvino_c.dll", "openvino_genai.dll", "openvino_genai_c.dll", "openvino_tokenizers.dll")) {
    if (-not (Test-Path (Join-Path $artifactPath $requiredLibrary))) {
        throw "OpenVINO GenAI native library missing: $requiredLibrary"
    }
}

Write-Host "OpenVINO GenAI runtime staged at $artifactPath"
