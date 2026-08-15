using System.Text;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Overrides;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Proxies;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Overrides;
using Stelliberty.Domain.Proxies;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.ViewModels;
using Xunit;
using DomainSubscription = Stelliberty.Domain.Subscriptions.Subscription;

namespace Stelliberty.Subscription.Tests;

public sealed class SubscriptionBusinessTests
{
    [Fact(DisplayName = "Page selects first subscription when added")]
    public void PageSelectsFirstSubscriptionWhenAdded()
    {
        var selectionStore = new FakeSubscriptionSelectionStore();
        var page = new SubscriptionPageViewModel(subscriptionDeleter: CreateDeleter(), subscriptionSelectionStore: selectionStore);
        string? selected = null;
        page.SubscriptionSelected += (_, id) => selected = id;

        page.AddSubscription(Item("sub-1", "Remote", false));

        Assert.Equal("sub-1", page.CurrentSubscriptionId);
        Assert.Equal("sub-1", selectionStore.CurrentSubscriptionId);
        Assert.Equal("sub-1", selected);
        Assert.True(page.Subscriptions.Single().IsCurrent);
    }

    [Fact(DisplayName = "Page does not raise selection event when selecting current subscription again")]
    public void PageDoesNotRaiseSelectionEventWhenSelectingCurrentSubscriptionAgain()
    {
        var page = new SubscriptionPageViewModel(subscriptionDeleter: CreateDeleter());
        var eventCount = 0;
        page.AddSubscription(Item("sub-1", "Remote", false));
        page.SubscriptionSelected += (_, _) => eventCount++;

        page.SelectSubscriptionCommand.Execute("sub-1");

        Assert.Equal(0, eventCount);
    }

    [Fact(DisplayName = "Loading subscriptions clears a missing persisted selection without raising an event")]
    public void PageLoadSubscriptionsClearsMissingPersistedSelectionWithoutRaisingSelectionEvent()
    {
        var selectionStore = new FakeSubscriptionSelectionStore("missing");
        var page = new SubscriptionPageViewModel(subscriptionDeleter: CreateDeleter(), subscriptionSelectionStore: selectionStore);
        var eventCount = 0;
        page.SubscriptionSelected += (_, _) => eventCount++;

        page.LoadSubscriptions([Subscription("sub-1")]);

        Assert.Null(page.CurrentSubscriptionId);
        Assert.Null(selectionStore.CurrentSubscriptionId);
        Assert.False(page.Subscriptions.Single().IsCurrent);
        Assert.Equal(0, eventCount);
    }

    [Fact(DisplayName = "Page delete current subscription clears selection and raises null selection")]
    public void PageDeleteCurrentSubscriptionClearsSelectionAndRaisesNullSelection()
    {
        var store = new FakeSubscriptionStore([Subscription("sub-1"), Subscription("sub-2")]);
        var selectionStore = new FakeSubscriptionSelectionStore("sub-1");
        var deleter = new SubscriptionDeleter(store, selectionStore);
        var page = new SubscriptionPageViewModel(subscriptionDeleter: deleter, subscriptionSelectionStore: selectionStore);
        page.LoadSubscriptions(store.LoadSubscriptions());
        string? selected = "not-called";
        page.SubscriptionSelected += (_, id) => selected = id;

        page.ShowDeleteDialogCommand.Execute("sub-1");
        page.ConfirmDeleteCommand.Execute(null);

        Assert.Null(page.CurrentSubscriptionId);
        Assert.Null(selectionStore.CurrentSubscriptionId);
        Assert.Null(selected);
        Assert.Single(page.Subscriptions);
        Assert.False(page.IsDeleteDialogVisible);
    }

    [Fact(DisplayName = "Deleting a non-current subscription keeps current selection without raising an event")]
    public void PageDeleteNonCurrentSubscriptionKeepsCurrentSelectionWithoutRaisingSelectionEvent()
    {
        var store = new FakeSubscriptionStore([Subscription("sub-1"), Subscription("sub-2")]);
        var selectionStore = new FakeSubscriptionSelectionStore("sub-1");
        var deleter = new SubscriptionDeleter(store, selectionStore);
        var page = new SubscriptionPageViewModel(subscriptionDeleter: deleter, subscriptionSelectionStore: selectionStore);
        page.LoadSubscriptions(store.LoadSubscriptions());
        var eventCount = 0;
        page.SubscriptionSelected += (_, _) => eventCount++;

        page.ShowDeleteDialogCommand.Execute("sub-2");
        page.ConfirmDeleteCommand.Execute(null);

        Assert.Equal("sub-1", page.CurrentSubscriptionId);
        Assert.Equal("sub-1", selectionStore.CurrentSubscriptionId);
        Assert.Equal(0, eventCount);
        Assert.Equal(["sub-1"], page.Subscriptions.Select(subscription => subscription.Id));
        Assert.True(page.Subscriptions.Single().IsCurrent);
    }

    [Fact(DisplayName = "Page delete dialog rejects missing subscription target")]
    public void PageDeleteDialogRejectsMissingSubscriptionTarget()
    {
        var page = new SubscriptionPageViewModel(subscriptionDeleter: CreateDeleter());
        page.LoadSubscriptions([Subscription("sub-1")]);

        page.ShowDeleteDialogCommand.Execute("missing");
        page.ConfirmDeleteCommand.Execute(null);

        Assert.Null(page.DeleteDialogSubscriptionId);
        Assert.False(page.IsDeleteDialogVisible);
        Assert.False(page.IsDialogOverlayVisible);
        Assert.Equal(["sub-1"], page.Subscriptions.Select(subscription => subscription.Id));
    }

    [Fact(DisplayName = "Page external editor opens only existing subscriptions")]
    public void PageExternalEditorOpensOnlyExistingSubscriptions()
    {
        var opener = new FakeSubscriptionFileOpener();
        var page = new SubscriptionPageViewModel(
            subscriptionDeleter: CreateDeleter(),
            subscriptionFileOpener: opener);
        page.LoadSubscriptions([Subscription("remote"), Subscription("local", isLocal: true)]);
        var localExternalEditor = page.Subscriptions
            .Single(subscription => subscription.Id == "local")
            .MenuOptions
            .Single(option => option.Action == SubscriptionRowMenuAction.OpenExternalEditor);

        page.OpenExternalEditorCommand.Execute("missing");
        page.OpenExternalEditorCommand.Execute("remote");
        page.RowMenuActionCommand.Execute(localExternalEditor);
        page.SelectedRowMenuAction = new SubscriptionRowMenuSelection("missing", SubscriptionRowMenuAction.OpenExternalEditor, "Missing");

        Assert.Equal(["remote", "local"], opener.OpenedSubscriptionIds);
        Assert.Null(page.SelectedRowMenuAction);
    }

    [Fact(DisplayName = "Page copy link reports success only for remote subscriptions")]
    public void PageCopyLinkReportsSuccessOnlyForRemoteSubscriptions()
    {
        var clipboard = new FakeClipboardWriter();
        var page = new SubscriptionPageViewModel(
            subscriptionDeleter: CreateDeleter(),
            clipboardWriter: clipboard,
            localization: new FakeLocalizationService());
        page.LoadSubscriptions([Subscription("remote"), Subscription("local", isLocal: true)]);
        var toasts = new List<(string Message, ToastType Type)>();
        page.ToastRequested += (_, toast) => toasts.Add(toast);

        page.CopyLinkCommand.Execute("remote");
        page.CopyLinkCommand.Execute("local");
        page.CopyLinkCommand.Execute("missing");

        Assert.Equal(["https://sub.example/config.yaml"], clipboard.Texts);
        Assert.Equal([("订阅链接已复制", ToastType.Success)], toasts);
    }

    [Fact(DisplayName = "Page move subscription with transient item updates view but skips persisting order")]
    public void PageMoveSubscriptionWithTransientItemUpdatesViewButSkipsPersistingOrder()
    {
        var store = new FakeSubscriptionStore([Subscription("sub-1"), Subscription("sub-2")]);
        var page = new SubscriptionPageViewModel(
            subscriptionDeleter: CreateDeleter(),
            subscriptionStore: store);
        page.LoadSubscriptions(store.LoadSubscriptions());
        page.AddSubscription(Item("transient", "Transient", false));

        page.MoveSubscriptionCommand.Execute(new SubscriptionMoveRequest("transient", 0));

        Assert.Equal(["transient", "sub-1", "sub-2"], page.Subscriptions.Select(subscription => subscription.Id));
        Assert.Equal(0, store.SaveSubscriptionsCount);
        Assert.Equal(["sub-1", "sub-2"], store.LoadSubscriptions().Select(subscription => subscription.Id));
    }

    [Fact(DisplayName = "Page applies subscription update result")]
    public void PageAppliesSubscriptionUpdateResult()
    {
        var page = new SubscriptionPageViewModel(subscriptionDeleter: CreateDeleter());
        page.AddSubscription(Item("remote", "Remote", false));
        page.AddSubscription(Item("local", "Local", true));
        SubscriptionUpdateResult? raised = null;
        page.SubscriptionsUpdated += (_, result) => raised = result;

        page.ApplySubscriptionUpdateResult(new SubscriptionUpdateResult(["remote"], ["local"]));

        Assert.Equal(["remote"], page.UpdatedSubscriptionIds);
        Assert.Equal(["local"], page.SkippedSubscriptionUpdateIds);
        Assert.NotNull(raised);
    }

