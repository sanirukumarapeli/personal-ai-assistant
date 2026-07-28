using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using PersonalAi.Core.Gmail;
using PersonalAi.Core.Options;

namespace PersonalAi.Core.Chat;

public interface IChatService
{
    Task<ChatResult> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default);
}

public sealed class ChatService : IChatService
{
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(45);

    public const string SystemPrompt =
        "You are a helpful personal AI assistant for one user. Be concise, clear, and practical. " +
        "Gmail tools: list unread, search mail, list drafts, create drafts, send a draft. " +
        "Calendar tools: list/search/get events, check availability, propose create/update/cancel, then confirm or discard. " +
        "Understand natural language. Never invent special command phrases the user must type. " +
        "When the user asks if they are free 'this week' / 'this weak' (typo), treat it as the current calendar week " +
        "(local Monday 00:00 through Sunday 23:59:59) and call check_availability; also briefly list busy blocks if any. " +
        "If they say 'yep', 'yes', 'this week', or similar after you asked a clarifying question, pick the most reasonable default and act — " +
        "do not keep asking them to choose from a menu of keywords. " +
        "Only ask a short clarifying question when a request is truly ambiguous and you cannot choose a sensible default. " +
        "Reminders: when the user says 'remind me …', create a short calendar event at that time (default 15 minutes long) " +
        "with reminder_minutes including 0 (notify at start). If they ask for '10 minutes before' / '30 min before', include those minutes too. " +
        "Still use propose_create_event then confirm — never write without confirmation. " +
        "NEVER send email unless the user clearly confirms. When creating a draft, show details and ask before send_draft. " +
        "NEVER create, update, or cancel a calendar event until the user clearly confirms. " +
        "Workflow for calendar writes: resolve natural-language dates to ISO-8601 in the user's local timezone; " +
        "use list/search/get/check_availability as needed; call propose_*; show the proposal; after the user confirms " +
        "(e.g. 'yes', 'confirm', 'do it'), call confirm_calendar_action with user_confirmed=true. " +
        "If they change their mind, call discard_calendar_action. " +
        "For update/cancel, find the event first and use its event id. " +
        "If Google is not connected, tell them to open http://localhost:5080/auth/google. " +
        "Do not invent email or calendar contents — only use tool results.";

    private readonly ChatClient _chatClient;
    private readonly ISessionStore _sessionStore;
    private readonly IGmailMailService _gmail;
    private readonly IGoogleCalendarService _calendar;
    private readonly IGoogleAuthService _googleAuth;
    private readonly IPendingMailActions _pendingMail;
    private readonly IPendingCalendarActions _pendingCalendar;
    private readonly string _modelId;
    private readonly string _apiKey;

