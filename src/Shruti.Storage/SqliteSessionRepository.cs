using Microsoft.Data.Sqlite;

namespace Shruti.Storage;

public sealed class SqliteSessionRepository : ISessionRepository
{
    private readonly AppDataPaths _paths;

    public SqliteSessionRepository(AppDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task SaveAsync(StoredDictationSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        Validate(session);

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await UpsertSessionAsync(connection, transaction, session, cancellationToken).ConfigureAwait(false);
        await DeleteSegmentsAsync(connection, transaction, session.Id, cancellationToken).ConfigureAwait(false);
        foreach (StoredTranscriptSegment segment in session.Segments.OrderBy(segment => segment.Index))
        {
            await InsertSegmentAsync(connection, transaction, session.Id, segment, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<StoredDictationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, started_at_utc, ended_at_utc, source_trigger, target_process_name,
                   target_window_title, model_id, provider_id, backend, language, status
            FROM dictation_sessions
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));

        SessionMetadata? metadata;
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            metadata = new SessionMetadata(
                Guid.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.IsDBNull(2)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetString(3),
                GetNullableString(reader, 4),
                GetNullableString(reader, 5),
                GetNullableString(reader, 6),
                GetNullableString(reader, 7),
                GetNullableString(reader, 8),
                reader.GetString(9),
                reader.GetString(10));
        }

        IReadOnlyList<StoredTranscriptSegment> segments = await LoadSegmentsAsync(connection, sessionId, cancellationToken)
            .ConfigureAwait(false);
        return new StoredDictationSession(
            metadata.Id,
            metadata.StartedAtUtc,
            metadata.EndedAtUtc,
            metadata.SourceTrigger,
            metadata.TargetProcessName,
            metadata.TargetWindowTitle,
            metadata.ModelId,
            metadata.ProviderId,
            metadata.Backend,
            metadata.Language,
            metadata.Status,
            segments);
    }

    private SqliteConnection CreateConnection()
    {
        Directory.CreateDirectory(_paths.RootPath);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        };
        return new SqliteConnection(builder.ToString());
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS dictation_sessions (
                id TEXT PRIMARY KEY NOT NULL,
                started_at_utc TEXT NOT NULL,
                ended_at_utc TEXT NULL,
                source_trigger TEXT NOT NULL,
                target_process_name TEXT NULL,
                target_window_title TEXT NULL,
                model_id TEXT NULL,
                provider_id TEXT NULL,
                backend TEXT NULL,
                language TEXT NOT NULL,
                status TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS transcript_segments (
                session_id TEXT NOT NULL,
                segment_index INTEGER NOT NULL,
                start_ticks INTEGER NOT NULL,
                end_ticks INTEGER NOT NULL,
                text TEXT NOT NULL,
                confidence REAL NULL,
                PRIMARY KEY (session_id, segment_index),
                FOREIGN KEY (session_id) REFERENCES dictation_sessions(id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StoredDictationSession session,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dictation_sessions (
                id, started_at_utc, ended_at_utc, source_trigger, target_process_name,
                target_window_title, model_id, provider_id, backend, language, status)
            VALUES (
                $id, $startedAtUtc, $endedAtUtc, $sourceTrigger, $targetProcessName,
                $targetWindowTitle, $modelId, $providerId, $backend, $language, $status)
            ON CONFLICT(id) DO UPDATE SET
                started_at_utc = excluded.started_at_utc,
                ended_at_utc = excluded.ended_at_utc,
                source_trigger = excluded.source_trigger,
                target_process_name = excluded.target_process_name,
                target_window_title = excluded.target_window_title,
                model_id = excluded.model_id,
                provider_id = excluded.provider_id,
                backend = excluded.backend,
                language = excluded.language,
                status = excluded.status;
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$startedAtUtc", session.StartedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$endedAtUtc", session.EndedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$sourceTrigger", session.SourceTrigger);
        command.Parameters.AddWithValue("$targetProcessName", session.TargetProcessName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$targetWindowTitle", session.TargetWindowTitle ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$modelId", session.ModelId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$providerId", session.ProviderId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$backend", session.Backend ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$language", session.Language);
        command.Parameters.AddWithValue("$status", session.Status);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteSegmentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM transcript_segments WHERE session_id = $sessionId;";
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertSegmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        StoredTranscriptSegment segment,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO transcript_segments (
                session_id, segment_index, start_ticks, end_ticks, text, confidence)
            VALUES ($sessionId, $index, $startTicks, $endTicks, $text, $confidence);
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$index", segment.Index);
        command.Parameters.AddWithValue("$startTicks", segment.Start.Ticks);
        command.Parameters.AddWithValue("$endTicks", segment.End.Ticks);
        command.Parameters.AddWithValue("$text", segment.Text);
        command.Parameters.AddWithValue("$confidence", segment.Confidence ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<StoredTranscriptSegment>> LoadSegmentsAsync(
        SqliteConnection connection,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT segment_index, start_ticks, end_ticks, text, confidence
            FROM transcript_segments
            WHERE session_id = $sessionId
            ORDER BY segment_index;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId.ToString("D"));

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var segments = new List<StoredTranscriptSegment>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            segments.Add(new StoredTranscriptSegment(
                reader.GetInt32(0),
                TimeSpan.FromTicks(reader.GetInt64(1)),
                TimeSpan.FromTicks(reader.GetInt64(2)),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetFloat(4)));
        }

        return segments;
    }

    private static string? GetNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static void Validate(StoredDictationSession session)
    {
        if (session.Id == Guid.Empty)
        {
            throw new ArgumentException("A stored dictation session requires an identifier.", nameof(session));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(session.SourceTrigger);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Language);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Status);

        if (session.EndedAtUtc is { } endedAt && endedAt < session.StartedAtUtc)
        {
            throw new ArgumentException("A dictation session cannot end before it starts.", nameof(session));
        }

        foreach (IGrouping<int, StoredTranscriptSegment> group in session.Segments.GroupBy(segment => segment.Index))
        {
            if (group.Key < 0 || group.Count() != 1)
            {
                throw new ArgumentException("Transcript segment indexes must be unique non-negative values.", nameof(session));
            }

            StoredTranscriptSegment segment = group.Single();
            if (segment.End < segment.Start)
            {
                throw new ArgumentException("A transcript segment cannot end before it starts.", nameof(session));
            }
        }
    }

    private sealed record SessionMetadata(
        Guid Id,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        string SourceTrigger,
        string? TargetProcessName,
        string? TargetWindowTitle,
        string? ModelId,
        string? ProviderId,
        string? Backend,
        string Language,
        string Status);
}
