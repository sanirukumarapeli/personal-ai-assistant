using Microsoft.Data.Sqlite;

namespace PersonalAi.Core.Gmail;

public sealed class SqliteGoogleTokenStore : IGoogleTokenStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SqliteGoogleTokenStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    public async Task<GoogleTokenRecord?> GetAsync(string accountKey = "default", CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT AccountKey, RefreshToken, AccessToken, AccessTokenExpiresAt, Email
            FROM GoogleTokens
            WHERE AccountKey = $key
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$key", accountKey);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        DateTimeOffset? expires = null;
        if (!reader.IsDBNull(3) && DateTimeOffset.TryParse(reader.GetString(3), out var parsed))
            expires = parsed;

        return new GoogleTokenRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            expires,
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    public async Task SaveAsync(GoogleTokenRecord record, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO GoogleTokens (AccountKey, RefreshToken, AccessToken, AccessTokenExpiresAt, Email, UpdatedAtUtc)
            VALUES ($key, $refresh, $access, $expires, $email, $updated)
            ON CONFLICT(AccountKey) DO UPDATE SET
                RefreshToken = excluded.RefreshToken,
                AccessToken = excluded.AccessToken,
                AccessTokenExpiresAt = excluded.AccessTokenExpiresAt,
                Email = excluded.Email,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        cmd.Parameters.AddWithValue("$key", record.AccountKey);
        cmd.Parameters.AddWithValue("$refresh", record.RefreshToken);
        cmd.Parameters.AddWithValue("$access", (object?)record.AccessToken ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$expires", (object?)record.AccessTokenExpiresAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$email", (object?)record.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string accountKey = "default", CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM GoogleTokens WHERE AccountKey = $key;";
        cmd.Parameters.AddWithValue("$key", accountKey);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsConnectedAsync(string accountKey = "default", CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(accountKey, cancellationToken).ConfigureAwait(false);
        return record is not null && !string.IsNullOrWhiteSpace(record.RefreshToken);
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
                CREATE TABLE IF NOT EXISTS GoogleTokens (
                    AccountKey TEXT PRIMARY KEY,
                    RefreshToken TEXT NOT NULL,
                    AccessToken TEXT NULL,
                    AccessTokenExpiresAt TEXT NULL,
                    Email TEXT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
