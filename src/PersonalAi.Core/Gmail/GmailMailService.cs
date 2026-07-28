using System.Text;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;

namespace PersonalAi.Core.Gmail;

public sealed record MailSnippet(
    string Id,
    string? ThreadId,
    string From,
    string Subject,
    string Snippet,
    string? Date);

public sealed record DraftSnippet(
    string DraftId,
    string? MessageId,
    string To,
    string Subject,
    string Snippet);

public interface IGmailMailService
{
    Task<IReadOnlyList<MailSnippet>> ListUnreadAsync(int max = 10, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MailSnippet>> SearchAsync(string query, int max = 10, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DraftSnippet>> ListDraftsAsync(int max = 10, CancellationToken cancellationToken = default);
    Task<DraftSnippet> CreateDraftAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
    Task<string> SendDraftAsync(string draftId, CancellationToken cancellationToken = default);
}

public sealed class GmailMailService : IGmailMailService
{
    private readonly IGoogleAuthService _auth;

    public GmailMailService(IGoogleAuthService auth)
    {
        _auth = auth;
    }

    public Task<IReadOnlyList<MailSnippet>> ListUnreadAsync(int max = 10, CancellationToken cancellationToken = default) =>
        SearchAsync("is:unread in:inbox", max, cancellationToken);

    public async Task<IReadOnlyList<MailSnippet>> SearchAsync(
        string query,
        int max = 10,
        CancellationToken cancellationToken = default)
    {
        var service = await _auth.CreateGmailServiceAsync(cancellationToken).ConfigureAwait(false);
        var listRequest = service.Users.Messages.List("me");
        listRequest.Q = query;
        listRequest.MaxResults = Math.Clamp(max, 1, 25);

        var list = await listRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (list.Messages is null || list.Messages.Count == 0)
            return [];

        var results = new List<MailSnippet>();
        foreach (var item in list.Messages.Take(Math.Clamp(max, 1, 25)))
        {
            var msg = await service.Users.Messages.Get("me", item.Id).ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            results.Add(ToSnippet(msg));
        }

        return results;
    }

    public async Task<IReadOnlyList<DraftSnippet>> ListDraftsAsync(int max = 10, CancellationToken cancellationToken = default)
    {
        var service = await _auth.CreateGmailServiceAsync(cancellationToken).ConfigureAwait(false);
        var listRequest = service.Users.Drafts.List("me");
        listRequest.MaxResults = Math.Clamp(max, 1, 25);
        var list = await listRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (list.Drafts is null || list.Drafts.Count == 0)
            return [];

        var results = new List<DraftSnippet>();
        foreach (var draftRef in list.Drafts.Take(Math.Clamp(max, 1, 25)))
        {
            var draft = await service.Users.Drafts.Get("me", draftRef.Id).ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            results.Add(ToDraftSnippet(draft));
        }

        return results;
    }

    public async Task<DraftSnippet> CreateDraftAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("Recipient is required.", nameof(to));

        var service = await _auth.CreateGmailServiceAsync(cancellationToken).ConfigureAwait(false);
        var raw = CreateRawEmail(to.Trim(), subject?.Trim() ?? "(no subject)", body?.Trim() ?? string.Empty);
        var draft = new Draft
        {
            Message = new Message { Raw = raw }
        };

        var created = await service.Users.Drafts.Create(draft, "me").ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        return ToDraftSnippet(created);
    }

    public async Task<string> SendDraftAsync(string draftId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftId))
            throw new ArgumentException("Draft id is required.", nameof(draftId));

        var service = await _auth.CreateGmailServiceAsync(cancellationToken).ConfigureAwait(false);
        var sent = await service.Users.Drafts.Send(new Draft { Id = draftId }, "me")
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        return sent.Id ?? draftId;
    }

    private static MailSnippet ToSnippet(Message msg)
    {
        var headers = msg.Payload?.Headers ?? [];
        string Header(string name) =>
            headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value
            ?? string.Empty;

        return new MailSnippet(
            msg.Id ?? string.Empty,
            msg.ThreadId,
            Header("From"),
            Header("Subject"),
            msg.Snippet ?? string.Empty,
            Header("Date"));
    }

    private static DraftSnippet ToDraftSnippet(Draft draft)
    {
        var msg = draft.Message;
        var headers = msg?.Payload?.Headers ?? [];
        string Header(string name) =>
            headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value
            ?? string.Empty;

        return new DraftSnippet(
            draft.Id ?? string.Empty,
            msg?.Id,
            Header("To"),
            Header("Subject"),
            msg?.Snippet ?? string.Empty);
    }

    private static string CreateRawEmail(string to, string subject, string body)
    {
        // Gmail API expects RFC 2822 base64url without From when using authenticated user.
        var sb = new StringBuilder();
        sb.Append("To: ").Append(to).Append("\r\n");
        sb.Append("Subject: ").Append(EncodeHeader(subject)).Append("\r\n");
        sb.Append("Content-Type: text/plain; charset=\"UTF-8\"\r\n");
        sb.Append("\r\n");
        sb.Append(body);

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string EncodeHeader(string value)
    {
        // Keep simple ASCII subjects as-is; UTF-8 for others.
        if (value.All(c => c < 128))
            return value;
        var bytes = Encoding.UTF8.GetBytes(value);
        return "=?UTF-8?B?" + Convert.ToBase64String(bytes) + "?=";
    }
}
