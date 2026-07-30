namespace PersonalAi.Core.Options;

/// <summary>
/// Google Gemini via AI Studio (OpenAI-compatible endpoint).
/// Separate from <see cref="GoogleOptions"/> (Gmail/Calendar OAuth).
/// </summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = "gemini-3.5-flash-lite";
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";

    /// <summary>Dedicated Gemini image model for generate_image (not the chat model).</summary>
    public string ImageModelId { get; set; } = "gemini-3.1-flash-image";
}

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = string.Empty;
    public bool Enabled => !string.IsNullOrWhiteSpace(BotToken);
}

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated Discord user snowflake IDs allowed to talk to the bot.
    /// Empty = allow any user (fine for a private server; set this for safety).
    /// </summary>
    public string AllowedUserIds { get; set; } = string.Empty;

    public bool Enabled => !string.IsNullOrWhiteSpace(BotToken);

    public HashSet<ulong> ParseAllowedUserIds()
    {
        var set = new HashSet<ulong>();
        if (string.IsNullOrWhiteSpace(AllowedUserIds))
            return set;

        foreach (var part in AllowedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ulong.TryParse(part, out var id))
                set.Add(id);
        }

        return set;
    }
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
        "https://www.googleapis.com/auth/gmail.compose",
        "https://www.googleapis.com/auth/calendar.readonly",
        "https://www.googleapis.com/auth/calendar.events"
    ];
}
