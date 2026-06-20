using Shruti.App.WinUI.Dictation;
using Shruti.Platform.Windows;

namespace Shruti.App.WinUI;

public sealed class AppComposition
{
    private readonly MockDictationAppServices dictationServices = MockDictationAppServices.Create();
    private readonly WindowsPlatformModule platformModule = new();

    public MainWindow CreateMainWindow()
    {
        var controller = dictationServices.CreateShellController();
        var triggerRouter = new DictationTriggerRouter(controller);
        return new MainWindow(
            controller,
            triggerRouter,
            platformModule.CreateGlobalTriggerService(),
            platformModule.CreateTrayIconService(),
            platformModule.CreateWindowVisibility());
    }
}
