using Microsoft.UI.Xaml.Controls;
using Shruti.Core.TranscriptQuality;
using Shruti.Storage;

namespace Shruti.App.WinUI;

public sealed class SnippetsControl : LibraryPage
{
    private readonly ISnippetRepository _repository = new SqliteSnippetRepository(AppDataPaths.CreateDefault());
    private readonly StackPanel _entries = new() { Spacing = 1 };
    private readonly TextBox _search = new() { PlaceholderText = "Search snippets", MaxLength = 256 };
    private readonly TextBox _language = new() { Text = "en", Header = "Language", Width = 90, MaxLength = 32 };
    private bool _descending;
    private int _generation;
    public SnippetsControl() : base("Snippets", "Say a saved phrase to insert its text. Use distinctive phrases such as ‘my email address’. Snippets also work within a sentence.")
    {
        Actions.Children.Add(Action("Add new", () => ShowEditorAsync(null)));
        var filters = FilterBar(_language,
            Action("Reverse order", async () => { _descending = !_descending; await RefreshAsync(); }));
        Body.Children.Add(filters); Body.Children.Add(_search); Body.Children.Add(_entries);
        _search.TextChanged += async (_, _) => await RefreshAsync();
        _language.LostFocus += async (_, _) => await RefreshAsync();
    }
    public override Task RefreshAsync() => RunAsync(async () =>
    {
        int generation = ++_generation;
        var snippets = await _repository.ListAsync(_language.Text, default);
        if (generation != _generation) return;
        _entries.Children.Clear();
        var ordered = _descending ? snippets.OrderByDescending(s => s.Phrase, StringComparer.CurrentCultureIgnoreCase) : snippets.OrderBy(s => s.Phrase, StringComparer.CurrentCultureIgnoreCase);
        foreach (var snippet in ordered.Where(s => (s.Phrase + " " + s.Text).Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase)))
        {
            var text = Text(snippet.Phrase + " → " + snippet.Text); text.MaxLines = 2; text.TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis;
            _entries.Children.Add(Card(Row(text, Action("Edit", () => ShowEditorAsync(snippet)), Action("Delete", async () =>
            {
                if (await ConfirmAsync("Delete snippet?", snippet.Phrase)) { await _repository.DeleteAsync(snippet.Id, default); await RefreshAsync(); }
            }))));
        }
        if (_entries.Children.Count == 0) _entries.Children.Add(Card(Text(_search.Text.Length > 0 ? "No matching snippets." : "Save text you use often, such as a link, an address, or a signature.")));
    });
    private Task ShowEditorAsync(TranscriptSnippet? snippet)
    {
        var language = Field("Language code", snippet?.Language ?? _language.Text, 32);
        var phrase = Field("When I say", snippet?.Phrase ?? "");
        var text = Field("Insert this exact text", snippet?.Text ?? "", 16000, true);
        var fields = new StackPanel { Spacing = 16 }; fields.Children.Add(language); fields.Children.Add(phrase); fields.Children.Add(text);
        fields.Children.Add(Text("Capitalization and line breaks are preserved. Saved text does not trigger other snippets.", 13));
        return EditAsync(snippet is null ? "Add snippet" : "Edit snippet", fields, async () =>
        {
            await _repository.SaveAsync(new(snippet?.Id ?? Guid.NewGuid(), language.Text, phrase.Text, text.Text), default);
            _language.Text = language.Text;
        });
    }
}
