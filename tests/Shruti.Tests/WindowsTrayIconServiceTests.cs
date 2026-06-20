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
        Assert.Equal(1, api.RemoveCount);
    }

    private sealed class FakeTrayIconApi : IWindowsTrayIconApi
    {
        public int AddCount { get; private set; }

        public int UpdateCount { get; private set; }

        public int RemoveCount { get; private set; }

        public string? LastTooltip { get; private set; }

        public WindowsTrayCommand? MenuCommand { get; init; }

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
            return true;
        }

        public void RemoveIcon(IntPtr windowHandle, uint iconId)
        {
            RemoveCount++;
        }

        public WindowsTrayCommand? ShowMenu(IntPtr windowHandle, bool isDictationRunning)
        {
            return MenuCommand;
        }
    }
}
