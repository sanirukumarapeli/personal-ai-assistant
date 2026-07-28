using PersonalAi.Api.Discord;
using PersonalAi.Api.Telegram;
using PersonalAi.Core;
using PersonalAi.Core.Chat;
using PersonalAi.Core.Gmail;
using PersonalAi.Core.Options;

var loadedEnv = EnvFileLoader.LoadNearest();
var builder = WebApplication.CreateBuilder(args);

if (loadedEnv is not null)
{
    Console.WriteLine($"Loaded environment file: {loadedEnv}");
}

builder.Services.Configure<GeminiOptions>(opts =>
{
    opts.ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty;
    opts.ModelId = Environment.GetEnvironmentVariable("GEMINI_MODEL_ID") ?? "gemini-3.5-flash-lite";
    opts.Endpoint = Environment.GetEnvironmentVariable("GEMINI_ENDPOINT")
                    ?? "https://generativelanguage.googleapis.com/v1beta/openai/";
});

builder.Services.Configure<TelegramOptions>(opts =>
{
    opts.BotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? string.Empty;
});

builder.Services.Configure<DiscordOptions>(opts =>
{
    opts.BotToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? string.Empty;
    opts.AllowedUserIds = Environment.GetEnvironmentVariable("DISCORD_ALLOWED_USER_IDS") ?? string.Empty;
});

builder.Services.Configure<GoogleOptions>(opts =>
{
    opts.ClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty;
    opts.ClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? string.Empty;
    opts.RedirectUri = Environment.GetEnvironmentVariable("GOOGLE_REDIRECT_URI")
                       ?? "http://localhost:5080/signin-google";
});

var dataDir = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "sessions.db");

builder.Services.AddSingleton<ISessionStore>(_ => new SqliteSessionStore(dbPath));
builder.Services.AddSingleton<IGoogleTokenStore>(_ => new SqliteGoogleTokenStore(dbPath));
builder.Services.AddSingleton<IPendingMailActions, PendingMailActions>();
builder.Services.AddSingleton<IPendingCalendarActions, PendingCalendarActions>();
builder.Services.AddSingleton<IGoogleAuthService, GoogleAuthService>();
builder.Services.AddSingleton<IGmailMailService, GmailMailService>();
builder.Services.AddSingleton<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddSingleton<IChatService, ChatService>();
builder.Services.AddHostedService<DiscordBotHostedService>();
builder.Services.AddHostedService<TelegramBotHostedService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("NextJsDev", policy =>
        policy.WithOrigins("http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors("NextJsDev");

app.MapGet("/health", async (IGoogleAuthService googleAuth, CancellationToken ct) =>
{
    var (connected, email) = await googleAuth.GetStatusAsync(ct).ConfigureAwait(false);
    return Results.Ok(new
    {
        status = "ok",
        model = Environment.GetEnvironmentVariable("GEMINI_MODEL_ID") ?? "gemini-3.5-flash-lite",
        geminiConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
        discordConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN")),
        telegramConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")),
        googleConfigured = googleAuth.IsConfigured,
        gmailConnected = connected,
        gmailEmail = email,
        sessionStore = dbPath
    });
});

app.MapGet("/auth/google", (IGoogleAuthService googleAuth) =>
{
    if (!googleAuth.IsConfigured)
    {
        return Results.Content(
            "<h1>Google not configured</h1><p>Set GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET in .env. See docs/YOU_GMAIL_SETUP.md.</p>",
            "text/html");
    }

    return Results.Redirect(googleAuth.GetAuthorizationUrl());
});

app.MapGet("/signin-google", async (string? code, string? error, IGoogleAuthService googleAuth, CancellationToken ct) =>
{
    if (!string.IsNullOrWhiteSpace(error))
    {
        return Results.Content(
            $"<h1>Google sign-in failed</h1><p>{System.Net.WebUtility.HtmlEncode(error)}</p>",
            "text/html");
    }

    if (string.IsNullOrWhiteSpace(code))
        return Results.BadRequest("Missing code");

    try
    {
        var record = await googleAuth.ExchangeCodeAsync(code, ct).ConfigureAwait(false);
        var email = System.Net.WebUtility.HtmlEncode(record.Email ?? "your account");
        return Results.Content(
            $"""
             <html><body style="font-family:Segoe UI,sans-serif;max-width:560px;margin:3rem auto;">
             <h1>Google connected</h1>
             <p>Signed in as <strong>{email}</strong> (Gmail + Calendar).</p>
             <p>You can close this tab and return to <a href="http://localhost:3000">the chat</a>.</p>
             </body></html>
             """,
            "text/html");
    }
    catch (Exception ex)
    {
        return Results.Content(
            $"<h1>Could not connect Gmail</h1><p>{System.Net.WebUtility.HtmlEncode(ex.Message)}</p>",
            "text/html");
    }
});

app.MapGet("/auth/google/status", async (IGoogleAuthService googleAuth, CancellationToken ct) =>
{
    var (connected, email) = await googleAuth.GetStatusAsync(ct).ConfigureAwait(false);
    return Results.Ok(new
    {
        configured = googleAuth.IsConfigured,
        connected,
        email
    });
});

app.MapPost("/auth/google/disconnect", async (IGoogleAuthService googleAuth, CancellationToken ct) =>
{
    await googleAuth.DisconnectAsync(ct).ConfigureAwait(false);
    return Results.Ok(new { disconnected = true });
});

app.MapPost("/v1/chat", async (ChatRequestDto body, IChatService chatService, CancellationToken ct) =>
{
    if (body is null || string.IsNullOrWhiteSpace(body.Message))
        return Results.BadRequest(new { error = "message is required" });

    try
    {
        var result = await chatService
            .ChatAsync(new ChatRequest(body.Message, body.SessionId), ct)
            .ConfigureAwait(false);

        return Results.Ok(new ChatResponseDto(result.SessionId, result.Reply));
    }
    catch (InvalidOperationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { error = $"Chat failed: {ex.Message}" },
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.Run();

public partial class Program;

public sealed record ChatRequestDto(string Message, string? SessionId = null);
public sealed record ChatResponseDto(string SessionId, string Reply);
