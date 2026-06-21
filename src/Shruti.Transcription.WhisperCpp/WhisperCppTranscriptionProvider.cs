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
                SupportsStreaming: true,
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
        private readonly StreamingTranscriptionOptions _streamingOptions;
        private readonly MemoryStream _pcmAudio = new();
        private readonly Channel<TranscriptEvent> _events = Channel.CreateUnbounded<TranscriptEvent>();
        private readonly CancellationTokenSource _partialTranscriptionCancellation = new();
        private readonly object _sync = new();
        private Task? _partialTranscriptionTask;
        private int _nextPartialSampleCount;
        private bool _partialTranscriptionInProgress;
        private bool _completed;
        private bool _cancelled;

        public WhisperCppTranscriptionSession(
            IWhisperCppTranscriptionEngine engine,
            TranscriptionSessionOptions options)
        {
            _engine = engine;
            _options = options;
            _streamingOptions = options.EffectiveStreamingOptions;
            ValidateStreamingOptions(_streamingOptions);
        }

        public AudioFormat RequiredInputFormat => AudioFormat.Speech16KhzMono;

        public IAsyncEnumerable<TranscriptEvent> Events => _events.Reader.ReadAllAsync();

        public ValueTask PushAudioAsync(ReadOnlyMemory<byte> pcmAudio, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pcmAudio.Length % sizeof(short) != 0)
            {
                throw new ArgumentException("PCM16 audio must contain complete samples.", nameof(pcmAudio));
            }

            lock (_sync)
            {
                ThrowIfUnavailable();
                _pcmAudio.Write(pcmAudio.Span);
            }

            SchedulePartialTranscriptionIfRequired();
            return ValueTask.CompletedTask;
        }

        public async Task<TranscriptResult> CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task? partialTranscriptionTask;
            lock (_sync)
            {
                ThrowIfUnavailable();

                if (_pcmAudio.Length == 0)
                {
                    throw new InvalidOperationException("whisper.cpp cannot transcribe an empty audio buffer.");
                }

                _completed = true;
                partialTranscriptionTask = _partialTranscriptionTask;
            }

            try
            {
                if (partialTranscriptionTask is not null)
                {
                    await partialTranscriptionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                byte[] audio = CopyBufferedAudio();
                WhisperCppTranscriptionResult nativeResult = await _engine.TranscribeAsync(
                        new WhisperCppTranscriptionRequest(
                            _options.Model.LocalPath,
                            ConvertPcm16ToFloat(audio),
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
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested ||
                _partialTranscriptionCancellation.IsCancellationRequested)
            {
                _events.Writer.TryComplete();
                throw;
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
            lock (_sync)
            {
                if (!_cancelled)
                {
                    _cancelled = true;
                    _partialTranscriptionCancellation.Cancel();
                    _events.Writer.TryComplete();
                }
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Task? partialTranscriptionTask;
            lock (_sync)
            {
                _cancelled = true;
                _partialTranscriptionCancellation.Cancel();
                partialTranscriptionTask = _partialTranscriptionTask;
                _events.Writer.TryComplete();
            }

            _pcmAudio.Dispose();
            if (partialTranscriptionTask is null || partialTranscriptionTask.IsCompleted)
            {
                _partialTranscriptionCancellation.Dispose();
            }
            else
            {
                _ = partialTranscriptionTask.ContinueWith(
                    _ => _partialTranscriptionCancellation.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            return ValueTask.CompletedTask;
        }

        private void SchedulePartialTranscriptionIfRequired()
        {
            if (!_streamingOptions.EnablePartialTranscription)
            {
                return;
            }

            lock (_sync)
            {
                if (_cancelled || _completed || _partialTranscriptionInProgress)
                {
                    return;
                }

                int sampleCount = checked((int)(_pcmAudio.Length / sizeof(short)));
                if (sampleCount < GetMinimumPartialSampleCount() || sampleCount < _nextPartialSampleCount)
                {
                    return;
                }

                byte[] audioSnapshot = CopyPartialAudioSnapshot();
                _partialTranscriptionInProgress = true;
                _nextPartialSampleCount = checked(sampleCount + GetPartialUpdateSampleCount());
                _partialTranscriptionTask = Task.Run(() => TranscribePartialAsync(audioSnapshot));
            }
        }

        private async Task TranscribePartialAsync(byte[] audioSnapshot)
        {
            try
            {
                WhisperCppTranscriptionResult partialResult = await _engine.TranscribeAsync(
                        new WhisperCppTranscriptionRequest(
                            _options.Model.LocalPath,
                            ConvertPcm16ToFloat(audioSnapshot),
                            _options.Language),
                        _partialTranscriptionCancellation.Token)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(partialResult.Text) && CanPublishPartialText())
                {
                    _events.Writer.TryWrite(new TranscriptEvent(TranscriptEventKind.PartialText, Text: partialResult.Text));
                }
            }
            catch (OperationCanceledException) when (_partialTranscriptionCancellation.IsCancellationRequested)
            {
                // A cancellation must not emit a partial transcript or a warning.
            }
            catch (Exception exception)
            {
                if (CanPublishPartialText())
                {
                    _events.Writer.TryWrite(new TranscriptEvent(
                        TranscriptEventKind.Warning,
                        Message: "A live transcription update failed; final transcription will continue.",
                        Error: exception));
                }
            }
            finally
            {
                lock (_sync)
                {
                    _partialTranscriptionInProgress = false;
                }

                SchedulePartialTranscriptionIfRequired();
            }
        }

        private byte[] CopyBufferedAudio()
        {
            lock (_sync)
            {
                return _pcmAudio.ToArray();
            }
        }

        private byte[] CopyPartialAudioSnapshot()
        {
            int audioLength = checked((int)_pcmAudio.Length);
            int maximumPartialAudioLength = checked(GetSampleCountForDuration(
                _streamingOptions.EffectivePartialAudioWindow) * sizeof(short));
            int offset = Math.Max(0, audioLength - maximumPartialAudioLength);
            return _pcmAudio.GetBuffer().AsSpan(offset, audioLength - offset).ToArray();
        }

        private bool CanPublishPartialText()
        {
            lock (_sync)
            {
                return !_cancelled && !_completed;
            }
        }

        private int GetMinimumPartialSampleCount()
        {
            return GetSampleCountForDuration(_streamingOptions.EffectiveMinimumAudioDuration);
        }

        private int GetPartialUpdateSampleCount()
        {
            return GetSampleCountForDuration(_streamingOptions.EffectiveUpdateInterval);
        }

        private static int GetSampleCountForDuration(TimeSpan duration)
        {
            return checked((int)Math.Ceiling(duration.TotalSeconds * AudioFormat.Speech16KhzMono.SampleRateHz));
        }

        private static void ValidateStreamingOptions(StreamingTranscriptionOptions options)
        {
            if (options.EffectiveMinimumAudioDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Minimum audio duration must be positive.");
            }

            if (options.EffectiveUpdateInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Partial update interval must be positive.");
            }

            if (options.EffectivePartialAudioWindow <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Partial audio window must be positive.");
            }
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
