using NAudio.CoreAudioApi;

namespace Shruti.Audio.Windows;

public sealed class WasapiAudioCaptureFactory : IWindowsAudioCaptureFactory
{
    public IWindowsAudioCaptureSource Create(WindowsAudioCaptureDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        using var enumerator = new MMDeviceEnumerator();
        MMDevice audioDevice = enumerator.GetDevice(device.Id);
        return new WasapiAudioCaptureSource(audioDevice);
    }
}
