using Microsoft.UI.Windowing;

namespace Shruti.App.WinUI;

internal static class AppIcon
{
    private static readonly string IconPath = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Shruti.ico");

    public static void Apply(AppWindow appWindow)
    {
        ArgumentNullException.ThrowIfNull(appWindow);
        appWindow.SetIcon(IconPath);
        appWindow.SetTaskbarIcon(IconPath);
    }
}
