using Shruti.Core.Platform;

namespace Shruti.Platform.Windows;

public sealed class WindowsTargetFocusService : ITargetFocusService
{
    private static readonly TimeSpan DefaultFocusSettleDelay = TimeSpan.FromMilliseconds(75);

    private readonly IWindowsWindowing _windowing;
    private readonly IWindowsProcessInspector _processInspector;
    private readonly IWindowsFocusedElementInspector _focusedElementInspector;
    private readonly TimeSpan _focusSettleDelay;

    public WindowsTargetFocusService()
        : this(
            new Win32Windowing(),
            new WindowsProcessInspector(),
            new WindowsFocusedElementInspector(),
            DefaultFocusSettleDelay)
    {
    }

    public WindowsTargetFocusService(
        IWindowsWindowing windowing,
        IWindowsProcessInspector processInspector,
        IWindowsFocusedElementInspector focusedElementInspector,
        TimeSpan? focusSettleDelay = null)
    {
        _windowing = windowing ?? throw new ArgumentNullException(nameof(windowing));
        _processInspector = processInspector ?? throw new ArgumentNullException(nameof(processInspector));
        _focusedElementInspector = focusedElementInspector ?? throw new ArgumentNullException(nameof(focusedElementInspector));
        _focusSettleDelay = focusSettleDelay ?? DefaultFocusSettleDelay;
    }

    public Task<FocusTarget?> CaptureCurrentTargetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IntPtr foregroundWindow = _windowing.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return Task.FromResult<FocusTarget?>(null);
        }

        WindowsWindowSnapshot? window = _windowing.CaptureWindow(foregroundWindow);
        if (window is null || window.ProcessId <= 0)
        {
            return Task.FromResult<FocusTarget?>(null);
        }

        WindowsProcessSnapshot? process = _processInspector.Inspect(window.ProcessId);
        if (process is null)
        {
            return Task.FromResult<FocusTarget?>(null);
        }

        FocusedElementSnapshot? focusedElement = _focusedElementInspector.CaptureFocusedElement(foregroundWindow);

        var target = new FocusTarget(
            window.WindowHandle,
            window.ProcessId,
            process.ProcessName,
            window.WindowTitle,
            focusedElement?.AutomationElementId,
            focusedElement?.IsEditable,
            focusedElement?.HasSelectedText,
            process.IsElevated,
            window.ThreadId);

        return Task.FromResult<FocusTarget?>(target);
    }

    public async Task<FocusRestoreResult> RestoreAsync(
        FocusTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (target.WindowHandle == IntPtr.Zero)
        {
            return new FocusRestoreResult(
                Restored: false,
                Message: "No captured window handle is available.");
        }

        if (!_windowing.IsWindow(target.WindowHandle))
        {
            return new FocusRestoreResult(
                Restored: false,
                Message: "The captured target window no longer exists.");
        }

        if (_windowing.GetForegroundWindow() == target.WindowHandle)
        {
            return new FocusRestoreResult(Restored: true);
        }

        if (_windowing.IsMinimized(target.WindowHandle))
        {
            _windowing.RestoreWindow(target.WindowHandle);
        }

        bool requestedForeground = _windowing.SetForegroundWindow(target.WindowHandle);
        if (!requestedForeground)
        {
            return new FocusRestoreResult(
                Restored: false,
                Message: "Windows did not allow Shruti to restore focus to the target app.");
        }

        if (_focusSettleDelay > TimeSpan.Zero)
        {
            await Task.Delay(_focusSettleDelay, cancellationToken).ConfigureAwait(false);
        }

        bool restored = _windowing.GetForegroundWindow() == target.WindowHandle;
        return restored
            ? new FocusRestoreResult(Restored: true)
            : new FocusRestoreResult(
                Restored: false,
                Message: "The captured target window was not foreground after restore.");
    }
}
