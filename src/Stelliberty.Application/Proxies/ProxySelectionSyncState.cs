namespace Stelliberty.Application.Proxies;

public sealed class ProxySelectionSyncState
{
    private int _canImportCoreSelections;

    public bool CanImportCoreSelections => Volatile.Read(ref _canImportCoreSelections) == 1;

    public void EnableCoreSelectionImport()
    {
        Interlocked.Exchange(ref _canImportCoreSelections, 1);
    }

    public void DisableCoreSelectionImport()
    {
        Interlocked.Exchange(ref _canImportCoreSelections, 0);
    }
}
