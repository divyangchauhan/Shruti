using Shruti.Core.Platform;
using Shruti.Transcription.Abstractions;

namespace Shruti.Core.TranscriptQuality;

public enum TranscriptAppCategory { Other, Email, WorkMessaging, PersonalMessaging, Document }
public enum TranscriptCleanupMode { Off, Light }

public sealed record TranscriptContext(string? BeforeCaret, string? SelectedText, string? AfterCaret,
    TranscriptAppCategory AppCategory);

public sealed record TranscriptEdit(string Rule, int Start, int Length, string Replacement);

public sealed record RefinedTranscript(TranscriptResult Raw, string Text,
    IReadOnlyList<TranscriptEdit> Edits, bool UsedRawFallback)
{
    public static RefinedTranscript Unchanged(TranscriptResult raw, bool fallback = false) => new(raw, raw.Text, [], fallback);
}

public sealed record TranscriptQualitySettings
{
    public TranscriptCleanupMode CleanupMode { get; init; } = TranscriptCleanupMode.Light;
    public string Language { get; init; } = "en";
    public bool EnableCursorContext { get; init; } = true;
    public bool EnableLocalModel { get; init; }
    public int MaximumPromptTokens { get; init; } = 128;
    public TimeSpan RefinementTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public string? LocalModelPath { get; init; }
    public string? LocalRuntimePath { get; init; }
}

public interface ITranscriptQualityPipeline
{
    Task<ITranscriptQualitySession> BeginAsync(FocusTarget? target, TranscriptQualitySettings settings,
        CancellationToken cancellationToken);
}

public interface ITranscriptQualitySession : IAsyncDisposable
{
    RecognitionHints RecognitionHints { get; }
    Task<RefinedTranscript> RefineAsync(TranscriptResult rawTranscript, CancellationToken cancellationToken);
}
