namespace Shruti.Core.Platform;

public sealed record TextInsertionResult(
    bool Inserted,
    TextInsertionMethod Method,
    string? Message = null);