    [Fact(DisplayName = "Page remote subscription import reports success and failure toasts")]
    public async Task PageRemoteSubscriptionImportReportsSuccessAndFailureToasts()
    {
        var store = new FakeSubscriptionStore([]);
        var downloader = new FakeRemoteSubscriptionDownloader();
        var page = new SubscriptionPageViewModel(
            subscriptionDeleter: CreateDeleter(),
            remoteSubscriptionImporter: new RemoteSubscriptionImporter(store, downloader),
            localization: new FakeLocalizationService());
        var toasts = new List<(string Message, ToastType Type)>();
        page.ToastRequested += (_, toast) => toasts.Add(toast);

        await page.AddRemoteSubscriptionAsync(new SubscriptionAddRemoteRequestedEventArgs(
            "Remote",
            "https://sub.example/config.yaml",
            SubscriptionDefaults.UserAgent,
            0,
            SubscriptionAutoUpdateMode.Disabled,
            0,
            SubscriptionUpdateProxyMode.Direct));

        Assert.Contains(toasts, toast => toast is { Message: "远程订阅导入成功：Remote", Type: ToastType.Success });

        var failingPage = new SubscriptionPageViewModel(
            subscriptionDeleter: CreateDeleter(),
            remoteSubscriptionImporter: new RemoteSubscriptionImporter(
                new FakeSubscriptionStore([]),
                new FakeRemoteSubscriptionDownloader { NextException = new InvalidOperationException("download failed") }),
            localization: new FakeLocalizationService());
        (string Message, ToastType Type)? failureToast = null;
        failingPage.ToastRequested += (_, toast) => failureToast = toast;

        await Assert.ThrowsAsync<InvalidOperationException>(() => failingPage.AddRemoteSubscriptionAsync(new SubscriptionAddRemoteRequestedEventArgs(
            "Remote",
            "https://sub.example/config.yaml",
            SubscriptionDefaults.UserAgent,
            0,
            SubscriptionAutoUpdateMode.Disabled,
            0,
            SubscriptionUpdateProxyMode.Direct)));

        Assert.Equal(ToastType.Error, failureToast?.Type);
        Assert.Equal("远程订阅导入失败，请稍后重试", failureToast?.Message);
    }

    [Fact(DisplayName = "Remote subscription import decrypts age content before validation")]
    public async Task RemoteSubscriptionImportDecryptsAgeContentBeforeValidation()
    {
        const string encryptedContent = "-----BEGIN AGE ENCRYPTED FILE-----\nbody";
        const string decryptedContent = "proxies: []\nproxy-groups: []\nrules: []";
        var store = new FakeSubscriptionStore([]);
        var downloader = new FakeRemoteSubscriptionDownloader { Content = encryptedContent };
        var decryptor = new FakeSubscriptionContentDecryptor { Output = decryptedContent };
        var importer = new RemoteSubscriptionImporter(store, downloader, contentDecryptor: decryptor);

        var subscription = await importer.ImportAsync(new RemoteSubscriptionImportRequest(
            "Remote",
            "https://sub.example/config.yaml",
            AgeSecretKey: " <age-secret-key> "));

        Assert.Equal("<age-secret-key>", subscription.AgeSecretKey);
        Assert.Equal("<age-secret-key>", downloader.LastRequest?.AgeSecretKey);
        Assert.Equal(encryptedContent, decryptor.LastContent);
        Assert.Equal("<age-secret-key>", decryptor.LastAgeSecretKey);
        Assert.Equal(decryptedContent, store.ReadContent(subscription.Id));
    }

    [Fact(DisplayName = "Page local subscription import reports success and failure toasts")]
    public void PageLocalSubscriptionImportReportsSuccessAndFailureToasts()
    {
        var page = new SubscriptionPageViewModel(
            subscriptionDeleter: CreateDeleter(),
            localFileImporter: new LocalSubscriptionFileImporter(
                new LocalSubscriptionImporter(new FakeSubscriptionStore([])),
                new FakeLocalSubscriptionFileReader("proxies: []\nproxy-groups: []\nrules: []")),
            localization: new FakeLocalizationService());
        var toasts = new List<(string Message, ToastType Type)>();
        page.ToastRequested += (_, toast) => toasts.Add(toast);

        page.AddLocalSubscription(new SubscriptionAddLocalRequestedEventArgs("Local", "test-data/subscriptions/local.yaml", 0));

        Assert.Contains(toasts, toast => toast is { Message: "本地订阅导入成功：Local", Type: ToastType.Success });

        var failingPage = new SubscriptionPageViewModel(
            subscriptionDeleter: CreateDeleter(),
            localFileImporter: new LocalSubscriptionFileImporter(
                new LocalSubscriptionImporter(new FakeSubscriptionStore([])),
                new FakeLocalSubscriptionFileReader(string.Empty, new InvalidOperationException("read failed"))),
            localization: new FakeLocalizationService());
        (string Message, ToastType Type)? failureToast = null;
        failingPage.ToastRequested += (_, toast) => failureToast = toast;

        Assert.Throws<InvalidOperationException>(() => failingPage.AddLocalSubscription(new SubscriptionAddLocalRequestedEventArgs("Local", "test-data/subscriptions/local.yaml", 0)));

        Assert.Equal(ToastType.Error, failureToast?.Type);
        Assert.Equal("本地订阅导入失败，请稍后重试", failureToast?.Message);
    }

    [Fact(DisplayName = "Edit dialog refreshes confirm command when opened")]
    public void EditDialogRefreshesConfirmCommandWhenOpened()
    {
        var dialog = new SubscriptionEditDialogViewModel();
        var canExecuteChanged = false;
        dialog.ConfirmCommand.CanExecuteChanged += (_, _) => canExecuteChanged = true;

        dialog.Open(new SubscriptionItemViewModel(
            "remote",
            "Remote",
            "https://sub.example/config.yaml",
            isLocalFile: false));

        Assert.True(canExecuteChanged);
        Assert.True(dialog.ConfirmCommand.CanExecute(null));
    }

    [Fact(DisplayName = "Add dialog remote submit trims and normalizes options")]
    public void AddDialogRemoteSubmitTrimsAndNormalizesOptions()
    {
        var dialog = new SubscriptionAddDialogViewModel();
        SubscriptionAddRemoteRequestedEventArgs? requested = null;
        dialog.RemoteRequested += (_, args) => requested = args;
        dialog.Open();

        dialog.Name = " Remote ";
        dialog.Url = " https://sub.example/config.yaml ";
        dialog.UserAgent = " ";
        dialog.AgeSecretKey = " <age-secret-key> ";
        dialog.AutoTestDelayIntervalMinutes = 15;
        dialog.SelectedAutoUpdateMode = SubscriptionAutoUpdateMode.Interval;
        dialog.AutoUpdateIntervalMinutes = 60;
        dialog.SelectCoreProxyModeCommand.Execute(null);
        dialog.ConfirmCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal("Remote", requested.Name);
        Assert.Equal("https://sub.example/config.yaml", requested.Url);
        Assert.Equal(SubscriptionDefaults.UserAgent, requested.UserAgent);
        Assert.Equal("<age-secret-key>", requested.AgeSecretKey);
        Assert.Equal(15, requested.AutoTestDelayIntervalMinutes);
        Assert.Equal(SubscriptionAutoUpdateMode.Interval, requested.AutoUpdateMode);
        Assert.Equal(60, requested.AutoUpdateIntervalMinutes);
        Assert.Equal(SubscriptionUpdateProxyMode.Core, requested.UpdateProxyMode);
    }

    [Fact(DisplayName = "Add dialog user agent edit restores default when blank")]
    public void AddDialogUserAgentEditRestoresDefaultWhenBlank()
    {
        var dialog = new SubscriptionAddDialogViewModel();
        SubscriptionAddRemoteRequestedEventArgs? requested = null;
        dialog.RemoteRequested += (_, args) => requested = args;
        dialog.Open();

        Assert.Equal("Common.Default", dialog.UserAgentText);
        dialog.BeginUserAgentEdit();
        Assert.Equal(SubscriptionDefaults.UserAgent, dialog.UserAgentText);
        dialog.UserAgentText = " ";
        dialog.EndUserAgentEdit();
        dialog.Name = "Remote";
        dialog.Url = "https://sub.example/config.yaml";
        dialog.ConfirmCommand.Execute(null);

        Assert.Equal(SubscriptionDefaults.UserAgent, dialog.UserAgent);
        Assert.Equal("Common.Default", dialog.UserAgentText);
        Assert.NotNull(requested);
        Assert.Equal(SubscriptionDefaults.UserAgent, requested.UserAgent);
    }

    [Fact(DisplayName = "Add dialog auto-delay input recovers from invalid text")]
    public void AddDialogAutoTestDelayInputRecoversFromInvalidText()
    {
        var dialog = new SubscriptionAddDialogViewModel();
        SubscriptionAddRemoteRequestedEventArgs? requested = null;
        dialog.RemoteRequested += (_, args) => requested = args;
        dialog.Open();
        dialog.Name = "Remote";
        dialog.Url = "https://sub.example/config.yaml";

        dialog.BeginAutoTestDelayIntervalEdit();
        dialog.AutoTestDelayIntervalMinutesText = "-1";
        dialog.ConfirmCommand.Execute(null);

        Assert.True(dialog.IsAutoTestDelayIntervalErrorVisible);
        Assert.Equal("Subscriptions.Validation.Minutes", dialog.AutoTestDelayIntervalMinutesError);
        Assert.Null(requested);

        dialog.AutoTestDelayIntervalMinutesText = " ";
        dialog.EndAutoTestDelayIntervalEdit();

        Assert.False(dialog.IsAutoTestDelayIntervalErrorVisible);
        Assert.Equal("Common.Disable", dialog.AutoTestDelayIntervalMinutesText);
        Assert.True(dialog.CanSubmit);
        dialog.ConfirmCommand.Execute(null);
        Assert.NotNull(requested);
        Assert.Equal(0, requested.AutoTestDelayIntervalMinutes);
    }

