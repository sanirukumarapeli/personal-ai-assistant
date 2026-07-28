using Microsoft.Extensions.Options;
using PersonalAi.Core.Chat;
using PersonalAi.Core.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PersonalAi.Api.Telegram;

public sealed class TelegramBotHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramBotHostedService> _logger;
    private readonly TelegramOptions _options;

    public TelegramBotHostedService(
        IOptions<TelegramOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramBotHostedService> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Telegram bot disabled (TELEGRAM_BOT_TOKEN not set).");
            return;
        }

        var bot = new TelegramBotClient(_options.BotToken);
        var me = await bot.GetMe(stoppingToken).ConfigureAwait(false);
        _logger.LogInformation("Telegram bot @{Username} started (long polling).", me.Username);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message]
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await bot.ReceiveAsync(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandleErrorAsync,
                    receiverOptions: receiverOptions,
                    cancellationToken: stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram polling crashed; restarting in 5s.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { Text: { Length: > 0 } text, Chat: { } chat })
            return;

        var sessionId = $"tg:{chat.Id}";
        await bot.SendChatAction(chat.Id, ChatAction.Typing, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
            var result = await chatService
                .ChatAsync(new ChatRequest(text, sessionId), cancellationToken)
                .ConfigureAwait(false);

            await bot.SendMessage(chat.Id, result.Reply, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle Telegram message from chat {ChatId}", chat.Id);
            await bot.SendMessage(
                    chat.Id,
                    "Sorry — I hit an error talking to the model. Check the API logs and your GITHUB_TOKEN.",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram polling error");
        return Task.CompletedTask;
    }
}
