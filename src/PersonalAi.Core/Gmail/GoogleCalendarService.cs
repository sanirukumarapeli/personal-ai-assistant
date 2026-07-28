using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace PersonalAi.Core.Gmail;

public sealed record CalendarEventSnippet(
    string Id,
    string Summary,
    string? Description,
    string? Location,
    string Start,
    string End,
    string? HtmlLink,
    string? Status,
    IReadOnlyList<string> Attendees,
    IReadOnlyList<int> ReminderMinutes);

public sealed record BusySlot(string Start, string End);

public sealed record CalendarAvailability(
    string TimeMin,
    string TimeMax,
    bool IsFree,
    IReadOnlyList<BusySlot> BusySlots);

public interface IGoogleCalendarService
{
    Task<IReadOnlyList<CalendarEventSnippet>> ListEventsAsync(
        DateTimeOffset timeMin,
        DateTimeOffset timeMax,
        int max = 20,
        string? query = null,
        CancellationToken cancellationToken = default);

    Task<CalendarEventSnippet?> GetEventAsync(
        string eventId,
        CancellationToken cancellationToken = default);

    Task<CalendarEventSnippet> CreateEventAsync(
        string summary,
        DateTimeOffset start,
        DateTimeOffset end,
        string? description = null,
        string? location = null,
        IReadOnlyList<string>? attendeeEmails = null,
        IReadOnlyList<int>? reminderMinutes = null,
        CancellationToken cancellationToken = default);

    Task<CalendarEventSnippet> UpdateEventAsync(
        string eventId,
        string? summary = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string? description = null,
        string? location = null,
        IReadOnlyList<string>? attendeeEmails = null,
        IReadOnlyList<int>? reminderMinutes = null,
        CancellationToken cancellationToken = default);

    Task CancelEventAsync(
        string eventId,
        CancellationToken cancellationToken = default);

    Task<CalendarAvailability> CheckAvailabilityAsync(
        DateTimeOffset timeMin,
        DateTimeOffset timeMax,
        CancellationToken cancellationToken = default);
}

public sealed class GoogleCalendarService : IGoogleCalendarService
{
    private readonly IGoogleAuthService _auth;

    public GoogleCalendarService(IGoogleAuthService auth)
    {
        _auth = auth;
    }