    [Fact(DisplayName = "Add dialog local submit clears remote-only options")]
    public void AddDialogLocalSubmitClearsRemoteOnlyOptions()
    {
        var dialog = new SubscriptionAddDialogViewModel();
        SubscriptionAddLocalRequestedEventArgs? requested = null;
        dialog.LocalRequested += (_, args) => requested = args;
        dialog.Open();
        dialog.SelectedAutoUpdateMode = SubscriptionAutoUpdateMode.Interval;
        dialog.AutoUpdateIntervalMinutes = 30;
        dialog.SelectCoreProxyModeCommand.Execute(null);
        dialog.AgeSecretKey = "<age-secret-key>";

        dialog.SelectLocalImportCommand.Execute(null);
        dialog.Name = " Local ";
        dialog.LocalFilePath = " test-data/subscriptions/local.yaml ";
        dialog.AutoTestDelayIntervalMinutes = 5;
        dialog.ConfirmCommand.Execute(null);

        Assert.True(dialog.IsLocalImportSelected);
        Assert.False(dialog.IsRemoteOptionsVisible);
        Assert.Equal(SubscriptionAutoUpdateMode.Disabled, dialog.SelectedAutoUpdateMode);
        Assert.Equal(0, dialog.AutoUpdateIntervalMinutes);
        Assert.Equal(SubscriptionUpdateProxyMode.Direct, dialog.SelectedUpdateProxyMode);
        Assert.Equal("", dialog.AgeSecretKey);
        Assert.NotNull(requested);
        Assert.Equal("Local", requested.Name);
        Assert.Equal("test-data/subscriptions/local.yaml", requested.LocalFilePath);
        Assert.Equal(5, requested.AutoTestDelayIntervalMinutes);
    }

    [Fact(DisplayName = "Edit dialog local subscription submits only local options")]
    public void EditDialogLocalSubscriptionSubmitsOnlyLocalOptions()
    {
        var dialog = new SubscriptionEditDialogViewModel();
        SubscriptionEditCompletedEventArgs? completed = null;
        dialog.Confirmed += (_, args) => completed = args;
        dialog.Open(new SubscriptionItemViewModel(
            "local",
            "Local",
            "test-data/subscriptions/local.yaml",
            isLocalFile: true,
            userAgent: "custom",
            ageSecretKey: "<age-secret-key-local>",
            autoTestDelayIntervalMinutes: 10,
            autoUpdateMode: SubscriptionAutoUpdateMode.Interval,
            autoUpdateIntervalMinutes: 60,
            updateProxyMode: SubscriptionUpdateProxyMode.Core));

        dialog.Name = " Local New ";
        dialog.Url = " test-data/subscriptions/new.yaml ";
        dialog.UserAgent = "another";
        dialog.SelectedAutoUpdateMode = SubscriptionAutoUpdateMode.Interval;
        dialog.AutoUpdateIntervalMinutes = 90;
        dialog.SelectSystemProxyModeCommand.Execute(null);
        dialog.ConfirmCommand.Execute(null);

        Assert.NotNull(completed);
        Assert.True(completed.IsLocalFile);
        Assert.Equal("Local New", completed.Name);
        Assert.Equal("test-data/subscriptions/new.yaml", completed.Url);
        Assert.Equal("", completed.UserAgent);
        Assert.Equal("", completed.AgeSecretKey);
        Assert.Equal(10, completed.AutoTestDelayIntervalMinutes);
        Assert.Equal(SubscriptionAutoUpdateMode.Disabled, completed.AutoUpdateMode);
        Assert.Equal(0, completed.AutoUpdateIntervalMinutes);
        Assert.Equal(SubscriptionUpdateProxyMode.Direct, completed.UpdateProxyMode);
    }

    [Fact(DisplayName = "Edit dialog remote subscription trims and keeps remote options")]
    public void EditDialogRemoteSubscriptionTrimsAndKeepsRemoteOptions()
    {
        var dialog = new SubscriptionEditDialogViewModel();
        SubscriptionEditCompletedEventArgs? completed = null;
        dialog.Confirmed += (_, args) => completed = args;
        dialog.Open(new SubscriptionItemViewModel(
            "remote",
            "Remote",
            "https://sub.example/old.yaml",
            isLocalFile: false,
            userAgent: SubscriptionDefaults.UserAgent,
            ageSecretKey: "<age-secret-key-old>",
            autoTestDelayIntervalMinutes: 5,
            autoUpdateMode: SubscriptionAutoUpdateMode.Startup,
            autoUpdateIntervalMinutes: 0,
            updateProxyMode: SubscriptionUpdateProxyMode.Direct));

        dialog.Name = " Remote New ";
        dialog.Url = " https://sub.example/new.yaml ";
        dialog.UserAgent = " CustomUA ";
        dialog.AgeSecretKey = " <age-secret-key-new> ";
        dialog.AutoTestDelayIntervalMinutes = 12;
        dialog.SelectedAutoUpdateMode = SubscriptionAutoUpdateMode.Interval;
        dialog.AutoUpdateIntervalMinutes = 45;
        dialog.SelectCoreProxyModeCommand.Execute(null);
        dialog.ConfirmCommand.Execute(null);

        Assert.NotNull(completed);
        Assert.False(completed.IsLocalFile);
        Assert.Equal("remote", completed.SubscriptionId);
        Assert.Equal("Remote New", completed.Name);
        Assert.Equal("https://sub.example/new.yaml", completed.Url);
        Assert.Equal("CustomUA", completed.UserAgent);
        Assert.Equal("<age-secret-key-new>", completed.AgeSecretKey);
        Assert.Equal(12, completed.AutoTestDelayIntervalMinutes);
        Assert.Equal(SubscriptionAutoUpdateMode.Interval, completed.AutoUpdateMode);
        Assert.Equal(45, completed.AutoUpdateIntervalMinutes);
        Assert.Equal(SubscriptionUpdateProxyMode.Core, completed.UpdateProxyMode);
        Assert.False(dialog.IsDialogVisible);
    }

    [Fact(DisplayName = "Edit dialog keeps new subscription when reopened before reset")]
    public async Task EditDialogKeepsNewSubscriptionWhenReopenedBeforeReset()
    {
        var dialog = new SubscriptionEditDialogViewModel();
        SubscriptionEditCompletedEventArgs? completed = null;
        dialog.Confirmed += (_, args) => completed = args;

        dialog.Open(new SubscriptionItemViewModel("sub-1", "Sub 1", "https://sub.example/old.yaml", isLocalFile: false));
        dialog.Close();
        dialog.Open(new SubscriptionItemViewModel("sub-2", "Sub 2", "https://sub.example/new.yaml", isLocalFile: false));
        await Task.Delay(250);
        dialog.Name = "Sub 2 Changed";
        dialog.ConfirmCommand.Execute(null);

        Assert.NotNull(completed);
        Assert.Equal("sub-2", completed.SubscriptionId);
        Assert.Equal("Sub 2 Changed", completed.Name);
        Assert.Equal("https://sub.example/new.yaml", completed.Url);
    }

    [Fact(DisplayName = "Override selector saves sorted selection and cancel restores saved state")]
    public async Task OverrideSelectorSavesSortedSelectionAndCancelRestoresSavedState()
    {
        var selector = new SubscriptionOverrideSelectorViewModel();
        selector.LoadAvailable(
        [
            new SubscriptionOverrideOptionViewModel("a", "A", "YAML"),
            new SubscriptionOverrideOptionViewModel("b", "B", "JavaScript"),
            new SubscriptionOverrideOptionViewModel("c", "C", "YAML")
        ]);
        selector.ApplySaved(Subscription("sub-1") with { OverrideIds = ["a"], OverrideSortPreference = ["a", "b", "c"] });
        SubscriptionOverrideSelectionSaveRequestedEventArgs? saved = null;
        selector.SaveRequested += (_, args) => saved = args;
        selector.Open("sub-1");

        selector.ToggleSelectionCommand.Execute("c");
        selector.MoveUpCommand.Execute("c");
        selector.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal(["a", "c"], saved.SelectedOverrideIds);
        Assert.Equal(["a", "c", "b"], saved.OverrideSortPreference);

        selector.Open("sub-1");
        selector.ToggleSelectionCommand.Execute("b");
        selector.CancelCommand.Execute(null);
        await WaitUntilAsync(() => !selector.IsDialogVisible && selector.SelectedOverrideIds.SequenceEqual(["a"]));

        Assert.Equal(["a"], selector.SelectedOverrideIds);
        Assert.Equal(["a", "b", "c"], selector.OverrideSortPreference);
    }

