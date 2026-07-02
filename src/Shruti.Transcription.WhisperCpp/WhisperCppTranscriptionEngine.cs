namespace Shruti.Transcription.WhisperCpp;

public sealed class WhisperCppTranscriptionEngine : IWhisperCppTranscriptionEngine
{
    private readonly IWhisperCppNativeApi _nativeApi;

    public WhisperCppTranscriptionEngine(IWhisperCppNativeApi nativeApi)
    {
        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
    }

    public WhisperCppBackendCapabilities Capabilities => _nativeApi.GetCapabilities();

    public Task<IWhisperCppInferenceSession> CreateSessionAsync(
        WhisperCppTranscriptionSessionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Language);
        cancellationToken.ThrowIfCancellationRequested();

        if (options.ThreadCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Thread count cannot be negative.");
        }

        if (options.GpuDevice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "GPU device index cannot be negative.");
        }

        return Task.Run<IWhisperCppInferenceSession>(
            () =>
            {
                IWhisperCppNativeContext context = _nativeApi.LoadModel(
                    options.ModelPath,
                    options.Backend,
                    options.GpuDevice);
                return new WhisperCppInferenceSession(
                    context,
                    options.Language,
                    options.EffectiveThreadCount);
            },
            cancellationToken);
    }

    private sealed class WhisperCppInferenceSession : IWhisperCppInferenceSession
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _operationGate = new(1, 1);
        private readonly string _language;
        private readonly int _threadCount;
        private IWhisperCppNativeContext? _context;
        private Task? _disposeTask;
        private bool _disposeStarted;

        public WhisperCppInferenceSession(
            IWhisperCppNativeContext context,
            string language,
            int threadCount)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _language = language;
            _threadCount = threadCount;
        }

        public async Task<WhisperCppTranscriptionResult> TranscribeAsync(
            float[] samples,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(samples);
            cancellationToken.ThrowIfCancellationRequested();

            if (samples.Length == 0)
            {
                throw new ArgumentException("whisper.cpp requires at least one audio sample.", nameof(samples));
            }

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                IWhisperCppNativeContext context;
                lock (_sync)
                {
                    ThrowIfDisposed();
                    context = _context!;
                }

                int status = context.Transcribe(samples, _language, _threadCount, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (status != 0)
                {
                    throw new InvalidOperationException($"whisper.cpp transcription failed with native status {status}.");
                }

                int segmentCount = context.GetSegmentCount();
                if (segmentCount < 0)
                {
                    throw new InvalidOperationException("whisper.cpp returned an invalid segment count.");
                }

                var segments = new List<WhisperCppSegment>(segmentCount);
                for (int index = 0; index < segmentCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    segments.Add(context.GetSegment(index));
                }

                return new WhisperCppTranscriptionResult(segments);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            Task disposeTask;
            lock (_sync)
            {
                _disposeStarted = true;
                _disposeTask ??= DisposeContextAsync();
                disposeTask = _disposeTask;
            }

            return new ValueTask(disposeTask);
        }

        private async Task DisposeContextAsync()
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                IWhisperCppNativeContext? context;
                lock (_sync)
                {
                    context = _context;
                    _context = null;
                }

                context?.Dispose();
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposeStarted || _context is null)
            {
                throw new ObjectDisposedException(nameof(WhisperCppInferenceSession));
            }
        }
    }
}
