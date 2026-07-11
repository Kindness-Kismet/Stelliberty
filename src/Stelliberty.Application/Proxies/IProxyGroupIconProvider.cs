namespace Stelliberty.Application.Proxies;

public interface IProxyGroupIconProvider
{
    Task<IReadOnlyDictionary<string, string>> LoadIconsAsync(CancellationToken cancellationToken = default);
}
