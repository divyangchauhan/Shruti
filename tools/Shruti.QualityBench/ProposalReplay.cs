using System.Security.Cryptography;
using System.Text.Json;
using Shruti.Core.Platform;
using Shruti.Core.TranscriptQuality;
using Shruti.TranscriptQuality;
using Shruti.Transcription.Abstractions;

namespace Shruti.QualityBench;

internal static class ProposalReplay
{
    public static async Task RunAsync(string manifest, string report, string destination)
    {
        byte[] bytes = await File.ReadAllBytesAsync(manifest);
        var fixtures = JsonSerializer.Deserialize<QualityCase[]>(bytes)!.ToDictionary(row => row.Id);
        using var source = JsonDocument.Parse(await File.ReadAllBytesAsync(report));
        var root = source.RootElement;
        if (root.GetProperty("Stage").GetString() != "refinement" ||
            root.GetProperty("ManifestSha256").GetString() != Convert.ToHexString(SHA256.HashData(bytes)))
            throw new InvalidDataException("Replay requires the original refinement manifest.");
        var results = new List<object>();
        int changed = 0;
        foreach (QualityMeasurement row in root.GetProperty("Cases").Deserialize<QualityMeasurement[]>()!)
        {
            if (!fixtures.Remove(row.Id, out var fixture) || row.Raw != (fixture.SavedRaw ?? fixture.ExpectedRaw))
                throw new InvalidDataException($"Unexpected fixture or input for {row.Id}.");
            string final;
            bool? acceptedProposal = null;
            if (row.UsedRawFallback && row.Proposal is null && row.Final == row.Raw)
                final = row.Raw!;
            else
            {
                var editor = new SavedEditor(row);
                using var pipeline = new TranscriptQualityPipeline(new FixtureVocabularyRepository(fixture), new FixtureContextReader(fixture), editor);
                var target = new FocusTarget(IntPtr.Zero, -1, "fixture", null);
                await using var session = await pipeline.BeginAsync(target, new TranscriptQualitySettings { EnableLocalModel = true }, default);
                var refined = await session.RefineAsync(TranscriptResult.FromText(row.Raw!), default);
                final = refined.Text;
                if (editor.Called != (row.Proposal is not null)) throw new InvalidDataException($"Editor coverage differs for {row.Id}.");
                if (editor.Called && row.Proposal != row.ModelInput)
                    acceptedProposal = refined.Edits.Any(edit => edit.Rule.StartsWith("local-", StringComparison.Ordinal));
            }
            if (final != row.Final) changed++;
            results.Add(new { row.Id, row.Raw, row.Proposal, Previous = row.Final, Final = final, row.UsedRawFallback, AcceptedProposal = acceptedProposal });
        }
        if (fixtures.Count != 0) throw new InvalidDataException("Missing fixture IDs in report.");
        await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{changed}/{results.Count} final outputs changed.");
    }

    private sealed class SavedEditor(QualityMeasurement row) : ILocalTranscriptEditor
    {
        internal bool Called { get; private set; }
        public Task PrepareAsync(TranscriptQualitySettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ProposedTranscriptEdit>> ProposeAsync(string text, IReadOnlyList<string> protectedTerms,
            TranscriptAppCategory category, TranscriptQualitySettings settings, CancellationToken cancellationToken)
        {
            if (Called || text != row.ModelInput || row.Proposal is null) throw new InvalidDataException($"Model input differs for {row.Id}.");
            Called = true;
            return Task.FromResult<IReadOnlyList<ProposedTranscriptEdit>>(text == row.Proposal ? [] : [new(text, row.Proposal, "rewrite")]);
        }
    }
}
