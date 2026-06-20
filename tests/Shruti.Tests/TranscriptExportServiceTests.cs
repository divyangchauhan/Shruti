using System.Text.Json;
using Shruti.Storage;
using Xunit;

namespace Shruti.Tests;

public sealed class TranscriptExportServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "Shruti.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Render_ProducesExpectedTextMarkdownAndJson()
    {
        var service = new TranscriptExportService();
        StoredDictationSession session = CreateSession();

        string text = service.Render(session, TranscriptExportFormat.Text);
        string markdown = service.Render(session, TranscriptExportFormat.Markdown);
        string json = service.Render(session, TranscriptExportFormat.Json);

        Assert.Equal("Hello world.\nA second line.", text);
        Assert.Contains("# Shruti Transcript", markdown, StringComparison.Ordinal);
        Assert.Contains("[00:00:00.000] Hello world.", markdown, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal("en", document.RootElement.GetProperty("language").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("segments").GetArrayLength());
    }

    [Fact]
    public async Task ExportAsync_WritesSrtAndVttFixtures()
    {
        var service = new TranscriptExportService();
        StoredDictationSession session = CreateSession();
        string srtPath = Path.Combine(_rootPath, "nested", "dictation.srt");
        string vttPath = Path.Combine(_rootPath, "nested", "dictation.vtt");

        await service.ExportAsync(session, TranscriptExportFormat.Srt, srtPath, CancellationToken.None);
        await service.ExportAsync(session, TranscriptExportFormat.Vtt, vttPath, CancellationToken.None);

        string srt = await File.ReadAllTextAsync(srtPath);
        string vtt = await File.ReadAllTextAsync(vttPath);
        Assert.Equal(
            "1\n00:00:00,000 --> 00:00:01,250\nHello world.\n\n2\n00:00:02,000 --> 00:00:03,500\nA second line.\n",
            srt);
        Assert.Equal($"WEBVTT\n\n{srt.Replace(',', '.')}", vtt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private static StoredDictationSession CreateSession()
    {
        return new StoredDictationSession(
            Guid.Parse("14c85c58-aa5f-430d-93d7-93e01487d36d"),
            new DateTimeOffset(2026, 6, 20, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 20, 9, 0, 4, TimeSpan.Zero),
            "AppButton",
            "notepad",
            "Untitled - Notepad",
            "tiny.en",
            "whisper.cpp",
            "Cpu",
            "en",
            "Complete",
            [
                new StoredTranscriptSegment(1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3.5), "A second line."),
                new StoredTranscriptSegment(0, TimeSpan.Zero, TimeSpan.FromSeconds(1.25), "Hello world.")
            ]);
    }
}
