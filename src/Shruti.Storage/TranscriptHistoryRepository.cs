using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Shruti.Core.Dictation;

namespace Shruti.Storage;

public sealed record TranscriptHistoryEntry(Guid Id, DateTimeOffset CreatedAtUtc, string Text, string RawText,
    string? ProcessName, double? AudioSeconds, int Words, int Edits);
public sealed record HistoryActivity(DateOnly Date, int Dictations, int Words);
public sealed record HistoryAppUsage(string Name, int Dictations);
public sealed record TranscriptInsights(int Dictations, int Words, double AudioSeconds, int Edits,
    IReadOnlyList<HistoryActivity> Activity, IReadOnlyList<HistoryAppUsage> Apps);

public sealed class TranscriptHistoryRepository(AppDataPaths paths)
{
    private async Task<SqliteConnection> OpenAsync()
    {
        Directory.CreateDirectory(paths.RootPath);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFilePath, Pooling = false }.ToString());
        try
        {
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA secure_delete=ON;
                CREATE TABLE IF NOT EXISTS transcript_history (
                    id TEXT PRIMARY KEY, created_at_utc TEXT NOT NULL, text TEXT NOT NULL, raw_text TEXT NOT NULL,
                    process_name TEXT NULL, audio_seconds REAL NULL, words INTEGER NOT NULL, edits INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_history_date ON transcript_history(created_at_utc DESC);
                """;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            return connection;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task SaveAsync(DictationRunResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Transcript?.Text) || result.Outcome is
            DictationRunOutcome.NoSpeech or DictationRunOutcome.Failed or DictationRunOutcome.Cancelled) return;
        await using var connection = await OpenAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transcript_history VALUES ($id,$date,$text,$raw,$process,$seconds,$words,$edits)
            ON CONFLICT(id) DO UPDATE SET text=excluded.text, words=excluded.words;
            """;
        command.Parameters.AddWithValue("$id", result.Id.ToString("D"));
        command.Parameters.AddWithValue("$date", result.CreatedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$text", result.Transcript.Text);
        command.Parameters.AddWithValue("$raw", result.RefinedTranscript?.Raw.Text ?? result.Transcript.Text);
        command.Parameters.AddWithValue("$process", result.Target?.ProcessName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$seconds", result.AudioDuration?.TotalSeconds ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$words", Regex.Matches(result.Transcript.Text, @"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*").Count);
        command.Parameters.AddWithValue("$edits", result.RefinedTranscript?.Edits.Count ?? 0);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TranscriptHistoryEntry>> ListAsync(string search = "", int limit = 100, int offset = 0)
    {
        await using var connection = await OpenAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, created_at_utc, text, raw_text, process_name, audio_seconds, words, edits
            FROM transcript_history WHERE instr(lower(text),lower($search)) > 0 OR $search=''
            ORDER BY created_at_utc DESC, id DESC LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$search", search);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        command.Parameters.AddWithValue("$offset", Math.Max(offset, 0));
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var rows = new List<TranscriptHistoryEntry>();
        while (await reader.ReadAsync().ConfigureAwait(false))
            rows.Add(new(Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5), reader.GetInt32(6), reader.GetInt32(7)));
        return rows;
    }

    public async Task<TranscriptInsights> GetInsightsAsync()
    {
        await using var connection = await OpenAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();
        // Read metadata only. Local date grouping follows the current Windows time zone.
        command.CommandText = "SELECT created_at_utc, process_name, audio_seconds, words, edits FROM transcript_history";
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        int count = 0, words = 0, edits = 0;
        double seconds = 0;
        var days = new Dictionary<DateOnly, (int Count, int Words)>();
        var apps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var date = DateOnly.FromDateTime(DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture).LocalDateTime);
            int rowWords = reader.GetInt32(3);
            count++; words += rowWords; edits += reader.GetInt32(4);
            seconds += reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
            var day = days.GetValueOrDefault(date);
            days[date] = (day.Count + 1, day.Words + rowWords);
            if (!reader.IsDBNull(1))
            {
                string app = reader.GetString(1);
                apps[app] = apps.GetValueOrDefault(app) + 1;
            }
        }
        return new(count, words, seconds, edits,
            days.OrderBy(d => d.Key).Select(d => new HistoryActivity(d.Key, d.Value.Count, d.Value.Words)).ToArray(),
            apps.OrderByDescending(a => a.Value).Select(a => new HistoryAppUsage(a.Key, a.Value)).ToArray());
    }

    public async Task DeleteAsync(Guid? id)
    {
        await using var connection = await OpenAsync().ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = id is null ? "DELETE FROM transcript_history" : "DELETE FROM transcript_history WHERE id=$id";
        if (id is not null) command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
