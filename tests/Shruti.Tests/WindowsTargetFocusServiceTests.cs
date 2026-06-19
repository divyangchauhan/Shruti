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
        IWindowsFocusedElementInspector focusedElementInspector)
    {
        return new WindowsTargetFocusService(
            windowing,
            processInspector,
            focusedElementInspector,
            focusSettleDelay: TimeSpan.Zero);
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
        private readonly WindowsProcessSnapshot? _process;

        public FakeProcessInspector(WindowsProcessSnapshot? process = null)
        {
            _process = process;
        }

        public WindowsProcessSnapshot? Inspect(int processId)
        {
            return _process?.ProcessId == processId ? _process : null;
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
}
