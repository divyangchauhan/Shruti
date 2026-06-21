using Shruti.Core.Platform;

namespace Shruti.Workflow.Dictation;

public sealed class MockTextInsertionService : ITextInsertionService
{
    public int InspectCount { get; private set; }

    public int InsertCount { get; private set; }

    public string? LastInsertedText { get; private set; }

    public TextInsertionOptions? LastOptions { get; private set; }

    public TaskCompletionSource? InsertCompletion { get; set; }

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

    public async Task<TextInsertionResult> InsertAsync(
        FocusTarget target,
        string text,
        TextInsertionOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InsertCount++;
        LastInsertedText = text;
        LastOptions = options;

        if (InsertCompletion is not null)
        {
            await InsertCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new TextInsertionResult(
            Inserted: true,
            TextInsertionMethod.DirectInput,
            "Inserted into the mock target.");
    }
}
