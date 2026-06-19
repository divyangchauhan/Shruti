namespace Shruti.Core.Platform;

public enum TextInsertionCapabilityOutcome
{
    DirectInputAvailable,
    ClipboardFallbackOnly,
    PreviewRecommended,
    Unsupported
}
