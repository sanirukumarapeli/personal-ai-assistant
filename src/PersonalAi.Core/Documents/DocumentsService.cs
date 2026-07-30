using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace PersonalAi.Core.Documents;

public sealed class DocumentsService : IDocumentsService
{
    public const string FolderNotes = "notes";
    public const string FolderPdfs = "pdfs";
    public const string FolderOffice = "office";
    public const string FolderUploads = "uploads";

    private readonly string _notesDir;
    private readonly string _pdfsDir;
    private readonly string _officeDir;
    private readonly string _uploadsDir;

    static DocumentsService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public DocumentsService(string notesDir, string pdfsDir, string officeDir, string uploadsDir)
    {
        _notesDir = notesDir;
        _pdfsDir = pdfsDir;
        _officeDir = officeDir;
        _uploadsDir = uploadsDir;
        Directory.CreateDirectory(_notesDir);
        Directory.CreateDirectory(_pdfsDir);
        Directory.CreateDirectory(_officeDir);
        Directory.CreateDirectory(_uploadsDir);
    }

    public IReadOnlyList<DocumentInfo> ListAllDocuments()
    {
        var list = new List<DocumentInfo>();
        list.AddRange(ListInFolder(FolderNotes, _notesDir, "note"));
        list.AddRange(ListInFolder(FolderPdfs, _pdfsDir, "pdf"));
        list.AddRange(ListInFolder(FolderOffice, _officeDir, kindFromExt: true));
        list.AddRange(ListInFolder(FolderUploads, _uploadsDir, kindFromExt: true));
        return list.OrderByDescending(d => d.ModifiedUtc).ToList();
    }

    public IReadOnlyList<DocumentInfo> ListNotes() =>
        ListInFolder(FolderNotes, _notesDir, "note");

    public IReadOnlyList<DocumentInfo> SearchNotes(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return ListNotes();

        var q = query.Trim();
        var hits = new List<DocumentInfo>();
        foreach (var path in Directory.EnumerateFiles(_notesDir, "*.md"))
        {
            var name = Path.GetFileName(path);
            var text = File.ReadAllText(path);
            if (name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || text.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(ToInfo(FolderNotes, path, "note"));
            }
        }

        return hits.OrderByDescending(d => d.ModifiedUtc).ToList();
    }

    public DocumentTextResult ReadNote(string name)
    {
        var safe = EnsureExtension(SanitizeFileName(name), ".md");
        var path = Path.Combine(_notesDir, safe);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {safe}");

        var text = File.ReadAllText(path);
        return new DocumentTextResult(FolderNotes, safe, "note", text, Truncated: false, DownloadUrl(FolderNotes, safe));
    }

