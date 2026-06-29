namespace Shruti.Core.Platform;

public sealed record TextInsertionOptions(
    bool AllowReplacingSelection = false,
    bool AllowClipboardFallback = true,
    bool BypassTargetPolicy = false,
    TextInsertionMethod PreferredMethodOverride = TextInsertionMethod.None);
