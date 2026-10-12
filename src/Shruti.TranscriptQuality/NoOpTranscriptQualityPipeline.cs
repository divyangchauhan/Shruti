using Shruti.Core.Platform;
using Shruti.Core.TranscriptQuality;
using Shruti.Transcription.Abstractions;

namespace Shruti.TranscriptQuality;

public sealed class NoOpTranscriptQualityPipeline : ITranscriptQualityPipeline
{
    public Task<ITranscriptQualitySession> BeginAsync(FocusTarget? target, TranscriptQualitySettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ITranscriptQualitySession>(new Session());
    }

    private sealed class Session : ITranscriptQualitySession
    {
        public RecognitionHints RecognitionHints => RecognitionHints.Empty;
        public Task<RefinedTranscript> RefineAsync(TranscriptResult rawTranscript, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(RefinedTranscript.Unchanged(rawTranscript));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
