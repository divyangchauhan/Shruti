namespace Shruti.Core.Platform;

public sealed record TextInsertionCapability(
    TextInsertionCapabilityOutcome Outcome,
    TextInsertionMethod PreferredMethod,
    string? Message = null);
