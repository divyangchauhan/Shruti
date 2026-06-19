namespace Shruti.Core.Platform;

public sealed record FocusRestoreResult(
    bool Restored,
    string? Message = null);
