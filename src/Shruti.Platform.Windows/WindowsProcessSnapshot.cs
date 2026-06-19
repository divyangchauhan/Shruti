namespace Shruti.Platform.Windows;

public sealed record WindowsProcessSnapshot(
    int ProcessId,
    string ProcessName,
    bool IsElevated);
