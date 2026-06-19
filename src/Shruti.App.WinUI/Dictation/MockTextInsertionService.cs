using Shruti.Core.Platform;

namespace Shruti.App.WinUI.Dictation;

public sealed class MockTextInsertionService : ITextInsertionService
{
    public int InspectCount { get; private set; }

    public int InsertCount { get; private set; }

    public string? LastInsertedText { get; private set; }

    public Task<TextInsertionCapability> InspectAsync(
        FocusTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InspectCount++;

        var capability = new TextInsertionCapability(
            TextInsertionCapabilityOutcome.DirectInputAvailable,
            TextInsertionMethod.DirectInput,
            "Mock target accepts direct insertion.");

        return Task.FromResult(capability);
    }

    public Task<TextInsertionResult> InsertAsync(
        FocusTarget target,
        string text,
        TextInsertionOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InsertCount++;
        LastInsertedText = text;

        return Task.FromResult(new TextInsertionResult(
            Inserted: true,
            TextInsertionMethod.DirectInput,
            "Inserted into the mock target."));
    }
}
