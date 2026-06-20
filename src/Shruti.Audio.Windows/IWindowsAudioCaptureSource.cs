namespace Shruti.Audio.Windows;

public interface IWindowsAudioCaptureSource : IDisposable
{
    WindowsAudioStreamFormat StreamFormat { get; }

    event EventHandler<WindowsAudioDataAvailableEventArgs>? DataAvailable;

    event EventHandler<WindowsAudioCaptureStoppedEventArgs>? CaptureStopped;

    void StartCapture();

    void StopCapture();
}
