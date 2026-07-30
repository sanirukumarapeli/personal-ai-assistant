using PersonalAi.Api.Discord;
using PersonalAi.Api.Telegram;
using PersonalAi.Core;
using PersonalAi.Core.Chat;
using PersonalAi.Core.Documents;
using PersonalAi.Core.Gmail;
using PersonalAi.Core.Images;
using PersonalAi.Core.Options;
using PersonalAi.Core.Web;

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
    opts.ImageModelId = Environment.GetEnvironmentVariable("GEMINI_IMAGE_MODEL_ID")
                        ?? "gemini-3.1-flash-image";
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
var notesDir = Path.Combine(dataDir, "notes");
var pdfsDir = Path.Combine(dataDir, "pdfs");
var officeDir = Path.Combine(dataDir, "office");
var uploadsDir = Path.Combine(dataDir, "uploads");

builder.Services.AddSingleton<ISessionStore>(_ => new SqliteSessionStore(dbPath));
builder.Services.AddSingleton<IGoogleTokenStore>(_ => new SqliteGoogleTokenStore(dbPath));
builder.Services.AddSingleton<IPendingMailActions, PendingMailActions>();
builder.Services.AddSingleton<IPendingCalendarActions, PendingCalendarActions>();
builder.Services.AddSingleton<IPendingDocumentActions, PendingDocumentActions>();
builder.Services.AddSingleton<IGoogleAuthService, GoogleAuthService>();
builder.Services.AddSingleton<IGmailMailService, GmailMailService>();
builder.Services.AddSingleton<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddSingleton<IDocumentsService>(_ =>
    new DocumentsService(notesDir, pdfsDir, officeDir, uploadsDir));
builder.Services.AddHttpClient<IWebBrowseService, WebBrowseService>();
builder.Services.AddHttpClient<IImageGenerationService, ImageGenerationService>();
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

app.MapGet("/health", async (IGoogleAuthService googleAuth, IDocumentsService docs, CancellationToken ct) =>
{
    var (connected, email) = await googleAuth.GetStatusAsync(ct).ConfigureAwait(false);
    return Results.Ok(new
    {
        status = "ok",
        model = Environment.GetEnvironmentVariable("GEMINI_MODEL_ID") ?? "gemini-3.5-flash-lite",
        imageModel = Environment.GetEnvironmentVariable("GEMINI_IMAGE_MODEL_ID") ?? "gemini-3.1-flash-image",
        geminiConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
        discordConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN")),
        telegramConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")),
        googleConfigured = googleAuth.IsConfigured,
        gmailConnected = connected,
        gmailEmail = email,
        documentsCount = docs.ListAllDocuments().Count,
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

app.MapGet("/v1/sessions", async (ISessionStore sessions, CancellationToken ct) =>
{
    var list = await sessions.ListSessionsAsync(ct).ConfigureAwait(false);
    return Results.Ok(new { sessions = list });
});

app.MapGet("/v1/sessions/{id}", async (string id, ISessionStore sessions, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id))
        return Results.BadRequest(new { error = "id is required" });

    var history = await sessions.GetHistoryAsync(id, ct).ConfigureAwait(false);
    return Results.Ok(new { sessionId = id, turns = history });
});

app.MapDelete("/v1/sessions/{id}", async (string id, ISessionStore sessions, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id))
        return Results.BadRequest(new { error = "id is required" });

    await sessions.DeleteSessionAsync(id, ct).ConfigureAwait(false);
    return Results.Ok(new { deleted = true, sessionId = id });
});

app.MapPost("/v1/sessions/{id}/truncate", async (string id, TruncateSessionDto? body, ISessionStore sessions, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id))
        return Results.BadRequest(new { error = "id is required" });
    if (body is null || body.KeepCount < 0)
        return Results.BadRequest(new { error = "keepCount must be >= 0" });

    await sessions.TruncateAfterAsync(id, body.KeepCount, ct).ConfigureAwait(false);
    return Results.Ok(new { truncated = true, sessionId = id, keepCount = body.KeepCount });
});

app.MapPost("/v1/sessions/{id}/confirm", async (string id, IChatService chatService, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id))
        return Results.BadRequest(new { error = "id is required" });

    try
    {
        var result = await chatService.ConfirmPendingAsync(id, ct).ConfigureAwait(false);
        return Results.Ok(new ChatResponseDto(result.SessionId, result.Reply, result.PendingConfirmation));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/v1/sessions/{id}/cancel", async (string id, IChatService chatService, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id))
        return Results.BadRequest(new { error = "id is required" });

    try
    {
        var result = await chatService.CancelPendingAsync(id, ct).ConfigureAwait(false);
        return Results.Ok(new ChatResponseDto(result.SessionId, result.Reply, result.PendingConfirmation));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/v1/documents", (IDocumentsService docs) =>
    Results.Ok(new { documents = docs.ListAllDocuments() }));

app.MapGet("/v1/documents/{folder}/{name}", (string folder, string name, IDocumentsService docs) =>
{
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        DocumentsService.FolderNotes,
        DocumentsService.FolderPdfs,
        DocumentsService.FolderOffice,
        DocumentsService.FolderUploads
    };
    if (!allowed.Contains(folder))
        return Results.BadRequest(new { error = "folder must be notes, pdfs, office, or uploads" });

    var path = docs.ResolveAbsolutePath(folder, Uri.UnescapeDataString(name));
    if (path is null)
        return Results.NotFound(new { error = "File not found" });

    var fileName = Path.GetFileName(path);
    return Results.File(path, docs.ContentTypeFor(folder, fileName), fileDownloadName: fileName);
});

app.MapPost("/v1/documents/upload", async (HttpRequest request, IDocumentsService docs, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Expected multipart form upload with field 'file'." });

    var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "Missing file. Use form field name 'file'." });

    try
    {
        await using var stream = file.OpenReadStream();
        var result = docs.SaveUpload(file.FileName, stream);
        return Results.Ok(new
        {
            result.Folder,
            result.Name,
            result.Kind,
            downloadUrl = result.DownloadUrl,
            absoluteUrl = result.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? result.DownloadUrl
                : $"http://localhost:5080{result.DownloadUrl}"
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
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

        return Results.Ok(new ChatResponseDto(result.SessionId, result.Reply, result.PendingConfirmation));
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.Json(new { cancelled = true, error = "cancelled" }, statusCode: 499);
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
public sealed record ChatResponseDto(
    string SessionId,
    string Reply,
    PendingConfirmation? PendingConfirmation = null);
public sealed record TruncateSessionDto(int KeepCount);
