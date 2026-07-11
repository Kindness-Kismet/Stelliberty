using System.Net.Http.Headers;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Updates;

namespace Stelliberty.Infrastructure.Updates;

public sealed class GitHubAppUpdateChecker : IAppUpdateChecker
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/Kindness-Kismet/stelliberty/releases?per_page=30";
    private const string LatestReleasePageUrl = "https://github.com/Kindness-Kismet/stelliberty/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();
    private readonly Func<string> _channelProvider;

    public GitHubAppUpdateChecker(Func<string>? channelProvider = null)
    {
        _channelProvider = channelProvider ?? (() => "stable");
    }

    public AppUpdateCheckResult CheckForUpdates()
    {
        AppLogger.Info("App update check requested");
        try
        {
            var channel = _channelProvider();
            var releases = FetchReleases();
            var selected = AppUpdateReleaseSelector.Select(releases, channel, AppMetadata.Version);
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
        catch (Exception exception)
        {
            AppLogger.Warning($"App update check failed: {exception.Message}");
            return new AppUpdateCheckResult(false, null, exception.Message, IsFailure: true);
        }
    }

    private static IReadOnlyList<AppUpdateReleaseInfo> FetchReleases()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
        using var response = Http.Send(request);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var releases = new List<AppUpdateReleaseInfo>();
        foreach (var item in document.RootElement.EnumerateArray())
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

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppRuntimeNames.FileNameToken, AppMetadata.Version));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
