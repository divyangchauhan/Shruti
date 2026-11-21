using Shruti.TranscriptQuality;
using Xunit;

namespace Shruti.Tests;

public class NumericCorrectionTests
{
    [Fact]
    public void ExplicitNumericVocabularyRemainsProtected()
    {
        const string input = "Use build 4.2, no, 4.3 today.";
        Assert.False(TranscriptEditValidator.TryApply(input,
            [new(input, "Use build 4.3 today.", "rewrite")], ["4.2"], out _, out _));
    }

    [Theory]
    [InlineData("Raise the limit from ten to twenty, no, twenty five, then retry.", "Raise the limit from ten to twenty-five, then retry.")]
    [InlineData("Use build four point two, wait, no, four point three today.", "Use build 4.3 today.")]
    [InlineData("Meet at nine, sorry, ten tomorrow.", "Meet at 10 tomorrow.")]
    [InlineData("Set it to 70, I mean, 17.", "Set it to 17.")]
    [InlineData("Use version two point zero one, no, two point zero two today.", "Use version 2.02 today.")]
    [InlineData("Use build 4.2, no, 4.3 today.", "Use build 4.3 today.")]
    public void AcceptsOnlyTheCorrectedNumberInPlace(string input, string output)
    {
        Assert.True(TranscriptEditValidator.TryApply(input, [new(input, output, "rewrite")],
            TranscriptEditValidator.InferProtectedTerms(input).ToArray(), out var actual, out var applied));
        Assert.Equal(output, actual);
        Assert.Single(applied);
    }

    [Theory]
    [InlineData("Raise it from ten to twenty, no, twenty five.", "Raise it from ten to twenty.")]
    [InlineData("Raise it from ten to twenty, no, twenty five.", "Raise it from ten to 26.")]
    [InlineData("Raise it from ten to twenty, no, twenty five.", "Raise it from eleven to 25.")]
    [InlineData("Raise it from ten to twenty, no, twenty five, then retry.", "Twenty five, then retry.")]
    [InlineData("Set it to ten, no, twelve. Do not send.", "Set it to twelve. Do send.")]
    [InlineData("Set it to ten, no, twelve. Ask Priya.", "Set it to twelve. Ask Maya.")]
    [InlineData("Set it to ten, no, twelve. I think it works.", "Set it to twelve. It works.")]
    [InlineData("Set it to ten, no, twelve hours.", "Set it to twelve days.")]
    [InlineData("Use build four point three, no, four point zero three.", "Use build 4.3.")]
    [InlineData("Set it to two, no more than five.", "Set it to five.")]
    [InlineData("Set it to one hundred and twenty, no, thirty.", "Set it to one hundred and thirty.")]
    [InlineData("Read the string \"set it to ten, no, twelve\".", "Read the string \"set it to twelve\".")]
    [InlineData("Read 'set it to ten, no, twelve today' aloud.", "Read 'set it to twelve today' aloud.")]
    [InlineData("Use build 4.2, no, 4.3 today.", "Use build 4.4 today.")]
    public void RejectsAChangeOutsideTheExplicitCorrection(string input, string output)
    {
        Assert.False(TranscriptEditValidator.TryApply(input, [new(input, output, "rewrite")],
            TranscriptEditValidator.InferProtectedTerms(input).ToArray(), out string actual, out _));
        Assert.Equal(input, actual);
    }
}
