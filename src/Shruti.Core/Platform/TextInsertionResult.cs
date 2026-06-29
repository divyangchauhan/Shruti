namespace Shruti.Core.Platform;

public sealed record TextInsertionResult(
    bool Inserted,
    TextInsertionMethod Method,
    string? Message = null,
    bool Submitted = false,
    IReadOnlyDictionary<string, string?>? Diagnostics = null)
{
    public bool Succeeded => Inserted;

    public bool Completed => Inserted || Submitted;

    public IReadOnlyDictionary<string, string?> OperationalDiagnostics => Diagnostics ??
        EmptyDiagnostics.Value;

    private static readonly Lazy<IReadOnlyDictionary<string, string?>> EmptyDiagnostics =
        new(() => new Dictionary<string, string?>());
}
