using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Shruti.Core.TranscriptQuality;

namespace Shruti.Storage;

public sealed class SqliteVocabularyRepository(AppDataPaths paths) : IVocabularyRepository
{
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.RootPath);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = paths.DatabaseFilePath, Pooling = false }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS vocabulary_entries (
                    id TEXT PRIMARY KEY, language TEXT NOT NULL, term TEXT NOT NULL,
                    normalized_term TEXT NOT NULL, created_at_utc TEXT NOT NULL,
                    UNIQUE(language, normalized_term));
                CREATE TABLE IF NOT EXISTS correction_rules (
                    id TEXT PRIMARY KEY, language TEXT NOT NULL, heard_text TEXT NOT NULL,
                    normalized_heard_text TEXT NOT NULL, replacement_text TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL, UNIQUE(language, normalized_heard_text));
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<IReadOnlyList<VocabularyEntry>> ListVocabularyAsync(string language, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, language, term, created_at_utc FROM vocabulary_entries WHERE language=$language ORDER BY normalized_term";
        command.Parameters.AddWithValue("$language", Language(language));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<VocabularyEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        return result;
    }

    public async Task<IReadOnlyList<CorrectionRule>> ListCorrectionsAsync(string language, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, language, heard_text, replacement_text, created_at_utc FROM correction_rules WHERE language=$language ORDER BY normalized_heard_text";
        command.Parameters.AddWithValue("$language", Language(language));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<CorrectionRule>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)));
        return result;
    }

    public Task SaveVocabularyAsync(VocabularyEntry entry, CancellationToken cancellationToken) => SaveAsync(entry.Id,
        entry.Language, entry.Term, null, entry.CreatedAtUtc, cancellationToken);
    public Task SaveCorrectionAsync(CorrectionRule rule, CancellationToken cancellationToken) => SaveAsync(rule.Id,
        rule.Language, rule.HeardText, rule.ReplacementText, rule.CreatedAtUtc, cancellationToken);

    private async Task SaveAsync(Guid id, string language, string text, string? replacement, DateTimeOffset created,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > 256 || text.Contains('\n') || text.Contains('\r') || text.Contains('\0'))
            throw new ArgumentException("Dictionary entries must be a single line of at most 256 characters.");
        if (replacement is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(replacement);
            if (replacement.Length > 256 || replacement.Any(char.IsControl))
                throw new ArgumentException("Replacement must be a single line of at most 256 characters.");
        }
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = replacement is null ? """
            INSERT INTO vocabulary_entries VALUES ($id,$language,$text,$normalized,$created)
            ON CONFLICT(language,normalized_term) DO UPDATE SET term=excluded.term
            """ : """
            INSERT INTO correction_rules VALUES ($id,$language,$text,$normalized,$replacement,$created)
            ON CONFLICT(language,normalized_heard_text) DO UPDATE SET replacement_text=excluded.replacement_text, heard_text=excluded.heard_text
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$language", Language(language));
        command.Parameters.AddWithValue("$text", text.Trim().Normalize(NormalizationForm.FormC));
        command.Parameters.AddWithValue("$normalized", Normalize(text));
        command.Parameters.AddWithValue("$created", created.ToString("O"));
        if (replacement is not null) command.Parameters.AddWithValue("$replacement", replacement.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteVocabularyAsync(Guid id, CancellationToken cancellationToken) => DeleteAsync("vocabulary_entries", id, cancellationToken);
    public Task DeleteCorrectionAsync(Guid id, CancellationToken cancellationToken) => DeleteAsync("correction_rules", id, cancellationToken);
    private async Task DeleteAsync(string table, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Language(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return language.Trim().ToLowerInvariant();
    }
    public static string Normalize(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .Normalize(NormalizationForm.FormC).ToUpperInvariant();
}
