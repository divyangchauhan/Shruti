using Shruti.Platform.Windows;
using Xunit;

namespace Shruti.Tests;

public sealed class WindowsTrayIconServiceTests
{
    [Fact]
    public void TrayIcon_HandlesClickAndMenuCommands()
    {
        var api = new FakeTrayIconApi { MenuCommand = WindowsTrayCommand.Cancel };
        using var service = new WindowsTrayIconService(api);
        var commands = new List<WindowsTrayCommand>();
        service.CommandInvoked += commands.Add;

        service.AttachWindow((IntPtr)42);
        service.SetVisible(isVisible: true);

        bool leftClickHandled = service.HandleWindowMessage(new WindowsWindowMessage(
            WindowsTrayIconService.CallbackWindowMessage,
            (IntPtr)1,
            (IntPtr)0x0202));
        bool rightClickHandled = service.HandleWindowMessage(new WindowsWindowMessage(
            WindowsTrayIconService.CallbackWindowMessage,
            (IntPtr)1,
            (IntPtr)0x0205));

        Assert.True(leftClickHandled);
        Assert.True(rightClickHandled);
        Assert.Equal(1, api.AddCount);
        Assert.Equal(
            new[] { WindowsTrayCommand.Toggle, WindowsTrayCommand.Cancel },
            commands);
    }

    [Fact]
    public void TrayIcon_ForwardsApiCommands()
    {
        var api = new FakeTrayIconApi();
        using var service = new WindowsTrayIconService(api);
        var commands = new List<WindowsTrayCommand>();
        service.CommandInvoked += commands.Add;

        service.AttachWindow((IntPtr)42);
        service.SetVisible(isVisible: true);
        api.RaiseCommand(WindowsTrayCommand.ShowWindow);
        api.RaiseCommand(WindowsTrayCommand.Quit);

        Assert.Equal(
            new[] { WindowsTrayCommand.ShowWindow, WindowsTrayCommand.Quit },
            commands);
    }

    [Fact]
    public void TrayIcon_UpdatesTooltipAndRemovesIconWhenDisabled()
    {
        var api = new FakeTrayIconApi();
        using var service = new WindowsTrayIconService(api);

        service.AttachWindow((IntPtr)42);
        service.SetVisible(isVisible: true);
        service.UpdateDictationState(isDictationRunning: true);
        service.SetVisible(isVisible: false);

        Assert.Equal("Shruti - Dictation running", api.LastTooltip);
        Assert.Equal(1, api.UpdateCount);
        Assert.Equal(2, api.RemoveCount);
    }

    [Fact]
    public void TrayIcon_ReaddsIconWhenExplorerRecreatesTaskbar()
    {
        var api = new FakeTrayIconApi();
        using var service = new WindowsTrayIconService(api);

        service.AttachWindow((IntPtr)42);
        service.SetVisible(isVisible: true);

        bool handled = service.HandleWindowMessage(new WindowsWindowMessage(
            WindowsTrayIconService.TaskbarCreatedWindowMessage,
            IntPtr.Zero,
            IntPtr.Zero));

        Assert.True(handled);
        Assert.Equal(2, api.AddCount);
        Assert.Equal(2, api.RemoveCount);
    }

    [Fact]
    public void TrayIcon_ReaddsIconWhenUpdateFails()
    {
        var api = new FakeTrayIconApi { UpdateSucceeds = false };
        using var service = new WindowsTrayIconService(api);

        service.AttachWindow((IntPtr)42);
        service.SetVisible(isVisible: true);
        service.UpdateDictationState(isDictationRunning: true);

        Assert.Equal(1, api.UpdateCount);
        Assert.Equal(2, api.AddCount);
        Assert.Equal(2, api.RemoveCount);
    }

    [Fact]
    public void TrayIcon_LeftClickShowsWindowWhenDictationCommandsAreDisabled()
    {
        var api = new FakeTrayIconApi();
        using var service = new WindowsTrayIconService(api);
        var commands = new List<WindowsTrayCommand>();
        service.CommandInvoked += commands.Add;

        service.AttachWindow((IntPtr)42);
        service.SetVisible(isVisible: true);
        service.SetDictationCommandsEnabled(isEnabled: false);

        bool handled = service.HandleWindowMessage(new WindowsWindowMessage(
            WindowsTrayIconService.CallbackWindowMessage,
            (IntPtr)1,
            (IntPtr)0x0202));

        Assert.True(handled);
        Assert.Equal(new[] { WindowsTrayCommand.ShowWindow }, commands);
    }

    [Fact]
    public void TrayIcon_RightClickMenuReceivesDictationCommandState()
    {
        var api = new FakeTrayIconApi { MenuCommand = WindowsTrayCommand.ShowWindow };
        using var service = new WindowsTrayIconService(api);

        service.AttachWindow((IntPtr)42);
        service.SetVisible(isVisible: true);
        service.SetDictationCommandsEnabled(isEnabled: false);
        service.UpdateDictationState(isDictationRunning: true);

        bool handled = service.HandleWindowMessage(new WindowsWindowMessage(
            WindowsTrayIconService.CallbackWindowMessage,
            (IntPtr)1,
            (IntPtr)0x0205));

        Assert.True(handled);
        Assert.False(api.LastAreDictationCommandsEnabled);
        Assert.True(api.LastIsDictationRunning);
    }

    private sealed class FakeTrayIconApi : IWindowsTrayIconApi
    {
        public event Action<WindowsTrayCommand>? CommandInvoked;

        public int AddCount { get; private set; }

        public int UpdateCount { get; private set; }

        public int RemoveCount { get; private set; }

        public string? LastTooltip { get; private set; }

        public bool? LastAreDictationCommandsEnabled { get; private set; }

        public bool? LastIsDictationRunning { get; private set; }

        public WindowsTrayCommand? MenuCommand { get; init; }

        public bool UpdateSucceeds { get; init; } = true;

        public void RaiseCommand(WindowsTrayCommand command)
        {
            CommandInvoked?.Invoke(command);
        }

        public bool AddIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string tooltip)
        {
            AddCount++;
            LastTooltip = tooltip;
            return true;
        }

        public bool UpdateIcon(IntPtr windowHandle, uint iconId, uint callbackMessage, string tooltip)
        {
            UpdateCount++;
            LastTooltip = tooltip;
            return UpdateSucceeds;
        }

        public void RemoveIcon(IntPtr windowHandle, uint iconId)
        {
            RemoveCount++;
        }

        public void SetCommandState(bool isDictationRunning, bool areDictationCommandsEnabled)
        {
            LastIsDictationRunning = isDictationRunning;
            LastAreDictationCommandsEnabled = areDictationCommandsEnabled;
        }

        public WindowsTrayCommand? ShowMenu(
            IntPtr windowHandle,
            bool isDictationRunning,
            bool areDictationCommandsEnabled)
        {
            LastIsDictationRunning = isDictationRunning;
            LastAreDictationCommandsEnabled = areDictationCommandsEnabled;
            return MenuCommand;
        }
    }
}
