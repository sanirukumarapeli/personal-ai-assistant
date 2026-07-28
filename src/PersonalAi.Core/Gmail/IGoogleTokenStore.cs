namespace PersonalAi.Core.Gmail;

public sealed record GoogleTokenRecord(
    string AccountKey,
    string RefreshToken,
    string? AccessToken,
    DateTimeOffset? AccessTokenExpiresAt,
    string? Email);

public interface IGoogleTokenStore
{
    Task<GoogleTokenRecord?> GetAsync(string accountKey = "default", CancellationToken cancellationToken = default);
    Task SaveAsync(GoogleTokenRecord record, CancellationToken cancellationToken = default);
    Task DeleteAsync(string accountKey = "default", CancellationToken cancellationToken = default);
    Task<bool> IsConnectedAsync(string accountKey = "default", CancellationToken cancellationToken = default);
}
