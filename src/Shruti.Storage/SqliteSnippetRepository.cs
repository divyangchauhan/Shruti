using Microsoft.Data.Sqlite;
using Shruti.Core.TranscriptQuality;

namespace Shruti.Storage;

public sealed class SqliteSnippetRepository(AppDataPaths paths) : ISnippetRepository
{
    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        Directory.CreateDirectory(paths.RootPath);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFilePath, Pooling = false }.ToString());
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS snippets (
                    id TEXT PRIMARY KEY, language TEXT NOT NULL, phrase TEXT NOT NULL,
                    normalized_phrase TEXT NOT NULL, text TEXT NOT NULL,
                    UNIQUE(language, normalized_phrase));
                """;
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return connection;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<IReadOnlyList<TranscriptSnippet>> ListAsync(string language, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, language, phrase, text FROM snippets WHERE language=$language ORDER BY normalized_phrase";
        command.Parameters.AddWithValue("$language", language.Trim().ToLowerInvariant());
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<TranscriptSnippet>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            entries.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return entries;
    }

    public async Task SaveAsync(TranscriptSnippet snippet, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snippet.Language);
        ArgumentException.ThrowIfNullOrWhiteSpace(snippet.Phrase);
        ArgumentException.ThrowIfNullOrWhiteSpace(snippet.Text);
        if (snippet.Id == Guid.Empty || snippet.Phrase.Length > 256 || snippet.Phrase.Any(char.IsControl) ||
            snippet.Text.Length > 16000 || snippet.Text.Contains('\0'))
            throw new ArgumentException("Use a phrase of up to 256 characters and text of up to 16,000 characters.");
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO snippets VALUES ($id,$language,$phrase,$normalized,$text)
            ON CONFLICT(id) DO UPDATE SET language=excluded.language, phrase=excluded.phrase,
                normalized_phrase=excluded.normalized_phrase, text=excluded.text;
            """;
        command.Parameters.AddWithValue("$id", snippet.Id.ToString("D"));
        command.Parameters.AddWithValue("$language", snippet.Language.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$phrase", snippet.Phrase.Trim());
        command.Parameters.AddWithValue("$normalized", SqliteVocabularyRepository.Normalize(snippet.Phrase));
        command.Parameters.AddWithValue("$text", snippet.Text);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM snippets WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
