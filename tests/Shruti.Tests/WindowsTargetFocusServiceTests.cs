using System.Diagnostics;
using Shruti.Core.Platform;
using Shruti.Platform.Windows;
using Xunit;

namespace Shruti.Tests;

public sealed class WindowsTargetFocusServiceTests
{
    [Fact]
    public void WindowsAutomationClientFactory_ResolvesAutomationClientType()
    {
        var factory = new WindowsAutomationClientFactory();

        Type? automationClientType = factory.ResolveAutomationClientType();

        Assert.NotNull(automationClientType);
    }

    [Fact]
    public void WindowsProcessInspector_InspectsCurrentProcess()
    {
        using Process currentProcess = Process.GetCurrentProcess();
        var inspector = new WindowsProcessInspector();

        WindowsProcessSnapshot? snapshot = inspector.Inspect(currentProcess.Id);

        Assert.NotNull(snapshot);
        Assert.Equal(currentProcess.Id, snapshot.ProcessId);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ProcessName));
    }

    [Fact]
    public async Task CaptureCurrentTargetAsync_ReturnsForegroundTargetWithProcessAndAutomationData()
    {
        var handle = new IntPtr(100);
        var windowing = new FakeWindowing
        {
            ForegroundWindow = handle,
            CapturedWindow = new WindowsWindowSnapshot(
                handle,
                ProcessId: 1234,
                ThreadId: 5678,
                WindowTitle: "Untitled - Notepad")
        };
        var processInspector = new FakeProcessInspector(
            new WindowsProcessSnapshot(
                ProcessId: 1234,
                ProcessName: "notepad",
                IsElevated: true));
        var focusedElementInspector = new FakeFocusedElementInspector(
            new FocusedElementSnapshot(
                AutomationElementId: "Edit",
                IsEditable: true,
                HasSelectedText: false));
        var service = CreateService(windowing, processInspector, focusedElementInspector);

        FocusTarget? target = await service.CaptureCurrentTargetAsync(CancellationToken.None);

        Assert.NotNull(target);
        Assert.Equal(handle, target.WindowHandle);
        Assert.Equal(1234, target.ProcessId);
        Assert.Equal(5678, target.ThreadId);
        Assert.Equal("notepad", target.ProcessName);
        Assert.Equal("Untitled - Notepad", target.WindowTitle);
        Assert.Equal("Edit", target.AutomationElementId);
        Assert.True(target.IsEditable);
        Assert.False(target.HasSelectedText);
        Assert.True(target.IsElevated);
    }

    [Fact]
    public async Task CaptureCurrentTargetAsync_UsesLastExternalTargetWhenForegroundIsShrutiWindow()
    {
        var externalHandle = new IntPtr(700);
        var shrutiHandle = new IntPtr(701);
        const int shrutiProcessId = 999;
        var windowing = new FakeWindowing
        {
            ForegroundWindow = externalHandle,
            ExistingWindow = externalHandle,
            CapturedWindow = new WindowsWindowSnapshot(
                externalHandle,
                ProcessId: 1234,
                ThreadId: 5678,
                WindowTitle: "Untitled - Notepad")
        };
        var processInspector = new FakeProcessInspector(
            new WindowsProcessSnapshot(
                ProcessId: 1234,
                ProcessName: "notepad",
                IsElevated: false),
            new WindowsProcessSnapshot(
                ProcessId: shrutiProcessId,
                ProcessName: "Shruti.App.WinUI",
                IsElevated: false));
        var service = CreateService(
            windowing,
            processInspector,
            new FakeFocusedElementInspector(
                new FocusedElementSnapshot(
                    AutomationElementId: "Edit",
                    IsEditable: true,
                    HasSelectedText: false)),
            currentProcessId: shrutiProcessId);

        await service.RememberCurrentForegroundTargetAsync(CancellationToken.None);

        windowing.ForegroundWindow = shrutiHandle;
        windowing.CapturedWindow = new WindowsWindowSnapshot(
            shrutiHandle,
            ProcessId: shrutiProcessId,
            ThreadId: 9876,
            WindowTitle: "Shruti");

        FocusTarget? target = await service.CaptureCurrentTargetAsync(CancellationToken.None);

        Assert.NotNull(target);
        Assert.Equal(externalHandle, target.WindowHandle);
        Assert.Equal("notepad", target.ProcessName);
        Assert.Equal("Untitled - Notepad", target.WindowTitle);
        Assert.True(target.IsEditable);
        Assert.False(target.HasSelectedText);
    }

    [Fact]
    public async Task ForegroundWindowChange_RemembersExternalTargetBeforeShrutiBecomesForeground()
    {
        var externalHandle = new IntPtr(750);
        var shrutiHandle = new IntPtr(751);
        const int shrutiProcessId = 999;
        var windowing = new FakeWindowing
        {
            ExistingWindow = externalHandle,
            CapturedWindow = new WindowsWindowSnapshot(
                externalHandle,
                ProcessId: 1234,
                ThreadId: 5678,
                WindowTitle: "Untitled - Notepad")
        };
        var foregroundTracker = new FakeForegroundWindowTracker();
        var service = CreateService(
            windowing,
            new FakeProcessInspector(
                new WindowsProcessSnapshot(
                    ProcessId: 1234,
                    ProcessName: "notepad",
                    IsElevated: false),
                new WindowsProcessSnapshot(
                    ProcessId: shrutiProcessId,
                    ProcessName: "Shruti.App.WinUI",
                    IsElevated: false)),
            new FakeFocusedElementInspector(
                new FocusedElementSnapshot(
                    AutomationElementId: "Edit",
                    IsEditable: true,
                    HasSelectedText: false)),
            currentProcessId: shrutiProcessId,
            foregroundWindowTracker: foregroundTracker);

        foregroundTracker.Publish(externalHandle);

        windowing.ForegroundWindow = shrutiHandle;
        windowing.CapturedWindow = new WindowsWindowSnapshot(
            shrutiHandle,
            ProcessId: shrutiProcessId,
            ThreadId: 9876,
            WindowTitle: "Shruti");

        FocusTarget? target = await service.CaptureCurrentTargetAsync(CancellationToken.None);

        Assert.True(foregroundTracker.Started);
        Assert.NotNull(target);
        Assert.Equal(externalHandle, target.WindowHandle);
        Assert.Equal("notepad", target.ProcessName);
        Assert.True(target.IsEditable);
        Assert.False(target.HasSelectedText);
    }

    [Fact]
    public async Task CaptureCurrentTargetAsync_ReturnsNullWhenForegroundIsShrutiWindowWithoutCachedExternalTarget()
    {
        var shrutiHandle = new IntPtr(800);
        const int shrutiProcessId = 999;
        var service = CreateService(
            new FakeWindowing
            {
                ForegroundWindow = shrutiHandle,
                CapturedWindow = new WindowsWindowSnapshot(
                    shrutiHandle,
                    ProcessId: shrutiProcessId,
                    ThreadId: 9876,
                    WindowTitle: "Shruti")
            },
            new FakeProcessInspector(
                new WindowsProcessSnapshot(
                    ProcessId: shrutiProcessId,
                    ProcessName: "Shruti.App.WinUI",
                    IsElevated: false)),
            new FakeFocusedElementInspector(),
            currentProcessId: shrutiProcessId);

        FocusTarget? target = await service.CaptureCurrentTargetAsync(CancellationToken.None);

        Assert.Null(target);
    }

    [Fact]
    public async Task CaptureCurrentTargetAsync_ReturnsNullWhenNoForegroundWindowExists()
    {
        var service = CreateService(
            new FakeWindowing { ForegroundWindow = IntPtr.Zero },
            new FakeProcessInspector(),
            new FakeFocusedElementInspector());

        FocusTarget? target = await service.CaptureCurrentTargetAsync(CancellationToken.None);

        Assert.Null(target);
    }

    [Fact]
    public async Task CaptureCurrentTargetAsync_ReturnsNullWhenProcessCannotBeInspected()
    {
        var handle = new IntPtr(200);
        var service = CreateService(
            new FakeWindowing
            {
                ForegroundWindow = handle,
                CapturedWindow = new WindowsWindowSnapshot(handle, 404, 505, "Unknown")
            },
            new FakeProcessInspector(),
            new FakeFocusedElementInspector());

        FocusTarget? target = await service.CaptureCurrentTargetAsync(CancellationToken.None);

        Assert.Null(target);
    }

    [Fact]
    public async Task RestoreAsync_ReturnsSuccessWhenTargetIsAlreadyForeground()
    {
        var handle = new IntPtr(300);
        var windowing = new FakeWindowing
        {
            ForegroundWindow = handle,
            ExistingWindow = handle
        };
        var service = CreateService(windowing, new FakeProcessInspector(), new FakeFocusedElementInspector());

        FocusRestoreResult result = await service.RestoreAsync(CreateTarget(handle), CancellationToken.None);

        Assert.True(result.Restored);
        Assert.Equal(0, windowing.SetForegroundCount);
        Assert.Equal(0, windowing.RestoreWindowCount);
    }

    [Fact]
    public async Task RestoreAsync_RestoresMinimizedWindowBeforeRequestingForeground()
    {
        var handle = new IntPtr(400);
        var windowing = new FakeWindowing
        {
            ForegroundWindow = new IntPtr(401),
            ExistingWindow = handle,
            MinimizedWindow = handle,
            SetForegroundResult = true,
            SetForegroundMakesWindowForeground = true
        };
        var service = CreateService(windowing, new FakeProcessInspector(), new FakeFocusedElementInspector());

        FocusRestoreResult result = await service.RestoreAsync(CreateTarget(handle), CancellationToken.None);

        Assert.True(result.Restored);
        Assert.Equal(1, windowing.RestoreWindowCount);
        Assert.Equal(1, windowing.SetForegroundCount);
    }

    [Fact]
    public async Task RestoreAsync_ReturnsFailureWhenCapturedWindowNoLongerExists()
    {
        var handle = new IntPtr(500);
        var service = CreateService(
            new FakeWindowing { ForegroundWindow = new IntPtr(501) },
            new FakeProcessInspector(),
            new FakeFocusedElementInspector());

        FocusRestoreResult result = await service.RestoreAsync(CreateTarget(handle), CancellationToken.None);

        Assert.False(result.Restored);
        Assert.Equal("The captured target window no longer exists.", result.Message);
    }

    [Fact]
    public async Task RestoreAsync_ReturnsFailureWhenWindowsRejectsForegroundChange()
    {
        var handle = new IntPtr(600);
        var service = CreateService(
            new FakeWindowing
            {
                ForegroundWindow = new IntPtr(601),
                ExistingWindow = handle,
                SetForegroundResult = false
            },
            new FakeProcessInspector(),
            new FakeFocusedElementInspector());

        FocusRestoreResult result = await service.RestoreAsync(CreateTarget(handle), CancellationToken.None);

        Assert.False(result.Restored);
        Assert.Equal("Windows did not allow Shruti to restore focus to the target app.", result.Message);
    }

    private static WindowsTargetFocusService CreateService(
        IWindowsWindowing windowing,
        IWindowsProcessInspector processInspector,
        IWindowsFocusedElementInspector focusedElementInspector,
        int? currentProcessId = null,
        IWindowsForegroundWindowTracker? foregroundWindowTracker = null)
    {
        return new WindowsTargetFocusService(
            windowing,
            processInspector,
            focusedElementInspector,
            focusSettleDelay: TimeSpan.Zero,
            currentProcessId: currentProcessId,
            foregroundWindowTracker: foregroundWindowTracker ?? new FakeForegroundWindowTracker());
    }

    private static FocusTarget CreateTarget(IntPtr handle)
    {
        return new FocusTarget(
            handle,
            ProcessId: 123,
            ProcessName: "notepad",
            WindowTitle: "Untitled - Notepad",
            ThreadId: 456);
    }

    private sealed class FakeWindowing : IWindowsWindowing
    {
        public IntPtr ForegroundWindow { get; set; }

        public IntPtr ExistingWindow { get; set; }

        public IntPtr MinimizedWindow { get; set; }

        public WindowsWindowSnapshot? CapturedWindow { get; set; }

        public bool SetForegroundResult { get; set; } = true;

        public bool SetForegroundMakesWindowForeground { get; set; }

        public int RestoreWindowCount { get; private set; }

        public int SetForegroundCount { get; private set; }

        public IntPtr GetForegroundWindow()
        {
            return ForegroundWindow;
        }

        public WindowsWindowSnapshot? CaptureWindow(IntPtr windowHandle)
        {
            return CapturedWindow?.WindowHandle == windowHandle ? CapturedWindow : null;
        }

        public bool IsWindow(IntPtr windowHandle)
        {
            return ExistingWindow == windowHandle || CapturedWindow?.WindowHandle == windowHandle;
        }

        public bool IsMinimized(IntPtr windowHandle)
        {
            return MinimizedWindow == windowHandle;
        }

        public bool RestoreWindow(IntPtr windowHandle)
        {
            RestoreWindowCount++;
            return true;
        }

        public bool SetForegroundWindow(IntPtr windowHandle)
        {
            SetForegroundCount++;
            if (SetForegroundResult && SetForegroundMakesWindowForeground)
            {
                ForegroundWindow = windowHandle;
            }

            return SetForegroundResult;
        }
    }

    private sealed class FakeProcessInspector : IWindowsProcessInspector
    {
        private readonly IReadOnlyDictionary<int, WindowsProcessSnapshot> _processes;

        public FakeProcessInspector(params WindowsProcessSnapshot[] processes)
        {
            _processes = processes.ToDictionary(process => process.ProcessId);
        }

        public WindowsProcessSnapshot? Inspect(int processId)
        {
            return _processes.TryGetValue(processId, out WindowsProcessSnapshot? process)
                ? process
                : null;
        }
    }

    private sealed class FakeFocusedElementInspector : IWindowsFocusedElementInspector
    {
        private readonly FocusedElementSnapshot? _focusedElement;

        public FakeFocusedElementInspector(FocusedElementSnapshot? focusedElement = null)
        {
            _focusedElement = focusedElement;
        }

        public FocusedElementSnapshot? CaptureFocusedElement(IntPtr ownerWindowHandle)
        {
            return _focusedElement;
        }
    }

    private sealed class FakeForegroundWindowTracker : IWindowsForegroundWindowTracker
    {
        public event EventHandler<IntPtr>? ForegroundWindowChanged;

        public bool Started { get; private set; }

        public bool Disposed { get; private set; }

        public void Start()
        {
            Started = true;
        }

        public void Publish(IntPtr windowHandle)
        {
            ForegroundWindowChanged?.Invoke(this, windowHandle);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
