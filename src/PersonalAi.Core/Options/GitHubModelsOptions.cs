namespace PersonalAi.Core.Options;

public sealed class GitHubModelsOptions
{
    public const string SectionName = "GitHubModels";

    public string Token { get; set; } = string.Empty;
    public string ModelId { get; set; } = "openai/gpt-5";
    public string Endpoint { get; set; } = "https://models.github.ai/inference";
}

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = string.Empty;
    public bool Enabled => !string.IsNullOrWhiteSpace(BotToken);
}

public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = "http://localhost:5080/signin-google";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public static readonly string[] Scopes =
    [
        "openid",
        "email",
        "profile",
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/gmail.compose"
    ];
}
