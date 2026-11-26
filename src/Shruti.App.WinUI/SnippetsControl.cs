using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Shruti.Core.TranscriptQuality;
using Shruti.Storage;

namespace Shruti.App.WinUI;

public sealed class SnippetsControl : LibraryPage
{
    private readonly ISnippetRepository _repository = new SqliteSnippetRepository(AppDataPaths.CreateDefault());
    private readonly StackPanel _entries;
    private readonly TextBox _search = new() { Header = "Search", PlaceholderText = "Search snippets", MaxLength = 256 };
    private readonly AutoSuggestBox _language = LanguageField("en");
    private readonly TextBlock _count;
    private readonly Button _sort;
    private readonly StackPanel _tools = new() { Spacing = 14 };
    private bool _descending;
    private int _generation;

    public SnippetsControl() : base("Snippets", "Say a short phrase to insert saved text anywhere you dictate.")
    {
        AddCreateAction("Add snippet", () => ShowEditorAsync(null));
        _language.Width = 172;
        _tools.Children.Add(SearchTools(_search, _language));
        _count = Heading("Saved snippets", 17);
        _sort = Action("A to Z", ToggleOrderAsync);
        AutomationProperties.SetName(_sort, "Sort snippets, A to Z");
        ToolTipService.SetToolTip(_sort, "Reverse alphabetical order");
        _tools.Children.Add(Row(_count, _sort));
        Body.Children.Add(_tools);
        _entries = AddEntryList();
        _search.TextChanged += async (_, _) => await RefreshAsync();
        _language.QuerySubmitted += async (_, _) => await RefreshAsync();
        _language.LostFocus += async (_, _) => await RefreshAsync();
    }

    private async Task ToggleOrderAsync()
    {
        _descending = !_descending;
        _sort.Content = _descending ? "Z to A" : "A to Z";
        AutomationProperties.SetName(_sort, $"Sort snippets, {_sort.Content}");
        await RefreshAsync();
    }

    public override Task RefreshAsync() => RunAsync(async () =>
    {
        int generation = ++_generation;
        if (string.IsNullOrWhiteSpace(LanguageCode(_language)))
        {
            _entries.Children.Clear();
            _count.Text = "Saved snippets";
            _sort.Visibility = Visibility.Collapsed;
            _tools.Visibility = Visibility.Visible;
            AddListEmpty(_entries, "\uE70B", "Choose a language", "Enter a language code, such as en, to see your snippets.", null);
            return;
        }
        var snippets = await _repository.ListAsync(LanguageCode(_language), default);
        if (generation != _generation) return;
        _entries.Children.Clear();
        var ordered = _descending ? snippets.OrderByDescending(s => s.Phrase, StringComparer.CurrentCultureIgnoreCase) : snippets.OrderBy(s => s.Phrase, StringComparer.CurrentCultureIgnoreCase);
        foreach (var snippet in ordered.Where(s => s.Phrase.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase) || s.Text.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase)))
        {
            AddEntryRow(_entries, snippet.Id, "\uE70B", snippet.Phrase, snippet.Text.Trim(), snippet.Phrase, () => ShowEditorAsync(snippet), async () =>
            {
                if (await ConfirmAsync("Delete snippet?", snippet.Phrase))
                {
                    await _repository.DeleteAsync(snippet.Id, default);
                    await RefreshAsync();
                }
            }, isTextPreview: true);
        }

        int count = _entries.Children.Count;
        string languageName = LanguageName(LanguageCode(_language));
        _count.Text = count == 1 ? $"1 snippet in {languageName}" : $"{count:N0} snippets in {languageName}";
        _sort.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        bool searching = _search.Text.Length > 0;
        bool defaultLanguage = string.Equals(LanguageCode(_language).Trim(), "en", StringComparison.OrdinalIgnoreCase);
        var languageAction = UpdateListTools(_tools, _language, count > 0 || searching || !defaultLanguage);
        if (searching && count == 0) languageAction = ResetSearch(_search);
        if (count == 0)
            AddListEmpty(_entries, "\uE70B", searching ? "No matching snippets" : defaultLanguage ? "No English snippets yet" : "No snippets in this language",
                searching ? "Change the search or language." : "Add an address, link, or signature to insert with a spoken phrase.", languageAction);
    });

    private Task ShowEditorAsync(TranscriptSnippet? snippet)
    {
        Guid id = snippet?.Id ?? Guid.NewGuid();
        var phrase = Field("When I say", snippet?.Phrase ?? "");
        string language = snippet?.Language ?? LanguageCode(_language);
        phrase.MinWidth = 120;
        phrase.PlaceholderText = "A phrase such as my signature";
        var text = Field("Insert this text", snippet?.Text ?? "", 16000, true);
        text.PlaceholderText = "Type or paste your text";
        text.MinWidth = 0;
        text.Header = null;
        var bodyLabel = Text("Insert this text", 14);
        AutomationProperties.SetLabeledBy(text, bodyLabel);
        text.Height = 128;
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Visible);
        var body = new StackPanel { Spacing = 5, Children = { bodyLabel, text } };
        var fields = new StackPanel { Spacing = 10 };
        fields.Children.Add(phrase);
        fields.Children.Add(body);
        return EditAsync(snippet is null ? "Add snippet" : "Edit snippet", fields, language, _entries, id, async code =>
        {
            await _repository.SaveAsync(new(id, code, phrase.Text, text.Text), default);
            SetLanguage(_language, code);
        }, phrase, text);
    }
}
