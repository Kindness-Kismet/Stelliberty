using Stelliberty.Domain.Overrides;
namespace Stelliberty.Application.Overrides;

public interface IRemoteOverrideDownloader
{
    Task<string> DownloadAsync(OverrideProfile overrideProfile, CancellationToken cancellationToken = default);
}
