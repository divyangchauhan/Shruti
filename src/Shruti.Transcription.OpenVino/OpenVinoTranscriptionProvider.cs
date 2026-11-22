using Shruti.Transcription.Abstractions;

namespace Shruti.Transcription.OpenVino;

public sealed class OpenVinoTranscriptionProvider : ITranscriptionProvider, IAsyncDisposable
{
    private static readonly string[] RequiredModelFiles =
    [
        "config.json",
        "generation_config.json",
        "openvino_decoder_model.bin",
        "openvino_decoder_model.xml",
        "openvino_encoder_model.bin",
        "openvino_encoder_model.xml",
        "openvino_tokenizer.bin",
        "openvino_tokenizer.xml"
    ];

    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private IReadOnlyList<EngineCapability>? _capabilities;
    private CachedPipeline? _cachedPipeline;
    private bool _disposed;

    public string Id => "openvino-genai";

    public string DisplayName => "OpenVINO GenAI";

    public Task<IReadOnlyList<EngineCapability>> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_capabilities is not null)
        {
            return Task.FromResult(_capabilities);
        }

        try
        {
            _capabilities = OpenVinoNativeApi.GetAvailableDevices()
                .Select(ToCapability)
                .Where(capability => capability is not null)
                .Cast<EngineCapability>()
                .DistinctBy(capability => capability.Backend)
                .OrderBy(capability => BackendRank(capability.Backend))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            _capabilities = [];
        }

        return Task.FromResult(_capabilities);
    }

    public async Task<bool> CanRunModelAsync(
        TranscriptionModelDescriptor model,
        ComputeBackend requestedBackend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!string.Equals(model.ProviderId, Id, StringComparison.Ordinal) || !IsModelAvailable(model.LocalPath))
        {
            return false;
        }

        IReadOnlyList<EngineCapability> capabilities = await ProbeAsync(cancellationToken).ConfigureAwait(false);
        return requestedBackend == ComputeBackend.Auto
            ? capabilities.Any(capability => model.SupportedBackends.Contains(capability.Backend))
            : model.SupportedBackends.Contains(requestedBackend) &&
                capabilities.Any(capability => capability.Backend == requestedBackend);
    }

    public async Task<ITranscriptionSession> CreateSessionAsync(
        TranscriptionSessionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ComputeBackend backend = await ResolveBackendAsync(options, cancellationToken).ConfigureAwait(false);
        if (!await CanRunModelAsync(options.Model, backend, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The selected OpenVINO model is unavailable or the requested device is unsupported.");
        }

        OpenVinoPipeline pipeline = await GetOrCreatePipelineAsync(
                options.Model.LocalPath,
                backend,
                cancellationToken)
            .ConfigureAwait(false);
        return new OpenVinoTranscriptionSession(pipeline, options);
    }

    public async ValueTask DisposeAsync()
    {
        await _cacheGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cachedPipeline?.Pipeline.Dispose();
            _cachedPipeline = null;
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private async Task<ComputeBackend> ResolveBackendAsync(
        TranscriptionSessionOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Backend != ComputeBackend.Auto)
        {
            return options.Backend;
        }

        IReadOnlyList<EngineCapability> capabilities = await ProbeAsync(cancellationToken).ConfigureAwait(false);
        return capabilities
            .Where(capability => options.Model.SupportedBackends.Contains(capability.Backend))
            .OrderBy(capability => BackendRank(capability.Backend))
            .Select(capability => capability.Backend)
            .FirstOrDefault(ComputeBackend.Auto);
    }

    private async Task<OpenVinoPipeline> GetOrCreatePipelineAsync(
        string modelPath,
        ComputeBackend backend,
        CancellationToken cancellationToken)
    {
        await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cachedPipeline is not null &&
                _cachedPipeline.Backend == backend &&
                string.Equals(_cachedPipeline.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedPipeline.Pipeline;
            }

            _cachedPipeline?.Pipeline.Dispose();
            var pipeline = await Task.Run(
                    () => new OpenVinoPipeline(modelPath, backend),
                    cancellationToken)
                .ConfigureAwait(false);
            _cachedPipeline = new CachedPipeline(modelPath, backend, pipeline);
            return pipeline;
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private static bool IsModelAvailable(string modelPath)
    {
        return Directory.Exists(modelPath) &&
            RequiredModelFiles.All(fileName => File.Exists(Path.Combine(modelPath, fileName)));
    }

    private static EngineCapability? ToCapability(string device)
    {
        ComputeBackend? backend = device.StartsWith("NPU", StringComparison.OrdinalIgnoreCase)
            ? ComputeBackend.Npu
            : device.StartsWith("GPU", StringComparison.OrdinalIgnoreCase)
                ? ComputeBackend.Gpu
                : device.StartsWith("CPU", StringComparison.OrdinalIgnoreCase)
                    ? ComputeBackend.Cpu
                    : null;
        return backend is null
            ? null
            : new EngineCapability(
                "openvino-genai",
                "OpenVINO GenAI",
                backend.Value,
                $"OpenVINO {device}",
                SupportsTimestamps: false,
                SupportsLanguageDetection: true,
                Warnings: []);
    }

    private static int BackendRank(ComputeBackend backend) => backend switch
    {
        ComputeBackend.Gpu => 0,
        ComputeBackend.Npu => 1,
        ComputeBackend.Cpu => 2,
        _ => 3
    };

    private sealed record CachedPipeline(string ModelPath, ComputeBackend Backend, OpenVinoPipeline Pipeline);

    private sealed class OpenVinoPipeline : IDisposable
    {
        private readonly SemaphoreSlim _inferenceGate = new(1, 1);
        private IntPtr _nativePipeline;

        public OpenVinoPipeline(string modelPath, ComputeBackend backend)
        {
            _nativePipeline = OpenVinoNativeApi.CreateWhisperPipeline(modelPath, ToDeviceName(backend));
        }

        public async Task<string> TranscribeAsync(float[] audio, CancellationToken cancellationToken)
        {
            await _inferenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_nativePipeline == IntPtr.Zero, this);
                string text = await Task.Run(
                        () => OpenVinoNativeApi.Transcribe(_nativePipeline, audio),
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return text.Trim();
            }
            finally
            {
                _inferenceGate.Release();
            }
        }

        public void Dispose()
        {
            IntPtr pipeline = Interlocked.Exchange(ref _nativePipeline, IntPtr.Zero);
            OpenVinoNativeApi.FreeWhisperPipeline(pipeline);
            _inferenceGate.Dispose();
        }

        private static string ToDeviceName(ComputeBackend backend) => backend switch
        {
            ComputeBackend.Npu => "NPU",
            ComputeBackend.Gpu => "GPU",
            ComputeBackend.Cpu => "CPU",
            _ => throw new ArgumentOutOfRangeException(nameof(backend), "OpenVINO requires a concrete device.")
        };
    }

    private sealed class OpenVinoTranscriptionSession : ITranscriptionSession
    {
        private readonly OpenVinoPipeline _pipeline;
        private readonly object _sync = new();
        private readonly int _maximumAudioBytes;
        private bool _completed;
        private bool _cancelled;

        public OpenVinoTranscriptionSession(OpenVinoPipeline pipeline, TranscriptionSessionOptions options)
        {
            _pipeline = pipeline;
            _maximumAudioBytes = options.GetMaximumPcm16Bytes(RequiredInputFormat);
        }

        public AudioFormat RequiredInputFormat => AudioFormat.Speech16KhzMono;

        public async Task<TranscriptResult> TranscribeAsync(ReadOnlyMemory<byte> pcmAudio, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float[] audio;
            lock (_sync)
            {
                ThrowIfUnavailable();
                if (pcmAudio.IsEmpty)
                {
                    throw new InvalidOperationException("OpenVINO cannot transcribe an empty audio buffer.");
                }

                audio = Pcm16Signal.DecodeSamples(pcmAudio.Span, _maximumAudioBytes);
                _completed = true;
            }

            string text = await _pipeline.TranscribeAsync(audio, cancellationToken).ConfigureAwait(false);
            TimeSpan duration = TimeSpan.FromSeconds((double)audio.Length / RequiredInputFormat.SampleRateHz);
            var segment = new TranscriptSegment(0, TimeSpan.Zero, duration, text);
            return new TranscriptResult(text, [segment]);
        }

        public Task CancelAsync()
        {
            lock (_sync)
            {
                _cancelled = true;
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => new(CancelAsync());

        private void ThrowIfUnavailable()
        {
            if (_cancelled)
            {
                throw new OperationCanceledException("The OpenVINO transcription session was cancelled.");
            }

            if (_completed)
            {
                throw new InvalidOperationException("The OpenVINO transcription session is already complete.");
            }
        }
    }
}
