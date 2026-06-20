using NAudio.CoreAudioApi;

namespace Shruti.Audio.Windows;

public sealed class WasapiAudioDeviceCatalog : IWindowsAudioDeviceCatalog
{
    public IReadOnlyList<WindowsAudioCaptureDevice> ListCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultDeviceId = TryGetDefaultDeviceId(enumerator);
        var devices = new List<WindowsAudioCaptureDevice>();

        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device)
            {
                devices.Add(new WindowsAudioCaptureDevice(
                    device.ID,
                    device.FriendlyName,
                    string.Equals(device.ID, defaultDeviceId, StringComparison.OrdinalIgnoreCase)));
            }
        }

        return devices;
    }

    private static string? TryGetDefaultDeviceId(MMDeviceEnumerator enumerator)
    {
        try
        {
            using MMDevice defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            return defaultDevice.ID;
        }
        catch
        {
            return null;
        }
    }
}
