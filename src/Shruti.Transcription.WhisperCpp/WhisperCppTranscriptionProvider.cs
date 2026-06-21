using System.Buffers.Binary;
using System.Threading.Channels;
using Shruti.Transcription.Abstractions;

namespace Shruti.Transcription.WhisperCpp;

public sealed class WhisperCppTranscriptionProvider : ITranscriptionProvider
{
    private readonly IWhisperCppTranscriptionEngine _engine;

    public WhisperCppTranscriptionProvider(IWhisperCppTranscriptionEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public string Id => "whisper.cpp";

    public string DisplayName => "whisper.cpp";

    public Task<IReadOnlyList<EngineCapability>> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<EngineCapability> capabilities =
        [
            new EngineCapability(
                Id,
                DisplayName,
                ComputeBackend.Cpu,
                "whisper.cpp CPU",
                SupportsStreaming: false,
                SupportsTimestamps: true,
                SupportsLanguageDetection: false,
                MeasuredRealtimeFactor: null,
                Warnings: [])
        ];

        return Task.FromResult(capabilities);
    }

    public Task<bool> CanRunModelAsync(
        TranscriptionModelDescriptor model,
        ComputeBackend requestedBackend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();

        bool supportedBackend = requestedBackend is ComputeBackend.Auto or ComputeBackend.Cpu;
        bool isCompatible = string.Equals(model.ProviderId, Id, StringComparison.Ordinal) &&
            supportedBackend &&
            model.SupportedBackends.Contains(ComputeBackend.Cpu) &&
            File.Exists(model.LocalPath);

        return Task.FromResult(isCompatible);
    }

    public async Task<ITranscriptionSession> CreateSessionAsync(
        TranscriptionSessionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!await CanRunModelAsync(options.Model, options.Backend, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The selected whisper.cpp model is unavailable or the requested backend is unsupported.");
        }

        return new WhisperCppTranscriptionSession(_engine, options);
    }

    private sealed class WhisperCppTranscriptionSession : ITranscriptionSession
    {
        private readonly IWhisperCppTranscriptionEngine _engine;
        private readonly TranscriptionSessionOptions _options;
        private readonly MemoryStream _pcmAudio = new();
        private readonly Channel<TranscriptEvent> _events = Channel.CreateUnbounded<TranscriptEvent>();
        private bool _completed;
        private bool _cancelled;

        public WhisperCppTranscriptionSession(
            IWhisperCppTranscriptionEngine engine,
            TranscriptionSessionOptions options)
        {
            _engine = engine;
            _options = options;
        }

        public AudioFormat RequiredInputFormat => AudioFormat.Speech16KhzMono;

        public IAsyncEnumerable<TranscriptEvent> Events => _events.Reader.ReadAllAsync();

        public ValueTask PushAudioAsync(ReadOnlyMemory<byte> pcmAudio, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfUnavailable();

            if (pcmAudio.Length % sizeof(short) != 0)
            {
                throw new ArgumentException("PCM16 audio must contain complete samples.", nameof(pcmAudio));
            }

            _pcmAudio.Write(pcmAudio.Span);
            return ValueTask.CompletedTask;
        }

        public async Task<TranscriptResult> CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfUnavailable();

            if (_pcmAudio.Length == 0)
            {
                throw new InvalidOperationException("whisper.cpp cannot transcribe an empty audio buffer.");
            }

            _completed = true;

            try
            {
                WhisperCppTranscriptionResult nativeResult = await _engine.TranscribeAsync(
                        new WhisperCppTranscriptionRequest(
                            _options.Model.LocalPath,
                            ConvertPcm16ToFloat(_pcmAudio.GetBuffer().AsSpan(0, checked((int)_pcmAudio.Length))),
                            _options.Language),
                        cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<TranscriptSegment> segments = nativeResult.Segments
                    .Select((segment, index) => new TranscriptSegment(index, segment.Start, segment.End, segment.Text))
                    .ToArray();
                foreach (TranscriptSegment segment in segments)
                {
                    _events.Writer.TryWrite(new TranscriptEvent(TranscriptEventKind.SegmentFinalized, Segment: segment));
                }

                var result = new TranscriptResult(nativeResult.Text, segments);
                _events.Writer.TryWrite(new TranscriptEvent(TranscriptEventKind.Completed, Text: result.Text));
                _events.Writer.TryComplete();
                return result;
            }
            catch (Exception exception)
            {
                _events.Writer.TryWrite(new TranscriptEvent(TranscriptEventKind.Failed, Error: exception));
                _events.Writer.TryComplete(exception);
                throw;
            }
        }

        public Task CancelAsync()
        {
            if (!_completed)
            {
                _cancelled = true;
                _events.Writer.TryComplete();
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _pcmAudio.Dispose();
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        private void ThrowIfUnavailable()
        {
            if (_cancelled)
            {
                throw new OperationCanceledException("The whisper.cpp transcription session was cancelled.");
            }

            if (_completed)
            {
                throw new InvalidOperationException("The whisper.cpp transcription session has already completed.");
            }
        }

        private static float[] ConvertPcm16ToFloat(ReadOnlySpan<byte> pcmAudio)
        {
            var samples = new float[pcmAudio.Length / sizeof(short)];
            for (int index = 0; index < samples.Length; index++)
            {
                short sample = BinaryPrimitives.ReadInt16LittleEndian(pcmAudio.Slice(index * sizeof(short), sizeof(short)));
                samples[index] = sample / 32768f;
            }

            return samples;
        }
    }
}
