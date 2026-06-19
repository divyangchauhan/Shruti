namespace Shruti.Core.Audio;

public sealed record AudioCaptureOptions(
    string? DeviceId = null,
    bool EnableLevelMeter = true);
