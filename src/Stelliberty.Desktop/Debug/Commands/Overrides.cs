#if DEBUG
using Avalonia.Input.Platform;
using Stelliberty.Presentation.ViewModels;
using OverrideFormat = Stelliberty.Domain.Overrides.OverrideFormat;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static async Task<string?> ExecuteOverridesCommandAsync(MainWindow window, string command)
    {
        var viewModel = RequireViewModel(window);
        var page = viewModel.OverridePage;
        var spec = command["overrides.".Length..].Trim();
        if (spec.StartsWith("add_remote ", StringComparison.OrdinalIgnoreCase))
        {
            var item = await page.AddRemoteOverrideAsync(ParseOverrideRemoteArgs(spec["add_remote ".Length..].Trim()));
            return item is null ? OverrideState(page) : $"id={item.Id};{OverrideState(page)}";
        }

        if (spec.StartsWith("add_local ", StringComparison.OrdinalIgnoreCase))
        {
            var item = await page.AddLocalOverrideAsync(ParseOverrideLocalArgs(spec["add_local ".Length..].Trim()));
            return item is null ? OverrideState(page) : $"id={item.Id};{OverrideState(page)}";
        }

        if (string.Equals(spec, "paste_url", StringComparison.OrdinalIgnoreCase))
        {
            return await PasteOverrideAddUrlAsync(window, page);
        }

        if (spec.StartsWith("create_blank ", StringComparison.OrdinalIgnoreCase))
        {
            var item = await page.CreateBlankOverrideAsync(ParseOverrideBlankArgs(spec["create_blank ".Length..].Trim()));
            return item is null ? OverrideState(page) : $"id={item.Id};{OverrideState(page)}";
        }

        if (spec.StartsWith("create_inline ", StringComparison.OrdinalIgnoreCase))
        {
            var args = ParseOverrideInlineArgs(spec["create_inline ".Length..].Trim());
            var item = await page.CreateBlankOverrideAsync(new OverrideAddCreateBlankRequestedEventArgs(args.Name, args.Format));
            if (item is null)
            {
                return OverrideState(page);
            }

            page.EditFileCommand.Execute(item.Id);
            page.FileEditor.Content = NormalizeInputValue(args.Content);
            page.FileEditor.ConfirmCommand.Execute(null);
            return $"id={item.Id};{OverrideState(page)}";
        }

        if (spec.StartsWith("select ", StringComparison.OrdinalIgnoreCase))
        {
            page.SelectOverride(spec["select ".Length..].Trim());
            return OverrideState(page);
        }

        if (spec.StartsWith("update ", StringComparison.OrdinalIgnoreCase))
        {
            await page.UpdateOverrideAsync(spec["update ".Length..].Trim());
            return OverrideState(page);
        }

        if (string.Equals(spec, "update_all", StringComparison.OrdinalIgnoreCase))
        {
            await page.UpdateAllOverridesAsync();
            return OverrideState(page);
        }

        if (spec.StartsWith("delete ", StringComparison.OrdinalIgnoreCase))
        {
            page.DeleteOverride(spec["delete ".Length..].Trim());
            return OverrideState(page);
        }

        if (spec.StartsWith("move_up ", StringComparison.OrdinalIgnoreCase))
        {
            page.MoveOverrideUp(spec["move_up ".Length..].Trim());
            return OverrideState(page);
        }

        if (spec.StartsWith("move_down ", StringComparison.OrdinalIgnoreCase))
        {
            page.MoveOverrideDown(spec["move_down ".Length..].Trim());
            return OverrideState(page);
        }

        if (spec.StartsWith("edit_metadata ", StringComparison.OrdinalIgnoreCase))
        {
            page.ShowEditDialog(spec["edit_metadata ".Length..].Trim());
            return OverrideState(page);
        }

        if (spec.StartsWith("edit_file ", StringComparison.OrdinalIgnoreCase))
        {
            page.EditFile(spec["edit_file ".Length..].Trim());
            return OverrideState(page);
        }

        if (spec.StartsWith("open_external_editor ", StringComparison.OrdinalIgnoreCase))
        {
            page.OpenExternalEditor(spec["open_external_editor ".Length..].Trim());
            return OverrideState(page);
        }

        if (spec.StartsWith("save_file ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["save_file ".Length..].Trim());
            if (tokens.Count < 2)
            {
                throw new InvalidOperationException("overrides.save_file usage: overrides.save_file <override_id> <content>");
            }

            page.EditFileCommand.Execute(tokens[0]);
            page.FileEditor.Content = NormalizeInputValue(tokens[1]);
            page.FileEditor.ConfirmCommand.Execute(null);
            await WaitRuntimeRefreshAsync(viewModel);
            return OverrideState(page);
        }

        if (string.Equals(spec, "list", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join("|", page.Overrides.Select(item =>
                $"{item.Id}\t{item.Name}\t{item.SourceLocation}\tformat={item.Format}\tlocal={item.IsLocalFile.ToString().ToLowerInvariant()}\tcurrent={(item.Id == page.CurrentOverrideId).ToString().ToLowerInvariant()}"));
        }

        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return OverrideState(page);
        }

        throw new InvalidOperationException($"Unknown overrides command: {command}");
    }

    private sealed record OverrideInlineArgs(string Name, string Content, OverrideFormat Format);

    private static string OverrideState(OverridePageViewModel page)
    {
        return string.Join(";", [
            $"total={page.Overrides.Count}",
            $"current={page.CurrentOverrideId ?? string.Empty}",
            $"batch={page.IsBatchUpdatingOverrides.ToString().ToLowerInvariant()}",
            $"updated={string.Join(',', page.UpdatedOverrideIds)}",
            $"updating={string.Join(',', page.UpdatingOverrideIds)}",
            $"skipped={string.Join(',', page.SkippedOverrideUpdateIds)}",
            $"deleted={string.Join(',', page.DeletedOverrideIds)}",
            $"dialog={page.IsDialogOverlayVisible.ToString().ToLowerInvariant()}"
        ]);
    }

    private static async Task<string> PasteOverrideAddUrlAsync(MainWindow window, OverridePageViewModel page)
    {
        if (!page.AddDialog.IsDialogVisible)
        {
            throw new InvalidOperationException("Override add dialog is not open");
        }

        var text = window.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() ?? string.Empty : string.Empty;
        page.AddDialog.SetClipboardTextAvailable(!string.IsNullOrWhiteSpace(text));
        page.AddDialog.PasteUrl(text);
        return $"url={OutputValue(page.AddDialog.SourceLocation)};canPaste={Bool(page.AddDialog.CanPasteUrlFromClipboard)}";
    }

    private static OverrideAddRemoteRequestedEventArgs ParseOverrideRemoteArgs(string spec)
    {
        var tokens = SplitCommandTokens(spec);
        if (tokens.Count < 2)
        {
            throw new InvalidOperationException("overrides.add_remote usage: overrides.add_remote <name> <url> [--format yaml|javascript] [--proxy direct|system|core]");
        }

        return new OverrideAddRemoteRequestedEventArgs(
            tokens[0],
            tokens[1],
            ParseOverrideFormat(ExtractFlag(tokens, "--format")),
            ParseOverrideProxyMode(ExtractFlag(tokens, "--proxy")));
    }

    private static OverrideAddLocalRequestedEventArgs ParseOverrideLocalArgs(string spec)
    {
        var tokens = SplitCommandTokens(spec);
        if (tokens.Count < 2)
        {
            throw new InvalidOperationException("overrides.add_local usage: overrides.add_local <name> <path> [--format yaml|javascript]");
        }

        return new OverrideAddLocalRequestedEventArgs(tokens[0], tokens[1], ParseOverrideFormat(ExtractFlag(tokens, "--format")));
    }

    private static OverrideAddCreateBlankRequestedEventArgs ParseOverrideBlankArgs(string spec)
    {
        var tokens = SplitCommandTokens(spec);
        if (tokens.Count < 1)
        {
            throw new InvalidOperationException("overrides.create_blank usage: overrides.create_blank <name> [--format yaml|javascript]");
        }

        return new OverrideAddCreateBlankRequestedEventArgs(tokens[0], ParseOverrideFormat(ExtractFlag(tokens, "--format")));
    }

    private static OverrideInlineArgs ParseOverrideInlineArgs(string spec)
    {
        var tokens = SplitCommandTokens(spec);
        if (tokens.Count < 2)
        {
            throw new InvalidOperationException("overrides.create_inline usage: overrides.create_inline <name> <content> [--format yaml|javascript]");
        }

        return new OverrideInlineArgs(tokens[0], tokens[1], ParseOverrideFormat(ExtractFlag(tokens, "--format")));
    }

    private static OverrideFormat ParseOverrideFormat(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "javascript" or "js" => OverrideFormat.JavaScript,
            _ => OverrideFormat.Yaml
        };
    }

    private static OverrideUpdateProxyMode ParseOverrideProxyMode(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "system" => OverrideUpdateProxyMode.SystemProxy,
            "core" => OverrideUpdateProxyMode.Core,
            _ => OverrideUpdateProxyMode.Direct
        };
    }
}
#endif
