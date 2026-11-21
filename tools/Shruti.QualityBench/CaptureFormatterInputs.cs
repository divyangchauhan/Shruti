using System.Text.Json;
using Shruti.Core.Platform;
using Shruti.Core.TranscriptQuality;
using Shruti.TranscriptQuality;
using Shruti.Transcription.Abstractions;

namespace Shruti.QualityBench;

internal sealed class CaptureFormatterInputs : ILocalTranscriptEditor
{
    private object? _request;
    public Task PrepareAsync(TranscriptQualitySettings settings, CancellationToken token) => Task.CompletedTask;
    public Task<IReadOnlyList<ProposedTranscriptEdit>> ProposeAsync(string text, IReadOnlyList<string> terms,
        TranscriptAppCategory category, TranscriptQualitySettings settings, CancellationToken token)
    {
        _request = new { transcript = text, spellingHints = terms.ToArray(), appCategory = category.ToString() };
        return Task.FromResult<IReadOnlyList<ProposedTranscriptEdit>>([]);
    }

    public static async Task ExportAsync(string manifest, string destination)
    {
        var cases = JsonSerializer.Deserialize<QualityCase[]>(await File.ReadAllBytesAsync(manifest))!;
        if (cases.Select(row => row.Id).Distinct().Count() != cases.Length) throw new InvalidDataException("Duplicate fixture ID.");
        var requests = new List<object>();
        foreach (var fixture in cases)
        {
            var editor = new CaptureFormatterInputs();
            using var pipeline = new TranscriptQualityPipeline(new FixtureVocabularyRepository(fixture), new FixtureContextReader(fixture), editor);
            var focus = new FocusTarget(IntPtr.Zero, -1, "fixture", null);
            await using var session = await pipeline.BeginAsync(focus, new TranscriptQualitySettings { EnableLocalModel = true }, default);
            string raw = fixture.SavedRaw ?? fixture.ExpectedRaw;
            var result = await session.RefineAsync(TranscriptResult.FromText(raw), default);
            requests.Add(new { fixture.Id, Raw = raw, Request = editor._request, RulesFinal = result.Text, fixture.Vocabulary });
        }
        await File.WriteAllBytesAsync(destination, JsonSerializer.SerializeToUtf8Bytes(requests));
    }
}