    public DocumentWriteResult SaveNote(string title, string content)
    {
        var safe = EnsureExtension(SanitizeFileName(string.IsNullOrWhiteSpace(title) ? "note" : title), ".md");
        var path = Path.Combine(_notesDir, safe);
        var body = content ?? string.Empty;
        if (!body.StartsWith("# ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(title))
            body = $"# {title.Trim()}\n\n{body}";
        File.WriteAllText(path, body, Encoding.UTF8);
        return WriteResult(FolderNotes, safe, "note", path);
    }

    public void DeleteNote(string name)
    {
        var safe = EnsureExtension(SanitizeFileName(name), ".md");
        var path = Path.Combine(_notesDir, safe);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {safe}");
        File.Delete(path);
    }

    public DocumentWriteResult CreatePdf(string title, string body)
    {
        var safe = EnsureExtension(SanitizeFileName(string.IsNullOrWhiteSpace(title) ? "document" : title), ".pdf");
        var path = Path.Combine(_pdfsDir, UniqueName(_pdfsDir, safe));
        WritePdfFile(path, title, body);
        return WriteResult(FolderPdfs, Path.GetFileName(path), "pdf", path);
    }

    public DocumentWriteResult CreateWordDocument(string title, string body)
    {
        var safe = EnsureExtension(SanitizeFileName(string.IsNullOrWhiteSpace(title) ? "document" : title), ".docx");
        var path = Path.Combine(_officeDir, UniqueName(_officeDir, safe));
        WriteWordFile(path, title, body);
        return WriteResult(FolderOffice, Path.GetFileName(path), "docx", path);
    }

    public DocumentWriteResult CreateSpreadsheet(
        string title,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var safe = EnsureExtension(SanitizeFileName(string.IsNullOrWhiteSpace(title) ? "sheet" : title), ".xlsx");
        var path = Path.Combine(_officeDir, UniqueName(_officeDir, safe));
        WriteSpreadsheetFile(path, title, headers, rows);
        return WriteResult(FolderOffice, Path.GetFileName(path), "xlsx", path);
    }

    public DocumentWriteResult CreatePresentation(string title, IReadOnlyList<PresentationSlide> slides)
    {
        var safe = EnsureExtension(SanitizeFileName(string.IsNullOrWhiteSpace(title) ? "presentation" : title), ".pptx");
        var path = Path.Combine(_officeDir, UniqueName(_officeDir, safe));
        WritePresentationFile(path, title, slides);
        return WriteResult(FolderOffice, Path.GetFileName(path), "pptx", path);
    }

    public DocumentTextResult ExtractDocumentText(string name, string? folderHint = null, int maxChars = 40_000)
    {
        var resolved = ResolveExistingFile(name, folderHint)
                       ?? throw new FileNotFoundException($"Document not found: {name}");
        var (folder, path) = resolved;
        var fileName = Path.GetFileName(path);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var kind = KindFromExtension(ext);
        var text = ext switch
        {
            ".md" => File.ReadAllText(path),
            ".pdf" => ExtractPdfText(path),
            ".docx" => ExtractWordText(path),
            ".xlsx" => ExtractExcelText(path),
            ".pptx" => ExtractPresentationText(path),
            _ => throw new InvalidOperationException($"Unsupported file type for text extract: {ext}")
        };

        var truncated = false;
        if (maxChars > 0 && text.Length > maxChars)
        {
            text = text[..maxChars];
            truncated = true;
        }

        return new DocumentTextResult(folder, fileName, kind, text, truncated, DownloadUrl(folder, fileName));
    }

    public DocumentWriteResult RewritePdf(string name, string title, string body, bool overwrite, string? saveAs = null)
    {
        var target = ResolveRewriteTarget(name, saveAs, overwrite, ".pdf", FolderPdfs, FolderUploads);
        WritePdfFile(target.Path, title, body);
        return WriteResult(target.Folder, Path.GetFileName(target.Path), "pdf", target.Path);
    }

    public DocumentWriteResult RewriteWord(string name, string title, string body, bool overwrite, string? saveAs = null)
    {
        var target = ResolveRewriteTarget(name, saveAs, overwrite, ".docx", FolderOffice, FolderUploads);
        WriteWordFile(target.Path, title, body);
        return WriteResult(target.Folder, Path.GetFileName(target.Path), "docx", target.Path);
    }

    public DocumentWriteResult RewriteSpreadsheet(
        string name,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool overwrite,
        string? saveAs = null)
    {
        var target = ResolveRewriteTarget(name, saveAs, overwrite, ".xlsx", FolderOffice, FolderUploads);
        var title = Path.GetFileNameWithoutExtension(target.Path);
        WriteSpreadsheetFile(target.Path, title, headers, rows);
        return WriteResult(target.Folder, Path.GetFileName(target.Path), "xlsx", target.Path);
    }

    public DocumentWriteResult RewritePresentation(
        string name,
        string title,
        IReadOnlyList<PresentationSlide> slides,
        bool overwrite,
        string? saveAs = null)
    {
        var target = ResolveRewriteTarget(name, saveAs, overwrite, ".pptx", FolderOffice, FolderUploads);
        WritePresentationFile(target.Path, title, slides);
        return WriteResult(target.Folder, Path.GetFileName(target.Path), "pptx", target.Path);
    }

    public const long MaxUploadBytes = 7 * 1024 * 1024;

    public DocumentWriteResult SaveUpload(string originalFileName, Stream content)
    {
        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".docx" or ".xlsx" or ".pptx" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif"))
            throw new InvalidOperationException(
                "Only .pdf, .docx, .xlsx, .pptx, and images (.png, .jpg, .jpeg, .webp, .gif) are supported.");

        if (content.CanSeek && content.Length > MaxUploadBytes)
            throw new InvalidOperationException($"File too large. Max upload size is {MaxUploadBytes / (1024 * 1024)} MB.");

        var safe = EnsureExtension(SanitizeFileName(Path.GetFileNameWithoutExtension(originalFileName)), ext);
        var path = Path.Combine(_uploadsDir, UniqueName(_uploadsDir, safe));

        using (var fs = File.Create(path))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > MaxUploadBytes)
                {
                    fs.Dispose();
                    try { File.Delete(path); } catch { /* ignore */ }
                    throw new InvalidOperationException(
                        $"File too large. Max upload size is {MaxUploadBytes / (1024 * 1024)} MB.");
                }

                fs.Write(buffer, 0, read);
            }
        }

