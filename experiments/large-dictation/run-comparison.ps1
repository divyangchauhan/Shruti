# Run from any directory after the model files, runtime and evaluation inputs are ready.
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '../..')
try {
    foreach ($config in @(@('utter', 64), @('qwen', 16))) {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            python -u -B experiments/large-dictation/batch.py $config[0] --inputs eval-inputs --slots $config[1]
            if ($LASTEXITCODE -eq 0) { break }
            if ($attempt -eq 3) { throw "Model run failed three times: $($config[0])" }
            Write-Warning "Retrying $($config[0]) from its saved checkpoint."
        }
    }
    python -B experiments/large-dictation/report.py
    if ($LASTEXITCODE -ne 0) { throw 'Scoring failed. See the error above.' }
}
finally {
    Pop-Location
}
