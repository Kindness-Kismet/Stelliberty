using System.Net;
using Stelliberty.Application.Overrides;
using Stelliberty.Application.Settings;
using Stelliberty.Domain.Overrides;
using Stelliberty.Infrastructure.Http;

namespace Stelliberty.Infrastructure.Overrides;

public sealed class HttpRemoteOverrideDownloader : IRemoteOverrideDownloader
{
    private readonly Func<(string Host, int Port)> _coreProxyEndpointProvider;

    public HttpRemoteOverrideDownloader(string coreProxyHost = "127.0.0.1", int coreProxyPort = AppSettings.DefaultMixedPort)
        : this(() => (coreProxyHost, coreProxyPort))
    {
    }

    public HttpRemoteOverrideDownloader(Func<(string Host, int Port)> coreProxyEndpointProvider)
    {
        _coreProxyEndpointProvider = coreProxyEndpointProvider;
    }

    public async Task<string> DownloadAsync(OverrideProfile overrideProfile, CancellationToken cancellationToken = default)
    {
        using var handler = new HttpClientHandler();
        if (overrideProfile.UpdateProxyMode == OverrideUpdateProxyMode.Direct)
        {
            handler.UseProxy = false;
        }
        else if (overrideProfile.UpdateProxyMode == OverrideUpdateProxyMode.SystemProxy)
        {
            handler.UseProxy = true;
            handler.Proxy = WebRequest.DefaultWebProxy;
        }
        else if (overrideProfile.UpdateProxyMode == OverrideUpdateProxyMode.Core)
        {
            var endpoint = _coreProxyEndpointProvider();
            handler.UseProxy = true;
            handler.Proxy = new WebProxy($"http://{endpoint.Host}:{endpoint.Port}");
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        using var response = await client.GetAsync(overrideProfile.SourceLocation, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await HttpContentTextReader.ReadAsStringAsync(response.Content, cancellationToken);
    }
}
