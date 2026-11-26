using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Shruti.Core.TranscriptQuality;
using Shruti.Storage;
using Shruti.TranscriptQuality;

namespace Shruti.App.WinUI;

public sealed class DictionaryControl : LibraryPage
{
    public IVocabularyRepository Repository { get; set; } = new SqliteVocabularyRepository(AppDataPaths.CreateDefault());
    private readonly StackPanel _entries;
    private readonly TextBox _search = new() { Header = "Search", PlaceholderText = "Search dictionary", MaxLength = 256 };
    private readonly AutoSuggestBox _language = LanguageField("en");
    private readonly ComboBox _filter = new() { Header = "Entry type", ItemsSource = new[] { "All entries", "Vocabulary", "Corrections" }, SelectedIndex = 0, MinWidth = 132 };
    private readonly Button _sort;
    private readonly TextBlock _count;
    private readonly StackPanel _tools = new() { Spacing = 14 };
    private bool _descending;
    private int _generation;

    public DictionaryControl() : base("Dictionary", "Choose the spelling Shruti uses for names, terms, and corrections.")
    {
        AddCreateAction("Add entry", () => ShowEditorAsync(null, null));
        _language.Width = 172;
        _tools.Children.Add(SearchTools(_search, _filter, _language));
        _count = Heading("Entries", 17);
        _sort = Action("A to Z", ToggleOrderAsync);
        AutomationProperties.SetName(_sort, "Sort entries, A to Z");
        ToolTipService.SetToolTip(_sort, "Reverse alphabetical order");
        _tools.Children.Add(Row(_count, _sort));
        Body.Children.Add(_tools);
        _entries = AddEntryList();
        _search.TextChanged += async (_, _) => await RefreshAsync();
        _filter.SelectionChanged += async (_, _) => await RefreshAsync();
        _language.QuerySubmitted += async (_, _) => await RefreshAsync();
        _language.LostFocus += async (_, _) => await RefreshAsync();
    }

    private async Task ToggleOrderAsync()
    {
        _descending = !_descending;
        _sort.Content = _descending ? "Z to A" : "A to Z";
        AutomationProperties.SetName(_sort, $"Sort entries, {_sort.Content}");
        await RefreshAsync();
    }

    public override Task RefreshAsync() => RunAsync(async () =>
    {
        int generation = ++_generation;
        if (string.IsNullOrWhiteSpace(LanguageCode(_language)))
        {
            _entries.Children.Clear();
            _count.Text = "Entries";
            _sort.Visibility = Visibility.Collapsed;
            _tools.Visibility = Visibility.Visible;
            AddListEmpty(_entries, "\uE82D", "Choose a language", "Enter a language code, such as en, to see your entries.", null);
            return;
        }
        var vocabulary = await Repository.ListVocabularyAsync(LanguageCode(_language), default);
        var corrections = await Repository.ListCorrectionsAsync(LanguageCode(_language), default);
        if (generation != _generation) return;
        var rows = new List<(Guid Id, string Phrase, string? Replacement, Func<Task> Edit, Func<Task> Delete)>();
        if (_filter.SelectedIndex != 2)
            rows.AddRange(vocabulary.Select(v => (v.Id, v.Term, (string?)null, (Func<Task>)(() => ShowEditorAsync(v, null)), (Func<Task>)(() => Repository.DeleteVocabularyAsync(v.Id, default)))));
        if (_filter.SelectedIndex != 1)
            rows.AddRange(corrections.Select(c => (c.Id, c.HeardText, (string?)c.ReplacementText, (Func<Task>)(() => ShowEditorAsync(null, c)), (Func<Task>)(() => Repository.DeleteCorrectionAsync(c.Id, default)))));

        var matches = rows.Where(r => r.Phrase.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase) ||
            (r.Replacement?.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase) ?? false));
        var ordered = _descending ? matches.OrderByDescending(r => r.Phrase, StringComparer.CurrentCultureIgnoreCase) : matches.OrderBy(r => r.Phrase, StringComparer.CurrentCultureIgnoreCase);
        _entries.Children.Clear();
        foreach (var row in ordered)
        {
            string description = row.Replacement is null ? "Vocabulary" : $"Replace with {row.Replacement}";
            AddEntryRow(_entries, row.Id, row.Replacement is null ? "\uE82D" : "\uE8AB", row.Phrase, description, row.Phrase, row.Edit, async () =>
            {
                if (await ConfirmAsync("Delete dictionary entry?", row.Phrase))
                {
                    await row.Delete();
                    await RefreshAsync();
                }
            });
        }

