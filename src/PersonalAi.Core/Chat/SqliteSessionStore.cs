using Microsoft.Data.Sqlite;

namespace PersonalAi.Core.Chat;

public sealed class SqliteSessionStore : ISessionStore, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SqliteSessionStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    public async Task<IReadOnlyList<ChatTurn>> GetHistoryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Role, Content, CreatedAtUtc
            FROM ChatTurns
            WHERE SessionId = $sessionId
            ORDER BY Id ASC;
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);

        var results = new List<ChatTurn>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            DateTimeOffset? created = null;
            if (!reader.IsDBNull(2) &&
                DateTimeOffset.TryParse(reader.GetString(2), out var dto))
            {
                created = dto;
            }

            results.Add(new ChatTurn(reader.GetString(0), reader.GetString(1), created));
        }

        return results;
    }

    public async Task TruncateAfterAsync(
        string sessionId,
        int keepCount,
        CancellationToken cancellationToken = default)
    {
        if (keepCount < 0)
            throw new ArgumentOutOfRangeException(nameof(keepCount));

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM ChatTurns
            WHERE SessionId = $sessionId
              AND Id NOT IN (
                  SELECT Id FROM ChatTurns
                  WHERE SessionId = $sessionId
                  ORDER BY Id ASC
                  LIMIT $keepCount
              );
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$keepCount", keepCount);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendAsync(string sessionId, ChatTurn user, ChatTurn assistant, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await InsertAsync(connection, (SqliteTransaction)tx, sessionId, user, cancellationToken).ConfigureAwait(false);
        await InsertAsync(connection, (SqliteTransaction)tx, sessionId, assistant, cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT SessionId,
                   MAX(CreatedAtUtc) AS UpdatedAtUtc,
                   (
                       SELECT Content
                       FROM ChatTurns t2
                       WHERE t2.SessionId = t1.SessionId
                       ORDER BY t2.Id DESC
                       LIMIT 1
                   ) AS Preview
            FROM ChatTurns t1
            GROUP BY SessionId
            ORDER BY UpdatedAtUtc DESC;
            """;

        var results = new List<SessionSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            var updatedRaw = reader.GetString(1);
            var preview = reader.IsDBNull(2) ? "" : reader.GetString(2);
            if (preview.Length > 80)
                preview = preview[..80] + "…";

            var updated = DateTimeOffset.TryParse(updatedRaw, out var dto)
                ? dto
                : DateTimeOffset.UtcNow;
            results.Add(new SessionSummary(id, updated, preview));
        }

        return results;
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM ChatTurns WHERE SessionId = $sessionId;";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        string sessionId,
        ChatTurn turn,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO ChatTurns (SessionId, Role, Content, CreatedAtUtc)
            VALUES ($sessionId, $role, $content, $createdAt);
            """;
        cmd.Parameters.AddWithValue("$sessionId", sessionId);
        cmd.Parameters.AddWithValue("$role", turn.Role);
        cmd.Parameters.AddWithValue("$content", turn.Content);
        cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS ChatTurns (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ChatTurns_SessionId ON ChatTurns(SessionId);
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
