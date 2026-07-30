namespace PersonalAi.Core.Chat;

public sealed record SessionSummary(string SessionId, DateTimeOffset UpdatedAtUtc, string Preview);

public interface ISessionStore
{
    Task<IReadOnlyList<ChatTurn>> GetHistoryAsync(string sessionId, CancellationToken cancellationToken = default);
    Task AppendAsync(string sessionId, ChatTurn user, ChatTurn assistant, CancellationToken cancellationToken = default);
    Task TruncateAfterAsync(string sessionId, int keepCount, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
