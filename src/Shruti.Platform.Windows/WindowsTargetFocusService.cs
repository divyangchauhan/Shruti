using Shruti.Core.Platform;
using System.Diagnostics;

namespace Shruti.Platform.Windows;

public sealed class WindowsTargetFocusService : ITargetFocusService, IDisposable
{
    private static readonly TimeSpan DefaultFocusSettleDelay = TimeSpan.FromMilliseconds(75);
    private const int ForegroundPollAttempts = 4;

    // Shell surfaces are never valid dictation targets; remembering them would
    // poison the "last external target" cache used when Shruti owns the
    // foreground (e.g. after a tray or Start-menu interaction).
    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Shell_InputSwitchTopLevelWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "TaskListThumbnailWnd",
        "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow",
        "ForegroundStaging",
        "Progman",
        "WorkerW"
    };

    private static readonly HashSet<string> ShellProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost",
        "SearchHost",
        "SearchApp",
        "ShellExperienceHost",
        "LockApp",
        "LogonUI"
    };

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

        FocusTarget? currentTarget = CaptureForegroundTarget(includeFocusedElement: true);
        if (currentTarget is null)
        {
            return Task.FromResult(RefreshAndGetLastExternalTarget());
        }

        RememberExternalTarget(currentTarget);
        return Task.FromResult<FocusTarget?>(currentTarget);
    }

    public Task RememberCurrentForegroundTargetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FocusTarget? currentTarget = CaptureForegroundTarget(includeFocusedElement: true);
        if (currentTarget is not null)
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
            // The tracker callback runs on the thread that owns the WinEvent
            // hook (the UI thread), so it must stay cheap: no UI Automation
            // inspection here. Focused-element metadata is refreshed when the
            // cached target is promoted for an insertion.
            FocusTarget? currentTarget = CaptureTarget(windowHandle, includeFocusedElement: false);
            if (currentTarget is not null)
            {
                RememberExternalTarget(currentTarget);
            }
        }
        catch
        {
            // Foreground tracking is a best-effort cache for later insertion.
        }
    }

    private FocusTarget? CaptureForegroundTarget(bool includeFocusedElement)
    {
        IntPtr foregroundWindow = _windowing.GetForegroundWindow();
        return CaptureTarget(foregroundWindow, includeFocusedElement);
    }

    private FocusTarget? CaptureTarget(IntPtr windowHandle, bool includeFocusedElement)
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

        // Skip Shruti's own windows before any UI Automation work: inspecting
        // this process's focused element from its own UI thread can stall on
        // the in-process automation provider.
        if (window.ProcessId == _currentProcessId)
        {
            return null;
        }

        if (IsShellWindow(windowHandle))
        {
            return null;
        }

        WindowsProcessSnapshot? process = _processInspector.Inspect(window.ProcessId);
        if (process is null)
        {
            return null;
        }

        if (ShellProcessNames.Contains(process.ProcessName))
        {
            return null;
        }

        FocusedElementSnapshot? focusedElement = includeFocusedElement
            ? _focusedElementInspector.CaptureFocusedElement(windowHandle)
            : null;

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

    private bool IsShellWindow(IntPtr windowHandle)
    {
        string? className = _windowing.GetWindowClassName(windowHandle);
        return !string.IsNullOrEmpty(className) && ShellWindowClasses.Contains(className);
    }

    private void RememberExternalTarget(FocusTarget target)
    {
        lock (_targetSync)
        {
            _lastExternalTarget = target;
        }
    }

    private FocusTarget? RefreshAndGetLastExternalTarget()
    {
        FocusTarget? cachedTarget;
        lock (_targetSync)
        {
            cachedTarget = _lastExternalTarget;
        }

        if (cachedTarget is null)
        {
            return null;
        }

        if (cachedTarget.WindowHandle == IntPtr.Zero ||
            !_windowing.IsWindow(cachedTarget.WindowHandle))
        {
            ForgetExternalTarget(cachedTarget);
            return null;
        }

        // Re-capture so title, elevation, and focused-element metadata reflect
        // the window as it is now instead of when it was last foreground.
        FocusTarget? refreshedTarget = CaptureTarget(cachedTarget.WindowHandle, includeFocusedElement: true);
        if (refreshedTarget is null)
        {
            // The window still exists but a fresh snapshot was unavailable;
            // fall back to the last known capture.
            return cachedTarget;
        }

        RememberExternalTarget(refreshedTarget);
        return refreshedTarget;
    }

    private void ForgetExternalTarget(FocusTarget staleTarget)
    {
        lock (_targetSync)
        {
            if (ReferenceEquals(_lastExternalTarget, staleTarget))
            {
                _lastExternalTarget = null;
            }
        }
    }

    public async Task<FocusRestoreResult> RestoreAsync(
        FocusTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IntPtr foregroundBefore = _windowing.GetForegroundWindow();

        if (target.WindowHandle == IntPtr.Zero)
        {
            return new FocusRestoreResult(
                Restored: false,
                Message: "No captured window handle is available.",
                TargetWindowHandle: target.WindowHandle,
                ForegroundWindowBefore: foregroundBefore,
                ForegroundWindowAfter: foregroundBefore);
        }

        if (!_windowing.IsWindow(target.WindowHandle))
        {
            return new FocusRestoreResult(
                Restored: false,
                Message: "The captured target window no longer exists.",
                TargetWindowHandle: target.WindowHandle,
                ForegroundWindowBefore: foregroundBefore,
                ForegroundWindowAfter: _windowing.GetForegroundWindow());
        }

        if (foregroundBefore == target.WindowHandle)
        {
            return new FocusRestoreResult(
                Restored: true,
                TargetWindowHandle: target.WindowHandle,
                ForegroundWindowBefore: foregroundBefore,
                ForegroundWindowAfter: foregroundBefore);
        }

        if (_windowing.IsMinimized(target.WindowHandle))
        {
            _windowing.RestoreWindow(target.WindowHandle);
        }

        // Windows' foreground lock frequently rejects a plain
        // SetForegroundWindow, so escalate through progressively stronger
        // strategies until the target actually owns the foreground.
        bool requestedForeground = _windowing.SetForegroundWindow(target.WindowHandle);
        bool restored = await WaitForForegroundAsync(target.WindowHandle, cancellationToken).ConfigureAwait(false);

        if (!restored)
        {
            requestedForeground |= _windowing.SetForegroundWindowWithThreadAttach(
                target.WindowHandle,
                target.ThreadId);
            restored = await WaitForForegroundAsync(target.WindowHandle, cancellationToken).ConfigureAwait(false);
        }

        if (!restored)
        {
            _windowing.SendForegroundPermissionInput();
            requestedForeground |= _windowing.SetForegroundWindow(target.WindowHandle);
            restored = await WaitForForegroundAsync(target.WindowHandle, cancellationToken).ConfigureAwait(false);
        }

        IntPtr foregroundAfter = _windowing.GetForegroundWindow();
        if (restored)
        {
            return new FocusRestoreResult(
                Restored: true,
                TargetWindowHandle: target.WindowHandle,
                ForegroundWindowBefore: foregroundBefore,
                ForegroundWindowAfter: foregroundAfter,
                RequestedForeground: true);
        }

        return new FocusRestoreResult(
            Restored: false,
            Message: requestedForeground
                ? "The captured target window was not foreground after restore."
                : "Windows did not allow Shruti to restore focus to the target app.",
            TargetWindowHandle: target.WindowHandle,
            ForegroundWindowBefore: foregroundBefore,
            ForegroundWindowAfter: foregroundAfter,
            RequestedForeground: true);
    }

    private async Task<bool> WaitForForegroundAsync(
        IntPtr targetWindowHandle,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < ForegroundPollAttempts; attempt++)
        {
            if (_windowing.GetForegroundWindow() == targetWindowHandle)
            {
                return true;
            }

            if (_focusSettleDelay <= TimeSpan.Zero)
            {
                return _windowing.GetForegroundWindow() == targetWindowHandle;
            }

            await Task.Delay(_focusSettleDelay, cancellationToken).ConfigureAwait(false);
        }

        return _windowing.GetForegroundWindow() == targetWindowHandle;
    }
}
