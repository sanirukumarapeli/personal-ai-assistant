using System.Collections.Concurrent;

namespace PersonalAi.Core.Gmail;

public interface IPendingMailActions
{
    void SetPendingDraft(string sessionId, string draftId, string summary);
    bool TryGetPendingDraft(string sessionId, out string draftId, out string summary);
    void Clear(string sessionId);
}

public sealed class PendingMailActions : IPendingMailActions
{
    private readonly ConcurrentDictionary<string, (string DraftId, string Summary)> _pending = new();

    public void SetPendingDraft(string sessionId, string draftId, string summary) =>
        _pending[sessionId] = (draftId, summary);

    public bool TryGetPendingDraft(string sessionId, out string draftId, out string summary)
    {
        if (_pending.TryGetValue(sessionId, out var value))
        {
            draftId = value.DraftId;
            summary = value.Summary;
            return true;
        }

        draftId = string.Empty;
        summary = string.Empty;
        return false;
    }

    public void Clear(string sessionId) => _pending.TryRemove(sessionId, out _);
}
