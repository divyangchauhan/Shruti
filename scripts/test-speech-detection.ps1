param(
    [Parameter(Mandatory)][string] $NativeDirectory,
    [string] $ModelPath,
    [string] $AudioDirectory,
    [string] $ReportPath
)
$ErrorActionPreference = 'Stop'
if (!$ModelPath) { $ModelPath = Join-Path $PSScriptRoot '../artifacts/speech-detection/ggml-silero-v6.2.0.bin' }
if (!$AudioDirectory) { $AudioDirectory = Join-Path $PSScriptRoot '../tests/quality/audio' }
if (!$ReportPath) { $ReportPath = Join-Path $PSScriptRoot '../artifacts/quality/speech-detection.json' }
Add-Type @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class SpeechDetectionProbe {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int Detect([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [In] float[] samples, int count);
    static Detect detect;
    public static void Load(string directory) {
        if (LoadLibraryExW(Path.Combine(Environment.SystemDirectory, "cfgmgr32.dll"), IntPtr.Zero, 0x800) == IntPtr.Zero)
            throw new InvalidOperationException("Could not load Windows configuration manager.");
        var module = LoadLibraryExW(Path.Combine(directory, "shruti_whisper.dll"), IntPtr.Zero, 0x8);
        if (module == IntPtr.Zero) throw new InvalidOperationException("Cannot load speech runtime: " + Marshal.GetLastWin32Error());
        var entry = GetProcAddress(module, "shruti_whisper_detect_speech");
        if (entry == IntPtr.Zero) throw new InvalidOperationException("Speech detection export is missing.");
        detect = (Detect)Marshal.GetDelegateForFunctionPointer(entry, typeof(Detect));
    }
    public static int Run(string model, float[] samples) { return detect(model, samples, samples.Length); }
    public static float[] ReadWave(string path) {
        using (var reader = new BinaryReader(File.OpenRead(path))) {
            if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Expected WAV.");
            reader.ReadInt32();
            if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Expected WAVE.");
            bool formatFound = false;
            while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length) {
                string id = new string(reader.ReadChars(4));
                int size = reader.ReadInt32();
                long next = reader.BaseStream.Position + size + (size % 2);
                if (id == "fmt ") {
                    if (reader.ReadInt16() != 1 || reader.ReadInt16() != 1 || reader.ReadInt32() != 16000)
                        throw new InvalidDataException("Expected PCM16 mono at 16 kHz.");
                    reader.ReadInt32(); reader.ReadInt16();
                    if (reader.ReadInt16() != 16) throw new InvalidDataException("Expected PCM16.");
                    formatFound = true;
                } else if (id == "data") {
                    if (!formatFound || size % 2 != 0) throw new InvalidDataException("Invalid PCM data.");
                    var audio = new float[size / 2];
                    for (int i = 0; i < audio.Length; i++) audio[i] = reader.ReadInt16() / 32768f;
                    return audio;
                }
                reader.BaseStream.Position = next;
            }
            throw new InvalidDataException("Missing WAV data.");
        }
    }
    public static float[] Noise(bool tone) {
        var samples = new float[32000]; var random = new Random(42);
        for (int i=0; i<samples.Length; i++) samples[i] = tone
            ? (float)(0.03 * Math.Sin(2 * Math.PI * 120 * i / 16000))
            : (float)((random.NextDouble() * 2 - 1) * 0.03);
        return samples;
    }
}
'@
$nativePath = (Resolve-Path -LiteralPath $NativeDirectory).Path
$model = (Resolve-Path -LiteralPath $ModelPath).Path
[SpeechDetectionProbe]::Load($nativePath)
$results = [Collections.Generic.List[object]]::new()
function Assert-Speech([string] $id, [float[]] $samples, [bool] $expected) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $actual = [SpeechDetectionProbe]::Run($model, $samples)
    $timer.Stop()
    $results.Add([pscustomobject]@{ Id=$id; ExpectedSpeech=$expected; DetectedSpeech=($actual -eq 1); Status=$actual; Milliseconds=$timer.ElapsedMilliseconds })
}
Assert-Speech 'digital-silence' ([float[]]::new(32000)) $false
Assert-Speech 'steady-hum' ([SpeechDetectionProbe]::Noise($true)) $false
Assert-Speech 'white-noise' ([SpeechDetectionProbe]::Noise($false)) $false
$click = [float[]]::new(32000); $click[8000] = 0.9
Assert-Speech 'single-click' $click $false
foreach ($file in Get-ChildItem -LiteralPath $AudioDirectory -Filter '*.wav') {
    Assert-Speech $file.BaseName ([SpeechDetectionProbe]::ReadWave($file.FullName)) $true
}
$report = [IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Force -Path (Split-Path $report) | Out-Null
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $report
$failed = @($results | Where-Object { $_.Status -lt 0 -or $_.DetectedSpeech -ne $_.ExpectedSpeech })
if ($failed.Count) { throw "Speech detection failed $($failed.Count) of $($results.Count) cases. See $report" }
Write-Output "PASS: $($results.Count) speech detection audio cases. Report: $report"
