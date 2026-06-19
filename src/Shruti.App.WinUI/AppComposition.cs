using Shruti.App.WinUI.Dictation;

namespace Shruti.App.WinUI;

public sealed class AppComposition
{
    private readonly MockDictationAppServices dictationServices = MockDictationAppServices.Create();

    public MainWindow CreateMainWindow()
    {
        return new MainWindow(dictationServices.CreateShellController());
    }
}
