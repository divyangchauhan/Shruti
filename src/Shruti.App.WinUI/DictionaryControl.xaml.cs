using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shruti.Core.TranscriptQuality;
using Shruti.Storage;

namespace Shruti.App.WinUI;

public sealed partial class DictionaryControl : UserControl
{
    public IVocabularyRepository Repository { get; set; } = new SqliteVocabularyRepository(AppDataPaths.CreateDefault());
    public DictionaryControl()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ExecuteAsync(RefreshAsync);
        LanguageBox.LostFocus += async (_, _) => await ExecuteAsync(RefreshAsync);
    }

    private async Task RefreshAsync()
    {
        VocabularyList.ItemsSource = await Repository.ListVocabularyAsync(LanguageBox.Text, CancellationToken.None);
        CorrectionList.ItemsSource = await Repository.ListCorrectionsAsync(LanguageBox.Text, CancellationToken.None);
    }
    private async void SaveVocabulary_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await Repository.SaveVocabularyAsync(new(Guid.NewGuid(), LanguageBox.Text, TermBox.Text, DateTimeOffset.UtcNow), CancellationToken.None);
        TermBox.Text = string.Empty;
        await RefreshAsync();
    });
    private async void SaveCorrection_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        await Repository.SaveCorrectionAsync(new(Guid.NewGuid(), LanguageBox.Text, HeardBox.Text, ReplacementBox.Text, DateTimeOffset.UtcNow), CancellationToken.None);
        HeardBox.Text = ReplacementBox.Text = string.Empty;
        await RefreshAsync();
    });
    private async void DeleteVocabulary_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        if (VocabularyList.SelectedItem is VocabularyEntry entry)
            await Repository.DeleteVocabularyAsync(entry.Id, CancellationToken.None);
        await RefreshAsync();
    });
    private async void DeleteCorrection_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        if (CorrectionList.SelectedItem is CorrectionRule rule)
            await Repository.DeleteCorrectionAsync(rule.Id, CancellationToken.None);
        await RefreshAsync();
    });
    private async Task ExecuteAsync(Func<Task> action)
    {
        try { await action(); MessageText.Text = string.Empty; }
        catch (Exception) { MessageText.Text = "Dictionary operation failed. Check the language and entry, then try again."; }
    }
}
