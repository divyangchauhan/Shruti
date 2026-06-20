[CmdletBinding()]
param()

$repositoryRoot = Split-Path -Parent $PSScriptRoot

& "$PSScriptRoot\build-whispercpp.ps1" -Configuration Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$projectPath = "$repositoryRoot\tools\Shruti.RealIntegration\Shruti.RealIntegration.csproj"
dotnet build $projectPath --configuration Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet "$repositoryRoot\tools\Shruti.RealIntegration\bin\x64\Release\net8.0\Shruti.RealIntegration.dll"
