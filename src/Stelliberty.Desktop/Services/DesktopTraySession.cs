using Stelliberty.Infrastructure.Tray;

namespace Stelliberty.Desktop.Services;

internal sealed class DesktopTraySession : IDisposable
{
    private readonly TrayIpcClient _client = new();
    private string? _sessionId;

    public event EventHandler? ActivationRequested;

    public async Task RegisterAsync(string sessionToken, CancellationToken cancellationToken)
    {
        _client.ActivationRequested += OnActivationRequested;
        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _client.HelloAsync(Environment.ProcessId, cancellationToken).ConfigureAwait(false);
        var result = await _client.RegisterUiAsync(
            sessionToken,
            Environment.ProcessId,
            cancellationToken).ConfigureAwait(false);
        _sessionId = result.SessionId;
    }

    public async Task UnregisterAsync(CancellationToken cancellationToken)
    {
        if (_sessionId is null)
        {
            return;
        }

        await _client.UnregisterUiAsync(_sessionId, cancellationToken).ConfigureAwait(false);
        _sessionId = null;
    }

    public Task ShutdownTrayAsync(CancellationToken cancellationToken) =>
        _client.ShutdownAsync(cancellationToken);

    private void OnActivationRequested(object? sender, EventArgs args) =>
        ActivationRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _client.ActivationRequested -= OnActivationRequested;
        _client.Dispose();
        _sessionId = null;
        ActivationRequested = null;
    }
}