    [Fact(DisplayName = "Chain proxy dialog loads context and saves built-in and custom changes")]
    public async Task ChainProxyDialogLoadsContextAndSavesBuiltinAndCustomChanges()
    {
        var dialog = new SubscriptionChainProxyDialogViewModel(contextLoader: _ => ChainContext(
            ["JP via HK"],
            [ProxyCandidate("HK", "ss"), ProxyCandidate("JP", "trojan")]));
        SubscriptionChainProxySaveEventArgs? saved = null;
        dialog.Saved += (_, args) => saved = args;

        dialog.Open("sub-1", [], []);
        await WaitUntilAsync(() => !dialog.IsLoading);
        dialog.ToggleBuiltinCommand.Execute("JP via HK");
        dialog.StartAddDraftCommand.Execute(null);
        dialog.DraftName = " JP via HK custom ";
        dialog.SelectCandidateCommand.Execute("Proxy:HK");
        dialog.SaveDraftCommand.Execute(null);
        Assert.False(dialog.IsDraftNodesErrorVisible);

        dialog.SelectCandidateCommand.Execute("Proxy:JP");
        dialog.SaveDraftCommand.Execute(null);
        dialog.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal(["JP via HK"], saved.DisabledBuiltinNames);
        var custom = Assert.Single(saved.CustomChainProxies);
        Assert.Equal("JP via HK custom", custom.DisplayName);
        Assert.Equal("GLOBAL", custom.ProxyGroupName);
        Assert.Equal(["HK", "JP"], custom.Hops.Select(hop => hop.Name));
    }

    [Fact(DisplayName = "Chain proxy dialog candidate toggle removes selected node and keeps order")]
    public async Task ChainProxyDialogCandidateToggleRemovesSelectedNodeAndKeepsOrder()
    {
        var dialog = new SubscriptionChainProxyDialogViewModel(contextLoader: _ => new SubscriptionChainProxyContext(
            [],
            [],
            [
                ProxyCandidate("HK", "ss"),
                ProxyCandidate("TW", "ss"),
                ProxyCandidate("JP", "trojan")
            ]));
        dialog.Open("sub-1", [], []);
        await WaitUntilAsync(() => !dialog.IsLoading);
        dialog.StartAddDraftCommand.Execute(null);

        dialog.SelectCandidateCommand.Execute("Proxy:JP");
        dialog.SelectCandidateCommand.Execute("Proxy:HK");
        dialog.SelectCandidateCommand.Execute("Proxy:JP");
        dialog.SelectCandidateCommand.Execute("Proxy:TW");

        Assert.Equal(["HK", "TW"], dialog.Slots.Select(slot => slot.DisplayName));
        var selectedByName = dialog.Candidates.ToDictionary(candidate => candidate.Name, candidate => candidate.IsSelected);
        Assert.True(selectedByName["HK"]);
        Assert.True(selectedByName["TW"]);
        Assert.False(selectedByName["JP"]);
    }

    [Fact(DisplayName = "Chain proxy dialog ignores stale context after subscription switch")]
    public async Task ChainProxyDialogIgnoresStaleContextAfterSubscriptionSwitch()
    {
        var sub1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sub1Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sub1Completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new SubscriptionChainProxyDialogViewModel(contextLoader: subscriptionId =>
        {
            if (subscriptionId == "sub-1")
            {
                sub1Started.TrySetResult();
                try
                {
                    if (!sub1Release.Task.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("sub-1 context release timed out");
                    }

                    return new SubscriptionChainProxyContext(["Old chain"], [], [ProxyCandidate("Old", "ss")]);
                }
                finally
                {
                    sub1Completed.TrySetResult();
                }
            }

            return new SubscriptionChainProxyContext(["New chain"], [], [ProxyCandidate("New", "trojan")]);
        });

        try
        {
            dialog.Open("sub-1", [], []);
            await WaitUntilAsync(() => sub1Started.Task.IsCompleted);
            dialog.Open("sub-2", [], []);
            await WaitUntilAsync(() => !dialog.IsLoading && dialog.BuiltinItems.Any(item => item.Name == "New chain"));
            sub1Release.TrySetResult();
            await WaitUntilAsync(() => sub1Completed.Task.IsCompleted);

            Assert.Equal("sub-2", dialog.DialogSubscriptionId);
            Assert.Equal(["New chain"], dialog.BuiltinItems.Select(item => item.Name));
            dialog.StartAddDraftCommand.Execute(null);
            Assert.Equal(["New"], dialog.Candidates.Select(candidate => candidate.Name));
        }
        finally
        {
            sub1Release.TrySetResult();
            if (sub1Started.Task.IsCompleted)
            {
                await WaitUntilAsync(() => sub1Completed.Task.IsCompleted);
            }
        }
    }

    [Fact(DisplayName = "Subscription file editor keeps new session when reopened before reset")]
    public async Task SubscriptionFileEditorKeepsNewSessionWhenReopenedBeforeReset()
    {
        var editor = new SubscriptionFileEditorViewModel();
        SubscriptionFileEditCompletedEventArgs? completed = null;
        editor.Confirmed += (_, args) => completed = args;

        editor.Open("sub-1", "old");
        editor.Close();
        editor.Open("sub-2", "new");
        await Task.Delay(250);
        editor.Content = "changed";
        editor.ConfirmCommand.Execute(null);

        Assert.NotNull(completed);
        Assert.Equal("sub-2", completed.SubscriptionId);
        Assert.Equal("changed", completed.Content);
        Assert.False(editor.IsDialogVisible);
    }

    [Fact(DisplayName = "Runtime config dialog keeps new preview when reopened before reset")]
    public async Task RuntimeConfigDialogKeepsNewPreviewWhenReopenedBeforeReset()
    {
        var dialog = new SubscriptionRuntimeConfigDialogViewModel();

        dialog.Open("sub-1", "mode: rule");
        dialog.Close();
        dialog.Open("sub-2", "mode: global");
        await Task.Delay(250);

        Assert.True(dialog.IsDialogVisible);
        Assert.Equal("sub-2", dialog.DialogSubscriptionId);
        Assert.Equal("mode: global", dialog.Content);

        dialog.ClearForSubscription("sub-1");
        Assert.True(dialog.IsDialogVisible);

        dialog.ClearForSubscription("sub-2");
        await WaitUntilAsync(() => !dialog.IsDialogVisible && dialog.DialogSubscriptionId is null);
        Assert.Equal("", dialog.Content);
    }

    [Fact(DisplayName = "Subscription updater disables auto update on permanent config failure")]
    public async Task SubscriptionUpdaterDisablesAutoUpdateOnPermanentConfigFailure()
    {
        var store = new FakeSubscriptionStore(
        [
            Subscription("remote") with
            {
                AutoUpdateMode = SubscriptionAutoUpdateMode.Interval,
                AutoUpdateIntervalMinutes = 30,
                UpdateProxyMode = SubscriptionUpdateProxyMode.Core
            }
        ]);
        var downloader = new FakeRemoteSubscriptionDownloader { Content = "not: clash" };
        var updater = new SubscriptionUpdater(store, downloader, now: () => DateTimeOffset.UnixEpoch.AddDays(1));

        var result = await updater.UpdateManyAsync(["remote", "missing"]);

        Assert.Empty(result.UpdatedSubscriptionIds);
        Assert.Equal(["remote", "missing"], result.SkippedSubscriptionIds);
        var updated = store.LoadSubscriptions().Single(item => item.Id == "remote");
        Assert.Equal(SubscriptionAutoUpdateMode.Disabled, updated.AutoUpdateMode);
        Assert.NotNull(updated.LastErrorAt);
        Assert.Contains("Configuration file", updated.LastError, StringComparison.Ordinal);
        Assert.Equal(SubscriptionUpdateProxyMode.Core, downloader.LastRequest?.ProxyMode);
    }

