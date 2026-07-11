using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Infrastructure.Overrides;

namespace Stelliberty.Desktop.Debug;

#if DEBUG
internal sealed class RemoteOverrideDownloader : IRemoteOverrideDownloader
{
    public Task<string> DownloadAsync(OverrideProfile overrideProfile, CancellationToken cancellationToken = default)
    {
        return new HttpRemoteOverrideDownloader().DownloadAsync(overrideProfile, cancellationToken);
    }
}
#endif
