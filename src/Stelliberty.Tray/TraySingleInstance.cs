using Stelliberty.Infrastructure.Tray;

namespace Stelliberty.Tray;

internal sealed class TraySingleInstance : IDisposable
{
    private readonly FileStream? _lockStream;

    public TraySingleInstance()
    {
        TrayEndpoint.PrepareRuntimeDirectory();
        try
        {
            _lockStream = new FileStream(
                TrayEndpoint.LockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            OwnsInstance = true;
        }
        catch (IOException)
        {
            OwnsInstance = false;
        }
    }

    public bool OwnsInstance { get; }

    public void Dispose() => _lockStream?.Dispose();
}
