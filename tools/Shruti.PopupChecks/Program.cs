using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Shruti.PopupChecks;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            try { _ = new CheckApp(); }
            catch (Exception ex)
            {
                string output = Environment.GetEnvironmentVariable("SHRUTI_POPUP_CHECK_OUTPUT")!;
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "startup-error.txt"), ex.ToString());
                Environment.Exit(1);
            }
        });
    }
}

public sealed partial class CheckApp : Application
{
    private readonly string _output = Environment.GetEnvironmentVariable("SHRUTI_POPUP_CHECK_OUTPUT") ?? Path.Combine(AppContext.BaseDirectory, "results");
    private readonly List<object> _results = [];
    private Window? _window;

    public CheckApp()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Directory.CreateDirectory(_output);
        try
        {
            _window = new Window { Title = "Shruti popup rendering check" };
            _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 700));
            var root = new StackPanel { Padding = new Thickness(48), Spacing = 16 };
            _window.Content = root;
            _window.Activate();
            await Task.Delay(300);
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                root.RequestedTheme = theme;
                root.Background = new SolidColorBrush(theme == ElementTheme.Dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
                foreach (var (name, combo) in CreateCombos())
                {
                    root.Children.Clear();
                    root.Children.Add(new TextBlock { Text = name });
                    ContentDialog? dialog = null;
                    if (name == "DictionaryEditorType")
                    {
                        dialog = new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = theme, Title = "Add dictionary entry", Content = combo, CloseButtonText = "Cancel" };
                        _ = dialog.ShowAsync();
                    }
                    else root.Children.Add(combo);
                    root.Children.Add(new TextBox { Text = "Text behind the popup must not show through." });
                    await Task.Delay(100);
                    combo.IsDropDownOpen = true;
                    await Task.Delay(180);
                    await CaptureAsync(root, $"{theme}-{name}", combo);
                    combo.IsDropDownOpen = false;
                    dialog?.Hide();
                    await Task.Delay(100);
                }
                foreach (var name in new[] { "HistoryMenu", "FloatingBarMenu" })
                {
                    root.Children.Clear();
                    var button = new Button { Content = name };
                    root.Children.Add(button);
                    await Task.Delay(100);
                    var menu = Shruti.App.WinUI.PopupAppearance.CreateMenuFlyout();
                    foreach (string label in name == "HistoryMenu" ? new[] { "View transcript", "Delete" } : new[] { "Settings", "Hide floating bar" })
                        menu.Items.Add(new MenuFlyoutItem { Text = label });
                    menu.ShowAt(button);
                    await Task.Delay(180);
                    await CaptureAsync(root, $"{theme}-{name}", null);
                    menu.Hide();
                    await Task.Delay(250);
                }
            }
            await File.WriteAllTextAsync(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(_output, "error.txt"), ex.ToString());
            Environment.ExitCode = 1;
        }
        finally { _window?.Close(); Exit(); }
    }

    private static IEnumerable<(string Name, ComboBox Combo)> CreateCombos()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Input/MainWindow.xaml"));
        foreach (var original in source.Descendants(xaml + "ComboBox"))
        {
            string name = original.Attribute(x + "Name")!.Value;
            var copy = new XElement(original);
            copy.Attribute(x + "Name")?.Remove();
            copy.Attribute("SelectionChanged")?.Remove();
            copy.Attribute("IsEnabled")?.Remove();
            var combo = (ComboBox)XamlReader.Load(copy.ToString());
            if (combo.Items.Count == 0)
            {
                string[] labels = name switch
                {
                    "AudioDeviceComboBox" => ["Default microphone", "Microphone Array (Intel Smart Sound Technology)", "USB Microphone"],
                    "ModelSelectionComboBox" => ["Whisper tiny English", "Whisper large-v3 Turbo Q5", "Whisper large-v3 Turbo"],
                    _ => ["Primary monitor", "Display 1: Lenovo DisplayHDR", "Display 2: Generic PnP Monitor"]
                };
                foreach (string label in labels) combo.Items.Add(label);
            }
            combo.SelectedIndex = 0;
            yield return (name, combo);
        }
        yield return ("DictionaryFilter", new ComboBox { Header = "Entry type", ItemsSource = new[] { "All entries", "Vocabulary", "Corrections" }, SelectedIndex = 0, MinWidth = 150 });
        yield return ("DictionaryEditorType", new ComboBox { Header = "Entry type", ItemsSource = new[] { "Vocabulary hint", "Exact correction" }, SelectedIndex = 0 });
    }

    private async Task CaptureAsync(FrameworkElement root, string name, ComboBox? combo)
    {
        var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot);
        var child = popups.Where(p => p.IsOpen).Select(p => p.Child).OfType<FrameworkElement>()
            .FirstOrDefault(p => combo is null ? Descendants(p).OfType<MenuFlyoutPresenter>().Any() : Descendants(p).OfType<Border>().Any(b => b.Name == "PopupBorder"))
            ?? throw new InvalidOperationException($"{name}: popup did not open");
        var elements = Descendants(child).ToArray();
        var border = elements.OfType<Border>().FirstOrDefault(b => b.Name == "PopupBorder");
        Brush? brush = border?.Background ?? elements.OfType<MenuFlyoutPresenter>().FirstOrDefault()?.Background;
        var bitmap = new RenderTargetBitmap();
        FrameworkElement rendered = border ?? (FrameworkElement?)elements.OfType<MenuFlyoutPresenter>().FirstOrDefault() ?? child;
        for (int attempt = 0; rendered.ActualWidth <= 0 && attempt < 20; attempt++)
        {
            rendered.UpdateLayout();
            await Task.Delay(50);
        }
        if (rendered.ActualWidth <= 0 || rendered.ActualHeight <= 0)
            throw new InvalidOperationException($"{name}: popup has no visible bounds.");
        var labels = Descendants(rendered).OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)).ToArray();
        if (labels.Any(t => t.IsTextTrimmed)) throw new InvalidOperationException($"{name}: popup text is clipped.");
        if (combo is not null)
        {
            foreach (var item in combo.Items)
            {
                string? label = item is ComboBoxItem option ? option.Content?.ToString() : item.ToString();
                if (!labels.Any(t => t.Text == label && t.ActualWidth > 0 && t.ActualHeight > 0))
                    throw new InvalidOperationException($"{name}: an option is not visible.");
            }
        }
        await bitmap.RenderAsync(rendered);
        IBuffer pixels = await bitmap.GetPixelsAsync();
        var file = await StorageFile.GetFileFromPathAsync(await CreateFileAsync(name + ".png"));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        using var reader = DataReader.FromBuffer(pixels);
        var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
        bool opaque = brush is SolidColorBrush solid && solid.Color.A == 255;
        _results.Add(new { name, opaque, theme = child.ActualTheme.ToString(), width = rendered.ActualWidth, height = rendered.ActualHeight,
            brush = brush?.GetType().Name, color = (brush as SolidColorBrush)?.Color.ToString(),
            items = combo?.Items.Count, text = elements.OfType<TextBlock>().Select(t => new { t.Text, t.ActualWidth, t.ActualHeight, color = (t.Foreground as SolidColorBrush)?.Color.ToString() }).ToArray() });
        await File.WriteAllTextAsync(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
        if (!opaque) throw new InvalidOperationException($"{name}: popup background is not an opaque solid brush.");
    }

    private async Task<string> CreateFileAsync(string name)
    {
        string path = Path.Combine(_output, name);
        await File.WriteAllBytesAsync(path, []);
        return path;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
