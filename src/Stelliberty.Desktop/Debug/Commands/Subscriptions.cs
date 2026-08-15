#if DEBUG
using System.Globalization;
using Avalonia.Input.Platform;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static async Task<string?> ExecuteSubscriptionsCommandAsync(MainWindow window, string command)
    {
        var viewModel = RequireViewModel(window);
        var page = viewModel.SubscriptionPage;
        var spec = command["subscriptions.".Length..].Trim();
        if (spec.StartsWith("add remote ", StringComparison.OrdinalIgnoreCase))
        {
            var item = await page.AddRemoteSubscriptionAsync(ParseSubscriptionRemoteArgs(spec["add remote ".Length..].Trim()));
            page.SelectSubscriptionCommand.Execute(item.Id);
            await WaitRuntimeRefreshAsync(viewModel);
            return $"id={item.Id};{SubscriptionState(page, viewModel)}";
        }

        if (spec.StartsWith("add local ", StringComparison.OrdinalIgnoreCase))
        {
            var item = page.AddLocalSubscription(ParseSubscriptionLocalArgs(spec["add local ".Length..].Trim()));
            page.SelectSubscriptionCommand.Execute(item.Id);
            await WaitRuntimeRefreshAsync(viewModel);
            return $"id={item.Id};{SubscriptionState(page, viewModel)}";
        }

        if (string.Equals(spec, "paste url", StringComparison.OrdinalIgnoreCase))
        {
            return await PasteSubscriptionAddUrlAsync(window, page);
        }

        if (spec.StartsWith("select ", StringComparison.OrdinalIgnoreCase))
        {
            page.SelectSubscriptionCommand.Execute(spec["select ".Length..].Trim());
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (string.Equals(spec, "update all", StringComparison.OrdinalIgnoreCase))
        {
            await page.UpdateAllSubscriptionsAsync();
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("update ", StringComparison.OrdinalIgnoreCase))
        {
            await page.UpdateSubscriptionAsync(spec["update ".Length..].Trim());
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

        if (string.Equals(spec, "state store", StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionStoreState(GetSubscriptionStore(window));
        }

        if (string.Equals(spec, "state selection", StringComparison.OrdinalIgnoreCase))
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

        if (spec.StartsWith("move up ", StringComparison.OrdinalIgnoreCase))
        {
            page.MoveSubscriptionUpCommand.Execute(spec["move up ".Length..].Trim());
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("move down ", StringComparison.OrdinalIgnoreCase))
        {
            page.MoveSubscriptionDownCommand.Execute(spec["move down ".Length..].Trim());
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("copy link ", StringComparison.OrdinalIgnoreCase))
        {
            page.CopyLinkCommand.Execute(spec["copy link ".Length..].Trim());
            return $"copied={page.CopiedLink ?? string.Empty};{SubscriptionState(page, viewModel)}";
        }

        if (spec.StartsWith("show qr ", StringComparison.OrdinalIgnoreCase))
        {
            page.ShowQrCodeCommand.Execute(spec["show qr ".Length..].Trim());
            return $"subscription={page.QrCodeSubscriptionId ?? string.Empty};dialog={page.IsQrCodeDialogVisible.ToString().ToLowerInvariant()}";
        }

        if (spec.StartsWith("chain-proxy.open ", StringComparison.OrdinalIgnoreCase))
        {
            page.ShowChainProxyDialogCommand.Execute(spec["chain-proxy.open ".Length..].Trim());
            return $"subscription={page.ChainProxy.DialogSubscriptionId ?? string.Empty};dialog={Bool(page.ChainProxy.IsDialogVisible)}";
        }

        if (spec.StartsWith("override-selector.move up ", StringComparison.OrdinalIgnoreCase))
        {
            page.OverrideSelector.MoveUpCommand.Execute(spec["override-selector.move up ".Length..].Trim());
            return OverrideSelectorOrder(page);
        }

        if (spec.StartsWith("override-selector.move down ", StringComparison.OrdinalIgnoreCase))
        {
            page.OverrideSelector.MoveDownCommand.Execute(spec["override-selector.move down ".Length..].Trim());
            return OverrideSelectorOrder(page);
        }

        if (spec.StartsWith("chain-proxy.move up ", StringComparison.OrdinalIgnoreCase))
        {
            MoveChainProxyHop(page.ChainProxy, FirstCommandToken(spec["chain-proxy.move up ".Length..]), -1);
            return ChainProxyHopOrder(page.ChainProxy);
        }

        if (spec.StartsWith("chain-proxy.move down ", StringComparison.OrdinalIgnoreCase))
        {
            MoveChainProxyHop(page.ChainProxy, FirstCommandToken(spec["chain-proxy.move down ".Length..]), 1);
            return ChainProxyHopOrder(page.ChainProxy);
        }

        if (string.Equals(spec, "chain-proxy.state", StringComparison.OrdinalIgnoreCase))
        {
            return ChainProxyState(page.ChainProxy);
        }

        if (spec.StartsWith("chain-proxy.toggle builtin ", StringComparison.OrdinalIgnoreCase))
        {
            page.ChainProxy.ToggleBuiltinCommand.Execute(FirstCommandToken(spec["chain-proxy.toggle builtin ".Length..]));
            return ChainProxyState(page.ChainProxy);
        }

        if (spec.StartsWith("chain-proxy.toggle custom ", StringComparison.OrdinalIgnoreCase))
        {
            page.ChainProxy.ToggleCustomCommand.Execute(FirstCommandToken(spec["chain-proxy.toggle custom ".Length..]));
            return ChainProxyState(page.ChainProxy);
        }

        if (string.Equals(spec, "chain-proxy.start add", StringComparison.OrdinalIgnoreCase))
        {
            page.ChainProxy.StartAddDraftCommand.Execute(null);
            return ChainProxyDraftState(page.ChainProxy);
        }

        if (spec.StartsWith("chain-proxy.set name ", StringComparison.OrdinalIgnoreCase))
        {
            page.ChainProxy.DraftName = FirstCommandToken(spec["chain-proxy.set name ".Length..]);
            return ChainProxyDraftState(page.ChainProxy);
        }

        if (spec.StartsWith("chain-proxy.set group ", StringComparison.OrdinalIgnoreCase))
        {
            var groupName = FirstCommandToken(spec["chain-proxy.set group ".Length..]);
            page.ChainProxy.DraftProxyGroup = page.ChainProxy.ProxyGroups.FirstOrDefault(group => group.Name == groupName)
                ?? throw new InvalidOperationException($"Chain proxy group not found: {groupName}");
            return ChainProxyDraftState(page.ChainProxy);
        }

        if (spec.StartsWith("chain-proxy.toggle hop ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["chain-proxy.toggle hop ".Length..]);
            if (tokens.Count < 2)
            {
                throw new InvalidOperationException("subscriptions.chain-proxy.toggle hop usage: subscriptions.chain-proxy.toggle hop <proxy|group> <name>");
            }

            var kind = tokens[0].ToLowerInvariant() switch
            {
                "proxy" => SubscriptionChainProxyHopKind.Proxy,
                "group" => SubscriptionChainProxyHopKind.ProxyGroup,
                _ => throw new InvalidOperationException($"Unknown chain proxy hop kind: {tokens[0]}")
            };
            page.ChainProxy.SelectCandidateCommand.Execute($"{kind}:{tokens[1]}");
            return ChainProxyDraftState(page.ChainProxy);
        }

        if (string.Equals(spec, "chain-proxy.save draft", StringComparison.OrdinalIgnoreCase))
        {
            page.ChainProxy.SaveDraftCommand.Execute(null);
            return ChainProxyDraftState(page.ChainProxy);
        }

        if (string.Equals(spec, "chain-proxy.save", StringComparison.OrdinalIgnoreCase))
        {
            page.ChainProxy.SaveCommand.Execute(null);
            await WaitRuntimeRefreshAsync(viewModel);
            return $"dialog={Bool(page.ChainProxy.IsDialogVisible)};{SubscriptionState(page, viewModel)}";
        }

        if (spec.StartsWith("chain-proxy.remove custom ", StringComparison.OrdinalIgnoreCase))
        {
            page.ChainProxy.RemoveCustomCommand.Execute(FirstCommandToken(spec["chain-proxy.remove custom ".Length..]));
            return ChainProxyState(page.ChainProxy);
        }

        if (spec.StartsWith("chain-proxy.edit custom ", StringComparison.OrdinalIgnoreCase))
        {
            page.ChainProxy.EditCustomCommand.Execute(FirstCommandToken(spec["chain-proxy.edit custom ".Length..]));
            return ChainProxyDraftState(page.ChainProxy);
        }

        if (string.Equals(spec, "chain-proxy.state draft", StringComparison.OrdinalIgnoreCase))
        {
            return ChainProxyDraftState(page.ChainProxy);
        }

        if (spec.StartsWith("open external-editor ", StringComparison.OrdinalIgnoreCase))
        {
            page.OpenExternalEditorCommand.Execute(spec["open external-editor ".Length..].Trim());
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("get runtime-config ", StringComparison.OrdinalIgnoreCase))
        {
            page.ShowRuntimeConfigDialogCommand.Execute(spec["get runtime-config ".Length..].Trim());
            return OutputValue(page.RuntimeConfigDialog.Content);
        }

        if (spec.StartsWith("save file ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["save file ".Length..].Trim());
            if (tokens.Count < 2)
            {
                throw new InvalidOperationException("subscriptions.save file usage: subscriptions.save file <subscription_id> <content>");
            }

            page.EditFileCommand.Execute(tokens[0]);
            page.FileEditor.Content = NormalizeInputValue(tokens[1]);
            page.FileEditor.ConfirmCommand.Execute(null);
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("edit metadata ", StringComparison.OrdinalIgnoreCase))
        {
            EditSubscriptionMetadata(page, spec["edit metadata ".Length..].Trim());
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("set overrides ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["set overrides ".Length..].Trim());
            if (tokens.Count < 1)
            {
                throw new InvalidOperationException("subscriptions.set overrides usage: subscriptions.set overrides <subscription_id> [override_id...]");
            }

            var overrideIds = tokens.Skip(1).Where(token => token != "__EMPTY__").ToList();
            page.SetOverridesForSubscription(tokens[0], overrideIds);
            await WaitRuntimeRefreshAsync(viewModel);
            return SubscriptionState(page, viewModel);
        }

        if (spec.StartsWith("provider.list ", StringComparison.OrdinalIgnoreCase))
        {
            await page.Provider.ShowAsync(spec["provider.list ".Length..].Trim());
            return ProviderRows(page);
        }

        if (string.Equals(spec, "provider.rows", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderRows(page);
        }

        if (spec.StartsWith("provider.sync ", StringComparison.OrdinalIgnoreCase)
            && !spec.StartsWith("provider.sync all ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["provider.sync ".Length..].Trim());
            if (tokens.Count < 2)
            {
                throw new InvalidOperationException("subscriptions.provider.sync usage: subscriptions.provider.sync <subscription_id> <provider_name>");
            }

            await page.Provider.ShowAsync(tokens[0]);
            await page.Provider.SyncProviderAsync(tokens[1]);
            return ProviderState(page);
        }

        if (spec.StartsWith("provider.sync all ", StringComparison.OrdinalIgnoreCase))
        {
            await page.Provider.ShowAsync(spec["provider.sync all ".Length..].Trim());
            await page.Provider.SyncAllProvidersAsync();
            return ProviderState(page);
        }

        if (spec.StartsWith("provider.upload ", StringComparison.OrdinalIgnoreCase))
        {
            var tokens = SplitCommandTokens(spec["provider.upload ".Length..].Trim());
            if (tokens.Count < 3)
            {
                throw new InvalidOperationException("subscriptions.provider.upload usage: subscriptions.provider.upload <subscription_id> <provider_name> <path>");
            }

            page.Provider.Show(tokens[0]);
            await page.Provider.UploadProviderAsync(tokens[1], tokens[2]);
            await WaitRuntimeRefreshAsync(viewModel);
            return ProviderState(page);
        }

        if (string.Equals(spec, "trigger auto-delay", StringComparison.OrdinalIgnoreCase))
        {
            return await RunSubscriptionAutoDelayTestTickAsync(window);
        }

        if (spec.StartsWith("set update-delay ", StringComparison.OrdinalIgnoreCase))
        {
            SetSubscriptionUpdateDelay(spec["set update-delay ".Length..].Trim());
            return null;
        }

        if (spec.StartsWith("edit file ", StringComparison.OrdinalIgnoreCase))
        {
            page.EditFileCommand.Execute(spec["edit file ".Length..].Trim());
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

    private static string OverrideSelectorOrder(SubscriptionPageViewModel page)
        => $"order={string.Join(',', page.OverrideSelector.OverrideSortPreference)}";

    private static void MoveChainProxyHop(SubscriptionChainProxyDialogViewModel dialog, string hopName, int offset)
    {
        var sourceIndex = dialog.Slots.ToList().FindIndex(slot => slot.DisplayName == hopName);
        if (sourceIndex < 0)
        {
            return;
        }

        dialog.MoveDraftNodeCommand.Execute(new SubscriptionChainProxyMoveRequest(dialog.Slots[sourceIndex].Key, sourceIndex + offset));
    }

    private static string ChainProxyHopOrder(SubscriptionChainProxyDialogViewModel dialog)
        => $"order={string.Join(',', dialog.Slots.Select(slot => slot.Key))}";

    // 内置项异步加载，打开弹窗后需经此命令读取稳定状态。
    private static string ChainProxyState(SubscriptionChainProxyDialogViewModel dialog)
    {
        return string.Join(";", [
            $"dialog={Bool(dialog.IsDialogVisible)}",
            $"builtins={string.Join(',', dialog.BuiltinItems.Select(item => $"{item.Name}:{(item.IsEnabled ? "on" : "off")}"))}",
            $"customs={string.Join(',', dialog.CustomChainProxies.Select(item => $"{item.Id}:{OutputValue(item.DisplayName)}:{(item.IsEnabled ? "on" : "off")}@{item.ProxyGroupName}[{string.Join('>', item.Hops.Select(hop => $"{hop.Kind}:{hop.Name}"))}]"))}"
        ]);
    }

    private static string ChainProxyDraftState(SubscriptionChainProxyDialogViewModel dialog)
    {
        return string.Join(";", [
            $"draft={Bool(dialog.IsEditingDraft)}",
            $"name={OutputValue(dialog.DraftName)}",
            $"group={dialog.DraftProxyGroup?.Name ?? string.Empty}",
            $"groups={string.Join(',', dialog.ProxyGroups.Select(group => group.Name))}",
            $"candidates={string.Join(',', dialog.Candidates.Select(candidate => $"{candidate.Kind}:{candidate.Name}"))}",
            $"order={string.Join(',', dialog.Slots.Select(slot => slot.Key))}",
            $"canSave={Bool(dialog.CanSaveDraft)}",
            $"nameError={Bool(dialog.IsDraftNameErrorVisible)}",
            $"groupError={Bool(dialog.IsDraftProxyGroupErrorVisible)}",
            $"hopsError={Bool(dialog.IsDraftNodesErrorVisible)}"
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
                "subscriptions.add remote usage: subscriptions.add remote <name> <url> [--ua <ua>] [--age-key <key>] [--auto disabled|startup|interval] [--interval <min>] [--proxy direct|system|core]");
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
            throw new InvalidOperationException("subscriptions.add local usage: subscriptions.add local <name> <path>");
        }

        return new SubscriptionAddLocalRequestedEventArgs(tokens[0], tokens[1], 0);
    }

    private static void EditSubscriptionMetadata(SubscriptionPageViewModel page, string spec)
    {
        var tokens = SplitCommandTokens(spec);
        if (tokens.Count < 1)
        {
            throw new InvalidOperationException(
                "subscriptions.edit metadata usage: subscriptions.edit metadata <subscription_id> [--name <name>] [--url <url>] [--ua <ua>] [--age-key <key>] [--delay <min>] [--auto disabled|startup|interval] [--interval <min>] [--proxy direct|system|core]");
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
            throw new InvalidOperationException("subscriptions.set update-delay usage: subscriptions.set update-delay <milliseconds>");
        }

        RemoteSubscriptionDownloader.DelayMilliseconds = milliseconds;
#endif
    }
}
#endif
