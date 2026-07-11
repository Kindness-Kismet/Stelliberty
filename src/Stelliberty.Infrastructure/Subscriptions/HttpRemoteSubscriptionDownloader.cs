using System.Net;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Http;

namespace Stelliberty.Infrastructure.Subscriptions;

public sealed class HttpRemoteSubscriptionDownloader : IRemoteSubscriptionDownloader
{
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

        using var response = await client.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        AppLogger.Info($"Remote subscription downloaded: {request.SourceLocation}");
        var content = await HttpContentTextReader.ReadAsStringAsync(response.Content, cancellationToken);
        var trafficInfo = response.Headers.TryGetValues("subscription-userinfo", out var values)
            ? SubscriptionTrafficInfo.ParseHeader(values.FirstOrDefault() ?? string.Empty)
            : null;
        return new RemoteSubscriptionDownloadResult(content, trafficInfo);
    }
}
