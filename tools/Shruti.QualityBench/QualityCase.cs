namespace Shruti.QualityBench;

public sealed record QualityCase
{
    public required string Id { get; init; }
    public string? Audio { get; init; }
    public string Language { get; init; } = "en";
    public string? BeforeCaret { get; init; }
    public string? SelectedText { get; init; }
    public string? AfterCaret { get; init; }
    public string AppCategory { get; init; } = "Other";
    public string[] Vocabulary { get; init; } = [];
    public Dictionary<string, string> Corrections { get; init; } = [];
    public required string ExpectedRaw { get; init; }
    public required string ExpectedFinal { get; init; }
    public string? SavedRaw { get; init; }
    public string[] Tags { get; init; } = [];
    public string[] ProperNames { get; init; } = [];
    public string[] TechnicalTerms { get; init; } = [];
    public string[] ProtectedTokens { get; init; } = [];
    public string[] ForbiddenTokens { get; init; } = [];
    public string AudioSource { get; init; } = "not-recorded";
}
