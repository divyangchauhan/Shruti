param([string] $OutputDirectory = 'artifacts\popup-checks\latest')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tools\Shruti.PopupChecks'
# Compile the app's actual resources into an isolated test application.
$appXaml = [IO.File]::ReadAllText((Join-Path $root 'src\Shruti.App.WinUI\App.xaml'))
[IO.File]::WriteAllText((Join-Path $project 'App.xaml'), $appXaml.Replace('Shruti.App.WinUI.App', 'Shruti.PopupChecks.CheckApp'))
dotnet build (Join-Path $project 'Shruti.PopupChecks.csproj') -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Popup check build failed.' }
$env:SHRUTI_POPUP_CHECK_OUTPUT = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) }
$executable = Join-Path $project 'bin\x64\Debug\net8.0-windows10.0.19041.0\Shruti.PopupChecks.exe'
$process = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(60000)) { $process.Kill(); throw 'Popup check timed out.' }
if ($process.ExitCode -ne 0) {
    Get-Content (Join-Path $env:SHRUTI_POPUP_CHECK_OUTPUT '*error.txt') -ErrorAction SilentlyContinue
    throw "Popup check failed with exit code $($process.ExitCode)."
}
$results = Get-Content (Join-Path $env:SHRUTI_POPUP_CHECK_OUTPUT 'results.json') -Raw | ConvertFrom-Json
$results | Select-Object name, opaque, theme, width, height, color | Format-Table -AutoSize
if ($results.Count -ne 22) { throw "Expected 22 popup checks, got $($results.Count)." }
