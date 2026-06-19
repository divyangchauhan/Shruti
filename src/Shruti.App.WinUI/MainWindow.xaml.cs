using Microsoft.UI.Xaml;

namespace Shruti.App.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void PrimaryDictationButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Mock dictation is ready for PR-02 wiring.";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "No active dictation session.";
    }
}
