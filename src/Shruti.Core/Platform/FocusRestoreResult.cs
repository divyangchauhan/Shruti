namespace Shruti.Core.Platform;

public sealed record FocusRestoreResult(
    bool Restored,
    string? Message = null,
    IntPtr TargetWindowHandle = default,
    IntPtr ForegroundWindowBefore = default,
    IntPtr ForegroundWindowAfter = default,
    bool RequestedForeground = false);
