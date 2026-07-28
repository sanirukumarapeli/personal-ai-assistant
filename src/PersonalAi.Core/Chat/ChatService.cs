using System.ClientModel;
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
    public const string SystemPrompt =
        "You are a helpful personal AI assistant for one user. Be concise, clear, and practical. " +
        "You can use Gmail tools when available: list unread, search mail, list drafts, create drafts, and send a draft. " +
        "NEVER send email unless the user clearly confirms (e.g. 'yes, send it' or 'send that draft'). " +
        "When creating a draft, tell the user the draft details and ask them to confirm before sending. " +
        "If Gmail is not connected, tell them to open http://localhost:5080/auth/google. " +
        "Do not invent email contents — only use tool results.";

    private readonly ChatClient _chatClient;
    private readonly ISessionStore _sessionStore;
    private readonly IGmailMailService _gmail;
    private readonly IPendingMailActions _pending;
    private readonly string _modelId;
    private readonly string _token;

    public ChatService(
        IOptions<GitHubModelsOptions> options,
        ISessionStore sessionStore,
        IGmailMailService gmail,
        IPendingMailActions pending)
    {
        var opts = options.Value;
        _token = opts.Token?.Trim() ?? string.Empty;
        _modelId = string.IsNullOrWhiteSpace(opts.ModelId) ? "openai/gpt-5" : opts.ModelId;
        var endpoint = string.IsNullOrWhiteSpace(opts.Endpoint)
            ? "https://models.github.ai/inference"
            : opts.Endpoint.TrimEnd('/');

        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        var openAi = new OpenAIClient(
            new ApiKeyCredential(string.IsNullOrEmpty(_token) ? "missing" : _token),
            clientOptions);
        _chatClient = openAi.GetChatClient(_modelId);
        _sessionStore = sessionStore;
        _gmail = gmail;
        _pending = pending;
    }

    public async Task<ChatResult> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            throw new InvalidOperationException(
                "GITHUB_TOKEN is missing. Create a fine-grained PAT with models:read and set it in .env. See docs/YOU_STEP_0.md.");
        }

        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("Message is required.", nameof(request));

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString("N")
            : request.SessionId.Trim();

        var history = await _sessionStore.GetHistoryAsync(sessionId, cancellationToken).ConfigureAwait(false);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(SystemPrompt)
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
            reply = await CompleteWithToolsAsync(messages, options, sessionId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ClientResultException ex) when (ex.Status is 401 or 403)
        {
            throw new InvalidOperationException(
                "GitHub Models rejected the token (401/403). Check GITHUB_TOKEN has models:read. See docs/YOU_STEP_0.md.",
                ex);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            throw new InvalidOperationException(
                "GitHub Models rate limit hit. Wait a bit and try again.",
                ex);
        }

        if (string.IsNullOrWhiteSpace(reply))
            reply = "(No text returned from the model.)";

        await _sessionStore.AppendAsync(
                sessionId,
                new ChatTurn("user", request.Message.Trim()),
                new ChatTurn("assistant", reply),
                cancellationToken)
            .ConfigureAwait(false);

        return new ChatResult(sessionId, reply);
    }

    private async Task<string> CompleteWithToolsAsync(
        List<ChatMessage> messages,
        ChatCompletionOptions options,
        string sessionId,
        CancellationToken cancellationToken)
    {
        const int maxRounds = 6;
        for (var round = 0; round < maxRounds; round++)
        {
            ChatCompletion completion = await _chatClient
                .CompleteChatAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);

            if (completion.FinishReason == ChatFinishReason.ToolCalls && completion.ToolCalls.Count > 0)
            {
                messages.Add(new AssistantChatMessage(completion.ToolCalls));

                foreach (var call in completion.ToolCalls)
                {
                    var result = await ExecuteToolAsync(call, sessionId, cancellationToken).ConfigureAwait(false);
                    messages.Add(new ToolChatMessage(call.Id, result));
                }

                continue;
            }

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
        _pending.SetPendingDraft(sessionId, draft.DraftId, summary);
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
            if (!_pending.TryGetPendingDraft(sessionId, out draftId, out _))
            {
                return JsonSerializer.Serialize(new { error = "No pending draft id. Create a draft first or pass draft_id." });
            }
        }

        var messageId = await _gmail.SendDraftAsync(draftId!, cancellationToken).ConfigureAwait(false);
        _pending.Clear(sessionId);
        return JsonSerializer.Serialize(new { sent = true, messageId, draftId });
    }
}
