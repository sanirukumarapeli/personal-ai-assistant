using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PersonalAi.Core.Options;

namespace PersonalAi.Core.Images;

public interface IImageGenerationService
{
    Task<GeneratedImageResult> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}

public sealed record GeneratedImageResult(byte[] Bytes, string MimeType, string? ModelText);

public sealed class ImageGenerationService : IImageGenerationService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _imageModelId;

    public ImageGenerationService(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http;
        var opts = options.Value;
        _apiKey = opts.ApiKey?.Trim() ?? string.Empty;
        _imageModelId = string.IsNullOrWhiteSpace(opts.ImageModelId)
            ? "gemini-3.1-flash-image"
            : opts.ImageModelId.Trim();
        _http.Timeout = TimeSpan.FromSeconds(120);
    }

    public async Task<GeneratedImageResult> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("GEMINI_API_KEY is missing. See docs/YOU_STEP_0.md.");

        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is required.", nameof(prompt));

        var model = Uri.EscapeDataString(_imageModelId);
        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(_apiKey)}";

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = prompt.Trim() } }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "TEXT", "IMAGE" }
            }
        };

        using var response = await _http.PostAsJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var snippet = body.Length > 400 ? body[..400] : body;
            if ((int)response.StatusCode == 404)
            {
                throw new InvalidOperationException(
                    $"Image model '{_imageModelId}' was not found (404). " +
                    "Set GEMINI_IMAGE_MODEL_ID to a model from AI Studio Rate limits " +
                    "(e.g. gemini-3.1-flash-lite-image or gemini-2.5-flash-image). See docs/YOU_STEP_0.md. " +
                    $"Details: {snippet}");
            }

            if ((int)response.StatusCode == 429)
            {
                throw new InvalidOperationException(
                    $"Image generation rate limit / quota hit (429). Wait and try again. Details: {snippet}");
            }

            throw new InvalidOperationException(
                $"Image generation failed ({(int)response.StatusCode}): {snippet}");
        }

        using var doc = JsonDocument.Parse(body);
        string? mime = null;
        byte[]? bytes = null;
        var textParts = new StringBuilder();

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Image generation returned no candidates.");
        }

        var content = candidates[0].GetProperty("content");
        if (!content.TryGetProperty("parts", out var parts))
            throw new InvalidOperationException("Image generation returned no parts.");

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textEl))
            {
                var t = textEl.GetString();
                if (!string.IsNullOrWhiteSpace(t))
                {
                    if (textParts.Length > 0) textParts.AppendLine();
                    textParts.Append(t.Trim());
                }
            }

            if (part.TryGetProperty("inlineData", out var inline)
                || part.TryGetProperty("inline_data", out inline))
            {
                mime = inline.TryGetProperty("mimeType", out var mt)
                    ? mt.GetString()
                    : inline.TryGetProperty("mime_type", out var mt2)
                        ? mt2.GetString()
                        : "image/png";
                var data = inline.GetProperty("data").GetString()
                           ?? throw new InvalidOperationException("Image part missing data.");
                bytes = Convert.FromBase64String(data);
            }
        }

        if (bytes is null || bytes.Length == 0)
        {
            throw new InvalidOperationException(
                "Image model returned no image data. Try a different GEMINI_IMAGE_MODEL_ID " +
                "(gemini-3.1-flash-image / gemini-3.1-flash-lite-image). " +
                (textParts.Length > 0 ? $"Model said: {textParts}" : ""));
        }

        return new GeneratedImageResult(
            bytes,
            string.IsNullOrWhiteSpace(mime) ? "image/png" : mime,
            textParts.Length > 0 ? textParts.ToString() : null);
    }
}
