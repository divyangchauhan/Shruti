namespace Shruti.Core.Audio;

public interface IAudioCaptureSession : IAsyncDisposable
{
    IAsyncEnumerable<AudioFrame> Frames { get; }

    IAsyncEnumerable<AudioLevelFrame> Levels { get; }

    Task PauseAsync(CancellationToken cancellationToken);

    Task ResumeAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
