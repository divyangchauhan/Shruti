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
            SupportsStreaming: false,
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
        private readonly MemoryStream _pcmAudio = new();
        private readonly Channel<TranscriptEvent> _events = Channel.CreateUnbounded<TranscriptEvent>();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly object _sync = new();
        private Task<TranscriptResult>? _completionTask;
        private Task? _disposalTask;
        private bool _completed;
        private bool _cancelled;
        private bool _maximumAudioDurationReached;

        public WhisperCppTranscriptionSession(IWhisperCppInferenceSession inferenceSession,
            TranscriptionSessionOptions options, Func<Task> releaseInferenceSessionAsync)
        {
            _inferenceSession = inferenceSession;
            _options = options;
            _releaseInferenceSessionAsync = releaseInferenceSessionAsync;
            if (options.EffectiveMaximumAudioDuration <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "Maximum audio duration must be positive.");
        }

        public AudioFormat RequiredInputFormat => AudioFormat.Speech16KhzMono;
        public IAsyncEnumerable<TranscriptEvent> Events => _events.Reader.ReadAllAsync();

        public ValueTask<TranscriptionAudioPushResult> PushAudioAsync(ReadOnlyMemory<byte> pcmAudio,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pcmAudio.Length % sizeof(short) != 0)
                throw new ArgumentException("PCM16 audio must contain complete samples.", nameof(pcmAudio));
            lock (_sync)
            {
                ThrowIfUnavailable();
                long maximumBytes = checked((long)Math.Ceiling(_options.EffectiveMaximumAudioDuration.TotalSeconds *
                    RequiredInputFormat.SampleRateHz) * sizeof(short));
                int accepted = checked((int)Math.Min(pcmAudio.Length, Math.Max(0, maximumBytes - _pcmAudio.Length)));
                _pcmAudio.Write(pcmAudio.Span[..accepted]);
                if (_pcmAudio.Length >= maximumBytes && !_maximumAudioDurationReached)
                {
                    _maximumAudioDurationReached = true;
                    _events.Writer.TryWrite(new TranscriptEvent(TranscriptEventKind.Warning,
                        Message: $"Recording limit of {_options.EffectiveMaximumAudioDuration.TotalMinutes:0.#} minutes reached; finalizing captured audio."));
                }
                return ValueTask.FromResult(_maximumAudioDurationReached
                    ? TranscriptionAudioPushResult.FinalizeAtMaximumDuration : TranscriptionAudioPushResult.Continue);
            }
        }

        public Task<TranscriptResult> CompleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                ThrowIfUnavailable();
                _completed = true;
                var audio = new float[_pcmAudio.Length / sizeof(short)];
                ReadOnlySpan<byte> bytes = _pcmAudio.GetBuffer().AsSpan(0, checked((int)_pcmAudio.Length));
                for (int i = 0; i < audio.Length; i++)
                    audio[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(i * sizeof(short), sizeof(short))) / AudioFormat.Pcm16SampleScale;
                _completionTask = CompleteCoreAsync(audio, cancellationToken);
                return _completionTask;
            }
        }

        private async Task<TranscriptResult> CompleteCoreAsync(float[] audio, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation.Token);
            try
            {
                WhisperCppTranscriptionResult native = audio.Length == 0
                    ? new WhisperCppTranscriptionResult([])
                    : await _inferenceSession.TranscribeAsync(audio, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                TranscriptSegment[] segments = native.Segments
                    .Where(segment => !TranscriptText.IsEmptyOrNonSpeech(segment.Text))
                    .Select((segment, index) => new TranscriptSegment(index, segment.Start, segment.End, segment.Text.Trim()))
                    .ToArray();
                var result = new TranscriptResult(string.Join(" ", segments.Select(segment => segment.Text)), segments);
                foreach (TranscriptSegment segment in segments)
                    _events.Writer.TryWrite(new TranscriptEvent(TranscriptEventKind.SegmentFinalized, Segment: segment));
                _events.Writer.TryWrite(new TranscriptEvent(TranscriptEventKind.Completed, Text: result.Text));
                _events.Writer.TryComplete();
                return result;
            }
            catch (OperationCanceledException)
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
                _cancelled = true;
                _cancellation.Cancel();
                _events.Writer.TryComplete();
            }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                if (_disposalTask is not null) return new ValueTask(_disposalTask);
                _cancelled = true;
                _cancellation.Cancel();
                _events.Writer.TryComplete();
                _pcmAudio.Dispose();
                _disposalTask = DisposeResourcesAsync();
                return new ValueTask(_disposalTask);
            }
        }

        private async Task DisposeResourcesAsync()
        {
            if (_completionTask is not null)
            {
                try { await _completionTask.ConfigureAwait(false); }
                catch { /* Completion already reports its result to the caller. */ }
            }
            _cancellation.Dispose();
            await _releaseInferenceSessionAsync().ConfigureAwait(false);
        }

        private void ThrowIfUnavailable()
        {
            if (_cancelled) throw new OperationCanceledException("The whisper.cpp transcription session was cancelled.");
            if (_completed) throw new InvalidOperationException("The whisper.cpp transcription session has already completed.");
        }
    }
}
