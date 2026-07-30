using System.Collections.Concurrent;

namespace PersonalAi.Core.Chat;

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, List<ChatTurn>> _sessions = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _updated = new();

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
        var now = DateTimeOffset.UtcNow;
        lock (list)
        {
            list.Add(user with { CreatedAtUtc = user.CreatedAtUtc ?? now });
            list.Add(assistant with { CreatedAtUtc = assistant.CreatedAtUtc ?? now });
        }

        _updated[sessionId] = now;
        return Task.CompletedTask;
    }

    public Task TruncateAfterAsync(string sessionId, int keepCount, CancellationToken cancellationToken = default)
    {
        if (keepCount < 0)
            throw new ArgumentOutOfRangeException(nameof(keepCount));

        var list = _sessions.GetOrAdd(sessionId, _ => []);
        lock (list)
        {
            if (keepCount >= list.Count)
                return Task.CompletedTask;
            list.RemoveRange(keepCount, list.Count - keepCount);
        }

        _updated[sessionId] = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var summaries = new List<SessionSummary>();
        foreach (var (id, list) in _sessions)
        {
            lock (list)
            {
                if (list.Count == 0)
                    continue;
                var preview = list[^1].Content;
                if (preview.Length > 80)
                    preview = preview[..80] + "…";
                var updated = _updated.TryGetValue(id, out var u) ? u : DateTimeOffset.UtcNow;
                summaries.Add(new SessionSummary(id, updated, preview));
            }
        }

        return Task.FromResult<IReadOnlyList<SessionSummary>>(
            summaries.OrderByDescending(s => s.UpdatedAtUtc).ToList());
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sessionId, out _);
        _updated.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
