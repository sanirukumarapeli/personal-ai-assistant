namespace PersonalAi.Core.Chat;

public interface ISessionStore
{
    Task<IReadOnlyList<ChatTurn>> GetHistoryAsync(string sessionId, CancellationToken cancellationToken = default);
    Task AppendAsync(string sessionId, ChatTurn user, ChatTurn assistant, CancellationToken cancellationToken = default);
}
