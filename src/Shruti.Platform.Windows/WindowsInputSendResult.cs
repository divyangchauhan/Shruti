namespace Shruti.Platform.Windows;

public sealed record WindowsInputSendResult(
    WindowsInputSendOutcome Outcome,
    uint SentInputCount,
    uint RequestedInputCount)
{
    public static WindowsInputSendResult FromCounts(
        uint sentInputCount,
        uint requestedInputCount)
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
            requestedInputCount);
    }
}
