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
    private readonly TextBox _language = new() { Text = "en", Header = "Language", Width = 80, MaxLength = 32 };
    private readonly TextBlock _count;
    private readonly Button _sort;
    private readonly StackPanel _tools = new() { Spacing = 22, Margin = new Thickness(0, 0, 0, -12) };
    private bool _descending;
    private int _generation;

    public SnippetsControl() : base("Snippets", "Say a short phrase to insert saved text anywhere you dictate.")
    {
        AddCreateAction("Add snippet", () => ShowEditorAsync(null));
        _tools.Children.Add(SearchTools(_search, _language));
        _count = Heading("Saved snippets", 17);
        _sort = Action("A to Z", ToggleOrderAsync);
        AutomationProperties.SetName(_sort, "Sort snippets, A to Z");
        ToolTipService.SetToolTip(_sort, "Reverse alphabetical order");
        _tools.Children.Add(Row(_count, _sort));
        Body.Children.Add(_tools);
        _entries = AddEntryList();
        _search.TextChanged += async (_, _) => await RefreshAsync();
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
        if (string.IsNullOrWhiteSpace(_language.Text))
        {
            _entries.Children.Clear();
            _count.Text = "Saved snippets";
            _sort.Visibility = Visibility.Collapsed;
            _tools.Visibility = Visibility.Visible;
            AddListEmpty(_entries, "\uE8C8", "Choose a language", "Enter a language code, such as en, to see your snippets.", null);
            return;
        }
        var snippets = await _repository.ListAsync(_language.Text, default);
        if (generation != _generation) return;
        _entries.Children.Clear();
        var ordered = _descending ? snippets.OrderByDescending(s => s.Phrase, StringComparer.CurrentCultureIgnoreCase) : snippets.OrderBy(s => s.Phrase, StringComparer.CurrentCultureIgnoreCase);
        foreach (var snippet in ordered.Where(s => s.Phrase.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase) || s.Text.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase)))
        {
            AddEntryRow(_entries, "\uE8C8", snippet.Phrase, snippet.Text, snippet.Phrase, () => ShowEditorAsync(snippet), async () =>
            {
                if (await ConfirmAsync("Delete snippet?", snippet.Phrase))
                {
                    await _repository.DeleteAsync(snippet.Id, default);
                    await RefreshAsync();
                }
            });
        }

        int count = _entries.Children.Count;
        _count.Text = count switch { 0 => "Saved snippets", 1 => "1 saved snippet", _ => $"{count:N0} saved snippets" };
        _sort.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        bool searching = _search.Text.Length > 0;
        bool defaultLanguage = string.Equals(_language.Text.Trim(), "en", StringComparison.OrdinalIgnoreCase);
        var languageAction = UpdateListTools(_tools, _language, count > 0 || searching || !defaultLanguage);
        if (count == 0)
            AddListEmpty(_entries, "\uE8C8", searching ? "No matching snippets" : defaultLanguage ? "No English snippets yet" : "No snippets in this language",
                searching ? "Change the search or language." : "Add an address, link, or signature to insert with a spoken phrase.", languageAction);
    });

    private Task ShowEditorAsync(TranscriptSnippet? snippet)
    {
        var language = Field("Language code", snippet?.Language ?? _language.Text, 32);
        language.Width = 100;
        language.MinWidth = 72;
        var phrase = Field("When I say", snippet?.Phrase ?? "");
        phrase.MinWidth = 120;
        phrase.PlaceholderText = "A phrase such as my signature";
        var text = Field("Insert this text", snippet?.Text ?? "", 16000, true);
        text.PlaceholderText = "Type or paste the text to insert";
        var fields = new StackPanel { Spacing = 16 };
        fields.Children.Add(Row(phrase, language));
        fields.Children.Add(text);
        fields.Children.Add(Secondary("Shruti keeps capitalization and line breaks. Snippets can expand within a sentence.", 13));
        return EditAsync(snippet is null ? "Add snippet" : "Edit snippet", fields, async () =>
        {
            await _repository.SaveAsync(new(snippet?.Id ?? Guid.NewGuid(), language.Text, phrase.Text, text.Text), default);
            _language.Text = language.Text;
        }, phrase, language, phrase, text);
    }
}
