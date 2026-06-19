namespace Shruti.Platform.Windows;

public sealed record WindowsWindowSnapshot(
    IntPtr WindowHandle,
    int ProcessId,
    int ThreadId,
    string? WindowTitle);
