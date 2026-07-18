using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Http;

namespace Stelliberty.Infrastructure.Subscriptions;

public sealed class HttpRemoteSubscriptionDownloader : IRemoteSubscriptionDownloader
{
    private const int FailureHtmlPrefixLength = 4096;
    private const int MaxDiagnosticValueLength = 200;
    private static readonly Regex HtmlTitleRegex = new(
        @"<title[^>]*>(?<title>.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly Func<(string Host, int Port)> _coreProxyEndpointProvider;

    public HttpRemoteSubscriptionDownloader(string coreProxyHost = "127.0.0.1", int coreProxyPort = AppSettings.DefaultMixedPort)
        : this(() => (coreProxyHost, coreProxyPort))
    {
    }

    public HttpRemoteSubscriptionDownloader(Func<(string Host, int Port)> coreProxyEndpointProvider)
    {
        _coreProxyEndpointProvider = coreProxyEndpointProvider;
    }

    public async Task<RemoteSubscriptionDownloadResult> DownloadAsync(RemoteSubscriptionDownloadRequest request, CancellationToken cancellationToken = default)
    {
        using var handler = new HttpClientHandler();
        if (request.ProxyMode == SubscriptionUpdateProxyMode.Direct)
        {
            handler.UseProxy = false;
        }
        else if (request.ProxyMode == SubscriptionUpdateProxyMode.SystemProxy)
        {
            handler.UseProxy = true;
            handler.Proxy = WebRequest.DefaultWebProxy;
        }
        else if (request.ProxyMode == SubscriptionUpdateProxyMode.Core)
        {
            var endpoint = _coreProxyEndpointProvider();
            handler.UseProxy = true;
            handler.Proxy = new WebProxy($"http://{endpoint.Host}:{endpoint.Port}");
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        using var message = new HttpRequestMessage(HttpMethod.Get, request.SourceLocation);
        if (!string.IsNullOrWhiteSpace(request.UserAgent))
        {
            message.Headers.UserAgent.ParseAdd(request.UserAgent);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new HttpRequestException(
                BuildTransportFailureMessage(request, exception),
                exception,
                exception.StatusCode);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateResponseFailureExceptionAsync(request, response, cancellationToken);
            }

            AppLogger.Info($"Remote subscription downloaded: {request.SourceLocation}");
            var content = await HttpContentTextReader.ReadAsStringAsync(response.Content, cancellationToken);
            var trafficInfo = response.Headers.TryGetValues("subscription-userinfo", out var values)
                ? SubscriptionTrafficInfo.ParseHeader(values.FirstOrDefault() ?? string.Empty)
                : null;
            return new RemoteSubscriptionDownloadResult(content, trafficInfo);
        }
    }

    private static async Task<HttpRequestException> CreateResponseFailureExceptionAsync(
        RemoteSubscriptionDownloadRequest request,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var finalUri = response.RequestMessage?.RequestUri;
        var requestedUri = Uri.TryCreate(request.SourceLocation, UriKind.Absolute, out var sourceUri) ? sourceUri : null;
        var wasRedirected = requestedUri is not null
            && finalUri is not null
            && Uri.Compare(requestedUri, finalUri, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) != 0;
        var title = await TryReadHtmlTitleAsync(response.Content, cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "unknown";
        var contentLength = response.Content.Headers.ContentLength?.ToString() ?? "unknown";
        var server = response.Headers.Server.ToString();
        var details = new List<string>
        {
            $"HTTP {(int)response.StatusCode} {SanitizeDiagnosticValue(response.ReasonPhrase, "unknown")}",
            $"type={SanitizeDiagnosticValue(contentType, "unknown")}",
            $"host={SanitizeDiagnosticValue(finalUri?.Host, "unknown")}",
            $"redirected={wasRedirected}",
            $"proxy={request.ProxyMode}",
            $"length={contentLength}"
        };
        if (!string.IsNullOrWhiteSpace(server))
        {
            details.Add($"server={SanitizeDiagnosticValue(server, "unknown")}");
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            details.Add($"title={title}");
        }

        return new HttpRequestException(
            $"Subscription download request failed: {string.Join("; ", details)}",
            null,
            response.StatusCode);
    }

    private static async Task<string?> TryReadHtmlTitleAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (!string.Equals(content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[FailureHtmlPrefixLength];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        if (length == 0)
        {
            return null;
        }

        var match = HtmlTitleRegex.Match(Encoding.UTF8.GetString(buffer, 0, length));
        return match.Success
            ? SanitizeDiagnosticValue(WebUtility.HtmlDecode(match.Groups["title"].Value), string.Empty)
            : null;
    }

    private static string BuildTransportFailureMessage(RemoteSubscriptionDownloadRequest request, HttpRequestException exception)
    {
        var host = Uri.TryCreate(request.SourceLocation, UriKind.Absolute, out var sourceUri)
            ? sourceUri.Host
            : "unknown";
        return $"Subscription download request could not be completed: host={SanitizeDiagnosticValue(host, "unknown")}; proxy={request.ProxyMode}; error={SanitizeDiagnosticValue(exception.Message, "unknown")}";
    }

    private static string SanitizeDiagnosticValue(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return AppLogSanitizer.Sanitize(trimmed.Length <= MaxDiagnosticValueLength ? trimmed : trimmed[..MaxDiagnosticValueLength]);
    }
}
