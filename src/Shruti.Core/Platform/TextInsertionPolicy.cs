namespace Shruti.Core.Platform;

public sealed record TextInsertionPolicy(
    string Id,
    TextInsertionPolicyMode Mode,
    string Message)
{
    public static TextInsertionPolicy Default { get; } = new(
        "default.direct-input",
        TextInsertionPolicyMode.DirectInputPreferred,
        "Shruti will try direct text input first.");
}
