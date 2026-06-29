using Shruti.Core.Platform;

namespace Shruti.Platform.Windows;

public sealed class WindowsPlatformModule
{
    public string Name => "Windows desktop integration";

    public WindowsTargetFocusService CreateTargetFocusService()
    {
        return new WindowsTargetFocusService();
    }

    public ITextInsertionService CreateTextInsertionService()
    {
        return new WindowsTextInsertionService();
    }

    public WindowsGlobalTriggerService CreateGlobalTriggerService()
    {
        return new WindowsGlobalTriggerService();
    }

    public WindowsTrayIconService CreateTrayIconService()
    {
        return new WindowsTrayIconService();
    }

    public IWindowsWindowVisibility CreateWindowVisibility()
    {
        return new Win32WindowVisibility();
    }
}
