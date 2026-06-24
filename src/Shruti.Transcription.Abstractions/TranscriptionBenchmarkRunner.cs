using System.Diagnostics;

namespace Shruti.Transcription.Abstractions;

public sealed class TranscriptionBenchmarkRunner
{
    private readonly ITranscriptionBenchmarkCache _benchmarkCache;

    public TranscriptionBenchmarkRunner(ITranscriptionBenchmarkCache benchmarkCache)
    {
        _benchmarkCache = benchmarkCache ?? throw new ArgumentNullException(nameof(benchmarkCache));
    }

    public async Task<TranscriptionBenchmarkResult> RunAsync(
        ITranscriptionProvider provider,
        TranscriptionModelDescriptor model,
        ComputeBackend backend,
        string? providerVersion,
        string? modelHash,
        string deviceName,
        TimeSpan audioDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        if (!Enum.IsDefined(backend) || backend == ComputeBackend.Auto)
        {
            throw new ArgumentOutOfRangeException(nameof(backend), "Benchmark backend must be a concrete backend.");
        }

        if (audioDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(audioDuration), "Benchmark audio duration must be positive.");
        }

        if (!await provider.CanRunModelAsync(model, backend, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Provider '{provider.DisplayName}' cannot benchmark model '{model.DisplayName}' on {backend}.");
        }

        var capability = new EngineCapability(
            provider.Id,
            provider.DisplayName,
            backend,
            deviceName,
            SupportsStreaming: false,
            SupportsTimestamps: false,
            SupportsLanguageDetection: false,
            MeasuredRealtimeFactor: null,
            Warnings: []);
        TranscriptionBenchmarkKey key = TranscriptionBenchmarkKey.Create(
            provider,
            providerVersion,
            model,
            modelHash,
            capability);

        Stopwatch stopwatch = Stopwatch.StartNew();
        await using ITranscriptionSession session = await provider
            .CreateSessionAsync(
                new TranscriptionSessionOptions(
                    model,
                    backend,
                    model.LanguageHint,
                    TranscriptionMode.Fast,
                    Streaming: new StreamingTranscriptionOptions(EnablePartialTranscription: false),
                    MaximumAudioDuration: audioDuration),
                cancellationToken)
            .ConfigureAwait(false);

        byte[] benchmarkAudio = CreateSilentAudio(session.RequiredInputFormat, audioDuration);
        await session.PushAudioAsync(benchmarkAudio, cancellationToken).ConfigureAwait(false);
        await session.CompleteAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        var result = new TranscriptionBenchmarkResult(
            key,
            stopwatch.Elapsed.TotalSeconds / audioDuration.TotalSeconds,
            audioDuration,
            stopwatch.Elapsed,
            DateTimeOffset.UtcNow,
            Warnings: []);
        await _benchmarkCache.SaveAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static byte[] CreateSilentAudio(AudioFormat format, TimeSpan audioDuration)
    {
        if (format.SampleFormat != AudioSampleFormat.Int16)
        {
            throw new NotSupportedException("Benchmark audio generation currently supports PCM16 providers only.");
        }

        int sampleCount = checked((int)Math.Ceiling(audioDuration.TotalSeconds * format.SampleRateHz));
        int byteCount = checked(sampleCount * format.ChannelCount * sizeof(short));
        return new byte[byteCount];
    }
}
