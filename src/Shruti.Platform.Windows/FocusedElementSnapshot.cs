namespace Shruti.Platform.Windows;

public sealed record FocusedElementSnapshot(
    string? AutomationElementId,
    bool? IsEditable,
    bool? HasSelectedText);
