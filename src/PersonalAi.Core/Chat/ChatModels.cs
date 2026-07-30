namespace PersonalAi.Core.Chat;

public sealed record PendingConfirmation(
    string Kind,
    string Summary,
    string ConfirmAction = "confirm",
    string CancelAction = "cancel");

public sealed record ChatTurn(string Role, string Content, DateTimeOffset? CreatedAtUtc = null);

public sealed record ChatRequest(string Message, string? SessionId = null);

public sealed record ChatResult(
    string SessionId,
    string Reply,
    PendingConfirmation? PendingConfirmation = null);
