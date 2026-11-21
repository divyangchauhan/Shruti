using System.Text.Json;
using Shruti.Models;
using Shruti.TranscriptQuality;
using Xunit;

namespace Shruti.Tests;

public sealed class LocalCleanupModelTests
{
    [Theory]
    [InlineData("utter-mini-v1-q4_0.gguf", true)]
    [InlineData("UTTER-MINI-V1-Q4_0.GGUF", true)]
    [InlineData("qwen3.5-2b-q4_k_m.gguf", false)]
    [InlineData("legacy.gguf", false)]
    public void SavedPathSelectsTheModelProtocol(string filename, bool utter)
    {
        Assert.Same(utter ? LocalCleanupModel.Utter : LocalCleanupModel.Qwen,
            LocalCleanupModel.FromPath(Path.Combine("Cleanup", filename)));
    }

    [Fact]
    public void UtterUsesTheLiteralTextAndQwenReadsTheTextField()
    {
        var choice = Choice("stop", """{"text":"Keep this."}""");
        Assert.Equal("""{"text":"Keep this."}""", LocalTranscriptEditor.ReadReplacement(choice, utter: true));
        Assert.Equal("Keep this.", LocalTranscriptEditor.ReadReplacement(choice, utter: false));
    }

    [Theory]
    [InlineData("length", "Half a sentence")]
    [InlineData("stop", "  ")]
    public void IncompleteUtterOutputCannotBecomeAProposedEdit(string reason, string content)
    {
        Assert.Throws<InvalidDataException>(() => LocalTranscriptEditor.ReadReplacement(Choice(reason, content), utter: true));
    }

    private static JsonElement Choice(string reason, string content) =>
        JsonSerializer.SerializeToElement(new { finish_reason = reason, message = new { content } });
}
