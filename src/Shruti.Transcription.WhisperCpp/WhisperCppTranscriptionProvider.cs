using System.Buffers.Binary;
using System.Threading.Channels;
using Shruti.Transcription.Abstractions;

namespace Shruti.Transcription.WhisperCpp;

public sealed class WhisperCppTranscriptionProvider : ITranscriptionProvider, IAsyncDisposable
{
    private readonly IWhisperCppTranscriptionEngine _engine;
    private readonly SemaphoreSlim _sessionCacheGate = new(1, 1);
    private CachedInferenceSession? _cachedInferenceSession;
    private bool _disposed;

    public WhisperCppTranscriptionProvider(IWhisperCppTranscriptionEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public string Id => "whisper.cpp";

    public string DisplayName => "whisper.cpp";

    public Task<IReadOnlyList<EngineCapability>> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WhisperCppBackendCapabilities nativeCapabilities = _engine.Capabilities;
        var capabilities = new List<EngineCapability>();
        if (nativeCapabilities.SupportsGpu)
        {
            capabilities.Add(CreateCapability(
                ComputeBackend.Gpu,
                "whisper.cpp GPU",
                nativeCapabilities.SystemInfo));
        }

        if (nativeCapabilities.SupportsCpu)
        {
            capabilities.Add(CreateCapability(
                ComputeBackend.Cpu,
                "whisper.cpp CPU",
                nativeCapabilities.SystemInfo));
        }

        return Task.FromResult<IReadOnlyList<EngineCapability>>(capabilities);
    }

    public Task<bool> CanRunModelAsync(
        TranscriptionModelDescriptor model,
        ComputeBackend requestedBackend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();

        WhisperCppBackendCapabilities nativeCapabilities = _engine.Capabilities;
        bool supportedBackend = SupportsRequestedBackend(model, nativeCapabilities, requestedBackend);
        bool isCompatible = string.Equals(model.ProviderId, Id, StringComparison.Ordinal) &&
            supportedBackend &&
            File.Exists(model.LocalPath);

        return Task.FromResult(isCompatible);
    }

    public async Task<ITranscriptionSession> CreateSessionAsync(
        TranscriptionSessionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ComputeBackend backend = ResolveBackend(options.Model, options.Backend);

        if (!await CanRunModelAsync(options.Model, backend, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The selected whisper.cpp model is unavailable or the requested backend is unsupported.");
        }

        var nativeOptions = new WhisperCppTranscriptionSessionOptions(
            options.Model.LocalPath,
            options.Language,
            Backend: backend);
        CachedInferenceSessionLease inferenceSessionLease = await AcquireInferenceSessionAsync(
                nativeOptions,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return new WhisperCppTranscriptionSession(
                inferenceSessionLease.Session,
                options,
                inferenceSessionLease.ReleaseAsync);
        }
        catch
        {
            await inferenceSessionLease.ReleaseAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        CachedInferenceSession? sessionToDispose = null;
        await _sessionCacheGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_cachedInferenceSession is not null)
            {
                _cachedInferenceSession.DisposeWhenReleased = true;
                if (_cachedInferenceSession.LeaseCount == 0)
                {
                    sessionToDispose = _cachedInferenceSession;
                    _cachedInferenceSession = null;
                }
            }
        }
        finally
        {
            _sessionCacheGate.Release();
        }

        if (sessionToDispose is not null)
        {
            await sessionToDispose.Session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<CachedInferenceSessionLease> AcquireInferenceSessionAsync(
        WhisperCppTranscriptionSessionOptions options,
        CancellationToken cancellationToken)
    {
        await _sessionCacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_cachedInferenceSession is { } cachedSession &&
                cachedSession.Options == options &&
                !cachedSession.DisposeWhenReleased)
            {
                cachedSession.LeaseCount++;
                return new CachedInferenceSessionLease(this, cachedSession);
            }

            if (_cachedInferenceSession is { } staleSession)
            {
                staleSession.DisposeWhenReleased = true;
                _cachedInferenceSession = null;
                if (staleSession.LeaseCount == 0)
                {
                    await staleSession.Session.DisposeAsync().ConfigureAwait(false);
                }
            }

            IWhisperCppInferenceSession inferenceSession = await _engine
                .CreateSessionAsync(options, cancellationToken)
                .ConfigureAwait(false);
            _cachedInferenceSession = new CachedInferenceSession(options, inferenceSession)
            {
                LeaseCount = 1
            };
            return new CachedInferenceSessionLease(this, _cachedInferenceSession);
        }
        finally
        {
            _sessionCacheGate.Release();
        }
    }

