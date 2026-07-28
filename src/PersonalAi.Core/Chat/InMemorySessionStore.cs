using System.Collections.Concurrent;

namespace PersonalAi.Core.Chat;

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, List<ChatTurn>> _sessions = new();

    public Task<IReadOnlyList<ChatTurn>> GetHistoryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var list = _sessions.GetOrAdd(sessionId, _ => []);
        lock (list)
        {
            return Task.FromResult<IReadOnlyList<ChatTurn>>(list.ToList());
        }
    }

    public Task AppendAsync(string sessionId, ChatTurn user, ChatTurn assistant, CancellationToken cancellationToken = default)
    {
        var list = _sessions.GetOrAdd(sessionId, _ => []);
        lock (list)
        {
            list.Add(user);
            list.Add(assistant);
        }

        return Task.CompletedTask;
    }
}
