namespace Shruti.Core.Platform;

public sealed record TextInsertionResult(
    bool Inserted,
    TextInsertionMethod Method,
    string? Message = null,
    bool Submitted = false)
{
    public bool Succeeded => Inserted || Submitted;
}
