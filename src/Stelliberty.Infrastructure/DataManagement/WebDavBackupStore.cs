using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;

namespace Stelliberty.Infrastructure.DataManagement;

public sealed class WebDavBackupStore : IWebDavBackupStore, IDisposable
{
    private static readonly HttpMethod PropFindMethod = new("PROPFIND");
    private static readonly HttpMethod MkColMethod = new("MKCOL");
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public WebDavBackupStore()
        : this(new HttpClient { Timeout = DefaultTimeout }, ownsHttpClient: true)
    {
    }

    public WebDavBackupStore(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task TestConnectionAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
    {
        var directoryUri = await EnsureDirectoryAsync(settings, cancellationToken);
        using var request = CreateRequest(PropFindMethod, directoryUri, settings);
        request.Headers.Add("Depth", "0");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureAllowedAsync(response, HttpStatusCode.MultiStatus, HttpStatusCode.OK);
    }

    public async Task<IReadOnlyList<RemoteBackupEntry>> ListAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
    {
        var directoryUri = await EnsureDirectoryAsync(settings, cancellationToken);
        using var request = CreateRequest(PropFindMethod, directoryUri, settings);
        request.Headers.Add("Depth", "1");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureAllowedAsync(response, HttpStatusCode.MultiStatus, HttpStatusCode.OK);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return ParseList(stream);
    }

    public async Task UploadAsync(WebDavBackupSettings settings, string fileName, Stream content, CancellationToken cancellationToken)
    {
        var fileUri = await BuildFileUriAsync(settings, fileName, cancellationToken);
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var request = CreateRequest(HttpMethod.Put, fileUri, settings);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureAllowedAsync(response, HttpStatusCode.Created, HttpStatusCode.NoContent, HttpStatusCode.OK);
    }

    public async Task DownloadAsync(WebDavBackupSettings settings, string fileName, string destinationPath, CancellationToken cancellationToken)
    {
        var fileUri = await BuildFileUriAsync(settings, fileName, cancellationToken);
        using var request = CreateRequest(HttpMethod.Get, fileUri, settings);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureAllowedAsync(response, HttpStatusCode.OK);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    public async Task DeleteAsync(WebDavBackupSettings settings, string fileName, CancellationToken cancellationToken)
    {
        var fileUri = await BuildFileUriAsync(settings, fileName, cancellationToken);
        using var request = CreateRequest(HttpMethod.Delete, fileUri, settings);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureAllowedAsync(response, HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.NotFound);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<Uri> BuildFileUriAsync(WebDavBackupSettings settings, string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('/') || fileName.Contains('\\'))
        {
            throw new InvalidOperationException("Invalid WebDAV backup file name");
        }

        var directoryUri = await EnsureDirectoryAsync(settings, cancellationToken);
        return new Uri(directoryUri, Uri.EscapeDataString(fileName));
    }

    private async Task<Uri> EnsureDirectoryAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
    {
        var current = BaseUri(settings);
        foreach (var segment in RemoteDirectorySegments(settings.RemoteDirectory))
        {
            current = new Uri(current, Uri.EscapeDataString(segment) + "/");
            using var request = CreateRequest(MkColMethod, current, settings);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureAllowedAsync(response, HttpStatusCode.Created, HttpStatusCode.MethodNotAllowed, HttpStatusCode.OK);
        }

        return current;
    }

    private static Uri BaseUri(WebDavBackupSettings settings)
    {
        if (!Uri.TryCreate(settings.Url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("WebDAV URL must be an absolute HTTP or HTTPS URL");
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    private static IReadOnlyList<string> RemoteDirectorySegments(string remoteDirectory)
    {
        var source = string.IsNullOrWhiteSpace(remoteDirectory) ? "stelliberty-backups" : remoteDirectory;
        var segments = source
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("WebDAV remote directory cannot contain path traversal");
        }

        return segments.Length == 0 ? ["stelliberty-backups"] : segments;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, WebDavBackupSettings settings)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.UserAgent.ParseAdd(AppRuntimeNames.UserAgent);
        if (!string.IsNullOrWhiteSpace(settings.UserName))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.UserName}:{settings.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        return request;
    }

    private static IReadOnlyList<RemoteBackupEntry> ParseList(Stream stream)
    {
        var document = XDocument.Load(stream);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "response")
            .Select(ParseResponse)
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .OrderByDescending(entry => entry.LastModified ?? DateTimeOffset.MinValue)
            .ThenByDescending(entry => entry.FileName, StringComparer.Ordinal)
            .ToList();
    }

    private static RemoteBackupEntry? ParseResponse(XElement response)
    {
        var href = response.Elements().FirstOrDefault(element => element.Name.LocalName == "href")?.Value;
        var fileName = FileNameFromHref(href);
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.EndsWith($".{AppRuntimeNames.FileNameToken}", StringComparison.Ordinal))
        {
            return null;
        }

        var prop = response.Descendants().FirstOrDefault(element => element.Name.LocalName == "prop");
        if (prop is null || prop.Descendants().Any(element => element.Name.LocalName == "collection"))
        {
            return null;
        }

        var size = ParseLong(prop.Elements().FirstOrDefault(element => element.Name.LocalName == "getcontentlength")?.Value);
        var modified = ParseDate(prop.Elements().FirstOrDefault(element => element.Name.LocalName == "getlastmodified")?.Value);
        return new RemoteBackupEntry(fileName, size, modified);
    }

    private static string FileNameFromHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        var trimmed = Uri.UnescapeDataString(href.TrimEnd('/'));
        var slashIndex = trimmed.LastIndexOf('/');
        return slashIndex >= 0 ? trimmed[(slashIndex + 1)..] : trimmed;
    }

    private static long? ParseLong(string? value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result
            : null;
    }

    private static async Task EnsureAllowedAsync(HttpResponseMessage response, params HttpStatusCode[] allowedStatusCodes)
    {
        if (allowedStatusCodes.Contains(response.StatusCode))
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"WebDAV request failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}".Trim());
    }
}
