using System.Collections.Concurrent;
using System.Text.Json;

namespace PersonalAi.Core.Documents;

public enum PendingDocumentKind
{
    NoteDelete,
    RewritePdf,
    RewriteWord,
    RewriteSpreadsheet,
    RewritePresentation
}

public sealed record PendingDocumentAction(
    PendingDocumentKind Kind,
    string SummaryForUser,
    string Name,
    string? Title = null,
    string? Body = null,
    string? SaveAs = null,
    string? HeadersJson = null,
    string? RowsJson = null,
    string? SlidesJson = null);

public interface IPendingDocumentActions
{
    void Set(string sessionId, PendingDocumentAction action);
    bool TryGet(string sessionId, out PendingDocumentAction action);
    void Clear(string sessionId);
}

public sealed class PendingDocumentActions : IPendingDocumentActions
{
    private readonly ConcurrentDictionary<string, PendingDocumentAction> _pending = new();

    public void Set(string sessionId, PendingDocumentAction action) =>
        _pending[sessionId] = action;

    public bool TryGet(string sessionId, out PendingDocumentAction action) =>
        _pending.TryGetValue(sessionId, out action!);

    public void Clear(string sessionId) =>
        _pending.TryRemove(sessionId, out _);
}

public static class PendingDocumentPayload
{
    public static string SerializeHeaders(IReadOnlyList<string> headers) =>
        JsonSerializer.Serialize(headers);

    public static string SerializeRows(IReadOnlyList<IReadOnlyList<string>> rows) =>
        JsonSerializer.Serialize(rows);

    public static string SerializeSlides(IReadOnlyList<PresentationSlide> slides) =>
        JsonSerializer.Serialize(slides.Select(s => new
        {
            title = s.Title,
            bullets = s.Bullets
        }));

    public static IReadOnlyList<string> DeserializeHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    public static IReadOnlyList<IReadOnlyList<string>> DeserializeRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        var rows = JsonSerializer.Deserialize<List<List<string>>>(json) ?? [];
        return rows.Select(r => (IReadOnlyList<string>)r).ToList();
    }

    public static IReadOnlyList<PresentationSlide> DeserializeSlides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var slides = new List<PresentationSlide>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var bullets = new List<string>();
            if (el.TryGetProperty("bullets", out var b) && b.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in b.EnumerateArray())
                    bullets.Add(item.GetString() ?? "");
            }

            slides.Add(new PresentationSlide(title, bullets));
        }

        return slides;
    }
}
