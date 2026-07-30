namespace PersonalAi.Core.Documents;

public sealed record DocumentInfo(
    string Folder,
    string Name,
    string Kind,
    long SizeBytes,
    DateTimeOffset ModifiedUtc,
    string DownloadUrl);

public sealed record DocumentWriteResult(
    string Folder,
    string Name,
    string Kind,
    string DownloadUrl,
    string AbsolutePath);

public sealed record DocumentTextResult(
    string Folder,
    string Name,
    string Kind,
    string Text,
    bool Truncated,
    string DownloadUrl);

public sealed record PresentationSlide(string Title, IReadOnlyList<string> Bullets);

public interface IDocumentsService
{
    IReadOnlyList<DocumentInfo> ListAllDocuments();
    IReadOnlyList<DocumentInfo> ListNotes();
    IReadOnlyList<DocumentInfo> SearchNotes(string query);
    DocumentTextResult ReadNote(string name);
    DocumentWriteResult SaveNote(string title, string content);
    void DeleteNote(string name);

    DocumentWriteResult CreatePdf(string title, string body);
    DocumentWriteResult CreateWordDocument(string title, string body);
    DocumentWriteResult CreateSpreadsheet(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows);
    DocumentWriteResult CreatePresentation(string title, IReadOnlyList<PresentationSlide> slides);

    DocumentTextResult ExtractDocumentText(string name, string? folderHint = null, int maxChars = 40_000);

    DocumentWriteResult RewritePdf(string name, string title, string body, bool overwrite, string? saveAs = null);
    DocumentWriteResult RewriteWord(string name, string title, string body, bool overwrite, string? saveAs = null);
    DocumentWriteResult RewriteSpreadsheet(
        string name,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool overwrite,
        string? saveAs = null);
    DocumentWriteResult RewritePresentation(
        string name,
        string title,
        IReadOnlyList<PresentationSlide> slides,
        bool overwrite,
        string? saveAs = null);

    DocumentWriteResult SaveUpload(string originalFileName, Stream content);
    DocumentWriteResult SaveGeneratedImage(byte[] imageBytes, string? suggestedName = null, string extension = ".png");
    bool IsImageDocument(string name);
    string? ResolveAbsolutePath(string folder, string name);
    string ContentTypeFor(string folder, string name);
}
