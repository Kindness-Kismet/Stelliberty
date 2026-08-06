namespace Stelliberty.Desktop;

internal enum DesktopLaunchMode
{
    Launcher,
    TrayUi,
    DirectUi,
    Invalid,
}

internal sealed record DesktopLaunchArguments(
    DesktopLaunchMode Mode,
    string? TraySessionToken,
    string[] AvaloniaArguments)
{
    private const string UiArgument = "--ui";
    private const string DirectUiArgument = "--direct-ui";
    private const string TraySessionArgument = "--tray-session";

    public static DesktopLaunchArguments Parse(string[] args)
    {
        if (args.Contains(DirectUiArgument, StringComparer.Ordinal))
        {
            return new DesktopLaunchArguments(
                DesktopLaunchMode.DirectUi,
                null,
                args.Where(argument => argument != DirectUiArgument).ToArray());
        }

        var uiIndex = Array.IndexOf(args, UiArgument);
        if (uiIndex < 0)
        {
            return new DesktopLaunchArguments(DesktopLaunchMode.Launcher, null, args);
        }

        var sessionIndex = Array.IndexOf(args, TraySessionArgument);
        if (sessionIndex < 0 || sessionIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[sessionIndex + 1]))
        {
            return new DesktopLaunchArguments(DesktopLaunchMode.Invalid, null, []);
        }

        var internalIndexes = new HashSet<int> { uiIndex, sessionIndex, sessionIndex + 1 };
        var avaloniaArguments = args
            .Where((_, index) => !internalIndexes.Contains(index))
            .ToArray();
        return new DesktopLaunchArguments(
            DesktopLaunchMode.TrayUi,
            args[sessionIndex + 1],
            avaloniaArguments);
    }
}