    [Fact(DisplayName = "Subscription updater disables auto update on permanent age decrypt failure")]
    public async Task SubscriptionUpdaterDisablesAutoUpdateOnPermanentAgeDecryptFailure()
    {
        var store = new FakeSubscriptionStore(
        [
            Subscription("remote") with
            {
                AgeSecretKey = "<age-secret-key>",
                AutoUpdateMode = SubscriptionAutoUpdateMode.Interval,
                AutoUpdateIntervalMinutes = 30
            }
        ]);
        var downloader = new FakeRemoteSubscriptionDownloader { Content = "-----BEGIN AGE ENCRYPTED FILE-----\nbody" };
        var decryptor = new FakeSubscriptionContentDecryptor
        {
            NextException = new InvalidOperationException("Age decryption failed: invalid identity")
        };
        var updater = new SubscriptionUpdater(store, downloader, now: () => DateTimeOffset.UnixEpoch.AddDays(1), contentDecryptor: decryptor);

        var result = await updater.UpdateAsync("remote");

        Assert.Empty(result.UpdatedSubscriptionIds);
        Assert.Equal(["remote"], result.SkippedSubscriptionIds);
        var updated = store.LoadSubscriptions().Single(item => item.Id == "remote");
        Assert.Equal(SubscriptionAutoUpdateMode.Disabled, updated.AutoUpdateMode);
        Assert.Contains("Age decryption failed", updated.LastError, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Subscription updater decrypts age content before saving content")]
    public async Task SubscriptionUpdaterDecryptsAgeContentBeforeSavingContent()
    {
        const string encryptedContent = "-----BEGIN AGE ENCRYPTED FILE-----\nbody";
        const string decryptedContent = "proxies: []\nproxy-groups: []\nrules: []";
        var store = new FakeSubscriptionStore(
        [
            Subscription("remote") with { AgeSecretKey = "<age-secret-key>" }
        ]);
        var downloader = new FakeRemoteSubscriptionDownloader { Content = encryptedContent };
        var decryptor = new FakeSubscriptionContentDecryptor { Output = decryptedContent };
        var updater = new SubscriptionUpdater(store, downloader, now: () => DateTimeOffset.UnixEpoch.AddDays(1), contentDecryptor: decryptor);

        var result = await updater.UpdateAsync("remote");

        Assert.Equal(["remote"], result.UpdatedSubscriptionIds);
        Assert.Equal("<age-secret-key>", downloader.LastRequest?.AgeSecretKey);
        Assert.Equal(encryptedContent, decryptor.LastContent);
        Assert.Equal("<age-secret-key>", decryptor.LastAgeSecretKey);
        Assert.Equal(decryptedContent, store.ReadContent("remote"));
    }

    [Fact(DisplayName = "Auto update planner filters startup and due interval subscriptions")]
    public void AutoUpdatePlannerFiltersStartupAndDueIntervalSubscriptions()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(2);
        var subscriptions = new[]
        {
            Subscription("startup") with { AutoUpdateMode = SubscriptionAutoUpdateMode.Startup },
            Subscription("local", isLocal: true) with { AutoUpdateMode = SubscriptionAutoUpdateMode.Startup },
            Subscription("due") with { AutoUpdateMode = SubscriptionAutoUpdateMode.Interval, AutoUpdateIntervalMinutes = 30, LastUpdatedAt = now.AddHours(-1) },
            Subscription("fresh") with { AutoUpdateMode = SubscriptionAutoUpdateMode.Interval, AutoUpdateIntervalMinutes = 30, LastUpdatedAt = now.AddMinutes(-5) }
        };
        var planner = new SubscriptionAutoUpdatePlanner();

        var startup = planner.PlanStartupUpdates(subscriptions);
        var interval = planner.PlanDueIntervalUpdates(subscriptions, now);

        Assert.Equal(["startup"], startup.UpdateSubscriptionIds);
        Assert.Equal(["due"], interval.UpdateSubscriptionIds);
        Assert.DoesNotContain("fresh", interval.UpdateSubscriptionIds);
    }

    [Fact(DisplayName = "Auto update planner waits one interval after failed attempt")]
    public void AutoUpdatePlannerWaitsOneIntervalAfterFailedAttempt()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(2);
        var subscription = Subscription("failed") with
        {
            AutoUpdateMode = SubscriptionAutoUpdateMode.Interval,
            AutoUpdateIntervalMinutes = 30,
            LastUpdatedAt = now.AddHours(-1),
            LastErrorAt = now.AddMinutes(-5)
        };
        var planner = new SubscriptionAutoUpdatePlanner();

        Assert.Empty(planner.PlanDueIntervalUpdates([subscription], now).UpdateSubscriptionIds);
        Assert.Equal(["failed"], planner.PlanDueIntervalUpdates([subscription], now.AddMinutes(25)).UpdateSubscriptionIds);
    }

    [Fact(DisplayName = "Auto update runner executes only subscriptions selected by planner")]
    public async Task AutoUpdateRunnerExecutesOnlySubscriptionsSelectedByPlanner()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(2);
        var store = new FakeSubscriptionStore(
        [
            Subscription("due") with
            {
                AutoUpdateMode = SubscriptionAutoUpdateMode.Interval,
                AutoUpdateIntervalMinutes = 30,
                LastUpdatedAt = now.AddHours(-1)
            },
            Subscription("fresh") with
            {
                AutoUpdateMode = SubscriptionAutoUpdateMode.Interval,
                AutoUpdateIntervalMinutes = 30,
                LastUpdatedAt = now.AddMinutes(-5)
            }
        ]);
        var downloader = new FakeRemoteSubscriptionDownloader();
        var runner = new SubscriptionAutoUpdateRunner(
            store,
            new SubscriptionAutoUpdatePlanner(),
            new SubscriptionUpdater(store, downloader, now: () => now));

        var result = await runner.RunDueIntervalUpdatesAsync(now);