        int count = _entries.Children.Count;
        string languageName = LanguageName(LanguageCode(_language));
        _count.Text = count == 1 ? $"1 entry in {languageName}" : $"{count:N0} entries in {languageName}";
        _sort.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        bool filtering = _search.Text.Length > 0 || _filter.SelectedIndex != 0;
        bool defaultLanguage = string.Equals(LanguageCode(_language).Trim(), "en", StringComparison.OrdinalIgnoreCase);
        var languageAction = UpdateListTools(_tools, _language, count > 0 || filtering || !defaultLanguage);
        if (count == 0)
        {
            if (filtering) languageAction = ResetSearch(_search, _filter);
            string title = filtering ? "No matching entries" : defaultLanguage ? "No English entries yet" : "No entries in this language";
            AddListEmpty(_entries, "\uE82D", title, filtering ? "Change the search, entry type, or language."
                : "Add a name Shruti should recognize or a spelling it should correct.", languageAction);
        }
    });

    public async void ReviewPreviewCorrection(string raw, string edited)
    {
        await RunAsync(async () =>
        {
            var suggestion = PreviewCorrectionSuggestion.Create(raw, edited);
            if (suggestion is null) { Message.Text = "Choose Add entry to save a specific correction."; return; }
            await ShowEditorAsync(null, new CorrectionRule(Guid.NewGuid(), LanguageCode(_language), suggestion.HeardText, suggestion.ReplacementText, DateTimeOffset.UtcNow));
        });
    }

    private async Task ShowEditorAsync(VocabularyEntry? word, CorrectionRule? correction)
    {
        Guid id = word?.Id ?? correction?.Id ?? Guid.NewGuid();
        bool editing = word is not null || correction is not null;
        var type = new ComboBox { Header = "Entry type", ItemsSource = new[] { "Vocabulary hint", "Exact correction" }, SelectedIndex = correction is null ? 0 : 1, IsEnabled = !editing, HorizontalAlignment = HorizontalAlignment.Stretch };
        var phrase = Field("Word or phrase", word?.Term ?? correction?.HeardText ?? "");
        string language = word?.Language ?? correction?.Language ?? LanguageCode(_language);
        var replacement = Field("Replace with", correction?.ReplacementText ?? "");
        phrase.TextWrapping = replacement.TextWrapping = TextWrapping.Wrap;
        phrase.MaxHeight = replacement.MaxHeight = 104;
        var usage = Secondary("", 14);
        void UpdateFields()
        {
            bool correcting = type.SelectedIndex == 1;
            phrase.Header = correcting ? "When Shruti hears" : "Word or phrase";
            phrase.PlaceholderText = correcting ? "Words to replace" : "Enter the exact spelling";
            replacement.MinWidth = 0;
            replacement.PlaceholderText = "Correct spelling or phrase";
            replacement.Visibility = correcting ? Visibility.Visible : Visibility.Collapsed;
            usage.Text = correcting ? "Replace matching whole words with the text you save." : "Help Shruti recognize a name or technical term during dictation.";
        }

        UpdateFields();
        type.SelectionChanged += (_, _) => UpdateFields();
        var fields = new StackPanel { Spacing = 10 };
        if (editing) fields.Children.Add(Heading(correction is null ? "Vocabulary hint" : "Exact correction", 16));
        else fields.Children.Add(type);
        phrase.MinWidth = 100;
        fields.Children.Add(phrase);
        fields.Children.Add(replacement);
        fields.Children.Add(usage);
        await EditAsync(editing ? "Edit dictionary entry" : "Add dictionary entry", fields, language, _entries, id, async code =>
        {
            if (type.SelectedIndex == 0) await Repository.SaveVocabularyAsync(new(id, code, phrase.Text, word?.CreatedAtUtc ?? DateTimeOffset.UtcNow), default);
            else await Repository.SaveCorrectionAsync(new(id, code, phrase.Text, replacement.Text, correction?.CreatedAtUtc ?? DateTimeOffset.UtcNow), default);
            SetLanguage(_language, code);
        }, phrase, replacement);
    }
}