        return WriteResult(FolderUploads, Path.GetFileName(path), KindFromExtension(ext), path);
    }

    public DocumentWriteResult SaveGeneratedImage(byte[] imageBytes, string? suggestedName = null, string extension = ".png")
    {
        if (imageBytes is null || imageBytes.Length == 0)
            throw new ArgumentException("Image bytes are empty.", nameof(imageBytes));
        if (imageBytes.Length > MaxUploadBytes)
            throw new InvalidOperationException($"Generated image too large (max {MaxUploadBytes / (1024 * 1024)} MB).");

        var ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif"))
            ext = ".png";

        var baseName = string.IsNullOrWhiteSpace(suggestedName) ? "generated-image" : suggestedName;
        var safe = EnsureExtension(SanitizeFileName(Path.GetFileNameWithoutExtension(baseName)), ext);
        var path = Path.Combine(_uploadsDir, UniqueName(_uploadsDir, safe));
        File.WriteAllBytes(path, imageBytes);
        return WriteResult(FolderUploads, Path.GetFileName(path), KindFromExtension(ext), path);
    }

    public bool IsImageDocument(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";
    }

    public string? ResolveAbsolutePath(string folder, string name)
    {
        var dir = DirForFolder(folder);
        if (dir is null)
            return null;
        var safe = SanitizeFileName(name);
        var path = Path.Combine(dir, safe);
        return File.Exists(path) ? path : null;
    }

    public string ContentTypeFor(string folder, string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".md" => "text/markdown; charset=utf-8",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }

    private (string Folder, string Path) ResolveRewriteTarget(
        string name,
        string? saveAs,
        bool overwrite,
        string requiredExt,
        params string[] preferredFolders)
    {
        if (!string.IsNullOrWhiteSpace(saveAs))
        {
            var safeAs = EnsureExtension(SanitizeFileName(saveAs), requiredExt);
            // Prefer same folder as source if found; else office/pdfs/uploads by extension.
            var source = ResolveExistingFile(name, null);
            var folder = source?.Folder
                         ?? (requiredExt == ".pdf" ? FolderPdfs :
                             requiredExt is ".docx" or ".xlsx" or ".pptx" ? FolderOffice : FolderUploads);
            if (source is not null && preferredFolders.Contains(source.Value.Folder))
                folder = source.Value.Folder;
            else if (folder == FolderUploads || preferredFolders.Contains(FolderUploads) && source?.Folder == FolderUploads)
                folder = FolderUploads;
            else if (requiredExt == ".pdf")
                folder = source?.Folder == FolderUploads ? FolderUploads : FolderPdfs;
            else
                folder = source?.Folder == FolderUploads ? FolderUploads : FolderOffice;

            var dir = DirForFolder(folder)!;
            var path = Path.Combine(dir, safeAs);
            if (File.Exists(path) && !overwrite)
                throw new InvalidOperationException(
                    $"Target '{safeAs}' already exists. Set user_confirmed=true to overwrite, or choose another save_as name.");
            return (folder, path);
        }

        var existing = ResolveExistingFile(name, null, requiredExt)
                       ?? throw new FileNotFoundException($"Document not found: {name}");
        if (!overwrite)
            throw new InvalidOperationException(
                "Overwrite requires user_confirmed=true, or pass save_as for a new file name.");
        return existing;
    }

    private (string Folder, string Path)? ResolveExistingFile(
        string name,
        string? folderHint,
        string? requiredExt = null)
    {
        var safe = SanitizeFileName(name);
        if (!string.IsNullOrWhiteSpace(requiredExt) && !safe.EndsWith(requiredExt, StringComparison.OrdinalIgnoreCase))
            safe = EnsureExtension(Path.GetFileNameWithoutExtension(safe), requiredExt);

        IEnumerable<string> folders = string.IsNullOrWhiteSpace(folderHint)
            ? [FolderUploads, FolderOffice, FolderPdfs, FolderNotes]
            : [folderHint.Trim().ToLowerInvariant()];

        foreach (var folder in folders)
        {
            var dir = DirForFolder(folder);
            if (dir is null)
                continue;

            var path = Path.Combine(dir, safe);
            if (File.Exists(path))
                return (folder, path);

            // Try with common extensions if none provided
            if (!Path.HasExtension(safe))
            {
                foreach (var ext in new[] { ".pdf", ".docx", ".xlsx", ".pptx", ".md", ".png", ".jpg", ".jpeg", ".webp", ".gif" })
                {
                    if (requiredExt is not null && !string.Equals(ext, requiredExt, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var candidate = Path.Combine(dir, safe + ext);
                    if (File.Exists(candidate))
                        return (folder, candidate);
                }
            }
        }

        return null;
    }

    private string? DirForFolder(string folder) => folder.ToLowerInvariant() switch
    {
        FolderNotes => _notesDir,
        FolderPdfs => _pdfsDir,
        FolderOffice => _officeDir,
        FolderUploads => _uploadsDir,
        _ => null
    };

    private static IReadOnlyList<DocumentInfo> ListInFolder(
        string folder,
        string dir,
        string? fixedKind = null,
        bool kindFromExt = false)
    {
        if (!Directory.Exists(dir))
            return [];

        return Directory.EnumerateFiles(dir)
            .Select(path =>
            {
                var kind = fixedKind ?? (kindFromExt ? KindFromExtension(Path.GetExtension(path)) : "file");
                return ToInfo(folder, path, kind);
            })
            .OrderByDescending(d => d.ModifiedUtc)
            .ToList();
    }

    private static DocumentInfo ToInfo(string folder, string path, string kind)
    {
        var name = Path.GetFileName(path);
        var info = new FileInfo(path);
        return new DocumentInfo(
            folder,
            name,
            kind,
            info.Length,
            info.LastWriteTimeUtc,
            DownloadUrl(folder, name));
    }

    private static DocumentWriteResult WriteResult(string folder, string name, string kind, string path) =>
        new(folder, name, kind, DownloadUrl(folder, name), path);

    private static string DownloadUrl(string folder, string name) =>
        $"http://localhost:5080/v1/documents/{folder}/{Uri.EscapeDataString(name)}";

    private static string KindFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".md" => "note",
        ".pdf" => "pdf",
        ".docx" => "docx",
        ".xlsx" => "xlsx",
        ".pptx" => "pptx",
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" => "image",
        _ => "file"
    };

    private static string SanitizeFileName(string name)
    {
        var baseName = Path.GetFileName(name.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "document";

        var ext = Path.GetExtension(baseName);
        var stem = Path.GetFileNameWithoutExtension(baseName);
        stem = Regex.Replace(stem, @"[^\w\-\s\.]+", "", RegexOptions.CultureInvariant);
        stem = Regex.Replace(stem, @"\s+", "-").Trim('-', '.');
        if (string.IsNullOrWhiteSpace(stem))
            stem = "document";
        if (stem.Length > 80)
            stem = stem[..80];
        return stem + ext.ToLowerInvariant();
    }

    private static string EnsureExtension(string name, string ext)
    {
        if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            return name;
        return Path.GetFileNameWithoutExtension(name) + ext;
    }

    private static string UniqueName(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            return fileName;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{stem}-{i}{ext}";
            if (!File.Exists(Path.Combine(dir, candidate)))
                return candidate;
        }

        return $"{stem}-{Guid.NewGuid():N}{ext}";
    }

    private static void WritePdfFile(string path, string title, string body)
    {
        var docTitle = string.IsNullOrWhiteSpace(title) ? "Document" : title.Trim();
        var docBody = body ?? string.Empty;

        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Size(PageSizes.A4);
                page.Header().Text(docTitle).SemiBold().FontSize(18);
                page.Content().PaddingTop(16).Text(docBody).FontSize(11).LineHeight(1.35f);
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf(path);
    }

    private static void WriteWordFile(string path, string title, string body)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Word.Document(new Word.Body());
        var bodyEl = main.Document.Body!;

        if (!string.IsNullOrWhiteSpace(title))
        {
            bodyEl.AppendChild(CreateParagraph(title.Trim(), bold: true, fontSize: "28"));
            bodyEl.AppendChild(CreateParagraph(string.Empty));
        }

        foreach (var line in (body ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            bodyEl.AppendChild(CreateParagraph(line));

        main.Document.Save();
    }

    private static Word.Paragraph CreateParagraph(string text, bool bold = false, string fontSize = "22")
    {
        var runProps = new Word.RunProperties(new Word.FontSize { Val = fontSize });
        if (bold)
            runProps.AppendChild(new Word.Bold());

        return new Word.Paragraph(new Word.Run(runProps, new Word.Text(text)));
    }

    private static void WriteSpreadsheetFile(
        string path,
        string title,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var wb = new XLWorkbook();
        var sheetName = string.IsNullOrWhiteSpace(title) ? "Sheet1" : SanitizeSheetName(title);
        var ws = wb.Worksheets.Add(sheetName);

        var colCount = Math.Max(headers?.Count ?? 0, rows.Count == 0 ? 1 : rows.Max(r => r.Count));
        if (headers is { Count: > 0 })
        {
            for (var c = 0; c < headers.Count; c++)
                ws.Cell(1, c + 1).Value = headers[c] ?? string.Empty;
            ws.Row(1).Style.Font.Bold = true;
        }

        var startRow = headers is { Count: > 0 } ? 2 : 1;
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (var c = 0; c < row.Count; c++)
                ws.Cell(startRow + r, c + 1).Value = row[c] ?? string.Empty;
        }

        if (colCount > 0)
            ws.Columns(1, colCount).AdjustToContents();

        wb.SaveAs(path);
    }

    private static string SanitizeSheetName(string title)
    {
        var s = Regex.Replace(title, @"[\\/*?:\[\]]", "");
        if (s.Length > 31)
            s = s[..31];
        return string.IsNullOrWhiteSpace(s) ? "Sheet1" : s;
    }

    private static string ExtractPdfText(string path)
    {
        using var document = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static string ExtractWordText(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var para in body.Elements<Word.Paragraph>())
        {
            var texts = para.Descendants<Word.Text>().Select(t => t.Text);
            sb.AppendLine(string.Concat(texts));
        }

        return sb.ToString().Trim();
    }

    private static string ExtractExcelText(string path)
    {
        using var wb = new XLWorkbook(path);
        var sb = new StringBuilder();
        foreach (var ws in wb.Worksheets)
        {
            sb.AppendLine($"# Sheet: {ws.Name}");
            var range = ws.RangeUsed();
            if (range is null)
            {
                sb.AppendLine();
                continue;
            }

            foreach (var row in range.RowsUsed())
            {
                var cells = row.CellsUsed()
                    .Select(c => c.GetFormattedString()?.Replace('\t', ' ') ?? string.Empty);
                sb.AppendLine(string.Join('\t', cells));
            }

            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static void WritePresentationFile(
        string path,
        string title,
        IReadOnlyList<PresentationSlide> slides)
    {
        var deck = (slides is { Count: > 0 }
            ? slides
            : [new PresentationSlide(string.IsNullOrWhiteSpace(title) ? "Presentation" : title.Trim(), [])])
            .ToList();

        // Ensure first slide carries deck title when empty.
        if (string.IsNullOrWhiteSpace(deck[0].Title) && !string.IsNullOrWhiteSpace(title))
            deck[0] = new PresentationSlide(title.Trim(), deck[0].Bullets);

        if (File.Exists(path))
            File.Delete(path);

        using var presentationDoc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentationPart = presentationDoc.AddPresentationPart();
        presentationPart.Presentation = new P.Presentation();

        var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>("rId1");
        slideMasterPart.SlideMaster = CreateSlideMaster();
        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>("rId1");
        slideLayoutPart.SlideLayout = CreateSlideLayout();
        slideMasterPart.SlideMaster.AppendChild(new P.SlideLayoutIdList(
            new P.SlideLayoutId { Id = 2147483649U, RelationshipId = "rId1" }));
        slideLayoutPart.AddPart(slideMasterPart, "rId1");

        presentationPart.Presentation.AppendChild(new P.SlideMasterIdList(
            new P.SlideMasterId { Id = 2147483648U, RelationshipId = "rId1" }));

        var slideIdList = presentationPart.Presentation.AppendChild(new P.SlideIdList());
        uint slideId = 256;
        for (var i = 0; i < deck.Count; i++)
        {
            var relId = $"rId{i + 2}";
            var slidePart = presentationPart.AddNewPart<SlidePart>(relId);
            slidePart.Slide = CreateContentSlide(deck[i]);
            slidePart.AddPart(slideLayoutPart, "rId1");
            slideIdList.AppendChild(new P.SlideId { Id = slideId++, RelationshipId = relId });
        }

        presentationPart.Presentation.Save();
    }

    private static P.SlideMaster CreateSlideMaster() =>
        new(
            new P.CommonSlideData(new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(new A.TransformGroup()))),
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
            });

    private static P.SlideLayout CreateSlideLayout() =>
        new(
            new P.CommonSlideData(new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(new A.TransformGroup()))),
            new P.ColorMapOverride(new A.MasterColorMapping()))
        {
            Type = P.SlideLayoutValues.Blank
        };

    private static P.Slide CreateContentSlide(PresentationSlide slide)
    {
        var title = string.IsNullOrWhiteSpace(slide.Title) ? "Slide" : slide.Title.Trim();
        var bullets = slide.Bullets?.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()).ToList()
                      ?? [];

        var shapeTree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new A.TransformGroup()),
            CreateTextShape(2, "Title", 457200, 274638, 8229600, 1143000, title, fontSize: 3200, bold: true),
            CreateBodyShape(3, "Content", 457200, 1600200, 8229600, 4525963, bullets));

        return new P.Slide(new P.CommonSlideData(shapeTree), new P.ColorMapOverride(new A.MasterColorMapping()));
    }

    private static P.Shape CreateTextShape(
        uint id,
        string name,
        long x,
        long y,
        long cx,
        long cy,
        string text,
        int fontSize,
        bool bold)
    {
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }),
            new P.TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(
                    new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Left },
                    new A.Run(
                        new A.RunProperties
                        {
                            Language = "en-US",
                            FontSize = fontSize,
                            Bold = bold,
                            Dirty = false
                        },
                        new A.Text(text)),
                    new A.EndParagraphRunProperties { Language = "en-US" })));
    }

    private static P.Shape CreateBodyShape(
        uint id,
        string name,
        long x,
        long y,
        long cx,
        long cy,
        IReadOnlyList<string> bullets)
    {
        var textBody = new P.TextBody(new A.BodyProperties(), new A.ListStyle());
        if (bullets.Count == 0)
        {
            textBody.AppendChild(new A.Paragraph(new A.EndParagraphRunProperties { Language = "en-US" }));
        }
        else
        {
            foreach (var bullet in bullets)
            {
                textBody.AppendChild(new A.Paragraph(
                    new A.ParagraphProperties(
                        new A.BulletFont { Typeface = "Arial" },
                        new A.CharacterBullet { Char = "•" })
                    {
                        Level = 0
                    },
                    new A.Run(
                        new A.RunProperties
                        {
                            Language = "en-US",
                            FontSize = 2000,
                            Dirty = false
                        },
                        new A.Text(bullet)),
                    new A.EndParagraphRunProperties { Language = "en-US" }));
            }
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }),
            textBody);
    }

    private static string ExtractPresentationText(string path)
    {
        using var doc = PresentationDocument.Open(path, false);
        var presentation = doc.PresentationPart?.Presentation;
        if (presentation?.SlideIdList is null)
            return string.Empty;

        var sb = new StringBuilder();
        var index = 1;
        foreach (var slideId in presentation.SlideIdList.Elements<P.SlideId>())
        {
            var relId = slideId.RelationshipId?.Value;
            if (string.IsNullOrWhiteSpace(relId))
                continue;

            var slidePart = (SlidePart?)doc.PresentationPart!.GetPartById(relId);
            if (slidePart?.Slide is null)
                continue;

            var texts = slidePart.Slide.Descendants<A.Text>()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            sb.AppendLine($"## Slide {index}");
            if (texts.Count > 0)
            {
                sb.AppendLine(texts[0]);
                foreach (var line in texts.Skip(1))
                    sb.AppendLine($"- {line}");
            }

            sb.AppendLine();
            index++;
        }

        return sb.ToString().Trim();
    }
}
