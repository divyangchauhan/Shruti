namespace Shruti.Transcription.Abstractions;

public interface ISpeechActivityDetector
{
    Task<bool> ContainsSpeechAsync(ReadOnlyMemory<byte> pcm16Audio, CancellationToken cancellationToken);
}
