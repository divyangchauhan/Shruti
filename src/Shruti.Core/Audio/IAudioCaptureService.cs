using Shruti.Transcription.Abstractions;

namespace Shruti.Core.Audio;

public interface IAudioCaptureService
{
    Task<IReadOnlyList<AudioInputDevice>> ListInputDevicesAsync(
        CancellationToken cancellationToken);

    Task<IAudioCaptureSession> StartAsync(
        AudioCaptureOptions options,
        AudioFormat outputFormat,
        CancellationToken cancellationToken);
}