        Assert.Equal(["due"], result.UpdatedSubscriptionIds);
        Assert.Empty(result.SkippedSubscriptionIds);
        Assert.Equal(["due"], downloader.Requests.Select(request => request.SubscriptionId));
    }

    [Fact(DisplayName = "Auto update runner executes startup subscriptions")]
    public async Task AutoUpdateRunnerExecutesStartupSubscriptions()
    {
        var store = new FakeSubscriptionStore(
        [
            Subscription("startup") with { AutoUpdateMode = SubscriptionAutoUpdateMode.Startup },
            Subscription("interval") with
            {
                AutoUpdateMode = SubscriptionAutoUpdateMode.Interval,
                AutoUpdateIntervalMinutes = 10
            }
        ]);
        var downloader = new FakeRemoteSubscriptionDownloader();
        var runner = new SubscriptionAutoUpdateRunner(
            store,
            new SubscriptionAutoUpdatePlanner(),
            new SubscriptionUpdater(store, downloader));

        var result = await runner.RunStartupUpdatesAsync();

        Assert.Equal(["startup"], result.UpdatedSubscriptionIds);
        Assert.Empty(result.SkippedSubscriptionIds);
        Assert.Equal(["startup"], downloader.Requests.Select(request => request.SubscriptionId));
    }

    [Fact(DisplayName = "Auto delay planner reschedules before first due run")]
    public void AutoDelayPlannerReschedulesBeforeFirstDueRun()
    {
        var planner = new SubscriptionAutoDelayPlanner();
        var now = DateTimeOffset.UnixEpoch;

        Assert.Equal(SubscriptionAutoDelayDecision.Rescheduled, planner.Evaluate("sub-1", 10, now));
        Assert.Equal(SubscriptionAutoDelayDecision.None, planner.Evaluate("sub-1", 10, now.AddMinutes(9)));
        Assert.Equal(SubscriptionAutoDelayDecision.Due, planner.Evaluate("sub-1", 10, now.AddMinutes(10)));
        planner.CompleteRun(10, now.AddMinutes(10));
        Assert.Equal(SubscriptionAutoDelayDecision.None, planner.Evaluate("sub-1", 10, now.AddMinutes(19)));
        Assert.Equal(SubscriptionAutoDelayDecision.None, planner.Evaluate(null, 10, now.AddMinutes(20)));
    }

    [Fact(DisplayName = "Auto delay planner reschedules when interval changes")]
    public void AutoDelayPlannerReschedulesWhenIntervalChanges()
    {
        var planner = new SubscriptionAutoDelayPlanner();
        var now = DateTimeOffset.UnixEpoch;

        Assert.Equal(SubscriptionAutoDelayDecision.Rescheduled, planner.Evaluate("sub-1", 10, now));
        Assert.Equal(SubscriptionAutoDelayDecision.Rescheduled, planner.Evaluate("sub-1", 20, now.AddMinutes(5)));
        Assert.Equal(SubscriptionAutoDelayDecision.None, planner.Evaluate("sub-1", 20, now.AddMinutes(24)));
        Assert.Equal(SubscriptionAutoDelayDecision.Due, planner.Evaluate("sub-1", 20, now.AddMinutes(25)));
    }

    [Fact(DisplayName = "Auto delay coordinator ignores overlapping due ticks")]
    public async Task AutoDelayCoordinatorIgnoresOverlappingDueTicks()
    {
        var now = DateTimeOffset.UnixEpoch;
        var subscriptionPage = new SubscriptionPageViewModel(subscriptionDeleter: CreateDeleter());
        subscriptionPage.AddSubscription(new SubscriptionItemViewModel(
            "sub-1",
            "Remote",
            "https://sub.example/config.yaml",
            isLocalFile: false,
            autoTestDelayIntervalMinutes: 1));
        var delayTester = new BlockingProxyDelayTester();
        var proxyPage = new ProxyPageViewModel(delayService: new ProxyDelayService(delayTester));
        proxyPage.LoadConfig(new ProxyConfig(
            [new ProxyGroup("Select", ProxyGroupTypes.Select, "Node", ["Node"])],
            new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
            {
                ["Node"] = new("Node", "ss", Server: "node.example", Port: 443),
            }));
        var coordinator = new SubscriptionAutoDelayCoordinator(subscriptionPage, proxyPage, () => now);

        await coordinator.RunDueAsync();
        now = now.AddMinutes(1);
        var firstTick = coordinator.RunDueAsync();
        await delayTester.Started.Task;

        await coordinator.RunDueAsync();

        Assert.Equal(1, delayTester.CallCount);
        delayTester.Release.TrySetResult();
        await firstTick;
    }

    [Fact(DisplayName = "Auto delay coordinator swallows failures and keeps next cycle")]
    public async Task AutoDelayCoordinatorSwallowsFailuresAndKeepsNextCycle()
    {
        var now = DateTimeOffset.UnixEpoch;
        var subscriptionPage = new SubscriptionPageViewModel(subscriptionDeleter: CreateDeleter());
        subscriptionPage.AddSubscription(new SubscriptionItemViewModel(
            "sub-1",
            "Remote",
            "https://sub.example/config.yaml",
            isLocalFile: false,
            autoTestDelayIntervalMinutes: 1));
        var delayTester = new ThrowingProxyDelayTester();
        var proxyPage = new ProxyPageViewModel(delayService: new ProxyDelayService(delayTester));
        proxyPage.LoadConfig(new ProxyConfig(
            [new ProxyGroup("Select", ProxyGroupTypes.Select, "Node", ["Node"])],
            new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
            {
                ["Node"] = new("Node", "ss", Server: "node.example", Port: 443),
            }));
        var coordinator = new SubscriptionAutoDelayCoordinator(subscriptionPage, proxyPage, () => now);

        await coordinator.RunDueAsync();
        now = now.AddMinutes(1);
        await coordinator.RunDueAsync();
        now = now.AddMinutes(1);
        await coordinator.RunDueAsync();

        Assert.Equal(2, delayTester.CallCount);
    }

    [Fact(DisplayName = "Provider parser handles YAML merge and counts")]
    public void ProviderParserHandlesYamlMergeAndCounts()
    {
        var providers = new SubscriptionProviderParser().Parse(
            """
            defaults: &defaults
              type: http
              path: ./provider.yaml
            proxy-providers:
              hk:
                <<: *defaults
                proxies:
                  - name: HK
                  - name: TW
            rule-providers:
              reject:
                <<: *defaults
                ruleCount: 3
            """);

        Assert.Contains(providers, provider => provider.Name == "hk" && provider.Type == "proxy" && provider.VehicleType == "HTTP" && provider.Count == 2);
        Assert.Contains(providers, provider => provider.Name == "reject" && provider.Type == "rule" && provider.Count == 3);
    }

    [Fact(DisplayName = "Provider catalog loader merges runtime state only for selected subscription")]
    public async Task ProviderCatalogLoaderMergesRuntimeStateOnlyForSelectedSubscription()
    {
        var store = new FakeSubscriptionStore([Subscription("sub-1"), Subscription("sub-2")]);
        store.SaveContent(
            "sub-1",
            """
            proxy-providers:
              hk:
                type: http
                path: ./hk.yaml
                proxies:
                  - name: static
            """);
        store.SaveContent(
            "sub-2",
            """
            proxy-providers:
              hk:
                type: http
                path: ./hk-other.yaml
                proxies:
                  - name: other
                  - name: other-2
            """);
        var selectionStore = new FakeSubscriptionSelectionStore("sub-1");
        var runtimeUpdatedAt = DateTimeOffset.UnixEpoch.AddHours(1);
        var loader = new SelectedSubscriptionProviderCatalogLoader(
            store,
            selectionStore,
            new SubscriptionProviderParser(),
            stateReader: new FakeSubscriptionProviderStateReader(
            [
                new SubscriptionProviderRuntimeState("hk", "proxy", 9, runtimeUpdatedAt)
            ]));

        var selected = await loader.LoadCatalogAsync("sub-1");
        var other = await loader.LoadCatalogAsync("sub-2");

        var selectedProvider = Assert.Single(selected.VisibleProviders);
        Assert.Equal(9, selectedProvider.Count);
        Assert.Equal(runtimeUpdatedAt, selectedProvider.UpdatedAt);
        var otherProvider = Assert.Single(other.VisibleProviders);
        Assert.Equal(2, otherProvider.Count);
        Assert.Null(otherProvider.UpdatedAt);
    }

    [Fact(DisplayName = "Provider selector switching subscription resets session state")]
    public async Task ProviderSelectorSwitchingSubscriptionResetsSessionState()
    {
        var store = new FakeSubscriptionStore([Subscription("sub-1"), Subscription("sub-2")]);
        store.SaveContent("sub-1", ProviderConfig("remote-a", "file-a"));
        store.SaveContent("sub-2", ProviderConfig("remote-b", "file-b"));
        var selectionStore = new FakeSubscriptionSelectionStore("sub-1");
        var syncer = new FakeSubscriptionProviderSyncer();
        var loader = new SelectedSubscriptionProviderCatalogLoader(
            store,
            selectionStore,
            new SubscriptionProviderParser(),
            syncer);
        var uploader = new FakeSubscriptionProviderUploader();
        var selector = new SubscriptionProviderViewModel(loader, uploader);

        await selector.ShowAsync("sub-1");
        selector.ProviderSearchKeyword = "remote-a";
        await selector.SyncProviderAsync("remote-a");
        await selector.UploadProviderAsync("file-a", "test-data/providers/file-a.yaml");

        Assert.Equal(["remote-a"], selector.SyncedProviderNames);
        Assert.Equal(["file-a"], selector.UploadedProviderNames);
        Assert.Equal(["remote-a", "file-a"], syncer.SyncRequests);
        Assert.True(selector.Providers.Single(item => item.Name == "remote-a").IsSynced);
        Assert.True(selector.Providers.Single(item => item.Name == "file-a").IsUploaded);

        await selector.ShowAsync("sub-2");

        Assert.Equal("sub-2", selector.ProviderSelectorSubscriptionId);
        Assert.Equal("", selector.ProviderSearchKeyword);
        Assert.Empty(selector.SyncedProviderNames);
        Assert.Empty(selector.UploadedProviderNames);
        Assert.Equal(["remote-b", "file-b"], selector.Providers.Select(item => item.Name));
        Assert.DoesNotContain(selector.Providers, item => item.IsSynced || item.IsUploaded);
    }

    [Fact(DisplayName = "Provider selector drops single sync result after subscription switch")]
    public async Task ProviderSelectorDropsSingleSyncResultAfterSubscriptionSwitch()
    {
        var store = new FakeSubscriptionStore([Subscription("sub-1"), Subscription("sub-2")]);
        store.SaveContent("sub-1", ProviderConfig("remote-a", "file-a"));
        store.SaveContent("sub-2", ProviderConfig("remote-b", "file-b"));
        var syncer = new BlockingSubscriptionProviderSyncer();
        var loader = new SelectedSubscriptionProviderCatalogLoader(
            store,
            new FakeSubscriptionSelectionStore("sub-1"),
            new SubscriptionProviderParser(),
            syncer);
        var selector = new SubscriptionProviderViewModel(loader, null);
        var syncedEvents = new List<SubscriptionProviderSyncCompletedEventArgs>();
        selector.ProvidersSynced += (_, args) => syncedEvents.Add(args);

        await selector.ShowAsync("sub-1");
        var syncTask = selector.SyncProviderAsync("remote-a");
        try
        {
            await syncer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await selector.ShowAsync("sub-2");

            syncer.Release.TrySetResult();
            await syncTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("sub-2", selector.ProviderSelectorSubscriptionId);
            Assert.Equal(["remote-b", "file-b"], selector.Providers.Select(item => item.Name));
            Assert.Empty(selector.SyncedProviderNames);
            Assert.Empty(syncedEvents);
            Assert.DoesNotContain(selector.Providers, item => item.IsSynced || item.IsSyncing);
            Assert.True(syncer.CancellationObserved);
        }
        finally
        {
            syncer.Release.TrySetResult();
        }
    }

    [Fact(DisplayName = "Provider selector sync all marks successes and reports failures")]
    public async Task ProviderSelectorSyncAllMarksSuccessesAndReportsFailures()
    {
        var store = new FakeSubscriptionStore([Subscription("sub-1")]);
        store.SaveContent(
            "sub-1",
            """
            proxy-providers:
              ok:
                type: http
                path: ./ok.yaml
                proxies:
                  - name: ok
              bad:
                type: http
                path: ./bad.yaml
                proxies:
                  - name: bad
              file:
                type: file
                path: ./file.yaml
                proxies:
                  - name: file
            """);
        var syncer = new FakeSubscriptionProviderSyncer { FailingProviderNames = ["bad"] };
        var loader = new SelectedSubscriptionProviderCatalogLoader(
            store,
            new FakeSubscriptionSelectionStore("sub-1"),
            new SubscriptionProviderParser(),
            syncer);
        var selector = new SubscriptionProviderViewModel(loader, null);
        (string Message, ToastType Type)? toast = null;
        SubscriptionProviderSyncCompletedEventArgs? synced = null;
        selector.ToastRequested += (_, args) => toast = args;
        selector.ProvidersSynced += (_, args) => synced = args;

        await selector.ShowAsync("sub-1");
        await selector.SyncAllProvidersAsync();

        Assert.Equal(["ok"], selector.SyncedProviderNames);
        Assert.True(selector.Providers.Single(item => item.Name == "ok").IsSynced);
        Assert.False(selector.Providers.Single(item => item.Name == "bad").IsSynced);
        Assert.False(selector.Providers.Single(item => item.Name == "file").IsSynced);
        Assert.Equal(["ok", "bad"], syncer.SyncRequests);
        Assert.True(selector.HasSyncedAllHttpProviders);
        Assert.True(selector.HasRefreshedProvidersAfterSync);
        Assert.False(selector.IsSyncingAll);
        Assert.DoesNotContain(selector.Providers, item => item.IsSyncing);
        Assert.Equal("sub-1", synced?.SubscriptionId);
        Assert.Equal(["ok"], synced?.SyncedProviderNames);
        Assert.Equal(ToastType.Error, toast?.Type);
        Assert.Equal("Subscriptions.Toast.ProviderSyncPartialFailed", toast?.Message);
    }

    [Fact(DisplayName = "Provider selector upload ignores blank path and keeps state on failure")]
    public async Task ProviderSelectorUploadIgnoresBlankPathAndKeepsStateOnFailure()
    {
        var uploader = new FakeSubscriptionProviderUploader { NextResult = SubscriptionProviderUploadResult.Skipped("format error") };
        var selector = new SubscriptionProviderViewModel(null, uploader);
        (string Message, ToastType Type)? toast = null;
        selector.ToastRequested += (_, args) => toast = args;
        selector.LoadProviders(
        [
            new SubscriptionProviderItemViewModel("file", "./file.yaml", "proxy", "File", 1, "Common.NotUpdated")
        ]);

        await selector.UploadProviderAsync("file", " ");
        await selector.UploadProviderAsync("file", "test-data/providers/file.yaml");

        var request = Assert.Single(uploader.UploadRequests);
        Assert.Equal("file", request.ProviderName);
        Assert.Equal("test-data/providers/file.yaml", request.SourcePath);
        Assert.Empty(selector.UploadedProviderNames);
        Assert.False(selector.Providers.Single().IsUploaded);
        Assert.False(selector.HasRefreshedProvidersAfterUpload);
        Assert.Equal(ToastType.Error, toast?.Type);
        Assert.Equal("Subscriptions.Toast.ProviderUploadFailed", toast?.Message);
    }

    [Fact(DisplayName = "Provider selector reloads runtime node count after file upload")]
    public async Task ProviderSelectorReloadsRuntimeNodeCountAfterFileUpload()
    {
        var store = new FakeSubscriptionStore([Subscription("sub-1")]);
        store.SaveContent(
            "sub-1",
            """
            proxy-providers:
              file:
                type: file
                path: ./file.yaml
            """);
        var stateReader = new SequenceSubscriptionProviderStateReader(
        [
            [new SubscriptionProviderRuntimeState("file", "proxy", 0, null)],
            [new SubscriptionProviderRuntimeState("file", "proxy", 1, DateTimeOffset.UnixEpoch.AddDays(1))],
        ]);
        var loader = new SelectedSubscriptionProviderCatalogLoader(
            store,
            new FakeSubscriptionSelectionStore("sub-1"),
            new SubscriptionProviderParser(),
            stateReader: stateReader);
        var selector = new SubscriptionProviderViewModel(loader, new FakeSubscriptionProviderUploader());

        await selector.ShowAsync("sub-1");
        Assert.Equal(0, Assert.Single(selector.Providers).Count);

        await selector.UploadProviderAsync("file", "test-data/providers/file.yaml");

        Assert.Equal(1, Assert.Single(selector.Providers).Count);
        Assert.True(selector.HasRefreshedProvidersAfterUpload);
        Assert.Equal(["file"], selector.UploadedProviderNames);
    }

    [Fact(DisplayName = "Provider selector keeps upload incomplete when core runtime refresh fails")]
    public async Task ProviderSelectorKeepsUploadIncompleteWhenCoreRuntimeRefreshFails()
    {
        var store = new FakeSubscriptionStore([Subscription("sub-1")]);
        store.SaveContent(
            "sub-1",
            """
            proxy-providers:
              file:
                type: file
                path: ./file.yaml
            """);
        var syncer = new FakeSubscriptionProviderSyncer { FailingProviderNames = ["file"] };
        var loader = new SelectedSubscriptionProviderCatalogLoader(
            store,
            new FakeSubscriptionSelectionStore("sub-1"),
            new SubscriptionProviderParser(),
            syncer);
        var selector = new SubscriptionProviderViewModel(loader, new FakeSubscriptionProviderUploader());
        (string Message, ToastType Type)? toast = null;
        SubscriptionProviderSyncCompletedEventArgs? synced = null;
        selector.ToastRequested += (_, args) => toast = args;
        selector.ProvidersSynced += (_, args) => synced = args;
        await selector.ShowAsync("sub-1");

        await selector.UploadProviderAsync("file", "test-data/providers/file.yaml");

        Assert.Equal(["file"], syncer.SyncRequests);
        Assert.Empty(selector.UploadedProviderNames);
        Assert.False(Assert.Single(selector.Providers).IsUploaded);
        Assert.False(selector.HasRefreshedProvidersAfterUpload);
        Assert.Null(synced);
        Assert.Equal(ToastType.Error, toast?.Type);
        Assert.Equal("Subscriptions.Toast.ProviderUploadFailed", toast?.Message);
    }

    [Fact(DisplayName = "Content normalizer keeps clash YAML and converts proxy links")]
    public void ContentNormalizerKeepsClashYamlAndConvertsProxyLinks()
    {
        var normalizer = new SubscriptionContentNormalizer();
        var clash = "proxy-groups: []\nproxies: []\nrules: []";
        var converted = normalizer.Normalize("ss://aes-128-gcm:pwd@server.example:8388#HK");

        Assert.Equal(clash, normalizer.Normalize(clash));
        Assert.Contains("proxy-groups", converted, StringComparison.Ordinal);
        Assert.Contains("HK", converted, StringComparison.Ordinal);
        Assert.Contains("MATCH,PROXY", converted, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Content normalizer converts base64 v2ray link subscription variants")]
    public void ContentNormalizerConvertsBase64V2RayLinkSubscriptionVariants()
    {
        var normalizer = new SubscriptionContentNormalizer();
        var vmessJson = """
            {"ps":"VMess WS","add":"vmess.example","port":"443","id":"11111111-1111-1111-1111-111111111111","aid":"0","scy":"auto","net":"ws","host":"ws.example","path":"/ws","tls":"tls","sni":"tls.example","fp":"chrome","alpn":"h2,http/1.1","allowInsecure":"1"}
            """;
        var vmess = $"vmess://{Base64UrlNoPadding(vmessJson)}";
        var vmessAead = "vmess://44444444-4444-4444-4444-444444444444@aead.example:443?encryption=auto&security=tls&type=http&host=h2.example&path=/h2&sni=aead.example&fp=chrome#VMess%20AEAD";
        var vless = "vless://22222222-2222-2222-2222-222222222222@vless.example:443?encryption=none&security=reality&sni=reality.example&fp=chrome&pbk=public-key&sid=ab12&type=grpc&serviceName=svc&flow=xtls-rprx-vision&allowInsecure=1&alpn=h2,http/1.1#VLESS%20Reality";
        var trojan = "trojan://secret@trojan.example:443?type=ws&host=trojan-host.example&path=/trojan&sni=trojan.example&fp=firefox&alpn=h2#Trojan%20WS";
        var shadowsocks = $"ss://{Base64UrlNoPadding("aes-128-gcm:pwd@ss.example:8388")}#SS%20Full";
        var hysteria2 = "hy2://hy-pass@hy2.example:443?sni=hy2.example&alpn=h3&pinSHA256=sha256-pin&up=30Mbps&down=100Mbps#HY2";
        var tuic = "tuic://33333333-3333-3333-3333-333333333333:tuic-pass@tuic.example:443?congestion_control=bbr&udp_relay_mode=native&alpn=h3&sni=tuic.example#TUIC";
        var encodedSubscription = Base64UrlNoPadding(string.Join('\n', [vmess, vmessAead, vless, trojan, shadowsocks, hysteria2, tuic]));

        var converted = normalizer.Normalize(encodedSubscription);

        new SubscriptionConfigValidator().Validate(converted);
        Assert.Contains("name: VMess WS", converted, StringComparison.Ordinal);
        Assert.Contains("type: vmess", converted, StringComparison.Ordinal);
        Assert.Contains("client-fingerprint: chrome", converted, StringComparison.Ordinal);
        Assert.Contains("name: VMess AEAD", converted, StringComparison.Ordinal);
        Assert.Contains("h2-opts", converted, StringComparison.Ordinal);
        Assert.Contains("name: VLESS Reality", converted, StringComparison.Ordinal);
        Assert.Contains("type: vless", converted, StringComparison.Ordinal);
        Assert.Contains("encryption: none", converted, StringComparison.Ordinal);
        Assert.Contains("reality-opts", converted, StringComparison.Ordinal);
        Assert.Contains("public-key: public-key", converted, StringComparison.Ordinal);
        Assert.Contains("short-id: ab12", converted, StringComparison.Ordinal);
        Assert.Contains("grpc-service-name: svc", converted, StringComparison.Ordinal);
        Assert.Contains("name: Trojan WS", converted, StringComparison.Ordinal);
        Assert.Contains("type: trojan", converted, StringComparison.Ordinal);
        Assert.Contains("name: SS Full", converted, StringComparison.Ordinal);
        Assert.Contains("server: ss.example", converted, StringComparison.Ordinal);
        Assert.Contains("name: HY2", converted, StringComparison.Ordinal);
        Assert.Contains("fingerprint: sha256-pin", converted, StringComparison.Ordinal);
        Assert.Contains("up: 30Mbps", converted, StringComparison.Ordinal);
        Assert.Contains("down: 100Mbps", converted, StringComparison.Ordinal);
        Assert.Contains("name: TUIC", converted, StringComparison.Ordinal);
        Assert.Contains("congestion-controller: bbr", converted, StringComparison.Ordinal);
        Assert.Contains("udp-relay-mode: native", converted, StringComparison.Ordinal);
        Assert.Contains("MATCH,PROXY", converted, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Override resolver orders selected overrides and rejects missing items")]
    public void OverrideResolverOrdersSelectedOverridesAndRejectsMissingItems()
    {
        var store = new FakeOverrideStore(
        [
            Override("a", "A"),
            Override("b", "B"),
            Override("c", "C")
        ]);
        var resolver = new SubscriptionOverrideResolver(store);
        var subscription = Subscription("sub-1") with
        {
            OverrideIds = ["a", "b", "c"],
            OverrideSortPreference = ["c", "a"]
        };

        var ordered = resolver.Resolve(subscription);

        Assert.Equal(["c", "a", "b"], ordered.Select(item => item.Id));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(subscription with { OverrideIds = ["missing"] }));
    }

    private static SubscriptionChainProxyContext ChainContext(
        IReadOnlyList<string> builtinNames,
        IReadOnlyList<ChainProxyHopOption> candidates)
    {
        return new SubscriptionChainProxyContext(
            builtinNames,
            [new ChainProxyGroupOption("GLOBAL", "select")],
            candidates);
    }

    private static ChainProxyHopOption ProxyCandidate(string name, string type)
        => new(new SubscriptionChainProxyHop(SubscriptionChainProxyHopKind.Proxy, name), type);

    private static SubscriptionItemViewModel Item(string id, string name, bool isLocal)
    {
        return new SubscriptionItemViewModel(id, name, isLocal ? "local.yaml" : "https://sub.example/config.yaml", isLocal);
    }

    private static DomainSubscription Subscription(string id, bool isLocal = false)
    {
        return new DomainSubscription(id, id, isLocal ? "local.yaml" : "https://sub.example/config.yaml", isLocal, DateTimeOffset.UnixEpoch);
    }

    private static OverrideProfile Override(string id, string name)
    {
        return new OverrideProfile(id, name, OverrideSourceType.Local, OverrideFormat.Yaml, $"{id}.yaml", DateTimeOffset.UnixEpoch);
    }

    private static string ProviderConfig(string remoteProviderName, string fileProviderName)
    {
        return $"""
            proxy-providers:
              {remoteProviderName}:
                type: http
                path: ./{remoteProviderName}.yaml
                proxies:
                  - name: remote-node
              {fileProviderName}:
                type: file
                path: ./{fileProviderName}.yaml
                proxies:
                  - name: file-node
            """;
    }

    private static string Base64UrlNoPadding(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 50; i++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate());
    }

    private static SubscriptionDeleter CreateDeleter()
    {
        return new SubscriptionDeleter(new FakeSubscriptionStore([]), new FakeSubscriptionSelectionStore());
    }

    private sealed class FakeSubscriptionSelectionStore(string? initial = null) : ISubscriptionSelectionStore
    {
        public string? CurrentSubscriptionId { get; private set; } = initial;

        public string? GetCurrentSubscriptionId() => CurrentSubscriptionId;

        public void SetCurrentSubscriptionId(string? subscriptionId)
        {
            CurrentSubscriptionId = subscriptionId;
        }
    }

    private sealed class FakeSubscriptionStore(IReadOnlyList<DomainSubscription> subscriptions) : ISubscriptionStore
    {
        private readonly List<DomainSubscription> _subscriptions = subscriptions.ToList();
        private readonly Dictionary<string, string> _configs = subscriptions.ToDictionary(item => item.Id, _ => "proxies: []\nproxy-groups: []\nrules: []", StringComparer.Ordinal);

        public int SaveSubscriptionsCount { get; private set; }

        public void Save(DomainSubscription subscription, string originalContent)
        {
            _subscriptions.Add(subscription);
            _configs[subscription.Id] = originalContent;
        }

        public void UpdateSubscription(DomainSubscription subscription)
        {
            var index = _subscriptions.FindIndex(item => item.Id == subscription.Id);
            if (index >= 0)
            {
                _subscriptions[index] = subscription;
            }
        }

        public void SaveSubscriptions(IReadOnlyList<DomainSubscription> subscriptions)
        {
            SaveSubscriptionsCount++;
            _subscriptions.Clear();
            _subscriptions.AddRange(subscriptions);
        }

        public void SaveContent(string subscriptionId, string originalContent)
        {
            _configs[subscriptionId] = originalContent;
        }

        public IReadOnlyList<DomainSubscription> LoadSubscriptions() => _subscriptions.ToList();

        public string ReadContent(string subscriptionId) => _configs[subscriptionId];

        public string GetContentPath(string subscriptionId) => $"{subscriptionId}.yaml";

        public void Delete(string subscriptionId)
        {
            _subscriptions.RemoveAll(item => item.Id == subscriptionId);
            _configs.Remove(subscriptionId);
        }
    }

    private sealed class FakeOverrideStore(IReadOnlyList<OverrideProfile> overrides) : IOverrideStore
    {
        private readonly List<OverrideProfile> _overrides = overrides.ToList();

        public void Save(OverrideProfile overrideProfile, string content)
        {
            _overrides.Add(overrideProfile);
        }

        public IReadOnlyList<OverrideProfile> LoadOverrides() => _overrides.ToList();

        public string ReadContent(string overrideId) => $"content-{overrideId}";

        public string GetContentPath(string overrideId) => $"{overrideId}.yaml";

        public void SaveOverrides(IReadOnlyList<OverrideProfile> overrides)
        {
            _overrides.Clear();
            _overrides.AddRange(overrides);
        }

        public void Delete(string overrideId)
        {
            _overrides.RemoveAll(item => item.Id == overrideId);
        }
    }

    private sealed class FakeSubscriptionProviderStateReader(IReadOnlyList<SubscriptionProviderRuntimeState> states) : ISubscriptionProviderStateReader
    {
        public Task<IReadOnlyList<SubscriptionProviderRuntimeState>> ReadStatesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(states);
        }
    }

    private sealed class FakeSubscriptionProviderSyncer : ISubscriptionProviderSyncer
    {
        public IReadOnlyList<string> FailingProviderNames { get; init; } = [];

        public List<string> SyncRequests { get; } = [];

        public Task SyncAsync(SubscriptionProvider provider, CancellationToken cancellationToken = default)
        {
            SyncRequests.Add(provider.Name);
            if (FailingProviderNames.Contains(provider.Name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("sync failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeSubscriptionFileOpener : ISubscriptionFileOpener
    {
        public List<string> OpenedSubscriptionIds { get; } = [];

        public void OpenSubscriptionFile(string subscriptionId)
        {
            OpenedSubscriptionIds.Add(subscriptionId);
        }
    }

    private sealed class FakeClipboardWriter : IClipboardWriter
    {
        public List<string> Texts { get; } = [];

        public void WriteText(string text)
        {
            Texts.Add(text);
        }
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.ZhHans;

        public AppLanguage EffectiveLanguage => CurrentLanguage;

        public event EventHandler? LanguageChanged;

        public void SetLanguage(AppLanguage language)
        {
            CurrentLanguage = language;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key)
        {
            return key switch
            {
                "Subscriptions.Toast.ImportRemoteSucceeded" => "远程订阅导入成功：{0}",
                "Subscriptions.Toast.ImportLocalSucceeded" => "本地订阅导入成功：{0}",
                "Subscriptions.Toast.ImportRemoteFailed" => "远程订阅导入失败，请稍后重试",
                "Subscriptions.Toast.ImportLocalFailed" => "本地订阅导入失败，请稍后重试",
                "Subscriptions.Toast.LinkCopied" => "订阅链接已复制",
                _ => key,
            };
        }
    }

    private sealed class FakeLocalSubscriptionFileReader(string content, Exception? exception = null) : ILocalSubscriptionFileReader
    {
        public string ReadAllText(string filePath)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return content;
        }
    }

    private sealed class BlockingSubscriptionProviderSyncer : ISubscriptionProviderSyncer
    {
        private int _cancellationObserved;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved => Volatile.Read(ref _cancellationObserved) == 1;

        public async Task SyncAsync(SubscriptionProvider provider, CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => Volatile.Write(ref _cancellationObserved, 1));
            Started.TrySetResult();
            await Release.Task;
        }
    }

    private sealed class FakeSubscriptionProviderUploader : ISubscriptionProviderUploader
    {
        public SubscriptionProviderUploadResult NextResult { get; init; } = SubscriptionProviderUploadResult.Uploaded();

        public List<(string ProviderName, string SourcePath)> UploadRequests { get; } = [];

        public Task<SubscriptionProviderUploadResult> UploadAsync(SubscriptionProvider provider, string sourcePath, CancellationToken cancellationToken = default)
        {
            UploadRequests.Add((provider.Name, sourcePath));
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeSubscriptionContentDecryptor : ISubscriptionContentDecryptor
    {
        public string Output { get; init; } = "proxies: []\nproxy-groups: []\nrules: []";
        public Exception? NextException { get; init; }
        public string? LastContent { get; private set; }
        public string? LastAgeSecretKey { get; private set; }

        public string DecryptIfNeeded(string content, string ageSecretKey)
        {
            LastContent = content;
            LastAgeSecretKey = ageSecretKey;
            if (NextException is not null)
            {
                throw NextException;
            }

            return Output;
        }
    }

    private sealed class FakeRemoteSubscriptionDownloader : IRemoteSubscriptionDownloader
    {
        public string Content { get; init; } = "proxies: []\nproxy-groups: []\nrules: []";
        public Exception? NextException { get; init; }
        public RemoteSubscriptionDownloadRequest? LastRequest { get; private set; }
        public List<RemoteSubscriptionDownloadRequest> Requests { get; } = [];

        public Task<RemoteSubscriptionDownloadResult> DownloadAsync(RemoteSubscriptionDownloadRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            Requests.Add(request);
            if (NextException is not null)
            {
                throw NextException;
            }

            return Task.FromResult(new RemoteSubscriptionDownloadResult(Content));
        }
    }

    private sealed class SequenceSubscriptionProviderStateReader(
        IReadOnlyList<IReadOnlyList<SubscriptionProviderRuntimeState>> states) : ISubscriptionProviderStateReader
    {
        private int _index;

        public Task<IReadOnlyList<SubscriptionProviderRuntimeState>> ReadStatesAsync(CancellationToken cancellationToken = default)
        {
            var current = states[Math.Min(_index, states.Count - 1)];
            _index++;
            return Task.FromResult(current);
        }
    }

    private sealed class ThrowingProxyDelayTester : IProxyDelayTester
    {
        public int CallCount { get; private set; }

        public Task<int> TestDelayAsync(string proxyName, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("delay backend offline");
        }
    }

    private sealed class BlockingProxyDelayTester : IProxyDelayTester
    {
        private int _callCount;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref _callCount);

        public async Task<int> TestDelayAsync(string proxyName, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            Started.TrySetResult();
            await Release.Task;
            return 10;
        }
    }
}
