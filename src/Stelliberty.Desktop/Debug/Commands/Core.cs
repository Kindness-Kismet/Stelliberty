#if DEBUG
namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static async Task<string?> ExecuteCoreCommandAsync(MainWindow window, string command)
    {
        var spec = command.StartsWith("core.", StringComparison.OrdinalIgnoreCase)
            ? command["core.".Length..].Trim()
            : "state";
        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            var viewModel = RequireViewModel(window);
            var coreManager = viewModel.CoreManager ?? throw new InvalidOperationException("CoreManager is not injected");
            return await CoreStatusFormatter.FormatAsync(coreManager).WaitAsync(TimeSpan.FromSeconds(8));
        }

        throw new InvalidOperationException($"Unknown core command: {command}");
    }
}
#endif
