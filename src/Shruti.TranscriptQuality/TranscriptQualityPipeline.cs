using Shruti.Core.Platform;
using Shruti.Core.TranscriptQuality;
using Shruti.Transcription.Abstractions;

namespace Shruti.TranscriptQuality;

public sealed class TranscriptQualityPipeline : ITranscriptQualityPipeline, IDisposable
{
    private readonly IVocabularyRepository repository;
    private readonly ITextContextReader? contextReader;
    private readonly ILocalTranscriptEditor _editor;
    public TranscriptQualityPipeline(IVocabularyRepository repository, ITextContextReader? contextReader = null)
        : this(repository, contextReader, new LocalTranscriptEditor()) { }
    internal TranscriptQualityPipeline(IVocabularyRepository repository, ITextContextReader? contextReader, ILocalTranscriptEditor editor)
    { this.repository = repository; this.contextReader = contextReader; _editor = editor; }
    public void Dispose() { if (_editor is IDisposable disposable) disposable.Dispose(); }

    public async Task<ITranscriptQualitySession> BeginAsync(FocusTarget? target, TranscriptQualitySettings settings,
        CancellationToken cancellationToken)
    {
        var vocabulary = await repository.ListVocabularyAsync(settings.Language, cancellationToken).ConfigureAwait(false);
        var corrections = await repository.ListCorrectionsAsync(settings.Language, cancellationToken).ConfigureAwait(false);
        TranscriptContext? context = null;
        if (settings.EnableCursorContext && target is not null && contextReader is not null)
            context = await contextReader.ReadAsync(target, cancellationToken).ConfigureAwait(false);
        if (settings.EnableLocalModel && settings.CleanupMode == TranscriptCleanupMode.Light)
            await _editor.PrepareAsync(settings, cancellationToken).ConfigureAwait(false);
        return new Session(vocabulary, corrections, context, settings, _editor, target?.ProcessName);
    }

    private sealed class Session : ITranscriptQualitySession
    {
        private TranscriptContext? _context;
        private readonly IReadOnlyList<CorrectionRule> _corrections;
        private readonly TranscriptQualitySettings _settings;
        private bool _disposed;
        private readonly ILocalTranscriptEditor _editor;
        private IReadOnlyList<string> _protectedTerms;
        public RecognitionHints RecognitionHints { get; private set; }

        public Session(IReadOnlyList<VocabularyEntry> vocabulary, IReadOnlyList<CorrectionRule> corrections,
            TranscriptContext? context, TranscriptQualitySettings settings, ILocalTranscriptEditor editor, string? processName)
        {
            _corrections = corrections; _context = context; _settings = settings; _editor = editor;
            RecognitionHints = RecognitionHintBuilder.Build(vocabulary, context, corrections, settings.MaximumPromptTokens,
                RecognitionHintBuilder.ApplicationTerms(processName));
            _protectedTerms = vocabulary.Select(entry => entry.Term).Concat(corrections.Select(rule => rule.ReplacementText))
                .Concat(RecognitionHintBuilder.ContextTerms(context)).Concat(RecognitionHints.Terms).Distinct(StringComparer.Ordinal).ToArray();
        }

        public async Task<RefinedTranscript> RefineAsync(TranscriptResult rawTranscript, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var edits = new List<TranscriptEdit>();
            string text = CorrectionMatcher.Apply(rawTranscript.Text, _corrections);
            Record(edits, "dictionary", rawTranscript.Text, text);
            string formatted = DeterministicRefiner.Refine(text, _protectedTerms, _context, _settings.CleanupMode, edits);
            Record(edits, "formatting", text, formatted);
            if (_settings.EnableLocalModel && _settings.CleanupMode == TranscriptCleanupMode.Light)
            {
                string[] protectedTerms = _protectedTerms.Concat(TranscriptEditValidator.InferProtectedTerms(formatted)).Distinct(StringComparer.Ordinal).ToArray();
                var proposed = await _editor.ProposeAsync(formatted, protectedTerms,
                    _context?.AppCategory ?? TranscriptAppCategory.Other, _settings, cancellationToken).ConfigureAwait(false);
                if (TranscriptEditValidator.TryApply(formatted, proposed, protectedTerms, out string cleaned, out var applied))
                {
                    formatted = DeterministicRefiner.SentenceCase(cleaned.Trim(), _protectedTerms);
                    edits.AddRange(applied);
                    Record(edits, "sentence-case", cleaned, formatted);
                }
                else System.Diagnostics.Trace.TraceWarning("Local transcript edits rejected; keeping deterministic output.");
            }
            string final = DeterministicRefiner.Join(formatted, _context, _protectedTerms);
            Record(edits, "cursor-join", formatted, final);
            _context = null;
            _protectedTerms = [];
            RecognitionHints = RecognitionHints.Empty;
            return new RefinedTranscript(rawTranscript, final, edits, false);
        }

        internal static void Record(List<TranscriptEdit> edits, string rule, string before, string after)
        {
            if (before != after) edits.Add(new(rule, 0, before.Length, after));
        }
        public ValueTask DisposeAsync() { _context = null; _protectedTerms = []; RecognitionHints = RecognitionHints.Empty; _disposed = true; return ValueTask.CompletedTask; }
    }
}
