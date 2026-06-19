using Shruti.Core.Platform;

namespace Shruti.Platform.Windows;

public sealed class WindowsPlatformModule
{
    public string Name => "Windows desktop integration";

    public ITargetFocusService CreateTargetFocusService()
    {
        return new WindowsTargetFocusService();
    }
}
