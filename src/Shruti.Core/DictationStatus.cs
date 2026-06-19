namespace Shruti.Core;

public sealed record DictationStatus(
    DictationSessionState State,
    string Message);
