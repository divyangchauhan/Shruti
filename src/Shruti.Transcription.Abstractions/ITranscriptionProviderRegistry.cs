namespace Shruti.Transcription.Abstractions;

public interface ITranscriptionProviderRegistry
{
    IReadOnlyList<ITranscriptionProvider> Providers { get; }

    ITranscriptionProvider? FindById(string providerId);
}
