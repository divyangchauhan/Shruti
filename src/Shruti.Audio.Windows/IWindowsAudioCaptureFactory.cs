namespace Shruti.Audio.Windows;

public interface IWindowsAudioCaptureFactory
{
    IWindowsAudioCaptureSource Create(WindowsAudioCaptureDevice device);
}
