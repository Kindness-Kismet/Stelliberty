using Stelliberty.Domain.Proxies;
namespace Stelliberty.Application.Proxies;

public sealed record QrCodeMatrix(int Size, IReadOnlyList<bool> Modules)
{
    public bool IsDark(int x, int y)
    {
        return Modules[y * Size + x];
    }
}
