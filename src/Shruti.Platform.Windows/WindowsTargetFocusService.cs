using Shruti.Core.Platform;
using System.Diagnostics;

namespace Shruti.Platform.Windows;

public sealed class WindowsTargetFocusService : ITargetFocusService, IDisposable
{
    private static readonly TimeSpan DefaultFocusSettleDelay = TimeSpan.FromMilliseconds(75);

    private readonly IWindowsWindowing _windowing;
    private readonly IWindowsProcessInspector _processInspector;
    private readonly IWindowsFocusedElementInspector _focusedElementInspector;
    private readonly IWindowsForegroundWindowTracker _foregroundWindowTracker;
    private readonly TimeSpan _focusSettleDelay;
    private readonly int _currentProcessId;
    private readonly object _targetSync = new();
    private FocusTarget? _lastExternalTarget;
    private bool _isDisposed;

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
        TimeSpan? focusSettleDelay = null,
        int? currentProcessId = null,
        IWindowsForegroundWindowTracker? foregroundWindowTracker = null)
    {
        _windowing = windowing ?? throw new ArgumentNullException(nameof(windowing));
        _processInspector = processInspector ?? throw new ArgumentNullException(nameof(processInspector));
        _focusedElementInspector = focusedElementInspector ?? throw new ArgumentNullException(nameof(focusedElementInspector));
        _foregroundWindowTracker = foregroundWindowTracker ?? new WindowsForegroundWindowTracker();
        _focusSettleDelay = focusSettleDelay ?? DefaultFocusSettleDelay;
        _currentProcessId = currentProcessId ?? Process.GetCurrentProcess().Id;
        _foregroundWindowTracker.ForegroundWindowChanged += ForegroundWindowTracker_ForegroundWindowChanged;
        _foregroundWindowTracker.Start();
    }

    public Task<FocusTarget?> CaptureCurrentTargetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FocusTarget? currentTarget = CaptureForegroundTarget();
        if (currentTarget is null)
        {
            return Task.FromResult(GetLastExternalTargetIfValid());
        }

        if (IsCurrentProcessTarget(currentTarget))
        {
            return Task.FromResult(GetLastExternalTargetIfValid());
        }

        RememberExternalTarget(currentTarget);
        return Task.FromResult<FocusTarget?>(currentTarget);
    }

    public Task RememberCurrentForegroundTargetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FocusTarget? currentTarget = CaptureForegroundTarget();
        if (currentTarget is not null && !IsCurrentProcessTarget(currentTarget))
        {
            RememberExternalTarget(currentTarget);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _foregroundWindowTracker.ForegroundWindowChanged -= ForegroundWindowTracker_ForegroundWindowChanged;
        _foregroundWindowTracker.Dispose();
    }

    private void ForegroundWindowTracker_ForegroundWindowChanged(object? sender, IntPtr windowHandle)
    {
        try
        {
            FocusTarget? currentTarget = CaptureTarget(windowHandle);
            if (currentTarget is not null && !IsCurrentProcessTarget(currentTarget))
            {
                RememberExternalTarget(currentTarget);
            }
        }
        catch
        {
            // Foreground tracking is a best-effort cache for later insertion.
        }
    }

    private FocusTarget? CaptureForegroundTarget()
    {
        IntPtr foregroundWindow = _windowing.GetForegroundWindow();
        return CaptureTarget(foregroundWindow);
    }

    private FocusTarget? CaptureTarget(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        WindowsWindowSnapshot? window = _windowing.CaptureWindow(windowHandle);
        if (window is null || window.ProcessId <= 0)
        {
            return null;
        }

        WindowsProcessSnapshot? process = _processInspector.Inspect(window.ProcessId);
        if (process is null)
        {
            return null;
        }

        FocusedElementSnapshot? focusedElement = _focusedElementInspector.CaptureFocusedElement(windowHandle);

        return new FocusTarget(
            window.WindowHandle,
            window.ProcessId,
            process.ProcessName,
            window.WindowTitle,
            focusedElement?.AutomationElementId,
            focusedElement?.IsEditable,
            focusedElement?.HasSelectedText,
            process.IsElevated,
            window.ThreadId);
    }

    private bool IsCurrentProcessTarget(FocusTarget target)
    {
        return target.ProcessId == _currentProcessId;
    }

    private void RememberExternalTarget(FocusTarget target)
    {
        lock (_targetSync)
        {
            _lastExternalTarget = target;
        }
    }

    private FocusTarget? GetLastExternalTargetIfValid()
    {
        lock (_targetSync)
        {
            if (_lastExternalTarget is null)
            {
                return null;
            }

            if (_lastExternalTarget.WindowHandle != IntPtr.Zero &&
                _windowing.IsWindow(_lastExternalTarget.WindowHandle))
            {
                return _lastExternalTarget;
            }

            _lastExternalTarget = null;
            return null;
        }
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
