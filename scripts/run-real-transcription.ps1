[CmdletBinding()]
param([switch]$Npu)

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
if ($Npu) {
    & dotnet $integrationAssembly --npu
}
else {
    & dotnet $integrationAssembly
}

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
