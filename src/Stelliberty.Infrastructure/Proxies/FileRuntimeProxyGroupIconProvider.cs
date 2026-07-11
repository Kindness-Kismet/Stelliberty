using Stelliberty.Application.Proxies;

namespace Stelliberty.Infrastructure.Proxies;

public sealed class FileRuntimeProxyGroupIconProvider(
    IProxyConfigSource source,
    ProxyConfigParser parser) : IProxyGroupIconProvider
{
    private readonly object _cacheGate = new();
    private string? _cachedContent;
    private IReadOnlyDictionary<string, string> _cachedIcons = new Dictionary<string, string>(StringComparer.Ordinal);

    public Task<IReadOnlyDictionary<string, string>> LoadIconsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = source.ReadRuntimeConfig();
            lock (_cacheGate)
            {
                if (string.Equals(content, _cachedContent, StringComparison.Ordinal))
                {
                    return _cachedIcons;
                }
            }

            var icons = ParseIcons(content);
            lock (_cacheGate)
            {
                _cachedContent = content;
                _cachedIcons = icons;
                return _cachedIcons;
            }
        }, cancellationToken);
    }

    private IReadOnlyDictionary<string, string> ParseIcons(string content)
    {
        var icons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in parser.Parse(content).Groups)
        {
            if (!string.IsNullOrWhiteSpace(group.Icon))
            {
                icons[group.Name] = group.Icon;
            }
        }

        return icons;
    }
}
