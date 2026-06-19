using Microsoft.UI.Xaml;

namespace Shruti.App.WinUI;

public partial class App : Application
{
    private readonly AppComposition composition = new();
    private Window? window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = composition.CreateMainWindow();
        window.Activate();
    }
}
