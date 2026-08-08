using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Services;

internal enum TrayIconState
{
    Disabled,
    ProxyEnabled,
    TunEnabled,
    ProxyTunEnabled,
}

internal static class TrayIconFactory
{
    public static WindowIcon Create(TrayIconState state)
    {
        var uri = new Uri($"avares://{AppRuntimeNames.ResourceAuthority}/Assets/{PlatformDir()}/tray/{FileName(state)}.{PlatformExt()}");
        using var stream = AssetLoader.Open(uri);
        return new WindowIcon(stream);
    }

    private static string FileName(TrayIconState state)
    {
        return state switch
        {
            TrayIconState.ProxyEnabled => "proxy_enabled",
            TrayIconState.TunEnabled => "tun_enabled",
            TrayIconState.ProxyTunEnabled => "proxy_tun_enabled",
            _ => "disabled",
        };
    }

    private static string PlatformDir()
    {
        if (OperatingSystem.IsWindows()) return "win";
        if (OperatingSystem.IsMacOS()) return "macos";
        return "linux";
    }

    private static string PlatformExt() => OperatingSystem.IsWindows() ? "ico" : "png";
}
