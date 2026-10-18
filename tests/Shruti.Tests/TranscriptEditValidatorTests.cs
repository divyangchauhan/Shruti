using Shruti.TranscriptQuality;
using Xunit;

namespace Shruti.Tests;

public sealed class TranscriptEditValidatorTests
{
    [Theory]
    [InlineData("Ask Shruti tomorrow.", "Shruti", "Claude")]
    [InlineData("Do not send it.", "not ", "")]
    [InlineData("Send 42 files.", "42", "43")]
    [InlineData("Ask Divyang tomorrow.", "Divyang", "Dinesh")]
    [InlineData("Send twenty files on Monday.", "twenty", "thirty")]
    [InlineData("Send it on monday.", "monday", "tuesday")]
    [InlineData("Read Shruti.cs now.", "Shruti.cs", "Shruti.txt")]
    [InlineData("Open https://example.com today.", "example.com", "other.com")]
    [InlineData("Say \"um hello\" exactly.", "um ", "")]
    public void Validation_RejectsProtectedChanges(string text, string original, string replacement)
        => Assert.False(TranscriptEditValidator.TryApply(text, [new(original, replacement, "grammar")], ["Shruti"], out _, out _));

    [Fact]
    public void Validation_AcceptsSmallEditsAndRejectsAmbiguousOrOverlappingEdits()
    {
        Assert.True(TranscriptEditValidator.TryApply("Um please send the report.", [new("Um", "", "filler")], [], out string result, out var applied));
        Assert.Equal(" please send the report.", result);
        Assert.Single(applied);
        Assert.True(TranscriptEditValidator.TryApply("Um please uh send report.",
            [new("Um ", "", "filler"), new("uh ", "", "filler")], [], out string cleaned, out var multiple));
        string replay = "Um please uh send report.";
        foreach (var edit in multiple) replay = replay.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        Assert.Equal(cleaned, replay);
        Assert.False(TranscriptEditValidator.TryApply("um um hello", [new("um", "", "filler")], [], out _, out _));
        Assert.False(TranscriptEditValidator.TryApply("hello world", [new("hello", "hi", "grammar"), new("hello world", "bye", "grammar")], [], out _, out _));
        Assert.False(TranscriptEditValidator.TryApply("send the report tomorrow", [new("send the report tomorrow", "cancel all invoices immediately", "grammar")], [], out _, out _));
    }

    [Fact]
    public void WordError_CountsInsertionsDeletionsAndSubstitutions()
    {
        Assert.Equal((1, 3), Shruti.QualityBench.QualityScorer.WordError("one two three", "one three"));
        Assert.Equal((1, 2), Shruti.QualityBench.QualityScorer.WordError("one two", "one two three"));
        Assert.Equal((1, 2), Shruti.QualityBench.QualityScorer.WordError("one two", "one three"));
    }
}
