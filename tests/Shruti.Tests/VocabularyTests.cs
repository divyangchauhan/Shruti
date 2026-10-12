using Shruti.Core.TranscriptQuality;
using Shruti.Storage;
using Shruti.TranscriptQuality;
using Xunit;

namespace Shruti.Tests;

public sealed class VocabularyTests
{
    [Fact]
    public async Task Repository_PersistsDeduplicatesAndDeletesWithoutMixingLanguages()
    {
        string root = Path.Combine(Path.GetTempPath(), "Shruti.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new SqliteVocabularyRepository(new(root));
            await repository.SaveVocabularyAsync(new(Guid.NewGuid(), "en", "Shruti", DateTimeOffset.UtcNow), default);
            await repository.SaveVocabularyAsync(new(Guid.NewGuid(), "en", "shruti", DateTimeOffset.UtcNow), default);
            await repository.SaveVocabularyAsync(new(Guid.NewGuid(), "hi", "Shruti", DateTimeOffset.UtcNow), default);
            var entries = await new SqliteVocabularyRepository(new(root)).ListVocabularyAsync("en", default);
            Assert.Single(entries);
            await repository.DeleteVocabularyAsync(entries[0].Id, default);
            Assert.Empty(await repository.ListVocabularyAsync("en", default));
            Assert.Single(await repository.ListVocabularyAsync("hi", default));
            await repository.SaveCorrectionAsync(Rule("Whisper Flow", "Wispr Flow"), default);
            await repository.SaveCorrectionAsync(Rule("whisper flow", "Wispr FLOW"), default);
            var corrections = await repository.ListCorrectionsAsync("en", default);
            Assert.Equal("Wispr FLOW", Assert.Single(corrections).ReplacementText);
            await repository.DeleteCorrectionAsync(corrections[0].Id, default);
            Assert.Empty(await repository.ListCorrectionsAsync("en", default));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("Ask CLOUD's team, not cloudscape.", "Ask Claude's team, not cloudscape.")]
    [InlineData("dark cloud", "dark Claude")]
    [InlineData("cloudy", "cloudy")]
    [InlineData("cloud’s", "Claude’s")]
    public void Corrections_RequireExplicitRuleAndWholeWords(string raw, string expected)
    {
        Assert.Equal(raw, CorrectionMatcher.Apply(raw, []));
        Assert.Equal(expected, CorrectionMatcher.Apply(raw, [Rule("cloud", "Claude")]));
    }

    [Fact]
    public void Corrections_UseLongestPhraseWithoutCascadingAndRespectContractions()
    {
        Assert.Equal("Wispr Flow", CorrectionMatcher.Apply("WHISPER   FLOW",
            [Rule("Whisper", "wrong"), Rule("Whisper Flow", "Wispr Flow"), Rule("Wispr", "wrong")]));
        Assert.Equal("don't can’t", CorrectionMatcher.Apply("don't can’t", [Rule("don", "wrong"), Rule("can", "wrong")]));
        Assert.Equal("Renée", CorrectionMatcher.Apply("renée", [Rule("Renée", "Renée")]));
    }

    internal static CorrectionRule Rule(string heard, string replacement) => new(Guid.NewGuid(), "en", heard, replacement, DateTimeOffset.UtcNow);
}
