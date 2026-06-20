namespace Shruti.Audio.Windows;

public sealed class WindowsAudioCaptureModule
{
    public string Name => "Windows WASAPI audio capture";

    public WindowsAudioCaptureService CreateCaptureService()
    {
        return new WindowsAudioCaptureService();
    }
}
