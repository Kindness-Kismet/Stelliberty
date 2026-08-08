using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Updates;

namespace Stelliberty.Infrastructure.Updates;

public sealed class GitHubAppUpdateChecker : IAppUpdateChecker
{
    private static readonly Uri ReleasesApiUri = new("https://api.github.com/repos/Kindness-Kismet/stelliberty/releases?per_page=30");
    private const string LatestReleasePageUrl = "https://github.com/Kindness-Kismet/stelliberty/releases/latest";
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<string> _channelProvider;
    private readonly Func<(string Host, int Port)> _coreProxyEndpointProvider;

    public GitHubAppUpdateChecker(
        Func<string>? channelProvider = null,
        Func<(string Host, int Port)>? coreProxyEndpointProvider = null)
    {
        _channelProvider = channelProvider ?? (() => "stable");
        _coreProxyEndpointProvider = coreProxyEndpointProvider ?? (() => ("127.0.0.1", AppSettings.DefaultMixedPort));
    }

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        AppLogger.Info("App update check requested");
        using var totalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalCancellation.CancelAfter(TotalTimeout);
        Exception? lastFailure = null;

        foreach (var route in BuildRoutes())
        {
            if (totalCancellation.IsCancellationRequested)
            {
                break;
            }

            try
            {
                AppLogger.Info($"App update check attempt: route={route.Name}");
                var releases = await FetchReleasesAsync(route, totalCancellation.Token);
                var selected = AppUpdateReleaseSelector.Select(releases, _channelProvider(), AppMetadata.Version);
                AppLogger.Info($"App update check completed: route={route.Name}");
                if (selected is null)
                {
                    return new AppUpdateCheckResult(false, null, "You are already on the latest version");
                }

                return new AppUpdateCheckResult(
                    true,
                    selected.Version,
                    $"New version available: {selected.Version}",
                    selected.Url);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception)
            {
                lastFailure = new TimeoutException($"App update check timed out via {route.Name}", exception);
                AppLogger.Warning(lastFailure.Message);
            }
            catch (HttpRequestException exception)
            {
                lastFailure = exception;
                AppLogger.Warning($"App update check route failed: route={route.Name} error={exception.Message}");
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"App update check failed: {exception.Message}");
                return new AppUpdateCheckResult(false, null, exception.Message, IsFailure: true);
            }
        }

        var message = lastFailure?.Message ?? "App update check timed out";
        AppLogger.Warning($"App update check failed after all routes: {message}");
        return new AppUpdateCheckResult(false, null, message, IsFailure: true);
    }

    private IReadOnlyList<UpdateRoute> BuildRoutes()
    {
        var routes = new List<UpdateRoute> { new("direct", null) };
        Uri? systemProxyUri = null;
        try
        {
            var systemProxy = WebRequest.DefaultWebProxy;
            if (systemProxy is not null && !systemProxy.IsBypassed(ReleasesApiUri))
            {
                var resolvedProxy = systemProxy.GetProxy(ReleasesApiUri);
                if (resolvedProxy is not null && !SameEndpoint(resolvedProxy, ReleasesApiUri))
                {
                    systemProxyUri = resolvedProxy;
                    routes.Add(new UpdateRoute("system", systemProxy, UseDefaultCredentials: true));
                }
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"System proxy resolution failed for app update check: {exception.Message}");
        }

        try
        {
            var endpoint = _coreProxyEndpointProvider();
            var coreProxyUri = new UriBuilder(Uri.UriSchemeHttp, endpoint.Host, endpoint.Port).Uri;
            if (systemProxyUri is null || !SameEndpoint(systemProxyUri, coreProxyUri))
            {
                routes.Add(new UpdateRoute("core", new WebProxy(coreProxyUri)));
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Core proxy resolution failed for app update check: {exception.Message}");
        }

        return routes;
    }

    private static async Task<IReadOnlyList<AppUpdateReleaseInfo>> FetchReleasesAsync(
        UpdateRoute route,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseProxy = route.Proxy is not null,
            Proxy = route.Proxy,
            DefaultProxyCredentials = route.UseDefaultCredentials ? CredentialCache.DefaultCredentials : null
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptCancellation.CancelAfter(AttemptTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(AppRuntimeNames.FileNameToken, AppMetadata.Version));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            attemptCancellation.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(attemptCancellation.Token);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(stream, cancellationToken: attemptCancellation.Token);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("App update check returned invalid JSON", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new HttpRequestException("App update check returned an unexpected response");
            }

            return ReadReleases(document.RootElement);
        }
    }

    private static IReadOnlyList<AppUpdateReleaseInfo> ReadReleases(JsonElement root)
    {
        var releases = new List<AppUpdateReleaseInfo>();
        foreach (var item in root.EnumerateArray())
        {
            var version = ReadString(item, "tag_name");
            if (string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            var url = ReadString(item, "html_url") ?? LatestReleasePageUrl;
            var isDraft = item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True;
            var isPre = item.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
            releases.Add(new AppUpdateReleaseInfo(version, url, isPre, isDraft));
        }

        return releases;
    }

    private static bool SameEndpoint(Uri left, Uri right)
        => Uri.Compare(
            left,
            right,
            UriComponents.SchemeAndServer,
            UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0;

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private sealed record UpdateRoute(string Name, IWebProxy? Proxy, bool UseDefaultCredentials = false);
}