    private async Task ReleaseInferenceSessionAsync(CachedInferenceSession releasedSession)
    {
        CachedInferenceSession? sessionToDispose = null;
        await _sessionCacheGate.WaitAsync().ConfigureAwait(false);
        try
        {
            releasedSession.LeaseCount--;
            if (releasedSession.LeaseCount < 0)
            {
                throw new InvalidOperationException("The whisper.cpp inference session lease was released too many times.");
            }

            if (releasedSession.LeaseCount == 0 &&
                (releasedSession.DisposeWhenReleased || _disposed))
            {
                sessionToDispose = releasedSession;
                if (ReferenceEquals(_cachedInferenceSession, releasedSession))
                {
                    _cachedInferenceSession = null;
                }
            }
        }
        finally
        {
            _sessionCacheGate.Release();
        }

        if (sessionToDispose is not null)
        {
            await sessionToDispose.Session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private ComputeBackend ResolveBackend(
        TranscriptionModelDescriptor model,
        ComputeBackend requestedBackend)
    {
        if (requestedBackend != ComputeBackend.Auto)
        {
            return requestedBackend;
        }

        WhisperCppBackendCapabilities nativeCapabilities = _engine.Capabilities;
        if (nativeCapabilities.SupportsGpu && model.SupportedBackends.Contains(ComputeBackend.Gpu))
        {
            return ComputeBackend.Gpu;
        }

        return nativeCapabilities.SupportsCpu && model.SupportedBackends.Contains(ComputeBackend.Cpu)
            ? ComputeBackend.Cpu
            : ComputeBackend.Auto;
    }

    private static EngineCapability CreateCapability(
        ComputeBackend backend,
        string deviceName,
        string systemInfo)
    {
        string[] warnings = string.IsNullOrWhiteSpace(systemInfo)
            ? []
            : [$"Native runtime: {systemInfo}"];
        return new EngineCapability(
            ProviderId: "whisper.cpp",
            ProviderDisplayName: "whisper.cpp",
            Backend: backend,
            DeviceName: deviceName,
            SupportsStreaming: true,
            SupportsTimestamps: true,
            SupportsLanguageDetection: false,
            MeasuredRealtimeFactor: null,
            Warnings: warnings);
    }

    private static bool SupportsRequestedBackend(
        TranscriptionModelDescriptor model,
        WhisperCppBackendCapabilities nativeCapabilities,
        ComputeBackend requestedBackend)
    {
        return requestedBackend switch
        {
            ComputeBackend.Auto => SupportsConcreteBackend(model, nativeCapabilities, ComputeBackend.Gpu) ||
                SupportsConcreteBackend(model, nativeCapabilities, ComputeBackend.Cpu),
            ComputeBackend.Cpu or ComputeBackend.Gpu => SupportsConcreteBackend(
                model,
                nativeCapabilities,
                requestedBackend),
            _ => false
        };
    }

    private static bool SupportsConcreteBackend(
        TranscriptionModelDescriptor model,
        WhisperCppBackendCapabilities nativeCapabilities,
        ComputeBackend backend)
    {
        return backend switch
        {
            ComputeBackend.Cpu => nativeCapabilities.SupportsCpu && model.SupportedBackends.Contains(ComputeBackend.Cpu),
            ComputeBackend.Gpu => nativeCapabilities.SupportsGpu && model.SupportedBackends.Contains(ComputeBackend.Gpu),
            _ => false
        };
    }

    private sealed class CachedInferenceSession(
        WhisperCppTranscriptionSessionOptions options,
        IWhisperCppInferenceSession session)
    {
        public WhisperCppTranscriptionSessionOptions Options { get; } = options;

        public IWhisperCppInferenceSession Session { get; } = session;

        public int LeaseCount { get; set; }

        public bool DisposeWhenReleased { get; set; }
    }

    private sealed class CachedInferenceSessionLease
    {
        private readonly WhisperCppTranscriptionProvider _owner;
        private readonly CachedInferenceSession _cachedSession;
        private bool _released;

        public CachedInferenceSessionLease(
            WhisperCppTranscriptionProvider owner,
            CachedInferenceSession cachedSession)
        {
            _owner = owner;
            _cachedSession = cachedSession;
        }

        public IWhisperCppInferenceSession Session => _cachedSession.Session;

        public Task ReleaseAsync()
        {
            if (_released)
            {
                return Task.CompletedTask;
            }

            _released = true;
            return _owner.ReleaseInferenceSessionAsync(_cachedSession);
        }
    }

    private sealed class WhisperCppTranscriptionSession : ITranscriptionSession
    {
        private readonly IWhisperCppInferenceSession _inferenceSession;
        private readonly TranscriptionSessionOptions _options;
        private readonly Func<Task> _releaseInferenceSessionAsync;
        private readonly StreamingTranscriptionOptions _streamingOptions;
        private readonly MemoryStream _pcmAudio = new();
        private readonly Channel<TranscriptEvent> _events = Channel.CreateUnbounded<TranscriptEvent>();
        private readonly CancellationTokenSource _partialTranscriptionCancellation = new();
        private readonly object _sync = new();
        private Task? _partialTranscriptionTask;
        private Task? _resourceDisposalTask;
        private int _lastPartialEndSampleCount;
        private int _nextPartialSampleCount;
        private bool _partialTranscriptionInProgress;
        private bool _completed;
        private bool _cancelled;
        private bool _maximumAudioDurationReached;

        public WhisperCppTranscriptionSession(
            IWhisperCppInferenceSession inferenceSession,
            TranscriptionSessionOptions options,
            Func<Task> releaseInferenceSessionAsync)
        {
            _inferenceSession = inferenceSession ?? throw new ArgumentNullException(nameof(inferenceSession));
            _options = options;
            _releaseInferenceSessionAsync = releaseInferenceSessionAsync ??
                throw new ArgumentNullException(nameof(releaseInferenceSessionAsync));
            _streamingOptions = options.EffectiveStreamingOptions;
            ValidateStreamingOptions(_streamingOptions);

            if (_options.EffectiveMaximumAudioDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Maximum audio duration must be positive.");
            }
        }

        public AudioFormat RequiredInputFormat => AudioFormat.Speech16KhzMono;

        public IAsyncEnumerable<TranscriptEvent> Events => _events.Reader.ReadAllAsync();

        public ValueTask<TranscriptionAudioPushResult> PushAudioAsync(
            ReadOnlyMemory<byte> pcmAudio,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pcmAudio.Length % sizeof(short) != 0)
            {
                throw new ArgumentException("PCM16 audio must contain complete samples.", nameof(pcmAudio));
            }

            TranscriptionAudioPushResult result;
            lock (_sync)
            {
                ThrowIfUnavailable();
                long maximumAudioBytes = GetMaximumAudioByteLength();
                int acceptedAudioByteCount = checked((int)Math.Min(
                    pcmAudio.Length,
                    Math.Max(0, maximumAudioBytes - _pcmAudio.Length)));
                if (acceptedAudioByteCount > 0)
                {
                    _pcmAudio.Write(pcmAudio.Span[..acceptedAudioByteCount]);
                }

                if (_pcmAudio.Length >= maximumAudioBytes && !_maximumAudioDurationReached)
                {
                    _maximumAudioDurationReached = true;
                    _events.Writer.TryWrite(new TranscriptEvent(
                        TranscriptEventKind.Warning,
                        Message: $"Recording limit of {_options.EffectiveMaximumAudioDuration.TotalMinutes:0.#} minutes reached; finalizing captured audio."));
                }

                result = _maximumAudioDurationReached
                    ? TranscriptionAudioPushResult.FinalizeAtMaximumDuration
                    : TranscriptionAudioPushResult.Continue;
            }

            SchedulePartialTranscriptionIfRequired();
            return ValueTask.FromResult(result);
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

                float[] audio = CopyBufferedAudioAsFloat();
                WhisperCppTranscriptionResult nativeResult = await _inferenceSession.TranscribeAsync(
                        audio,
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
            Task resourceDisposalTask;
            lock (_sync)
            {
                _cancelled = true;
                _partialTranscriptionCancellation.Cancel();
                _events.Writer.TryComplete();

                if (_resourceDisposalTask is null)
                {
                    Task? partialTranscriptionTask = _partialTranscriptionTask;
                    _pcmAudio.Dispose();
                    _resourceDisposalTask = DisposeResourcesAsync(partialTranscriptionTask);
                }

                resourceDisposalTask = _resourceDisposalTask;
            }

            return new ValueTask(resourceDisposalTask);
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

                int partialStartSampleCount = GetPartialStartSampleCount(sampleCount);
                byte[] audioSnapshot = CopyPartialAudioSnapshot(partialStartSampleCount, sampleCount);
                _partialTranscriptionInProgress = true;
                _lastPartialEndSampleCount = sampleCount;
                _nextPartialSampleCount = checked(sampleCount + GetPartialUpdateSampleCount());
                _partialTranscriptionTask = Task.Run(() => TranscribePartialAsync(audioSnapshot));
            }
        }

        private async Task TranscribePartialAsync(byte[] audioSnapshot)
        {
            try
            {
                WhisperCppTranscriptionResult partialResult = await _inferenceSession.TranscribeAsync(
                        ConvertPcm16ToFloat(audioSnapshot),
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

        private float[] CopyBufferedAudioAsFloat()
        {
            lock (_sync)
            {
                int audioLength = checked((int)_pcmAudio.Length);
                return ConvertPcm16ToFloat(_pcmAudio.GetBuffer().AsSpan(0, audioLength));
            }
        }

        private async Task DisposeResourcesAsync(Task? partialTranscriptionTask)
        {
            try
            {
                if (partialTranscriptionTask is not null)
                {
                    await partialTranscriptionTask.ConfigureAwait(false);
                }
            }
            finally
            {
                _partialTranscriptionCancellation.Dispose();
                await _releaseInferenceSessionAsync().ConfigureAwait(false);
            }
        }

        private byte[] CopyPartialAudioSnapshot(int startSampleCount, int endSampleCount)
        {
            int startByteOffset = checked(startSampleCount * sizeof(short));
            int endByteOffset = checked(endSampleCount * sizeof(short));
            return _pcmAudio.GetBuffer().AsSpan(startByteOffset, endByteOffset - startByteOffset).ToArray();
        }

        private int GetPartialStartSampleCount(int endSampleCount)
        {
            int overlapSampleCount = GetSampleCountForDuration(_streamingOptions.EffectivePartialAudioOverlap);
            int maximumPartialSampleCount = GetSampleCountForDuration(
                _streamingOptions.EffectiveMaximumPartialAudioDuration);
            int startSampleCount = Math.Max(0, _lastPartialEndSampleCount - overlapSampleCount);
            return Math.Max(startSampleCount, endSampleCount - maximumPartialSampleCount);
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

        private long GetMaximumAudioByteLength()
        {
            return checked((long)GetSampleCountForDuration(_options.EffectiveMaximumAudioDuration) * sizeof(short));
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

            if (options.EffectiveMaximumPartialAudioDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Maximum partial audio duration must be positive.");
            }

            if (options.EffectivePartialAudioOverlap < TimeSpan.Zero ||
                options.EffectivePartialAudioOverlap >= options.EffectiveMaximumPartialAudioDuration)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Partial audio overlap must be non-negative and shorter than the maximum partial audio duration.");
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
                samples[index] = sample / AudioFormat.Pcm16SampleScale;
            }

            return samples;
        }
    }
}
