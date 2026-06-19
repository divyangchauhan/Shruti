namespace Shruti.Core.Platform;

public interface ITextInsertionService
{
    Task<TextInsertionCapability> InspectAsync(
        FocusTarget target,
        CancellationToken cancellationToken);

    Task<TextInsertionResult> InsertAsync(
        FocusTarget target,
        string text,
        TextInsertionOptions options,
        CancellationToken cancellationToken);
}