    public async Task<IReadOnlyList<CalendarEventSnippet>> ListEventsAsync(
        DateTimeOffset timeMin,
        DateTimeOffset timeMax,
        int max = 20,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await _auth.CreateCalendarServiceAsync(cancellationToken).ConfigureAwait(false);
            var request = service.Events.List("primary");
            request.TimeMinDateTimeOffset = timeMin;
            request.TimeMaxDateTimeOffset = timeMax;
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            request.MaxResults = Math.Clamp(max, 1, 50);
            if (!string.IsNullOrWhiteSpace(query))
                request.Q = query.Trim();

            var result = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (result.Items is null || result.Items.Count == 0)
                return [];

            return result.Items.Select(ToSnippet).ToList();
        }
        catch (Exception ex)
        {
            throw WrapCalendarError(ex);
        }
    }

    public async Task<CalendarEventSnippet?> GetEventAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("Event id is required.", nameof(eventId));

        try
        {
            var service = await _auth.CreateCalendarServiceAsync(cancellationToken).ConfigureAwait(false);
            var evt = await service.Events.Get("primary", eventId.Trim())
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            return evt is null ? null : ToSnippet(evt);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw WrapCalendarError(ex);
        }
    }

    public async Task<CalendarEventSnippet> CreateEventAsync(
        string summary,
        DateTimeOffset start,
        DateTimeOffset end,
        string? description = null,
        string? location = null,
        IReadOnlyList<string>? attendeeEmails = null,
        IReadOnlyList<int>? reminderMinutes = null,
        CancellationToken cancellationToken = default)
    {
        ValidateCreate(summary, start, end);

        try
        {
            var service = await _auth.CreateCalendarServiceAsync(cancellationToken).ConfigureAwait(false);
            var evt = BuildEvent(summary, start, end, description, location, attendeeEmails, reminderMinutes);
            var created = await service.Events.Insert(evt, "primary")
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            return ToSnippet(created);
        }
        catch (Exception ex)
        {
            throw WrapCalendarError(ex);
        }
    }

    public async Task<CalendarEventSnippet> UpdateEventAsync(
        string eventId,
        string? summary = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string? description = null,
        string? location = null,
        IReadOnlyList<string>? attendeeEmails = null,
        IReadOnlyList<int>? reminderMinutes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("Event id is required.", nameof(eventId));

        try
        {
            var service = await _auth.CreateCalendarServiceAsync(cancellationToken).ConfigureAwait(false);
            var existing = await service.Events.Get("primary", eventId.Trim())
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Event not found.");

            if (summary is not null)
                existing.Summary = summary.Trim();
            if (description is not null)
                existing.Description = description;
            if (location is not null)
                existing.Location = location;

            var timeZone = GetIanaTimeZoneId();
            if (start is not null)
            {
                existing.Start = new EventDateTime
                {
                    DateTimeDateTimeOffset = start,
                    TimeZone = timeZone,
                    Date = null
                };
            }

            if (end is not null)
            {
                existing.End = new EventDateTime
                {
                    DateTimeDateTimeOffset = end,
                    TimeZone = timeZone,
                    Date = null
                };
            }

            if (attendeeEmails is not null)
            {
                existing.Attendees = attendeeEmails
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(e => new EventAttendee { Email = e.Trim() })
                    .ToList();
            }

            if (reminderMinutes is not null)
                existing.Reminders = BuildReminders(reminderMinutes);

            var resolvedStart = existing.Start?.DateTimeDateTimeOffset;
            var resolvedEnd = existing.End?.DateTimeDateTimeOffset;
            if (resolvedStart is not null && resolvedEnd is not null && resolvedEnd <= resolvedStart)
                throw new ArgumentException("End must be after start.");

            var updated = await service.Events.Update(existing, "primary", eventId.Trim())
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            return ToSnippet(updated);
        }
        catch (Exception ex)
        {
            throw WrapCalendarError(ex);
        }
    }

    public async Task CancelEventAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("Event id is required.", nameof(eventId));

        try
        {
            var service = await _auth.CreateCalendarServiceAsync(cancellationToken).ConfigureAwait(false);
            await service.Events.Delete("primary", eventId.Trim())
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw WrapCalendarError(ex);
        }
    }

    public async Task<CalendarAvailability> CheckAvailabilityAsync(
        DateTimeOffset timeMin,
        DateTimeOffset timeMax,
        CancellationToken cancellationToken = default)
    {
        if (timeMax <= timeMin)
            throw new ArgumentException("time_max must be after time_min.");

        try
        {
            var service = await _auth.CreateCalendarServiceAsync(cancellationToken).ConfigureAwait(false);
            var requestBody = new FreeBusyRequest
            {
                TimeMinDateTimeOffset = timeMin,
                TimeMaxDateTimeOffset = timeMax,
                Items = [new FreeBusyRequestItem { Id = "primary" }]
            };

            var result = await service.Freebusy.Query(requestBody)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            var busy = new List<BusySlot>();
            if (result.Calendars is not null
                && result.Calendars.TryGetValue("primary", out var cal)
                && cal.Busy is not null)
            {
                foreach (var slot in cal.Busy)
                {
                    var start = slot.StartDateTimeOffset?.ToString("O") ?? string.Empty;
                    var end = slot.EndDateTimeOffset?.ToString("O") ?? string.Empty;
                    if (!string.IsNullOrEmpty(start) || !string.IsNullOrEmpty(end))
                        busy.Add(new BusySlot(start, end));
                }
            }

            return new CalendarAvailability(
                timeMin.ToString("O"),
                timeMax.ToString("O"),
                busy.Count == 0,
                busy);
        }
        catch (Exception ex)
        {
            throw WrapCalendarError(ex);
        }
    }

    private static void ValidateCreate(string summary, DateTimeOffset start, DateTimeOffset end)
    {
        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("Event title is required.", nameof(summary));
        if (end <= start)
            throw new ArgumentException("End must be after start.");
    }

    private static Event BuildEvent(
        string summary,
        DateTimeOffset start,
        DateTimeOffset end,
        string? description,
        string? location,
        IReadOnlyList<string>? attendeeEmails,
        IReadOnlyList<int>? reminderMinutes)
    {
        var timeZone = GetIanaTimeZoneId();
        var evt = new Event
        {
            Summary = summary.Trim(),
            Description = description,
            Location = location,
            Start = new EventDateTime
            {
                DateTimeDateTimeOffset = start,
                TimeZone = timeZone
            },
            End = new EventDateTime
            {
                DateTimeDateTimeOffset = end,
                TimeZone = timeZone
            }
        };

        if (attendeeEmails is { Count: > 0 })
        {
            evt.Attendees = attendeeEmails
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => new EventAttendee { Email = e.Trim() })
                .ToList();
        }

        if (reminderMinutes is not null)
            evt.Reminders = BuildReminders(reminderMinutes);

        return evt;
    }

    private static Event.RemindersData BuildReminders(IReadOnlyList<int> reminderMinutes)
    {
        var overrides = reminderMinutes
            .Select(m => Math.Clamp(m, 0, 40320))
            .Distinct()
            .OrderBy(m => m)
            .Take(5)
            .Select(m => new EventReminder { Method = "popup", Minutes = m })
            .ToList();

        return new Event.RemindersData
        {
            UseDefault = false,
            Overrides = overrides
        };
    }

    private static string GetIanaTimeZoneId()
    {
        var localId = TimeZoneInfo.Local.Id;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(localId, out var iana) && !string.IsNullOrWhiteSpace(iana))
            return iana;
        return localId;
    }

    private static Exception WrapCalendarError(Exception ex)
    {
        if (ex is GoogleApiException googleEx)
        {
            var msg = googleEx.Error?.Message ?? googleEx.Message ?? string.Empty;
            if (googleEx.HttpStatusCode is System.Net.HttpStatusCode.Forbidden
                    or System.Net.HttpStatusCode.Unauthorized
                || msg.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("ACCESS_TOKEN_SCOPE", StringComparison.OrdinalIgnoreCase))
            {
                return new InvalidOperationException(
                    "Google Calendar permission is missing on this login. Click Disconnect, then Connect Google again, and allow Calendar. Also confirm Calendar API + scopes in docs/YOU_CALENDAR_SETUP.md.",
                    ex);
            }
        }

        return ex is InvalidOperationException ? ex : new InvalidOperationException(ex.Message, ex);
    }

    private static CalendarEventSnippet ToSnippet(Event evt)
    {
        var start = evt.Start?.DateTimeDateTimeOffset?.ToString("O")
                    ?? evt.Start?.Date
                    ?? string.Empty;
        var end = evt.End?.DateTimeDateTimeOffset?.ToString("O")
                  ?? evt.End?.Date
                  ?? string.Empty;

        var attendees = evt.Attendees?
            .Select(a => a.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!)
            .ToList()
            ?? [];

        var reminders = evt.Reminders?.Overrides?
            .Where(r => r.Minutes is not null)
            .Select(r => r.Minutes!.Value)
            .Distinct()
            .OrderBy(m => m)
            .ToList()
            ?? [];

        return new CalendarEventSnippet(
            evt.Id ?? string.Empty,
            evt.Summary ?? "(no title)",
            evt.Description,
            evt.Location,
            start,
            end,
            evt.HtmlLink,
            evt.Status,
            attendees,
            reminders);
    }
}
