namespace Shruti.Platform.Windows;

public sealed record WindowsInputSendResult(
    WindowsInputSendOutcome Outcome,
    uint SentInputCount,
    uint RequestedInputCount,
    int? LastError = null)
{
    public static WindowsInputSendResult FromCounts(
        uint sentInputCount,
        uint requestedInputCount,
        int? lastError = null)
    {
        WindowsInputSendOutcome outcome = sentInputCount switch
        {
            0 => WindowsInputSendOutcome.None,
            _ when sentInputCount >= requestedInputCount => WindowsInputSendOutcome.Complete,
            _ => WindowsInputSendOutcome.Partial
        };

        return new WindowsInputSendResult(
            outcome,
            sentInputCount,
            requestedInputCount,
            lastError);
    }

    public static WindowsInputSendResult Combine(IEnumerable<WindowsInputSendResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        WindowsInputSendResult[] materializedResults = results.ToArray();
        if (materializedResults.Length == 0)
        {
            return FromCounts(0, 0);
        }

        uint sentInputCount = 0;
        uint requestedInputCount = 0;
        int? lastError = null;
        foreach (WindowsInputSendResult result in materializedResults)
        {
            checked
            {
                sentInputCount += result.SentInputCount;
                requestedInputCount += result.RequestedInputCount;
            }

            lastError ??= result.LastError;
        }

        return FromCounts(sentInputCount, requestedInputCount, lastError);
    }
}
