namespace Shruti.Audio.Windows;

public interface IWindowsAudioDeviceCatalog
{
    IReadOnlyList<WindowsAudioCaptureDevice> ListCaptureDevices();
}
