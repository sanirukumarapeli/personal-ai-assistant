using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonalAi.Core.Web;

public sealed record WebSearchResult(string Title, string Url, string Snippet);

public sealed record WebPageContent(string Url, string Title, string Text, bool Truncated);

public interface IWebBrowseService
{
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int maxResults = 5, CancellationToken cancellationToken = default);
    Task<WebPageContent> FetchUrlAsync(string url, int maxChars = 12_000, CancellationToken cancellationToken = default);
}

public sealed class WebBrowseService : IWebBrowseService
{
    private static readonly Regex ResultBlock = new(
        @"class=""result__a""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>[\s\S]*?class=""result__snippet""[^>]*>(.*?)</(?:a|td|div)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagStrip = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex TitleTag = new(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ScriptStyle = new(@"<(script|style)[^>]*>[\s\S]*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;

    public WebBrowseService(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(20);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "PersonalAiAssistant/1.0 (+local; educational)");
        }
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("query is required.");

        maxResults = Math.Clamp(maxResults, 1, 10);
        var url = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query.Trim());
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await ReadLimitedStringAsync(response, 1_000_000, cancellationToken).ConfigureAwait(false);

        var results = new List<WebSearchResult>();
        foreach (Match match in ResultBlock.Matches(html))
        {
            if (results.Count >= maxResults)
                break;

            var rawHref = WebUtility.HtmlDecode(match.Groups[1].Value);
            var title = CleanHtmlText(match.Groups[2].Value);
            var snippet = CleanHtmlText(match.Groups[3].Value);
            var resolved = ResolveDuckDuckGoUrl(rawHref);
            if (string.IsNullOrWhiteSpace(resolved) || string.IsNullOrWhiteSpace(title))
                continue;

            results.Add(new WebSearchResult(title, resolved, snippet));
        }

        // Fallback simpler parse if regex missed (DDG markup changes)
        if (results.Count == 0)
        {
            var simple = Regex.Matches(
                html,
                @"uddg=([^&""]+)[^>]*>\s*([^<]{3,120})",
                RegexOptions.IgnoreCase);
            foreach (Match m in simple)
            {
                if (results.Count >= maxResults)
                    break;
                var link = Uri.UnescapeDataString(m.Groups[1].Value);
                var title = CleanHtmlText(m.Groups[2].Value);
                if (!Uri.TryCreate(link, UriKind.Absolute, out var u) ||
                    u.Scheme is not ("http" or "https"))
                    continue;
                results.Add(new WebSearchResult(title, link, ""));
            }
        }

        return results;
    }

    public async Task<WebPageContent> FetchUrlAsync(
        string url,
        int maxChars = 12_000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("url is required.");

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Only absolute http/https URLs are allowed.");

        await EnsureNotPrivateHostAsync(uri, cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported content type: {contentType}");

        var html = await ReadLimitedStringAsync(response, 1_000_000, cancellationToken).ConfigureAwait(false);
        var titleMatch = TitleTag.Match(html);
        var title = titleMatch.Success ? CleanHtmlText(titleMatch.Groups[1].Value) : uri.Host;
        var text = HtmlToText(html);
        var truncated = false;
        maxChars = Math.Clamp(maxChars, 500, 50_000);
        if (text.Length > maxChars)
        {
            text = text[..maxChars];
            truncated = true;
        }

        return new WebPageContent(uri.ToString(), title, text, truncated);
    }

    private static async Task EnsureNotPrivateHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        var host = uri.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fetching localhost/private hosts is blocked.");

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IsPrivateIp(ip))
                throw new InvalidOperationException("Fetching private IP addresses is blocked.");
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not resolve host: {host}", ex);
        }

        if (addresses.Any(IsPrivateIp))
            throw new InvalidOperationException("Fetching hosts that resolve to private IPs is blocked.");
    }

    private static bool IsPrivateIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168)
                   || (bytes[0] == 169 && bytes[1] == 254);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal
                   || ip.IsIPv6SiteLocal
                   || ip.IsIPv6UniqueLocal
                   || ip.IsIPv6Teredo;
        }

        return false;
    }

    private static string ResolveDuckDuckGoUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return "";

        href = WebUtility.HtmlDecode(href);
        if (href.StartsWith("//", StringComparison.Ordinal))
            href = "https:" + href;

        if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            var uddg = GetQueryParam(uri.Query, "uddg");
            if (!string.IsNullOrWhiteSpace(uddg))
                return Uri.UnescapeDataString(uddg);
            if (uri.Scheme is "http" or "https" && !uri.Host.Contains("duckduckgo", StringComparison.OrdinalIgnoreCase))
                return uri.ToString();
        }

        return "";
    }

    private static string? GetQueryParam(string query, string name)
    {
        if (string.IsNullOrEmpty(query))
            return null;
        var trimmed = query.TrimStart('?');
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
                continue;
            var key = Uri.UnescapeDataString(part[..idx]);
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                continue;
            return Uri.UnescapeDataString(part[(idx + 1)..]);
        }

        return null;
    }

    private static string HtmlToText(string html)
    {
        var noScript = ScriptStyle.Replace(html, " ");
        var text = TagStrip.Replace(noScript, " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    private static string CleanHtmlText(string value) =>
        WebUtility.HtmlDecode(TagStrip.Replace(value, " ")).Trim();

    private static async Task<string> ReadLimitedStringAsync(
        HttpResponseMessage response,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var limited = new LimitedReadStream(stream, maxBytes);
        using var reader = new StreamReader(limited, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _max;
        private long _read;

        public LimitedReadStream(Stream inner, long max)
        {
            _inner = inner;
            _max = max;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read >= _max)
                return 0;
            var toRead = (int)Math.Min(count, _max - _read);
            var n = _inner.Read(buffer, offset, toRead);
            _read += n;
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_read >= _max)
                return 0;
            var toRead = (int)Math.Min(count, _max - _read);
            var n = await _inner.ReadAsync(buffer.AsMemory(offset, toRead), cancellationToken).ConfigureAwait(false);
            _read += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