    public ChatService(
        IOptions<GeminiOptions> options,
        ISessionStore sessionStore,
        IGmailMailService gmail,
        IGoogleCalendarService calendar,
        IGoogleAuthService googleAuth,
        IPendingMailActions pendingMail,
        IPendingCalendarActions pendingCalendar)
    {
        var opts = options.Value;
        _apiKey = opts.ApiKey?.Trim() ?? string.Empty;
        _modelId = string.IsNullOrWhiteSpace(opts.ModelId) ? "gemini-3.5-flash-lite" : opts.ModelId;
        var endpoint = string.IsNullOrWhiteSpace(opts.Endpoint)
            ? "https://generativelanguage.googleapis.com/v1beta/openai/"
            : opts.Endpoint.TrimEnd('/') + "/";

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = NetworkTimeout,
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        };
        var openAi = new OpenAIClient(
            new ApiKeyCredential(string.IsNullOrEmpty(_apiKey) ? "missing" : _apiKey),
            clientOptions);
        _chatClient = openAi.GetChatClient(_modelId);
        _sessionStore = sessionStore;
        _gmail = gmail;
        _calendar = calendar;
        _googleAuth = googleAuth;
        _pendingMail = pendingMail;
        _pendingCalendar = pendingCalendar;
    }

    public async Task<ChatResult> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException(
                "GEMINI_API_KEY is missing. Create a key in Google AI Studio and set it in .env. See docs/YOU_STEP_0.md.");
        }

        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("Message is required.", nameof(request));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(OverallTimeout);
        var ct = timeoutCts.Token;

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString("N")
            : request.SessionId.Trim();

        var history = await _sessionStore.GetHistoryAsync(sessionId, ct).ConfigureAwait(false);

        var nowLocal = DateTimeOffset.Now;
        var (googleConnected, googleEmail) = await _googleAuth.GetStatusAsync(ct).ConfigureAwait(false);
        var googleStatus = googleConnected
            ? $"Google is connected as {googleEmail ?? "unknown"}. Use Gmail and Calendar tools when needed; do not ask the user to connect unless a tool returns a permission/reconnect error."
            : "Google is NOT connected. For mail/calendar, tell the user to click Connect Google in the UI (or open http://localhost:5080/auth/google).";

        var pendingNote = _pendingCalendar.TryGet(sessionId, out var pending)
            ? $" There is a pending calendar {pending.Kind.ToString().ToLowerInvariant()} awaiting confirmation: {pending.SummaryForUser}."
            : string.Empty;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                SystemPrompt +
                $" Current local datetime: {nowLocal:O}. Timezone: {TimeZoneInfo.Local.Id}. {googleStatus}{pendingNote}")
        };

        foreach (var turn in history)
        {
            if (string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                messages.Add(new AssistantChatMessage(turn.Content));
            else
                messages.Add(new UserChatMessage(turn.Content));
        }

        messages.Add(new UserChatMessage(request.Message.Trim()));

        var tools = CreateTools();
        var options = new ChatCompletionOptions();
        foreach (var tool in tools)
            options.Tools.Add(tool);

        string reply;
        try
        {
            reply = await CompleteWithToolsAsync(messages, options, sessionId, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "Gemini took too long (likely rate-limited or overloaded). Wait a minute and try again.");
        }
        catch (ClientResultException ex) when (ex.Status is 401 or 403)
        {
            throw new InvalidOperationException(
                "Gemini rejected the API key (401/403). Check GEMINI_API_KEY in .env. See docs/YOU_STEP_0.md.",
                ex);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            throw new InvalidOperationException(FormatGemini429Message(ex), ex);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(FormatGemini404Message(_modelId, ex), ex);
        }
        catch (ClientResultException ex)
        {
            var detail = ex.Message;
            throw new InvalidOperationException(
                $"Gemini request failed ({ex.Status}): {detail}",
                ex);
        }

        if (string.IsNullOrWhiteSpace(reply))
            reply = "(No text returned from the model.)";

        await _sessionStore.AppendAsync(
                sessionId,
                new ChatTurn("user", request.Message.Trim()),
                new ChatTurn("assistant", reply),
                ct)
            .ConfigureAwait(false);

        return new ChatResult(sessionId, reply);
    }

    private static string FormatGemini404Message(string modelId, ClientResultException ex)
    {
        var raw = (ex.Message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        var lower = raw.ToLowerInvariant();
        var retiredForNewUsers = lower.Contains("no longer available to new users", StringComparison.Ordinal);

        // Rate limits UI can still list retired models; chat completions reject them with 404.
        if (retiredForNewUsers)
        {
            return
                $"Gemini model '{modelId}' is not available to new API keys (404). " +
                "Set GEMINI_MODEL_ID in .env to a current free-tier model such as gemini-3.5-flash-lite " +
                "(or gemini-3.6-flash if you need more quality), then restart the API. " +
                "See docs/YOU_STEP_0.md and https://ai.google.dev/gemini-api/docs/models";
        }

        var snippet = raw.Length > 180 ? raw[..180] + "…" : raw;
        return string.IsNullOrWhiteSpace(snippet)
            ? $"Gemini model '{modelId}' was not found (404). Try GEMINI_MODEL_ID=gemini-3.5-flash-lite. See docs/YOU_STEP_0.md."
            : $"Gemini model '{modelId}' was not found (404): {snippet} Try gemini-3.5-flash-lite. See docs/YOU_STEP_0.md.";
    }

    private static string FormatGemini429Message(ClientResultException ex)
    {
        var raw = ex.Message ?? string.Empty;
        var lower = raw.ToLowerInvariant();
        var zeroQuota =
            lower.Contains("limit: 0", StringComparison.Ordinal)
            || lower.Contains("limit\": 0", StringComparison.Ordinal)
            || (lower.Contains("quota exceeded", StringComparison.Ordinal)
                && lower.Contains("free_tier", StringComparison.Ordinal));

        if (zeroQuota || lower.Contains("resource_exhausted", StringComparison.Ordinal))
        {
            return
                "Gemini quota denied (429). This can happen on the first message when the free-tier limit for your " +
                "API key/project is 0 (billing/quota not enabled), not because you sent many messages. " +
                "Check AI Studio rate limits / enable billing, or create a new AI Studio key (AIza…). " +
                "See docs/YOU_STEP_0.md and https://ai.google.dev/gemini-api/docs/rate-limits";
        }

        // Keep a short Google detail when present, without dumping a huge payload.
        var snippet = raw.Replace("\r", " ").Replace("\n", " ").Trim();
        if (snippet.Length > 220)
            snippet = snippet[..220] + "…";

        // Free-tier RPM is often the bottleneck (tool rounds = multiple API calls per user message).
        return string.IsNullOrWhiteSpace(snippet)
            ? "Gemini rate limit / quota hit (429). Prefer GEMINI_MODEL_ID=gemini-3.5-flash-lite for higher free-tier RPM, wait ~1 min, or enable billing. See docs/YOU_STEP_0.md."
            : $"Gemini rate limit / quota hit (429): {snippet} Prefer gemini-3.5-flash-lite / wait ~1 min / check AI Studio Rate limits. See docs/YOU_STEP_0.md.";
    }

    private async Task<string> CompleteWithToolsAsync(
        List<ChatMessage> messages,
        ChatCompletionOptions options,
        string sessionId,
        CancellationToken cancellationToken)
    {
        const int maxRounds = 8;
        for (var round = 0; round < maxRounds; round++)
        {
            ChatCompletion completion = await _chatClient
                .CompleteChatAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);

            // Gemini sometimes returns tool calls without FinishReason == ToolCalls.
            if (completion.ToolCalls is { Count: > 0 })
            {
                messages.Add(new AssistantChatMessage(completion.ToolCalls));

                foreach (var call in completion.ToolCalls)
                {
                    var result = await ExecuteToolAsync(call, sessionId, cancellationToken).ConfigureAwait(false);
                    messages.Add(new ToolChatMessage(call.Id, result));
                }

                continue;
            }

            if (completion.Content is null || completion.Content.Count == 0)
                return "(No text returned from the model.)";

            return string.Concat(completion.Content.Select(part => part.Text)).Trim();
        }

        return "I hit the tool-call limit while working on that. Please try a simpler request.";
    }

    private static List<ChatTool> CreateTools() =>
    [
        ChatTool.CreateFunctionTool(
            "list_unread_emails",
            "List unread emails in the inbox.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "max": { "type": "integer", "description": "Max messages to return (1-25)", "default": 10 }
                  }
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "search_emails",
            "Search Gmail with a query (topic, from:, subject:, newer_than:, etc.).",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "query": { "type": "string", "description": "Gmail search query" },
                    "max": { "type": "integer", "default": 10 }
                  },
                  "required": ["query"]
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "list_drafts",
            "List existing Gmail drafts.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "max": { "type": "integer", "default": 10 }
                  }
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "create_draft",
            "Create a Gmail draft. Do not send. Ask the user to confirm before calling send_draft.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "to": { "type": "string" },
                    "subject": { "type": "string" },
                    "body": { "type": "string" }
                  },
                  "required": ["to", "subject", "body"]
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "send_draft",
            "Send a previously created Gmail draft. Only call after the user explicitly confirms sending.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "draft_id": { "type": "string", "description": "Draft id to send. Omit to use the latest pending draft for this chat." },
                    "user_confirmed": { "type": "boolean", "description": "Must be true; user explicitly confirmed send." }
                  },
                  "required": ["user_confirmed"]
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "list_events",
            "List Google Calendar events on the primary calendar between time_min and time_max (ISO-8601).",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "time_min": { "type": "string" },
                    "time_max": { "type": "string" },
                    "query": { "type": "string", "description": "Optional free-text filter (title/description)" },
                    "max": { "type": "integer", "default": 20 }
                  },
                  "required": ["time_min", "time_max"]
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "get_event",
            "Get one calendar event by id.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "event_id": { "type": "string" }
                  },
                  "required": ["event_id"]
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "search_events",
            "Search calendar events by text within a time range.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "query": { "type": "string" },
                    "time_min": { "type": "string" },
                    "time_max": { "type": "string" },
                    "max": { "type": "integer", "default": 20 }
                  },
                  "required": ["query", "time_min", "time_max"]
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "check_availability",
            "Check whether the primary calendar is free or busy between time_min and time_max (ISO-8601). For 'this week', use local Monday 00:00 through Sunday end-of-day.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "time_min": { "type": "string" },
                    "time_max": { "type": "string" }
                  },
                  "required": ["time_min", "time_max"]
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "propose_create_event",
            "Stage a new calendar event or reminder for user confirmation. For 'remind me at …', use a short event and set reminder_minutes (include 0 for at-start). Does not write until confirm_calendar_action.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "summary": { "type": "string" },
                    "start": { "type": "string" },
                    "end": { "type": "string" },
                    "description": { "type": "string" },
                    "location": { "type": "string" },
                    "attendees": { "type": "array", "items": { "type": "string" } },
                    "reminder_minutes": {
                      "type": "array",
                      "items": { "type": "integer" },
                      "description": "Popup reminders N minutes before start. Use [0] for remind-at-time; e.g. [0,10,30]."
                    }
                  },
                  "required": ["summary", "start", "end"]
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "propose_update_event",
            "Stage an update to an existing event. Pass only fields to change. Does not write until confirm_calendar_action.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "event_id": { "type": "string" },
                    "summary": { "type": "string" },
                    "start": { "type": "string" },
                    "end": { "type": "string" },
                    "description": { "type": "string" },
                    "location": { "type": "string" },
                    "attendees": { "type": "array", "items": { "type": "string" } },
                    "reminder_minutes": {
                      "type": "array",
                      "items": { "type": "integer" },
                      "description": "Replace popup reminders with these minute offsets before start."
                    }
                  },
                  "required": ["event_id"]
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "propose_cancel_event",
            "Stage cancellation/deletion of an event. Does not delete until confirm_calendar_action.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "event_id": { "type": "string" }
                  },
                  "required": ["event_id"]
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "confirm_calendar_action",
            "Execute the pending calendar create/update/cancel after the user explicitly confirms.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {
                    "user_confirmed": { "type": "boolean" }
                  },
                  "required": ["user_confirmed"]
                }
                """u8.ToArray())),
        ChatTool.CreateFunctionTool(
            "discard_calendar_action",
            "Discard the pending calendar create/update/cancel without applying it.",
            BinaryData.FromBytes("""
                {
                  "type": "object",
                  "properties": {}
                }
                """u8.ToArray()))
    ];

    private async Task<string> ExecuteToolAsync(
        ChatToolCall call,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var rawArgs = call.FunctionArguments?.ToString() ?? "{}";
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawArgs) ? "{}" : rawArgs);
            var root = doc.RootElement;

            return call.FunctionName switch
            {
                "list_unread_emails" => await ListUnreadAsync(root, cancellationToken).ConfigureAwait(false),
                "search_emails" => await SearchAsync(root, cancellationToken).ConfigureAwait(false),
                "list_drafts" => await ListDraftsAsync(root, cancellationToken).ConfigureAwait(false),
                "create_draft" => await CreateDraftAsync(root, sessionId, cancellationToken).ConfigureAwait(false),
                "send_draft" => await SendDraftAsync(root, sessionId, cancellationToken).ConfigureAwait(false),
                "list_events" => await ListEventsAsync(root, cancellationToken).ConfigureAwait(false),
                "get_event" => await GetEventAsync(root, cancellationToken).ConfigureAwait(false),
                "search_events" => await SearchEventsAsync(root, cancellationToken).ConfigureAwait(false),
                "check_availability" => await CheckAvailabilityAsync(root, cancellationToken).ConfigureAwait(false),
                "propose_create_event" => ProposeCreateEvent(root, sessionId),
                "propose_update_event" => await ProposeUpdateEventAsync(root, sessionId, cancellationToken).ConfigureAwait(false),
                "propose_cancel_event" => await ProposeCancelEventAsync(root, sessionId, cancellationToken).ConfigureAwait(false),
                "confirm_calendar_action" => await ConfirmCalendarActionAsync(root, sessionId, cancellationToken).ConfigureAwait(false),
                "discard_calendar_action" => DiscardCalendarAction(sessionId),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {call.FunctionName}" })
            };
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private async Task<string> ListUnreadAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var max = root.TryGetProperty("max", out var m) && m.TryGetInt32(out var n) ? n : 10;
        var items = await _gmail.ListUnreadAsync(max, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { count = items.Count, emails = items });
    }

    private async Task<string> SearchAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var query = root.GetProperty("query").GetString() ?? string.Empty;
        var max = root.TryGetProperty("max", out var m) && m.TryGetInt32(out var n) ? n : 10;
        var items = await _gmail.SearchAsync(query, max, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { query, count = items.Count, emails = items });
    }

    private async Task<string> ListDraftsAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var max = root.TryGetProperty("max", out var m) && m.TryGetInt32(out var n) ? n : 10;
        var items = await _gmail.ListDraftsAsync(max, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { count = items.Count, drafts = items });
    }

    private async Task<string> CreateDraftAsync(JsonElement root, string sessionId, CancellationToken cancellationToken)
    {
        var to = root.GetProperty("to").GetString() ?? string.Empty;
        var subject = root.GetProperty("subject").GetString() ?? string.Empty;
        var body = root.GetProperty("body").GetString() ?? string.Empty;
        var draft = await _gmail.CreateDraftAsync(to, subject, body, cancellationToken).ConfigureAwait(false);
        var summary = $"To: {draft.To}; Subject: {draft.Subject}";
        _pendingMail.SetPendingDraft(sessionId, draft.DraftId, summary);
        return JsonSerializer.Serialize(new
        {
            created = true,
            draft,
            note = "Draft saved. Ask the user to confirm before calling send_draft."
        });
    }

    private async Task<string> SendDraftAsync(JsonElement root, string sessionId, CancellationToken cancellationToken)
    {
        var confirmed = root.TryGetProperty("user_confirmed", out var c) && c.ValueKind == JsonValueKind.True;
        if (!confirmed)
        {
            return JsonSerializer.Serialize(new
            {
                error = "Refused: user_confirmed must be true. Ask the user to confirm sending first."
            });
        }

        var draftId = root.TryGetProperty("draft_id", out var d) ? d.GetString() : null;
        if (string.IsNullOrWhiteSpace(draftId))
        {
            if (!_pendingMail.TryGetPendingDraft(sessionId, out draftId, out _))
            {
                return JsonSerializer.Serialize(new { error = "No pending draft id. Create a draft first or pass draft_id." });
            }
        }

        var messageId = await _gmail.SendDraftAsync(draftId!, cancellationToken).ConfigureAwait(false);
        _pendingMail.Clear(sessionId);
        return JsonSerializer.Serialize(new { sent = true, messageId, draftId });
    }

    private async Task<string> ListEventsAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var timeMin = ParseDateTimeOffset(root.GetProperty("time_min").GetString());
        var timeMax = ParseDateTimeOffset(root.GetProperty("time_max").GetString());
        var max = root.TryGetProperty("max", out var m) && m.TryGetInt32(out var n) ? n : 20;
        var query = root.TryGetProperty("query", out var q) ? q.GetString() : null;
        var items = await _calendar.ListEventsAsync(timeMin, timeMax, max, query, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            count = items.Count,
            timeMin = timeMin.ToString("O"),
            timeMax = timeMax.ToString("O"),
            events = items
        });
    }

    private async Task<string> GetEventAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var eventId = root.GetProperty("event_id").GetString() ?? string.Empty;
        var evt = await _calendar.GetEventAsync(eventId, cancellationToken).ConfigureAwait(false);
        if (evt is null)
            return JsonSerializer.Serialize(new { found = false, eventId });
        return JsonSerializer.Serialize(new { found = true, @event = evt });
    }

    private async Task<string> SearchEventsAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var query = root.GetProperty("query").GetString() ?? string.Empty;
        var timeMin = ParseDateTimeOffset(root.GetProperty("time_min").GetString());
        var timeMax = ParseDateTimeOffset(root.GetProperty("time_max").GetString());
        var max = root.TryGetProperty("max", out var m) && m.TryGetInt32(out var n) ? n : 20;
        var items = await _calendar.ListEventsAsync(timeMin, timeMax, max, query, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { query, count = items.Count, events = items });
    }

    private async Task<string> CheckAvailabilityAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var timeMin = ParseDateTimeOffset(root.GetProperty("time_min").GetString());
        var timeMax = ParseDateTimeOffset(root.GetProperty("time_max").GetString());
        var availability = await _calendar.CheckAvailabilityAsync(timeMin, timeMax, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(availability);
    }

    private string ProposeCreateEvent(JsonElement root, string sessionId)
    {
        var summary = root.GetProperty("summary").GetString() ?? string.Empty;
        var start = ParseDateTimeOffset(root.GetProperty("start").GetString());
        var end = ParseDateTimeOffset(root.GetProperty("end").GetString());
        if (end <= start)
            return JsonSerializer.Serialize(new { error = "End must be after start." });

        var description = OptionalString(root, "description");
        var location = OptionalString(root, "location");
        var attendees = OptionalAttendees(root);
        var reminders = OptionalReminderMinutes(root) ?? [0];

        var reminderText = reminders.Count == 0
            ? "no popup reminders"
            : $"reminders at {string.Join(", ", reminders.Select(m => m == 0 ? "start" : $"{m} min before"))}";

        var forUser =
            $"CREATE “{summary}” from {start:ddd MMM d, h:mm tt} to {end:h:mm tt}" +
            (string.IsNullOrWhiteSpace(location) ? "" : $", at {location}") +
            (attendees is { Count: > 0 } ? $", with {string.Join(", ", attendees)}" : "") +
            $", {reminderText}";

        _pendingCalendar.Set(sessionId, new PendingCalendarAction(
            PendingCalendarKind.Create,
            forUser,
            Title: summary.Trim(),
            Start: start,
            End: end,
            Description: description,
            Location: location,
            Attendees: attendees,
            ReminderMinutes: reminders,
            SetTitle: true,
            SetStart: true,
            SetEnd: true,
            SetDescription: description is not null,
            SetLocation: location is not null,
            SetAttendees: attendees is not null,
            SetReminders: true));

        return JsonSerializer.Serialize(new
        {
            staged = true,
            kind = "create",
            proposal = forUser,
            reminder_minutes = reminders,
            note = "Ask the user to confirm, then call confirm_calendar_action with user_confirmed=true."
        });
    }

    private async Task<string> ProposeUpdateEventAsync(
        JsonElement root,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var eventId = root.GetProperty("event_id").GetString() ?? string.Empty;
        var existing = await _calendar.GetEventAsync(eventId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return JsonSerializer.Serialize(new { error = $"Event not found: {eventId}" });

        var setTitle = root.TryGetProperty("summary", out _);
        var setStart = root.TryGetProperty("start", out _);
        var setEnd = root.TryGetProperty("end", out _);
        var setDescription = root.TryGetProperty("description", out _);
        var setLocation = root.TryGetProperty("location", out _);
        var setAttendees = root.TryGetProperty("attendees", out _);
        var setReminders = root.TryGetProperty("reminder_minutes", out _);

        if (!setTitle && !setStart && !setEnd && !setDescription && !setLocation && !setAttendees && !setReminders)
            return JsonSerializer.Serialize(new { error = "Provide at least one field to update." });

        var title = setTitle ? root.GetProperty("summary").GetString() : existing.Summary;
        var start = setStart ? ParseDateTimeOffset(root.GetProperty("start").GetString()) : ParseDateTimeOffset(existing.Start);
        var end = setEnd ? ParseDateTimeOffset(root.GetProperty("end").GetString()) : ParseDateTimeOffset(existing.End);
        if (end <= start)
            return JsonSerializer.Serialize(new { error = "End must be after start." });

        var description = setDescription ? OptionalString(root, "description") : existing.Description;
        var location = setLocation ? OptionalString(root, "location") : existing.Location;
        var attendees = setAttendees ? OptionalAttendees(root) : existing.Attendees;
        var reminders = setReminders ? OptionalReminderMinutes(root) ?? [] : existing.ReminderMinutes;

        var reminderText = reminders.Count == 0
            ? "no popup reminders"
            : $"reminders at {string.Join(", ", reminders.Select(m => m == 0 ? "start" : $"{m} min before"))}";

        var forUser =
            $"UPDATE “{existing.Summary}” (id {eventId}) → “{title}” from {start:ddd MMM d, h:mm tt} to {end:h:mm tt}" +
            (string.IsNullOrWhiteSpace(location) ? "" : $", at {location}") +
            $", {reminderText}";

        _pendingCalendar.Set(sessionId, new PendingCalendarAction(
            PendingCalendarKind.Update,
            forUser,
            EventId: eventId,
            Title: title,
            Start: start,
            End: end,
            Description: description,
            Location: location,
            Attendees: attendees,
            ReminderMinutes: reminders,
            SetTitle: setTitle,
            SetStart: setStart,
            SetEnd: setEnd,
            SetDescription: setDescription,
            SetLocation: setLocation,
            SetAttendees: setAttendees,
            SetReminders: setReminders));

        return JsonSerializer.Serialize(new
        {
            staged = true,
            kind = "update",
            current = existing,
            proposal = forUser,
            note = "Ask the user to confirm, then call confirm_calendar_action with user_confirmed=true."
        });
    }

    private async Task<string> ProposeCancelEventAsync(
        JsonElement root,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var eventId = root.GetProperty("event_id").GetString() ?? string.Empty;
        var existing = await _calendar.GetEventAsync(eventId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return JsonSerializer.Serialize(new { error = $"Event not found: {eventId}" });

        var forUser = $"CANCEL “{existing.Summary}” ({existing.Start} → {existing.End}), id {eventId}";
        _pendingCalendar.Set(sessionId, new PendingCalendarAction(
            PendingCalendarKind.Cancel,
            forUser,
            EventId: eventId,
            Title: existing.Summary));

        return JsonSerializer.Serialize(new
        {
            staged = true,
            kind = "cancel",
            @event = existing,
            proposal = forUser,
            note = "Ask the user to confirm, then call confirm_calendar_action with user_confirmed=true."
        });
    }

    private async Task<string> ConfirmCalendarActionAsync(
        JsonElement root,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var confirmed = root.TryGetProperty("user_confirmed", out var c) && c.ValueKind == JsonValueKind.True;
        if (!confirmed)
        {
            return JsonSerializer.Serialize(new
            {
                error = "Refused: user_confirmed must be true. Ask the user to confirm first."
            });
        }

        if (!_pendingCalendar.TryGet(sessionId, out var pending))
            return JsonSerializer.Serialize(new { error = "No pending calendar action. Propose create/update/cancel first." });

        switch (pending.Kind)
        {
            case PendingCalendarKind.Create:
            {
                var created = await _calendar.CreateEventAsync(
                        pending.Title ?? "Untitled",
                        pending.Start!.Value,
                        pending.End!.Value,
                        pending.Description,
                        pending.Location,
                        pending.Attendees,
                        pending.ReminderMinutes,
                        cancellationToken)
                    .ConfigureAwait(false);
                _pendingCalendar.Clear(sessionId);
                return JsonSerializer.Serialize(new { applied = true, kind = "create", @event = created });
            }
            case PendingCalendarKind.Update:
            {
                var updated = await _calendar.UpdateEventAsync(
                        pending.EventId!,
                        summary: pending.SetTitle ? pending.Title : null,
                        start: pending.SetStart ? pending.Start : null,
                        end: pending.SetEnd ? pending.End : null,
                        description: pending.SetDescription ? pending.Description : null,
                        location: pending.SetLocation ? pending.Location : null,
                        attendeeEmails: pending.SetAttendees ? pending.Attendees : null,
                        reminderMinutes: pending.SetReminders ? pending.ReminderMinutes : null,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _pendingCalendar.Clear(sessionId);
                return JsonSerializer.Serialize(new { applied = true, kind = "update", @event = updated });
            }
            case PendingCalendarKind.Cancel:
            {
                await _calendar.CancelEventAsync(pending.EventId!, cancellationToken).ConfigureAwait(false);
                _pendingCalendar.Clear(sessionId);
                return JsonSerializer.Serialize(new
                {
                    applied = true,
                    kind = "cancel",
                    eventId = pending.EventId,
                    title = pending.Title
                });
            }
            default:
                return JsonSerializer.Serialize(new { error = $"Unknown pending kind: {pending.Kind}" });
        }
    }

    private string DiscardCalendarAction(string sessionId)
    {
        var had = _pendingCalendar.TryGet(sessionId, out var pending);
        _pendingCalendar.Clear(sessionId);
        return JsonSerializer.Serialize(new
        {
            discarded = had,
            previous = had ? pending.SummaryForUser : null
        });
    }

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) ? el.GetString() : null;

    private static IReadOnlyList<string>? OptionalAttendees(JsonElement root)
    {
        if (!root.TryGetProperty("attendees", out var att) || att.ValueKind != JsonValueKind.Array)
            return null;

        return att.EnumerateArray()
            .Select(e => e.GetString())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!)
            .ToList();
    }

    private static IReadOnlyList<int>? OptionalReminderMinutes(JsonElement root)
    {
        if (!root.TryGetProperty("reminder_minutes", out var rem) || rem.ValueKind != JsonValueKind.Array)
            return null;

        return rem.EnumerateArray()
            .Where(e => e.TryGetInt32(out _))
            .Select(e => Math.Clamp(e.GetInt32(), 0, 40320))
            .Distinct()
            .OrderBy(m => m)
            .Take(5)
            .ToList();
    }

    private static DateTimeOffset ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Datetime value is required.");

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var dto))
            return dto;

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var dt))
            return new DateTimeOffset(dt);

        throw new ArgumentException($"Could not parse datetime: {value}");
    }
}
