using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace PawsPatchLauncher;

public static class ModDbDownload
{
    public const string ArcanePage = "https://www.moddb.com/mods/kohan-2-arcane-wars-v078beta/downloads/arcane-wars-082beta";
    public const string ArcaneStart = "https://www.moddb.com/downloads/start/249627";

    public static bool IsStartUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "www.moddb.com" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && Regex.IsMatch(uri.AbsolutePath, @"\A/downloads/start/[1-9][0-9]*\z");

    public static string ParseMirror(string start, string html)
    {
        if (!IsStartUrl(start)) throw new InvalidDataException("Invalid ModDB download page.");
        var id = new Uri(start).Segments.Last();
        foreach (Match match in Regex.Matches(html, "href\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
        {
            var link = WebUtility.HtmlDecode(match.Groups[1].Value);
            if (!Uri.TryCreate(new Uri(start), link, out var uri)) continue;
            if (uri.Scheme == "https" && uri.Host == "www.moddb.com" && uri.IsDefaultPort && uri.UserInfo.Length == 0
                && uri.Query.Length == 0 && uri.Fragment.Length == 0
                && Regex.IsMatch(uri.AbsolutePath, @"\A/downloads/mirror/" + id + @"/[0-9]+/[a-zA-Z0-9]+/?\z"))
                return uri.AbsoluteUri;
        }
        throw new IOException("ModDB did not provide a download mirror. Please try again later: " + ArcanePage);
    }

    public static async Task<string> ResolveAsync(HttpClient http, string start, CancellationToken ct)
    {
        if (!IsStartUrl(start)) throw new InvalidDataException("Invalid ModDB download page.");
        using var request = new HttpRequestMessage(HttpMethod.Get, start);
        request.Headers.Referrer = new Uri(ArcanePage);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (bytes.Length + n > 1024 * 1024) throw new InvalidDataException("ModDB download page is too large.");
            bytes.Write(buffer, 0, n);
        }
        return ParseMirror(start, Encoding.UTF8.GetString(bytes.ToArray()));
    }
}
