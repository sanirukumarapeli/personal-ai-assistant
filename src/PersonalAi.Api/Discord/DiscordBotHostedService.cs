using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using PersonalAi.Core.Chat;
using PersonalAi.Core.Options;

namespace PersonalAi.Api.Discord;

public sealed class DiscordBotHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DiscordBotHostedService> _logger;
    private readonly DiscordOptions _options;
    private DiscordSocketClient? _client;

    public DiscordBotHostedService(
        IOptions<DiscordOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<DiscordBotHostedService> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Discord bot disabled (DISCORD_BOT_TOKEN not set).");
            return;
        }

        var allowed = _options.ParseAllowedUserIds();
        if (allowed.Count == 0)
        {
            _logger.LogWarning(
                "DISCORD_ALLOWED_USER_IDS is empty — any user who can message the bot will get replies. " +
                "Set your Discord user id in .env for a personal assistant.");
        }

        var config = new DiscordSocketConfig
        {
            // MessageContentIntent is required to read message text (must also be enabled in Developer Portal).
            GatewayIntents =
                GatewayIntents.Guilds
                | GatewayIntents.GuildMessages
                | GatewayIntents.DirectMessages
                | GatewayIntents.MessageContent,
            LogLevel = LogSeverity.Info
        };

        _client = new DiscordSocketClient(config);
        _client.Log += msg =>
        {
            var level = msg.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                _ => LogLevel.Debug
            };
            _logger.Log(level, msg.Exception, "[Discord] {Source}: {Message}", msg.Source, msg.Message);
            return Task.CompletedTask;
        };

        _client.Ready += () =>
        {
            _logger.LogInformation(
                "Discord bot logged in as {Username}#{Discriminator} (id {Id}).",
                _client.CurrentUser.Username,
                _client.CurrentUser.Discriminator,
                _client.CurrentUser.Id);
            return Task.CompletedTask;
        };

        _client.MessageReceived += message => HandleMessageAsync(message, allowed, stoppingToken);

        try
        {
            await _client.LoginAsync(TokenType.Bot, _options.BotToken).ConfigureAwait(false);
            await _client.StartAsync().ConfigureAwait(false);
            _logger.LogInformation("Discord bot started (Gateway).");

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
        finally
        {
            if (_client is not null)
            {
                await _client.StopAsync().ConfigureAwait(false);
                await _client.LogoutAsync().ConfigureAwait(false);
                await _client.DisposeAsync().ConfigureAwait(false);
                _client = null;
            }
        }
    }

    private async Task HandleMessageAsync(
        SocketMessage message,
        HashSet<ulong> allowed,
        CancellationToken cancellationToken)
    {
        if (message is not SocketUserMessage userMessage)
            return;
        if (userMessage.Author.IsBot || userMessage.Author.IsWebhook)
            return;
        if (string.IsNullOrWhiteSpace(userMessage.Content))
            return;
        if (cancellationToken.IsCancellationRequested)
            return;

        if (allowed.Count > 0 && !allowed.Contains(userMessage.Author.Id))
            return;

        // In servers: only reply when @mentioned (avoids chatting every channel message).
        // In DMs: reply to every text message.
        var isDm = userMessage.Channel is IDMChannel;
        if (!isDm)
        {
            if (_client?.CurrentUser is null)
                return;
            var mentioned = userMessage.MentionedUsers.Any(u => u.Id == _client.CurrentUser.Id)
                            || userMessage.Content.Contains($"<@{_client.CurrentUser.Id}>", StringComparison.Ordinal)
                            || userMessage.Content.Contains($"<@!{_client.CurrentUser.Id}>", StringComparison.Ordinal);
            if (!mentioned)
                return;
        }

        var text = userMessage.Content;
        if (_client?.CurrentUser is not null)
        {
            text = text
                .Replace($"<@{_client.CurrentUser.Id}>", " ", StringComparison.Ordinal)
                .Replace($"<@!{_client.CurrentUser.Id}>", " ", StringComparison.Ordinal)
                .Trim();
        }

        if (string.IsNullOrWhiteSpace(text))
            return;

        var sessionId = $"discord:{userMessage.Author.Id}";

        using var typing = userMessage.Channel.EnterTypingState();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            var result = await chatService
                .ChatAsync(new ChatRequest(text, sessionId), cancellationToken)
                .ConfigureAwait(false);

            foreach (var chunk in SplitDiscordMessage(result.Reply))
            {
                await userMessage.Channel.SendMessageAsync(chunk).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle Discord message from user {UserId}", userMessage.Author.Id);
            await userMessage.Channel
                .SendMessageAsync(
                    "Sorry — I hit an error talking to the model. Check the API logs and your GEMINI_API_KEY.")
                .ConfigureAwait(false);
        }
    }

    /// <summary>Discord hard-limits messages to 2000 characters.</summary>
    private static IEnumerable<string> SplitDiscordMessage(string text)
    {
        const int max = 2000;
        if (string.IsNullOrEmpty(text))
        {
            yield return "(No text returned from the model.)";
            yield break;
        }

        for (var i = 0; i < text.Length; i += max)
        {
            var len = Math.Min(max, text.Length - i);
            yield return text.Substring(i, len);
        }
    }
}
