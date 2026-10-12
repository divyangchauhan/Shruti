[CmdletBinding()]
param([string]$Manifest = (Join-Path (Split-Path $PSScriptRoot -Parent) 'tests\quality\manifest.json'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$cases = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$root = Split-Path ([IO.Path]::GetFullPath($Manifest)) -Parent
New-Item -ItemType Directory -Force -Path (Join-Path $root 'audio') | Out-Null
$synthesizer = [System.Speech.Synthesis.SpeechSynthesizer]::new()
try {
    foreach ($case in $cases) {
        $path = "audio/$($case.id).wav"
        $format = [System.Speech.AudioFormat.SpeechAudioFormatInfo]::new(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
        $synthesizer.SetOutputToWaveFile((Join-Path $root $path), $format)
        $synthesizer.Speak($case.expectedRaw)
        $synthesizer.SetOutputToNull()
        if ($case.audioCondition -in @('quiet', 'white-noise')) {
            $wavePath = Join-Path $root $path
            [byte[]]$bytes = [IO.File]::ReadAllBytes($wavePath)
            $offset = 12
            $random = [Random]::new(42)
            while ($offset + 8 -le $bytes.Length) {
                $chunk = [Text.Encoding]::ASCII.GetString($bytes, $offset, 4)
                $length = [BitConverter]::ToUInt32($bytes, $offset + 4)
                if ($chunk -eq 'data') {
                    for ($sample = $offset + 8; $sample + 1 -lt $offset + 8 + $length; $sample += 2) {
                        $value = [BitConverter]::ToInt16($bytes, $sample)
                        $value = if ($case.audioCondition -eq 'quiet') { $value * 0.1 } else { $value + ($random.NextDouble() * 2 - 1) * 1000 }
                        $converted = [BitConverter]::GetBytes([int16][Math]::Clamp([Math]::Round($value), -32768, 32767))
                        $bytes[$sample] = $converted[0]; $bytes[$sample + 1] = $converted[1]
                    }
                }
                $offset += 8 + $length + ($length % 2)
            }
            [IO.File]::WriteAllBytes($wavePath, $bytes)
        }
        $case | Add-Member -NotePropertyName audio -NotePropertyValue $path -Force
        $source = 'windows-synthetic-smoke'
        if ($case.audioCondition) { $source += '-' + $case.audioCondition }
        $case | Add-Member -NotePropertyName audioSource -NotePropertyValue $source -Force
    }
    $cases | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Manifest -Encoding utf8
} finally { $synthesizer.Dispose() }
