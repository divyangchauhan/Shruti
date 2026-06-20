namespace Shruti.Core.Audio;

public interface IAudioCaptureControl
{
    Task StopActiveCaptureAsync(CancellationToken cancellationToken = default);

    Task PauseActiveCaptureAsync(CancellationToken cancellationToken = default);

    Task ResumeActiveCaptureAsync(CancellationToken cancellationToken = default);
}
