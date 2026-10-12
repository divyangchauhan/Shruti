using Shruti.Core.Platform;
using Shruti.Core.TranscriptQuality;
using Shruti.Transcription.Abstractions;

namespace Shruti.TranscriptQuality;

public sealed class TranscriptQualityPipeline(IVocabularyRepository repository, ITextContextReader? contextReader = null) : ITranscriptQualityPipeline
{
    public async Task<ITranscriptQualitySession> BeginAsync(FocusTarget? target, TranscriptQualitySettings settings,
        CancellationToken cancellationToken)
    {
        var vocabulary = await repository.ListVocabularyAsync(settings.Language, cancellationToken).ConfigureAwait(false);
        var corrections = await repository.ListCorrectionsAsync(settings.Language, cancellationToken).ConfigureAwait(false);
        TranscriptContext? context = null;
        if (settings.EnableCursorContext && target is not null && contextReader is not null)
            context = await contextReader.ReadAsync(target, cancellationToken).ConfigureAwait(false);
        return new Session(vocabulary, corrections, context, settings);
    }

    private sealed class Session : ITranscriptQualitySession
    {
        private TranscriptContext? _context;
        private readonly IReadOnlyList<CorrectionRule> _corrections;
        private readonly TranscriptQualitySettings _settings;
        private bool _disposed;
        public RecognitionHints RecognitionHints { get; }

        public Session(IReadOnlyList<VocabularyEntry> vocabulary, IReadOnlyList<CorrectionRule> corrections,
            TranscriptContext? context, TranscriptQualitySettings settings)
        {
            _corrections = corrections; _context = context; _settings = settings;
            RecognitionHints = RecognitionHintBuilder.Build(vocabulary, context, corrections, settings.MaximumPromptTokens);
        }

        public Task<RefinedTranscript> RefineAsync(TranscriptResult rawTranscript, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var edits = new List<TranscriptEdit>();
            string text = CorrectionMatcher.Apply(rawTranscript.Text, _corrections);
            Record(edits, "dictionary", rawTranscript.Text, text);
            string formatted = DeterministicRefiner.Refine(text, RecognitionHints.Terms, _context, _settings.CleanupMode, edits);
            Record(edits, "formatting", text, formatted);
            string final = DeterministicRefiner.Join(formatted, _context, RecognitionHints.Terms);
            Record(edits, "cursor-join", formatted, final);
            _context = null;
            return Task.FromResult(new RefinedTranscript(rawTranscript, final, edits, false));
        }

        internal static void Record(List<TranscriptEdit> edits, string rule, string before, string after)
        {
            if (before != after) edits.Add(new(rule, 0, before.Length, after));
        }
        public ValueTask DisposeAsync() { _context = null; _disposed = true; return ValueTask.CompletedTask; }
    }
}
