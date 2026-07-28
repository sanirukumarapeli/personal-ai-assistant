namespace PersonalAi.Core.Chat;

public sealed record ChatTurn(string Role, string Content);

public sealed record ChatRequest(string Message, string? SessionId = null);

public sealed record ChatResult(string SessionId, string Reply);
