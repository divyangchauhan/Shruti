namespace Shruti.Core.Platform;

public sealed record TextInsertionOptions(
    bool AllowReplacingSelection = false,
    bool AllowClipboardFallback = true);
