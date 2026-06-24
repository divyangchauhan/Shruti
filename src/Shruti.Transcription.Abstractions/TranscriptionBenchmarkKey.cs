namespace Shruti.Transcription.Abstractions;

public sealed record TranscriptionBenchmarkKey(
    string ProviderId,
    string ProviderVersion,
    string ModelId,
    string ModelHash,
    ComputeBackend Backend,
    string DeviceName)
{
    public const string UnknownProviderVersion = "unknown";
    public const string UnknownModelHash = "unknown";
    public const string UnknownDeviceName = "unknown device";

    public static TranscriptionBenchmarkKey Create(
        ITranscriptionProvider provider,
        string? providerVersion,
        TranscriptionModelDescriptor model,
        string? modelHash,
        EngineCapability capability)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(capability);

        return new TranscriptionBenchmarkKey(
            provider.Id,
            Normalize(providerVersion, UnknownProviderVersion),
            model.Id,
            Normalize(modelHash, UnknownModelHash),
            capability.Backend,
            Normalize(capability.DeviceName, UnknownDeviceName));
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
