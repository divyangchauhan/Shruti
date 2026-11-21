using Shruti.TranscriptQuality;
using Xunit;

namespace Shruti.Tests;

public sealed class TranscriptEditValidatorTests
{
    [Fact]
    public void Validation_AllowsNormalSentenceCaseForTitleCasedRecognition()
    {
        const string raw = "Do Your Recommendation Make Sense? I Agree With Move Forward Implementing It";
        const string cleaned = "Does your recommendation make sense? I agree with moving forward and implementing it.";
        var protectedTerms = TranscriptEditValidator.InferProtectedTerms(raw).ToArray();
        Assert.True(TranscriptEditValidator.TryApply(raw, [new(raw, cleaned, "rewrite")], protectedTerms, out string result, out _));
        Assert.Equal(cleaned, result);
    }

    [Fact]
    public void Validation_TitleCaseKeepsExplicitNamesDatesAndTechnicalSpellingProtected()
    {
        const string raw = "Please Send The OpenAI API Report To Divyang On Monday.";
        const string cleaned = "Please send the OpenAI API report to Divyang on Monday.";
        string[] terms = ["Divyang", .. TranscriptEditValidator.InferProtectedTerms(raw)];
        Assert.True(TranscriptEditValidator.TryApply(raw, [new(raw, cleaned, "rewrite")], terms, out _, out _));
        foreach (string changed in new[] { cleaned.Replace("Divyang", "Dinesh"), cleaned.Replace("OpenAI", "Openai"),
            cleaned.Replace("API", "Api"), cleaned.Replace("Monday", "Tuesday") })
            Assert.False(TranscriptEditValidator.TryApply(raw, [new(raw, changed, "rewrite")], terms, out _, out _));
        Assert.Contains("Divyang", TranscriptEditValidator.InferProtectedTerms("Ask Divyang about the report."));
    }

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
    public void Validation_AcceptsLongRestartAndReplaysEdits()
    {
        const string raw = "Um I wanted to ask if we could I need the report tomorrow.";
        ProposedTranscriptEdit[] proposed = [new("Um ", "", "filler"),
            new("I wanted to ask if we could I need the report tomorrow.", "I need the report tomorrow.", "restart")];
        Assert.True(TranscriptEditValidator.TryApply(raw, proposed, [], out string result, out var edits));
        Assert.Equal("I need the report tomorrow.", result);
        string replay = raw;
        foreach (var edit in edits) replay = replay.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
        Assert.Equal(result, replay);
    }

    [Theory]
    [InlineData("filler")]
    [InlineData("self-correction")]
    [InlineData("grammar")]
    [InlineData("rewrite")]
    public void Validation_RecognizesRestartRegardlessOfModelLabel(string reason)
    {
        const string raw = "I wanted to ask if we could I need the report tomorrow.";
        Assert.True(TranscriptEditValidator.TryApply(raw, [new(raw, "I need the report tomorrow.", reason)], [], out string result, out var edits));
        Assert.Equal("I need the report tomorrow.", result);
        Assert.Equal("local-restart", Assert.Single(edits).Rule);
    }

    [Theory]
    [InlineData("Send it Friday, no, send it Monday.", "send it Monday.")]
    [InlineData("I don't want to I want to send 42 files.", "I want to send 42 files.")]
    [InlineData("Ask Divyang about the Ask Divyang about the report.", "Ask Divyang about the report.")]
    [InlineData("We need to arrange a delivery to we need the package delivered tomorrow.", "We need the package delivered tomorrow.")]
    [InlineData("I was thinking we should send the we should send the report tomorrow.", "We should send the report tomorrow.")]
    public void Validation_RestartKeepsFactsInTheCompletedClause(string raw, string expected)
    {
        Assert.True(TranscriptEditValidator.TryApply(raw, [new(raw, expected, "restart")], ["Divyang"], out string result, out _));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("I wanted to I need 42 files.", "I need 43 files.")]
    [InlineData("I wanted to I do not need files.", "I do need files.")]
    [InlineData("I wanted to I need files.", "")]
    [InlineData("I wanted to I need files.", "I wanted to")]
    [InlineData("I wanted to I need files.", "need files.")]
    public void Validation_RestartCannotRewriteOrDeleteTheCompletedClause(string raw, string replacement)
        => Assert.False(TranscriptEditValidator.TryApply(raw, [new(raw, replacement, "restart")], [], out _, out _));

    [Fact]
    public void Validation_RestartDoesNotRelaxProtectionForOtherEdits()
    {
        const string raw = "I wanted to I need the report. Do not send 42 files.";
        Assert.False(TranscriptEditValidator.TryApply(raw,
            [new("I wanted to I need the report.", "I need the report.", "restart"), new("42", "43", "grammar")], [], out _, out _));
        Assert.False(TranscriptEditValidator.TryApply(raw,
            [new("I wanted to I need the report.", "I need the report.", "restart"), new("not ", "", "grammar")], [], out _, out _));
    }

    [Fact]
    public void Validation_AllowsMeaningPreservingRewriteBeyondSmallEditBudget()
    {
        const string raw = "I wanted to let you know that I cannot attend the meeting tomorrow.";
        const string rewritten = "I cannot attend tomorrow's meeting.";
        Assert.True(TranscriptEditValidator.TryApply(raw, [new(raw, rewritten, "rewrite")], [], out string result, out _));
        Assert.Equal(rewritten, result);
        Assert.False(TranscriptEditValidator.TryApply(raw, [new(raw, "I can attend tomorrow's meeting.", "rewrite")], [], out _, out _));
        Assert.False(TranscriptEditValidator.TryApply("Send 42 files to Divyang.",
            [new("Send 42 files to Divyang.", "Send 43 files to Dinesh.", "rewrite")], ["Divyang"], out _, out _));
    }

    [Fact]
    public void WordError_CountsInsertionsDeletionsAndSubstitutions()
    {
        Assert.Equal((1, 3), Shruti.QualityBench.QualityScorer.WordError("one two three", "one three"));
        Assert.Equal((1, 2), Shruti.QualityBench.QualityScorer.WordError("one two", "one two three"));
        Assert.Equal((1, 2), Shruti.QualityBench.QualityScorer.WordError("one two", "one three"));
    }
}
