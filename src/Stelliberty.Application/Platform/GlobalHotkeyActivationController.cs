namespace Stelliberty.Application.Platform;

public sealed class GlobalHotkeyActivationController(
    Action<GlobalHotkeyAction> activated,
    Func<long>? tickCount = null)
{
    private const long CooldownMilliseconds = 500;
    private readonly Func<long> _tickCount = tickCount ?? (() => Environment.TickCount64);
    private long _lastActivationAt;
    private bool _hasActivated;
    private bool _isSuppressed;

    public bool TryActivate(GlobalHotkeyAction action)
    {
        if (_isSuppressed)
        {
            return false;
        }

        var now = _tickCount();
        if (_hasActivated && now - _lastActivationAt < CooldownMilliseconds)
        {
            return false;
        }

        _lastActivationAt = now;
        _hasActivated = true;
        activated(action);
        return true;
    }

    public void SetSuppressed(bool isSuppressed)
    {
        _isSuppressed = isSuppressed;
    }
}
