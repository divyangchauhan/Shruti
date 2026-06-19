namespace Shruti.Core.Audio;

public sealed record AudioInputDevice(
    string Id,
    string DisplayName,
    bool IsDefault);
