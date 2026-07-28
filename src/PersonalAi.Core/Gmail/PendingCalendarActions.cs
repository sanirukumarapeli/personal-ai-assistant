using System.Collections.Concurrent;

namespace PersonalAi.Core.Gmail;

public enum PendingCalendarKind
{
    Create,
    Update,
    Cancel
}

public sealed record PendingCalendarAction(
    PendingCalendarKind Kind,
    string SummaryForUser,
    string? EventId = null,
    string? Title = null,
    DateTimeOffset? Start = null,
    DateTimeOffset? End = null,
    string? Description = null,
    string? Location = null,
    IReadOnlyList<string>? Attendees = null,
    IReadOnlyList<int>? ReminderMinutes = null,
    bool SetTitle = false,
    bool SetStart = false,
    bool SetEnd = false,
    bool SetDescription = false,
    bool SetLocation = false,
    bool SetAttendees = false,
    bool SetReminders = false);

public interface IPendingCalendarActions
{
    void Set(string sessionId, PendingCalendarAction action);
    bool TryGet(string sessionId, out PendingCalendarAction action);
    void Clear(string sessionId);
}

public sealed class PendingCalendarActions : IPendingCalendarActions
{
    private readonly ConcurrentDictionary<string, PendingCalendarAction> _pending = new();

    public void Set(string sessionId, PendingCalendarAction action) =>
        _pending[sessionId] = action;

    public bool TryGet(string sessionId, out PendingCalendarAction action) =>
        _pending.TryGetValue(sessionId, out action!);

    public void Clear(string sessionId) =>
        _pending.TryRemove(sessionId, out _);
}
