using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shruti.Core.Platform;
using Shruti.Core.TranscriptQuality;
using Shruti.QualityBench;
using Shruti.Transcription.Abstractions;
using Shruti.Transcription.WhisperCpp;
using Shruti.TranscriptQuality;

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
string manifest = args.FirstOrDefault() ?? "tests/quality/manifest.json";
string Option(string name, string fallback = "")
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}
string stage = Option("--stage", "refinement");
if (stage is not ("recognition" or "refinement" or "full")) throw new ArgumentException("Stage must be recognition, refinement, or full.");
string modelPath = Option("--model");
string nativeDirectory = Option("--native-directory");
string cleanupModel = Option("--cleanup-model");
string cleanupRuntime = Option("--cleanup-runtime");
string backendName = Option("--backend", "Cpu");
bool disableRecognitionHints = args.Contains("--no-hints");
string outputName = Option("--name", stage + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
if (Path.GetFileName(outputName) != outputName) throw new ArgumentException("Report name must not contain a path.");
if (nativeDirectory.Length > 0)
    NativeLibrary.SetDllImportResolver(typeof(WhisperCppNativeApi).Assembly, (name, _, _) =>
        NativeLibrary.Load(Path.Combine(Path.GetFullPath(nativeDirectory), name + ".dll")));
QualityCase[] cases = JsonSerializer.Deserialize<QualityCase[]>(await File.ReadAllTextAsync(manifest), jsonOptions)!;
if (cases.Select(test => test.Id).Distinct().Count() != cases.Length) throw new InvalidDataException("Fixture IDs must be unique.");
WhisperCppTranscriptionProvider? provider = stage == "refinement" ? null : new(new WhisperCppTranscriptionEngine(new WhisperCppNativeApi()));
var rows = new List<QualityMeasurement>();
using var editor = new LocalTranscriptEditor();
try
{
    foreach (QualityCase fixture in cases)
    {
        if (stage != "refinement" && fixture.Audio is null)
        { rows.Add(new(fixture.Id, stage, fixture.Tags, fixture.AudioSource, SkippedReason: "No audio fixture")); continue; }
        var pipeline = new TranscriptQualityPipeline(new FixtureVocabularyRepository(fixture), new FixtureContextReader(fixture), editor);
        var settings = new TranscriptQualitySettings
        {
            EnableLocalModel = stage != "recognition" && cleanupModel.Length > 0,
            LocalModelPath = cleanupModel,
            LocalRuntimePath = cleanupRuntime,
            RefinementTimeout = TimeSpan.FromSeconds(10)
        };
        var target = new FocusTarget(IntPtr.Zero, -1, "fixture", null);
        var cold = Stopwatch.StartNew();
        await using var quality = await pipeline.BeginAsync(target, settings, default);
        cold.Stop();
        string raw = fixture.SavedRaw ?? fixture.ExpectedRaw;
        double recognitionMs = 0;
        var latency = new Stopwatch();
        if (provider is not null)
        {
            var descriptor = new TranscriptionModelDescriptor(Path.GetFileNameWithoutExtension(modelPath), "Benchmark model", "whisper.cpp", modelPath,
                "en", new FileInfo(modelPath).Length,
                new HashSet<ComputeBackend> { ComputeBackend.Cpu, ComputeBackend.Gpu });
            var options = new TranscriptionSessionOptions(descriptor, Enum.Parse<ComputeBackend>(backendName), fixture.Language.Split('-')[0], TranscriptionMode.Balanced,
                RecognitionHints: disableRecognitionHints ? null : quality.RecognitionHints);
            await using var recognition = await provider.CreateSessionAsync(options, default);
            string audio = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest))!, fixture.Audio!));
            await recognition.PushAudioAsync(PcmWaveReader.Read(audio), default);
            latency.Start();
            raw = (await recognition.CompleteAsync(default)).Text;
            recognitionMs = latency.Elapsed.TotalMilliseconds;
        }
        else latency.Start();
        var refined = RefinedTranscript.Unchanged(TranscriptResult.FromText(raw));
        if (stage != "recognition")
        {
            using var deadline = new CancellationTokenSource(settings.RefinementTimeout);
            try { refined = await quality.RefineAsync(refined.Raw, deadline.Token); }
            catch (Exception) { refined = RefinedTranscript.Unchanged(refined.Raw, true); }
        }
        latency.Stop();
        var wer = QualityScorer.WordError(fixture.ExpectedRaw, raw);
        rows.Add(new(fixture.Id, stage, fixture.Tags, fixture.AudioSource, Raw: raw, Final: refined.Text,
            WordErrors: wer.Errors, ReferenceWords: wer.ReferenceWords, FormattingExactMatch: refined.Text == fixture.ExpectedFinal,
            ProperNamesCorrect: fixture.ProperNames.Count(term => QualityScorer.ContainsTerm(raw, term)), ProperNameCount: fixture.ProperNames.Length,
            TechnicalTermsCorrect: fixture.TechnicalTerms.Count(term => QualityScorer.ContainsTerm(raw, term)), TechnicalTermCount: fixture.TechnicalTerms.Length,
            DictionarySuccess: fixture.Corrections.Count == 0 ? null : fixture.Corrections.Values.All(term => QualityScorer.ContainsTerm(refined.Text, term)),
            VocabularyFalsePositive: fixture.ForbiddenTokens.Any(term => QualityScorer.ContainsTerm(refined.Text, term)),
            SelfCorrectionSuccess: fixture.Tags.Contains("self-correction") ? refined.Text == fixture.ExpectedFinal : null,
            ProtectedTokenChanges: fixture.ProtectedTokens.Count(term => QualityScorer.CountTerm(raw, term) != QualityScorer.CountTerm(refined.Text, term)),
            FalseDeletion: fixture.Tags.Contains("false-deletion") ? fixture.ProtectedTokens.Any(term => !QualityScorer.ContainsTerm(refined.Text, term)) : null,
            StopToResultMs: latency.Elapsed.TotalMilliseconds, RecognitionMs: recognitionMs, PreparationMs: cold.Elapsed.TotalMilliseconds,
            UsedRawFallback: refined.UsedRawFallback, EditRules: refined.Edits.Select(edit => edit.Rule).ToArray()));
        Console.WriteLine($"{fixture.Id}: {latency.Elapsed.TotalMilliseconds:F0} ms; raw fallback {refined.UsedRawFallback}.");
    }
}
finally { if (provider is not null) await provider.DisposeAsync(); }
Directory.CreateDirectory("artifacts/quality");
var completed = rows.Where(row => row.SkippedReason is null).ToArray();
int totalWords = completed.Sum(row => row.ReferenceWords);
var report = new
{
    SchemaVersion = 1,
    CreatedAtUtc = DateTimeOffset.UtcNow,
    Stage = stage,
    Backend = backendName,
    RecognitionHintsEnabled = !disableRecognitionHints,
    AsrModel = modelPath.Length == 0 ? null : Path.GetFileName(modelPath),
    AsrModelSha256 = modelPath.Length == 0 ? null : Hash(modelPath),
    CleanupModel = cleanupModel.Length == 0 ? null : Path.GetFileName(cleanupModel),
    CleanupModelSha256 = cleanupModel.Length == 0 ? null : Hash(cleanupModel),
    CleanupProcessPeakWorkingSetBytes = editor.PeakWorkingSetBytes,
    NativeSha256 = nativeDirectory.Length == 0 ? null : Hash(Path.Combine(nativeDirectory, "shruti_whisper.dll")),
    Runtime = RuntimeInformation.FrameworkDescription,
    OperatingSystem = RuntimeInformation.OSDescription,
    WordErrorRate = totalWords == 0 ? (double?)null : completed.Sum(row => row.WordErrors) / (double)totalWords,
    FormattingExactMatchRate = completed.Length == 0 ? (double?)null : completed.Count(row => row.FormattingExactMatch) / (double)completed.Length,
    MeaningReview = "Required. Automated protected-token checks do not prove meaning preservation.",
    LatencyScope = "Recognition completion plus refinement; excludes capture, model preparation, focus restoration, and insertion.",
    Cases = rows
};
await File.WriteAllTextAsync($"artifacts/quality/{outputName}.json", JsonSerializer.Serialize(report, jsonOptions));
var markdown = new StringBuilder($"# Quality report\n\nStage: {stage}. Backend: {backendName}.\n\nWER: {report.WordErrorRate:P1}. Formatting exact match: {report.FormattingExactMatchRate:P1}.\n\nSynthetic audio is smoke evidence only. Meaning preservation requires review.\n\n| Case | Exact match | Result ms | Protected changes | Raw fallback |\n| --- | --- | --- | --- | --- |\n");
foreach (var row in rows) markdown.AppendLine($"| {row.Id} | {(row.SkippedReason ?? row.FormattingExactMatch.ToString())} | {row.StopToResultMs:F0} | {row.ProtectedTokenChanges} | {row.UsedRawFallback} |");
await File.WriteAllTextAsync($"artifacts/quality/{outputName}.md", markdown.ToString());
Console.WriteLine($"{completed.Length}/{rows.Count} cases. WER {report.WordErrorRate:P1}, exact match {report.FormattingExactMatchRate:P1}. Reports: artifacts/quality/{outputName}");
static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

internal sealed record QualityMeasurement(string Id, string Stage, string[] Tags, string AudioSource,
    string? SkippedReason = null, string? Raw = null, string? Final = null, int WordErrors = 0, int ReferenceWords = 0,
    bool FormattingExactMatch = false, int ProperNamesCorrect = 0, int ProperNameCount = 0,
    int TechnicalTermsCorrect = 0, int TechnicalTermCount = 0, bool? DictionarySuccess = null,
    bool VocabularyFalsePositive = false, bool? SelfCorrectionSuccess = null, int ProtectedTokenChanges = 0,
    bool? FalseDeletion = null, double StopToResultMs = 0, double RecognitionMs = 0, double PreparationMs = 0,
    bool UsedRawFallback = false, string[]? EditRules = null);
