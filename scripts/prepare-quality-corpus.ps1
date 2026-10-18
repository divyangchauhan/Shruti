[CmdletBinding()]
param(
    [string]$Destination = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\quality-corpus\librispeech-test-clean-v1'),
    [ValidateRange(1, 2620)][int]$Count = 100,
    [string]$Ffmpeg = 'ffmpeg.exe'
)
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
$converter = (Get-Command $Ffmpeg -ErrorAction Stop).Source
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$archive = Join-Path $Destination 'test-clean.tar.gz'
# OpenSLR's published checksum identifies this version of the public corpus.
# https://www.openslr.org/resources/12/md5sum.txt
$expectedMd5 = '32fa31d27d2e1cad72775fee3f4849a9'
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm MD5).Hash -ne $expectedMd5) {
    $download = "$archive.download"
    & curl.exe --fail --location --retry 3 --continue-at - --output $download 'https://www.openslr.org/resources/12/test-clean.tar.gz'
    if ($LASTEXITCODE -ne 0) { throw 'Corpus download failed.' }
    if ((Get-FileHash -LiteralPath $download -Algorithm MD5).Hash -ne $expectedMd5) { throw 'Corpus checksum mismatch.' }
    Move-Item -LiteralPath $download -Destination $archive -Force
}
& tar.exe -xzf $archive -C $Destination
if ($LASTEXITCODE -ne 0) { throw 'Corpus extraction failed.' }
$audioDirectory = Join-Path $Destination 'audio'
New-Item -ItemType Directory -Force -Path $audioDirectory | Out-Null
$cases = [Collections.Generic.List[object]]::new()
foreach ($transcript in (Get-ChildItem (Join-Path $Destination 'LibriSpeech\test-clean') -Recurse -Filter '*.trans.txt' | Sort-Object FullName)) {
    foreach ($line in Get-Content -LiteralPath $transcript.FullName) {
        if ($cases.Count -ge $Count) { break }
        $id, $expected = $line.Split(' ', 2)
        $wave = Join-Path $audioDirectory "$id.wav"
        & $converter -v error -y -i (Join-Path $transcript.DirectoryName "$id.flac") -ar 16000 -ac 1 -c:a pcm_s16le $wave
        if ($LASTEXITCODE -ne 0) { throw "Audio conversion failed: $id" }
        $cases.Add([ordered]@{
            id = "librispeech-$id"; audio = "audio/$id.wav"; language = 'en'
            expectedRaw = $expected; expectedFinal = $expected; tags = @('human-read-speech', 'recognition-baseline')
            audioSource = 'OpenSLR12-LibriSpeech-test-clean-CC-BY-4.0'
        })
    }
    if ($cases.Count -ge $Count) { break }
}
ConvertTo-Json -InputObject $cases.ToArray() -Depth 8 | Set-Content -LiteralPath (Join-Path $Destination 'manifest.json') -Encoding utf8
@'
LibriSpeech test-clean, OpenSLR SLR12, CC BY 4.0.
Authors: Vassil Panayotov, Guoguo Chen, Daniel Povey, Sanjeev Khudanpur.
Source: https://www.openslr.org/12/
License: https://creativecommons.org/licenses/by/4.0/
The manifest uses the first N utterances in sorted speaker/chapter order. Do not tune on held-out cases.
This is read speech, not an Indian English dictation or formatting benchmark.
ExpectedFinal equals the unformatted reference. Use the recognition stage for this corpus.
'@ | Set-Content -LiteralPath (Join-Path $Destination 'ATTRIBUTION.txt') -Encoding utf8
Write-Output "Prepared $($cases.Count) public cases at $Destination."
