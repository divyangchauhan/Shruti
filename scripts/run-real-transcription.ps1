[CmdletBinding()]
param([switch]$Npu, [switch]$Gpu, [string]$ModelId, [switch]$Silence)

if ($Npu -and $Gpu) {
    throw "Choose either -Gpu or -Npu."
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot

if ($Npu) {
    & "$PSScriptRoot\build-openvino-genai.ps1" -Configuration Release
}
else {
    & "$PSScriptRoot\build-whispercpp.ps1" -Configuration Release
}
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$projectPath = "$repositoryRoot\tools\Shruti.RealIntegration\Shruti.RealIntegration.csproj"
dotnet build $projectPath --configuration Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$integrationAssembly = "$repositoryRoot\tools\Shruti.RealIntegration\bin\x64\Release\net8.0\Shruti.RealIntegration.dll"
$integrationArguments = @()
if ($Npu) {
    $integrationArguments += "--npu"
}
elseif ($Gpu) {
    $integrationArguments += "--gpu"
}
if (-not [string]::IsNullOrWhiteSpace($ModelId)) {
    $integrationArguments += @("--model", $ModelId)
}
if ($Silence) {
    $integrationArguments += "--silence"
}
& dotnet $integrationAssembly @integrationArguments

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
