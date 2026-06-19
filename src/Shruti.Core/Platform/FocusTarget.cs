namespace Shruti.Core.Platform;

public sealed record FocusTarget(
    IntPtr WindowHandle,
    int ProcessId,
    string ProcessName,
    string? WindowTitle,
    string? AutomationElementId = null,
    bool? IsEditable = null,
    bool? HasSelectedText = null,
    bool IsElevated = false,
    int ThreadId = 0);
