using Microsoft.UI.Xaml;
using Shruti.Platform.Windows;

namespace Shruti.App.WinUI;

public partial class App : Application
{
    private readonly AppComposition composition = new();
    private readonly WindowsSingleInstanceCoordinator singleInstanceCoordinator = new();
    private MainWindow? window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (window is not null)
        {
            ShowExistingWindow();
            return;
        }

        if (!await singleInstanceCoordinator.TryStartAsync())
        {
            Exit();
            return;
        }

        singleInstanceCoordinator.ActivationRequested += SingleInstanceCoordinator_ActivationRequested;
        window = composition.CreateMainWindow();
        window.Activate();
    }

    private void SingleInstanceCoordinator_ActivationRequested(object? sender, EventArgs e)
    {
        window?.DispatcherQueue.TryEnqueue(ShowExistingWindow);
    }

    private void ShowExistingWindow()
    {
        window?.ShowFromExternalActivation();
    }
}
