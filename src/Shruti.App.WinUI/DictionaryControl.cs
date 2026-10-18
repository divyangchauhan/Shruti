using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Shruti.Core.TranscriptQuality;
using Shruti.Storage;
using Shruti.TranscriptQuality;

namespace Shruti.App.WinUI;

public sealed class DictionaryControl : LibraryPage
{
    public IVocabularyRepository Repository { get; set; } = new SqliteVocabularyRepository(AppDataPaths.CreateDefault());
    private readonly StackPanel _entries = new() { Spacing = 1 };
    private readonly TextBox _search = new() { PlaceholderText = "Search dictionary", MaxLength = 256 };
    private readonly TextBox _language = new() { Text = "en", Header = "Language", Width = 90, MaxLength = 32 };
    private readonly ComboBox _filter = new() { Header = "Entry type", ItemsSource = new[] { "All entries", "Vocabulary", "Corrections" }, SelectedIndex = 0, MinWidth = 150 };
    private bool _descending;
    private int _generation;
    public DictionaryControl() : base("Dictionary", "Teach Shruti names and technical terms, or save an exact correction for words it mishears.")
    {
        Actions.Children.Add(Action("Add new", () => ShowEditorAsync(null, null)));
        var filters = FilterBar(_filter, _language,
            Action("Reverse order", async () => { _descending = !_descending; await RefreshAsync(); }));
        Body.Children.Add(filters); Body.Children.Add(_search); Body.Children.Add(_entries);
        _search.TextChanged += async (_, _) => await RefreshAsync();
        _filter.SelectionChanged += async (_, _) => await RefreshAsync();
        _language.LostFocus += async (_, _) => await RefreshAsync();
    }
    public override Task RefreshAsync() => RunAsync(async () =>
    {
        int generation = ++_generation;
        var vocabulary = await Repository.ListVocabularyAsync(_language.Text, default);
        var corrections = await Repository.ListCorrectionsAsync(_language.Text, default);
        if (generation != _generation) return;
        var rows = new List<(string Label, Func<Task> Edit, Func<Task> Delete)>();
        if (_filter.SelectedIndex != 2)
            rows.AddRange(vocabulary.Select(v => (v.Term, (Func<Task>)(() => ShowEditorAsync(v, null)), (Func<Task>)(() => Repository.DeleteVocabularyAsync(v.Id, default)))));
        if (_filter.SelectedIndex != 1)
            rows.AddRange(corrections.Select(c => ($"{c.HeardText} → {c.ReplacementText}", (Func<Task>)(() => ShowEditorAsync(null, c)), (Func<Task>)(() => Repository.DeleteCorrectionAsync(c.Id, default)))));
        var ordered = _descending ? rows.OrderByDescending(r => r.Label, StringComparer.CurrentCultureIgnoreCase) : rows.OrderBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase);
        _entries.Children.Clear();
        foreach (var row in ordered.Where(r => r.Label.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase)))
            _entries.Children.Add(Card(Row(Text(row.Label), Action("Edit", row.Edit), Action("Delete", async () =>
            {
                if (await ConfirmAsync("Delete dictionary entry?", row.Label)) { await row.Delete(); await RefreshAsync(); }
            }))));
        if (_entries.Children.Count == 0) _entries.Children.Add(Card(Text(_search.Text.Length > 0 ? "No matching entries." : "No entries yet. Add a name, technical term, or correction.")));
    });
    public async void ReviewPreviewCorrection(string raw, string edited)
    {
        await RunAsync(async () =>
        {
            var suggestion = PreviewCorrectionSuggestion.Create(raw, edited);
            if (suggestion is null) { Message.Text = "Choose Add new to save a specific word or phrase correction."; return; }
            await ShowEditorAsync(null, new CorrectionRule(Guid.NewGuid(), _language.Text, suggestion.HeardText, suggestion.ReplacementText, DateTimeOffset.UtcNow));
        });
    }
    private async Task ShowEditorAsync(VocabularyEntry? word, CorrectionRule? correction)
    {
        bool editing = word is not null || correction is not null;
        var type = new ComboBox { Header = "Entry type", ItemsSource = new[] { "Vocabulary hint", "Exact correction" }, SelectedIndex = correction is null ? 0 : 1, IsEnabled = !editing };
        var language = Field("Language code", word?.Language ?? correction?.Language ?? _language.Text, 32);
        var phrase = Field("Word or phrase", word?.Term ?? correction?.HeardText ?? "");
        var replacement = Field("Replace with", correction?.ReplacementText ?? "");
        replacement.Visibility = type.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        type.SelectionChanged += (_, _) => replacement.Visibility = type.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        var fields = new StackPanel { Spacing = 16 };
        fields.Children.Add(type); fields.Children.Add(language); fields.Children.Add(phrase); fields.Children.Add(replacement);
        fields.Children.Add(Text("Vocabulary hints help recognition. Corrections replace matching whole words with the exact spelling you save.", 13));
        await EditAsync(editing ? "Edit dictionary entry" : "Add dictionary entry", fields, async () =>
        {
            if (type.SelectedIndex == 0) await Repository.SaveVocabularyAsync(new(word?.Id ?? Guid.NewGuid(), language.Text, phrase.Text, word?.CreatedAtUtc ?? DateTimeOffset.UtcNow), default);
            else await Repository.SaveCorrectionAsync(new(correction?.Id ?? Guid.NewGuid(), language.Text, phrase.Text, replacement.Text, correction?.CreatedAtUtc ?? DateTimeOffset.UtcNow), default);
            _language.Text = language.Text;
        });
    }
}
