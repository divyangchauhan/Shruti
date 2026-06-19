using Shruti.Core.Platform;

namespace Shruti.App.WinUI.Dictation;

public sealed class MockTargetFocusService : ITargetFocusService
{
    private static readonly FocusTarget Target = new(
        new IntPtr(42),
        ProcessId: 4242,
        ProcessName: "Mock target app",
        WindowTitle: "Draft message",
        AutomationElementId: "mock-edit",
        IsEditable: true,
        HasSelectedText: false);

    public int CaptureCount { get; private set; }

    public int RestoreCount { get; private set; }

    public Task<FocusTarget?> CaptureCurrentTargetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CaptureCount++;
        return Task.FromResult<FocusTarget?>(Target);
    }

    public Task<FocusRestoreResult> RestoreAsync(
        FocusTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RestoreCount++;
        return Task.FromResult(new FocusRestoreResult(Restored: true));
    }
}
