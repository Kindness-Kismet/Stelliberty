#if DEBUG
using System.Globalization;
using Avalonia.Input.Platform;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static async Task<string?> ExecuteSubscriptionsCommandAsync(MainWindow window, string command)
    {
        var viewModel = RequireViewModel(window);
        var page = viewModel.SubscriptionPage;
        var spec = command["subscriptions.".Length..].Trim();
        if (spec.StartsWith("add_remote ", StringComparison.OrdinalIgnoreCase))
        {
            var item = await page.AddRemoteSubscriptionAsync(ParseSubscriptionRemoteArgs(spec["add_remote ".Length..].Trim()));
            page.SelectSubscriptionCommand.Execute(item.Id);
            await WaitRuntimeRefreshAsync(viewModel);
            return $"id={item.Id};{SubscriptionState(page, viewModel)}";
        }

        if (spec.StartsWith("add_local ", StringComparison.OrdinalIgnoreCase))
        {
            var item = page.AddLocalSubscription(ParseSubscriptionLocalArgs(spec["add_local ".Length..].Trim()));
            page.SelectSubscriptionCommand.Execute(item.Id);
            await WaitRuntimeRefreshAsync(viewModel);
            return $"id={item.Id};{SubscriptionState(page, viewModel)}";
        }

        if (string.Equals(spec, "paste_url", StringComparison.OrdinalIgnoreCase))
        {
            return await PasteSubscriptionAddUrlAsync(window, page);
        }

        if (spec.StartsWith("select ", StringComparison.OrdinalIgnoreCase))
        {
            page.SelectSubscriptionCommand.Execute(spec["select ".Length..].Trim());
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("update ", StringComparison.OrdinalIgnoreCase))
        {
            await page.UpdateSubscriptionAsync(spec["update ".Length..].Trim());
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (string.Equals(spec, "update_all", StringComparison.OrdinalIgnoreCase))
        {
            await page.UpdateAllSubscriptionsAsync();
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (string.Equals(spec, "list", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join("|", page.Subscriptions.Select(item =>
                $"{item.Id}\t{item.Name}\t{item.SourceLocation}\tlocal={item.IsLocalFile.ToString().ToLowerInvariant()}\tcurrent={item.IsCurrent.ToString().ToLowerInvariant()}\toverrides={item.OverrideCount}\ticon={item.IconType}\ticonTag={item.IconTag}\terror={OutputValue(item.LastError ?? string.Empty)}"));
        }

        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionState(page, viewModel);
        }

        if (string.Equals(spec, "store_state", StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionStoreState(GetSubscriptionStore(window));
        }

        if (string.Equals(spec, "selection_state", StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionSelectionState(GetSelectionStore(window), GetSubscriptionStore(window));
        }

        if (spec.StartsWith("delete ", StringComparison.OrdinalIgnoreCase))
        {
            page.ShowDeleteDialogCommand.Execute(spec["delete ".Length..].Trim());
            page.ConfirmDeleteCommand.Execute(null);
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("move_up ", StringComparison.OrdinalIgnoreCase))
        {
            page.MoveSubscriptionUpCommand.Execute(spec["move_up ".Length..].Trim());
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("move_down ", StringComparison.OrdinalIgnoreCase))
        {
            page.MoveSubscriptionDownCommand.Execute(spec["move_down ".Length..].Trim());
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("copy_link ", StringComparison.OrdinalIgnoreCase))
        {
            page.CopyLinkCommand.Execute(spec["copy_link ".Length..].Trim());
            return $"copied={page.CopiedLink ?? string.Empty};{SubscriptionState(page, viewModel)}";
        }

        if (spec.StartsWith("show_qr ", StringComparison.OrdinalIgnoreCase))
        {
            page.ShowQrCodeCommand.Execute(spec["show_qr ".Length..].Trim());
            return $"subscription={page.QrCodeSubscriptionId ?? string.Empty};dialog={page.IsQrCodeDialogVisible.ToString().ToLowerInvariant()}";
        }

        if (spec.StartsWith("chain_proxy ", StringComparison.OrdinalIgnoreCase))
        {
            page.ShowChainProxyDialogCommand.Execute(spec["chain_proxy ".Length..].Trim());
            return $"subscription={page.ChainProxy.DialogSubscriptionId ?? string.Empty};dialog={page.ChainProxy.IsDialogVisible.ToString().ToLowerInvariant()};builtins={page.ChainProxy.BuiltinItems.Count};customs={page.ChainProxy.CustomItems.Count}";
        }

        if (spec.StartsWith("open_external_editor ", StringComparison.OrdinalIgnoreCase))
        {
            page.OpenExternalEditorCommand.Execute(spec["open_external_editor ".Length..].Trim());
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("runtime_config ", StringComparison.OrdinalIgnoreCase))
        {
            page.ShowRuntimeConfigDialogCommand.Execute(spec["runtime_config ".Length..].Trim());
            return OutputValue(page.RuntimeConfigDialog.Content);
        }

        if (spec.StartsWith("save_file ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["save_file ".Length..].Trim());
            if (tokens.Count < 2)
            {
                throw new InvalidOperationException("subscriptions.save_file usage: subscriptions.save_file <subscription_id> <content>");
            }

            page.EditFileCommand.Execute(tokens[0]);
            page.FileEditor.Content = NormalizeInputValue(tokens[1]);
            page.FileEditor.ConfirmCommand.Execute(null);
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("edit_metadata ", StringComparison.OrdinalIgnoreCase))
        {
            EditSubscriptionMetadata(page, spec["edit_metadata ".Length..].Trim());
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("set_overrides ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["set_overrides ".Length..].Trim());
            if (tokens.Count < 1)
            {
                throw new InvalidOperationException("subscriptions.set_overrides usage: subscriptions.set_overrides <subscription_id> [override_id...]");
            }

            var overrideIds = tokens.Skip(1).Where(token => token != "__EMPTY__").ToList();
            page.SetOverridesForSubscription(tokens[0], overrideIds);
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("provider_list ", StringComparison.OrdinalIgnoreCase))
        {
            await page.Provider.ShowAsync(spec["provider_list ".Length..].Trim());
            return ProviderRows(page);
        }

        if (string.Equals(spec, "provider_rows", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderRows(page);
        }

        if (spec.StartsWith("provider_sync ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["provider_sync ".Length..].Trim());
            if (tokens.Count < 2)
            {
                throw new InvalidOperationException("subscriptions.provider_sync usage: subscriptions.provider_sync <subscription_id> <provider_name>");
            }

            await page.Provider.ShowAsync(tokens[0]);
            await page.Provider.SyncProviderAsync(tokens[1]);
            return ProviderState(page);
        }

        if (spec.StartsWith("provider_sync_all ", StringComparison.OrdinalIgnoreCase))
        {
            await page.Provider.ShowAsync(spec["provider_sync_all ".Length..].Trim());
            await page.Provider.SyncAllProvidersAsync();
            return ProviderState(page);
        }

        if (spec.StartsWith("provider_upload ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["provider_upload ".Length..].Trim());
            if (tokens.Count < 3)
            {
                throw new InvalidOperationException("subscriptions.provider_upload usage: subscriptions.provider_upload <subscription_id> <provider_name> <path>");
            }

            page.Provider.Show(tokens[0]);
            await page.Provider.UploadProviderAsync(tokens[1], tokens[2]);
            await WaitRuntimeRefreshAsync(viewModel);
            return ProviderState(page);
        }

        if (string.Equals(spec, "auto_delay_tick", StringComparison.OrdinalIgnoreCase))
        {
            return await RunSubscriptionAutoDelayTestTickAsync(window);
        }

        if (spec.StartsWith("set_update_delay ", StringComparison.OrdinalIgnoreCase))
        {
            SetSubscriptionUpdateDelay(spec["set_update_delay ".Length..].Trim());
            return null;
        }

        if (spec.StartsWith("edit_file ", StringComparison.OrdinalIgnoreCase))
        {
            page.EditFileCommand.Execute(spec["edit_file ".Length..].Trim());
            return null;
        }

        throw new InvalidOperationException($"Unknown subscriptions command: {command}");
    }

    private static string SubscriptionState(SubscriptionPageViewModel page, MainWindowViewModel viewModel)
    {
        return string.Join(";", [
            $"total={page.TotalSubscriptionCount}",
            $"current={page.CurrentSubscriptionId ?? string.Empty}",
            $"batch={page.IsBatchUpdatingSubscriptions.ToString().ToLowerInvariant()}",
            $"updated={string.Join(',', page.UpdatedSubscriptionIds)}",
            $"updating={string.Join(',', page.UpdatingSubscriptionIds)}",
            $"skipped={string.Join(',', page.SkippedSubscriptionUpdateIds)}",
            $"failed={string.Join(',', page.Subscriptions.Where(item => item.HasError).Select(item => item.Id))}",
            $"apply={viewModel.LastRuntimeApplyMode}",
            $"pid={viewModel.LastRuntimeApplyPid?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}",
            $"error={viewModel.LastRuntimeApplyError ?? string.Empty}",
            $"dialog={page.IsDialogOverlayVisible.ToString().ToLowerInvariant()}"
        ]);
    }

    private static string SubscriptionStoreState(ISubscriptionStore store)
    {
        var subscriptions = store.LoadSubscriptions();
        return string.Join(";", [
            $"total={subscriptions.Count}",
            $"remote={subscriptions.Count(item => !item.IsLocalFile)}",
            $"local={subscriptions.Count(item => item.IsLocalFile)}",
            $"ids={string.Join(',', subscriptions.Select(item => item.Id))}"
        ]);
    }

    private static string SubscriptionSelectionState(ISubscriptionSelectionStore selectionStore, ISubscriptionStore store)
    {
        var currentId = selectionStore.GetCurrentSubscriptionId();
        var exists = currentId is not null
            && store.LoadSubscriptions().Any(item => string.Equals(item.Id, currentId, StringComparison.Ordinal));
        return string.Join(";", [
            $"current={currentId ?? string.Empty}",
            $"exists={Bool(exists)}"
        ]);
    }

    private static string ProviderRows(SubscriptionPageViewModel page)
    {
        return string.Join("|", page.Provider.Providers.Select(item =>
            $"{item.Name}\t{item.DisplayName}\ttype={item.Type}\tvehicle={item.VehicleType}\tcount={item.Count}\tupdated={OutputValue(item.UpdatedAt)}\tcanSync={Bool(item.CanSync)}\tcanUpload={Bool(item.CanUpload)}\tsyncing={Bool(item.IsSyncing)}\tsynced={Bool(item.IsSynced)}\tuploaded={Bool(item.IsUploaded)}"));
    }

    private static string ProviderState(SubscriptionPageViewModel page)
    {
        return string.Join(";", [
            $"subscription={page.Provider.ProviderSelectorSubscriptionId ?? string.Empty}",
            $"providers={page.Provider.Providers.Count}",
            $"synced={string.Join(',', page.Provider.SyncedProviderNames)}",
            $"uploaded={string.Join(',', page.Provider.UploadedProviderNames)}",
            $"syncedAll={Bool(page.Provider.HasSyncedAllHttpProviders)}",
            $"refreshedAfterSync={Bool(page.Provider.HasRefreshedProvidersAfterSync)}",
            $"refreshedAfterUpload={Bool(page.Provider.HasRefreshedProvidersAfterUpload)}"
        ]);
    }

    private static async Task<string> PasteSubscriptionAddUrlAsync(MainWindow window, SubscriptionPageViewModel page)
    {
        if (!page.AddDialog.IsDialogVisible)
        {
            throw new InvalidOperationException("Subscription add dialog is not open");
        }

        var text = window.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() ?? string.Empty : string.Empty;
        page.AddDialog.SetClipboardTextAvailable(!string.IsNullOrWhiteSpace(text));
        page.AddDialog.PasteUrl(text);
        return $"url={OutputValue(page.AddDialog.Url)};canPaste={Bool(page.AddDialog.CanPasteUrlFromClipboard)}";
    }

    private static async Task WaitRuntimeRefreshAsync(MainWindowViewModel viewModel)
    {
        if (viewModel.LastRuntimeRefreshTask is { } task)
        {
            await task;
        }
    }

    private static SubscriptionAddRemoteRequestedEventArgs ParseSubscriptionRemoteArgs(string spec)
    {
        var tokens = SplitCommandTokens(spec);
        if (tokens.Count < 2)
        {
            throw new InvalidOperationException(
                "subscriptions.add_remote usage: subscriptions.add_remote <name> <url> [--ua <ua>] [--age-key <key>] [--auto disabled|startup|interval] [--interval <min>] [--proxy direct|system|core]");
        }

        return new SubscriptionAddRemoteRequestedEventArgs(
            tokens[0],
            tokens[1],
            ExtractFlag(tokens, "--ua") ?? string.Empty,
            0,
            ParseSubscriptionAutoUpdate(ExtractFlag(tokens, "--auto")),
            ParseInt(ExtractFlag(tokens, "--interval")),
            ParseSubscriptionUpdateProxy(ExtractFlag(tokens, "--proxy")),
            ExtractFlag(tokens, "--age-key") ?? string.Empty);
    }

    private static SubscriptionAddLocalRequestedEventArgs ParseSubscriptionLocalArgs(string spec)
    {
        var tokens = SplitCommandTokens(spec);
        if (tokens.Count < 2)
        {
            throw new InvalidOperationException("subscriptions.add_local usage: subscriptions.add_local <name> <path>");
        }

        return new SubscriptionAddLocalRequestedEventArgs(tokens[0], tokens[1], 0);
    }

    private static void EditSubscriptionMetadata(SubscriptionPageViewModel page, string spec)
    {
        var tokens = SplitCommandTokens(spec);
        if (tokens.Count < 1)
        {
            throw new InvalidOperationException(
                "subscriptions.edit_metadata usage: subscriptions.edit_metadata <subscription_id> [--name <name>] [--url <url>] [--ua <ua>] [--age-key <key>] [--delay <min>] [--auto disabled|startup|interval] [--interval <min>] [--proxy direct|system|core]");
        }

        page.ShowEditDialogCommand.Execute(tokens[0]);
        var editor = page.EditDialog;
        if (!editor.IsDialogVisible)
        {
            throw new InvalidOperationException($"Subscription not found: {tokens[0]}");
        }

        if (ExtractFlag(tokens, "--name") is { } name)
        {
            editor.Name = name;
        }

        if (ExtractFlag(tokens, "--url") is { } url)
        {
            editor.Url = url;
        }

        if (ExtractFlag(tokens, "--ua") is { } userAgent)
        {
            editor.UserAgent = userAgent;
        }

        if (ExtractFlag(tokens, "--age-key") is { } ageSecretKey)
        {
            editor.AgeSecretKey = ageSecretKey;
        }

        if (ExtractFlag(tokens, "--delay") is { } delay)
        {
            editor.AutoTestDelayIntervalMinutes = ParseInt(delay);
        }

        if (ExtractFlag(tokens, "--auto") is { } auto)
        {
            editor.SelectedAutoUpdateMode = ParseSubscriptionAutoUpdate(auto);
        }

        if (ExtractFlag(tokens, "--interval") is { } interval)
        {
            editor.AutoUpdateIntervalMinutes = ParseInt(interval);
        }

        if (ExtractFlag(tokens, "--proxy") is { } proxy)
        {
            editor.SelectedUpdateProxyMode = ParseSubscriptionUpdateProxy(proxy);
        }

        editor.ConfirmCommand.Execute(null);
    }

    private static SubscriptionAutoUpdateMode ParseSubscriptionAutoUpdate(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "startup" => SubscriptionAutoUpdateMode.Startup,
            "interval" => SubscriptionAutoUpdateMode.Interval,
            _ => SubscriptionAutoUpdateMode.Disabled
        };
    }

    private static SubscriptionUpdateProxyMode ParseSubscriptionUpdateProxy(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "system" => SubscriptionUpdateProxyMode.SystemProxy,
            "core" => SubscriptionUpdateProxyMode.Core,
            _ => SubscriptionUpdateProxyMode.Direct
        };
    }

    private static async Task<string> RunSubscriptionAutoDelayTestTickAsync(MainWindow window)
    {
        if (window.DataContext is not MainWindowViewModel viewModel)
        {
            throw new InvalidOperationException("DataContext is not ready");
        }

        await viewModel.SubscriptionAutoDelay.TickAsync();
        var visibleNodeNames = viewModel.ProxyPage.VisibleNodeRows.Select(row => row.Name).ToHashSet(StringComparer.Ordinal);
        return string.Join("|", viewModel.ProxyPage.BatchDelayTestedNodeNames.Where(visibleNodeNames.Contains));
    }

    private static void SetSubscriptionUpdateDelay(string value)
    {
#if DEBUG
        if (!int.TryParse(value, out var milliseconds) || milliseconds < 0)
        {
            throw new InvalidOperationException("subscriptions.set_update_delay usage: subscriptions.set_update_delay <milliseconds>");
        }

        RemoteSubscriptionDownloader.DelayMilliseconds = milliseconds;
#endif
    }
}
#endif
