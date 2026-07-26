using Stelliberty.Application.Localization;
using Stelliberty.Application.Overrides;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Overrides;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Overrides;
using Stelliberty.Infrastructure.Subscriptions;
using Stelliberty.Presentation.ViewModels;
using Xunit;

namespace Stelliberty.Override.Tests;

public sealed class OverrideBusinessTests
{
    [Fact(DisplayName = "Page applies override update result")]
    public void PageAppliesOverrideUpdateResult()
    {
        var page = new OverridePageViewModel(overrideDeleter: new OverrideDeleter(
            overrideStore: new InMemoryOverrideStore(),
            subscriptionStore: new InMemorySubscriptionStore()));
        page.AddOverride(Item("remote", isLocal: false));
        page.AddOverride(Item("local", isLocal: true));
        OverrideUpdateResult? raised = null;
        page.OverridesUpdated += (_, result) => raised = result;

        page.ApplyOverrideUpdateResult(new OverrideUpdateResult(["remote"], ["local"]));

        Assert.Equal(["remote"], page.UpdatedOverrideIds);
        Assert.Equal(["local"], page.SkippedOverrideUpdateIds);
        Assert.NotNull(raised);
    }

    [Fact(DisplayName = "Delete override clears selection and removes subscription references")]
    public void DeleteOverrideClearsSelectionAndRemovesSubscriptionReferences()
    {
        var overrideStore = new FakeOverrideStore([Override("a"), Override("b")]);
        var subscriptionStore = new FakeSubscriptionStore(
        [
            new Subscription("sub-1", "Sub", "source", false, DateTimeOffset.UnixEpoch, OverrideIds: ["a", "b"], OverrideSortPreference: ["b", "a"])
        ]);
        var page = new OverridePageViewModel(
            overrideStore: overrideStore,
            overrideDeleter: new OverrideDeleter(overrideStore, subscriptionStore));
        page.LoadOverrides(overrideStore.LoadOverrides());
        page.SelectOverrideCommand.Execute("a");
        OverrideDeleteResult? raised = null;
        page.OverrideDeleted += (_, result) => raised = result;

        page.ShowDeleteDialogCommand.Execute("a");
        page.ConfirmDeleteCommand.Execute(null);

        var subscription = subscriptionStore.LoadSubscriptions().Single();
        Assert.Null(page.CurrentOverrideId);
        Assert.Equal(["a"], page.DeletedOverrideIds);
        Assert.Equal(["b"], subscription.OverrideIds);
        Assert.Equal(["b"], subscription.OverrideSortPreference);
        Assert.Equal(["sub-1"], raised?.AffectedSubscriptionIds);
        Assert.False(page.IsDeleteDialogVisible);
    }

    [Fact(DisplayName = "Move override persists visible order")]
    public void MoveOverridePersistsVisibleOrder()
    {
        var store = new FakeOverrideStore([Override("a"), Override("b"), Override("c")]);
        var page = new OverridePageViewModel(overrideDeleter: new OverrideDeleter(
            overrideStore: new InMemoryOverrideStore(),
            subscriptionStore: new InMemorySubscriptionStore()), overrideStore: store);
        page.LoadOverrides(store.LoadOverrides());

        page.MoveOverrideDownCommand.Execute("a");

        Assert.Equal(["b", "a", "c"], store.LoadOverrides().Select(item => item.Id));
    }

    [Fact(DisplayName = "Move override clamps target and persists only stored items")]
    public void MoveOverrideClampsTargetAndPersistsOnlyStoredItems()
    {
        var store = new FakeOverrideStore([Override("a"), Override("b"), Override("c")]);
        var page = new OverridePageViewModel(overrideDeleter: new OverrideDeleter(
            overrideStore: new InMemoryOverrideStore(),
            subscriptionStore: new InMemorySubscriptionStore()), overrideStore: store);
        page.LoadOverrides(store.LoadOverrides());
        page.AddOverride(Item("draft", isLocal: true));

        page.MoveOverrideCommand.Execute(new OverrideMoveRequest("c", -10));

        Assert.Equal(["c", "a", "b", "draft"], page.Overrides.Select(item => item.Id));
        Assert.Equal(["c", "a", "b"], store.LoadOverrides().Select(item => item.Id));

        page.MoveOverrideCommand.Execute(new OverrideMoveRequest("c", 99));

        Assert.Equal(["a", "b", "draft", "c"], page.Overrides.Select(item => item.Id));
        Assert.Equal(["a", "b", "c"], store.LoadOverrides().Select(item => item.Id));
        Assert.Equal(2, store.SaveOverridesCount);
    }

    [Fact(DisplayName = "Load overrides clears missing current selection and delete dialog target")]
    public void LoadOverridesClearsMissingCurrentSelectionAndDeleteDialogTarget()
    {
        var page = new OverridePageViewModel(overrideDeleter: new OverrideDeleter(
            overrideStore: new InMemoryOverrideStore(),
            subscriptionStore: new InMemorySubscriptionStore()));
        page.LoadOverrides([Override("a"), Override("b")]);
        page.SelectOverrideCommand.Execute("a");
        page.ShowDeleteDialogCommand.Execute("b");

        page.LoadOverrides([Override("c")]);

        Assert.Null(page.CurrentOverrideId);
        Assert.Null(page.DeleteDialogOverrideId);
        Assert.False(page.IsDeleteDialogVisible);
        Assert.False(page.IsDialogOverlayVisible);
        Assert.Equal(["c"], page.Overrides.Select(item => item.Id));
    }

    [Fact(DisplayName = "Deleting other override keeps delete dialog target")]
    public void DeletingOtherOverrideKeepsDeleteDialogTarget()
    {
        var store = new FakeOverrideStore([Override("a"), Override("b"), Override("c")]);
        var page = new OverridePageViewModel(
            overrideStore: store,
            overrideDeleter: new OverrideDeleter(store, new FakeSubscriptionStore([])));
        page.LoadOverrides(store.LoadOverrides());
        page.SelectOverrideCommand.Execute("b");
        page.ShowDeleteDialogCommand.Execute("b");

        page.DeleteOverrideCommand.Execute("a");

        Assert.Equal("b", page.CurrentOverrideId);
        Assert.Equal("b", page.DeleteDialogOverrideId);
        Assert.True(page.IsDeleteDialogVisible);
        Assert.Equal(["a"], page.DeletedOverrideIds);
        Assert.Equal(["b", "c"], page.Overrides.Select(item => item.Id));

        page.ConfirmDeleteCommand.Execute(null);

        Assert.Null(page.CurrentOverrideId);
        Assert.False(page.IsDeleteDialogVisible);
        Assert.Equal(["a", "b"], page.DeletedOverrideIds);
        Assert.Equal(["c"], page.Overrides.Select(item => item.Id));
    }

    [Fact(DisplayName = "Delete dialog rejects missing override target")]
    public void DeleteDialogRejectsMissingOverrideTarget()
    {
        var page = new OverridePageViewModel(overrideDeleter: new OverrideDeleter(
            overrideStore: new InMemoryOverrideStore(),
            subscriptionStore: new InMemorySubscriptionStore()));
        page.LoadOverrides([Override("a")]);

        page.ShowDeleteDialogCommand.Execute("missing");
        page.ConfirmDeleteCommand.Execute(null);

        Assert.Null(page.DeleteDialogOverrideId);
        Assert.False(page.IsDeleteDialogVisible);
        Assert.False(page.IsDialogOverlayVisible);
        Assert.Equal(["a"], page.Overrides.Select(item => item.Id));
    }

    [Fact(DisplayName = "Add dialog validates remote URL and submits trimmed remote request")]
    public void AddDialogValidatesRemoteUrlAndSubmitsTrimmedRemoteRequest()
    {
        var dialog = new OverrideAddDialogViewModel();
        string? validation = null;
        OverrideAddRemoteRequestedEventArgs? requested = null;
        dialog.ValidationFailed += (_, message) => validation = message;
        dialog.RemoteRequested += (_, args) => requested = args;
        dialog.Open();

        dialog.Name = " Remote ";
        dialog.SourceLocation = "local.yaml";
        dialog.ConfirmCommand.Execute(null);

        Assert.Equal("Overrides.Validation.Url", validation);
        Assert.False(dialog.IsSubmitting);
        Assert.Null(requested);

        dialog.SourceLocation = " https://override.example/a.js ";
        dialog.SelectJavaScriptFormatCommand.Execute(null);
        dialog.SelectCoreProxyModeCommand.Execute(null);
        dialog.ConfirmCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal("Remote", requested.Name);
        Assert.Equal("https://override.example/a.js", requested.SourceLocation);
        Assert.Equal(OverrideFormat.JavaScript, requested.Format);
        Assert.Equal(OverrideUpdateProxyMode.Core, requested.UpdateProxyMode);
        Assert.True(dialog.IsSubmitting);
    }

    [Fact(DisplayName = "Add dialog refreshes confirm command when required fields change")]
    public void AddDialogRefreshesConfirmCommandWhenRequiredFieldsChange()
    {
        var dialog = new OverrideAddDialogViewModel();
        var canExecuteStates = new List<bool>();
        dialog.Open();
        dialog.ConfirmCommand.CanExecuteChanged += (_, _) =>
            canExecuteStates.Add(dialog.ConfirmCommand.CanExecute(null));

        dialog.Name = "Remote";
        dialog.SourceLocation = "https://override.example/a.yaml";

        Assert.Equal([false, true], canExecuteStates);
    }

    [Fact(DisplayName = "Edit dialog refreshes confirm command when opened")]
    public void EditDialogRefreshesConfirmCommandWhenOpened()
    {
        var dialog = new OverrideEditDialogViewModel();
        var canExecuteChanged = false;
        dialog.ConfirmCommand.CanExecuteChanged += (_, _) => canExecuteChanged = true;

        dialog.Open(Item("remote", isLocal: false));

        Assert.True(canExecuteChanged);
        Assert.True(dialog.ConfirmCommand.CanExecute(null));
    }

    [Fact(DisplayName = "Add dialog switches method and creates blank without source")]
    public void AddDialogSwitchesMethodAndCreatesBlankWithoutSource()
    {
        var dialog = new OverrideAddDialogViewModel();
        OverrideAddCreateBlankRequestedEventArgs? blank = null;
        OverrideAddLocalRequestedEventArgs? local = null;
        dialog.CreateBlankRequested += (_, args) => blank = args;
        dialog.LocalRequested += (_, args) => local = args;
        dialog.Open();
        dialog.Name = " Override ";
        dialog.SourceLocation = " https://override.example/a.yaml ";

        dialog.SelectLocalMethodCommand.Execute(null);
        Assert.Equal("", dialog.SourceLocation);
        dialog.SourceLocation = " test-data/overrides/local.yaml ";
        dialog.ConfirmCommand.Execute(null);

        Assert.NotNull(local);
        Assert.Equal("test-data/overrides/local.yaml", local.SourceLocation);

        dialog.EndSubmit();
        dialog.SelectBlankMethodCommand.Execute(null);
        Assert.Equal("", dialog.SourceLocation);
        dialog.SelectJavaScriptFormatCommand.Execute(null);
        dialog.ConfirmCommand.Execute(null);

        Assert.NotNull(blank);
        Assert.Equal("Override", blank.Name);
        Assert.Equal(OverrideFormat.JavaScript, blank.Format);
    }

    [Fact(DisplayName = "Metadata updater preserves or replaces content")]
    public void MetadataUpdaterPreservesOrReplacesContent()
    {
        var store = new FakeOverrideStore([Override("a")]);
        store.Save(Override("a"), "original");
        var updater = new OverrideMetadataUpdater(store);

        updater.Save("a", new OverrideMetadataEdit("Renamed", "new.yaml", OverrideFormat.JavaScript, OverrideUpdateProxyMode.Core));
        Assert.Equal("original", store.ReadContent("a"));

        updater.Save("a", new OverrideMetadataEdit("Renamed", "new.yaml", OverrideFormat.Yaml, OverrideUpdateProxyMode.Direct), "changed");
        Assert.Equal("changed", store.ReadContent("a"));
        Assert.Equal("Renamed", store.LoadOverrides().Single(item => item.Id == "a").Name);
    }

    [Fact(DisplayName = "File editor keeps new session when reopened before reset")]
    public async Task FileEditorKeepsNewSessionWhenReopenedBeforeReset()
    {
        var editor = new OverrideFileEditorViewModel();
        OverrideFileEditCompletedEventArgs? completed = null;
        editor.Confirmed += (_, args) => completed = args;

        editor.Open("a", "old");
        editor.Close();
        editor.Open("b", "new");
        await Task.Delay(250);
        editor.Content = "changed";
        editor.ConfirmCommand.Execute(null);

        Assert.NotNull(completed);
        Assert.Equal("b", completed.OverrideId);
        Assert.Equal("changed", completed.Content);
        Assert.False(editor.IsDialogVisible);
    }

    [Fact(DisplayName = "Edit dialog rejects blank name and clears only matching override")]
    public async Task EditDialogRejectsBlankNameAndClearsOnlyMatchingOverride()
    {
        var editor = new OverrideEditDialogViewModel();
        OverrideEditCompletedEventArgs? completed = null;
        editor.Confirmed += (_, args) => completed = args;
        editor.Open(Item("a", isLocal: false));

        editor.Name = " ";
        editor.ConfirmCommand.Execute(null);

        Assert.Null(completed);
        Assert.True(editor.IsDialogVisible);
        Assert.False(editor.CanSubmit);

        editor.Name = "Renamed";
        editor.SelectJavaScriptFormatCommand.Execute(null);
        editor.SelectCoreProxyModeCommand.Execute(null);
        editor.ClearForOverride("other");

        Assert.True(editor.IsDialogVisible);

        editor.ConfirmCommand.Execute(null);

        Assert.NotNull(completed);
        Assert.Equal("a", completed.OverrideId);
        Assert.Equal("Renamed", completed.Name);
        Assert.Equal(OverrideFormat.JavaScript, completed.Format);
        Assert.Equal(OverrideUpdateProxyMode.Core, completed.UpdateProxyMode);
        await WaitUntilAsync(() => !editor.IsDialogVisible && editor.OverrideId is null);
    }

    [Fact(DisplayName = "Edit dialog keeps new session when reopened before reset")]
    public async Task EditDialogKeepsNewSessionWhenReopenedBeforeReset()
    {
        var editor = new OverrideEditDialogViewModel();
        OverrideEditCompletedEventArgs? completed = null;
        editor.Confirmed += (_, args) => completed = args;

        editor.Open(Item("a", isLocal: false));
        editor.Close();
        editor.Open(Item("b", isLocal: false));
        await Task.Delay(250);
        editor.Name = "B Changed";
        editor.SourceLocation = "https://override.example/b.yaml";
        editor.SelectJavaScriptFormatCommand.Execute(null);
        editor.SelectCoreProxyModeCommand.Execute(null);
        editor.ConfirmCommand.Execute(null);

        Assert.NotNull(completed);
        Assert.Equal("b", completed.OverrideId);
        Assert.Equal("B Changed", completed.Name);
        Assert.Equal("https://override.example/b.yaml", completed.SourceLocation);
        Assert.Equal(OverrideFormat.JavaScript, completed.Format);
        Assert.Equal(OverrideUpdateProxyMode.Core, completed.UpdateProxyMode);
    }

    [Fact(DisplayName = "Page edit dialog persists metadata and raises edited event")]
    public void PageEditDialogPersistsMetadataAndRaisesEditedEvent()
    {
        var store = new FakeOverrideStore([Override("a", sourceType: OverrideSourceType.Remote)]);
        var page = new OverridePageViewModel(overrideDeleter: new OverrideDeleter(
            overrideStore: new InMemoryOverrideStore(),
            subscriptionStore: new InMemorySubscriptionStore()), overrideStore: store);
        page.LoadOverrides(store.LoadOverrides());
        IReadOnlyList<string>? editedIds = null;
        page.OverridesEdited += (_, ids) => editedIds = ids;

        page.ShowEditDialogCommand.Execute("a");
        page.EditDialog.Name = "Remote Changed";
        page.EditDialog.SourceLocation = "https://override.example/changed.js";
        page.EditDialog.SelectJavaScriptFormatCommand.Execute(null);
        page.EditDialog.SelectCoreProxyModeCommand.Execute(null);
        page.EditDialog.ConfirmCommand.Execute(null);

        var row = Assert.Single(page.Overrides);
        Assert.Equal("Remote Changed", row.Name);
        Assert.Equal("https://override.example/changed.js", row.SourceLocation);
        Assert.Equal(OverrideFormat.JavaScript, row.Format);
        Assert.Equal(OverrideUpdateProxyMode.Core, row.UpdateProxyMode);
        var persisted = store.LoadOverrides().Single();
        Assert.Equal("Remote Changed", persisted.Name);
        Assert.Equal("https://override.example/changed.js", persisted.SourceLocation);
        Assert.Equal(OverrideUpdateProxyMode.Core, persisted.UpdateProxyMode);
        Assert.Equal("content-a", store.ReadContent("a"));
        Assert.Equal(["a"], editedIds);
    }

    [Fact(DisplayName = "Page file edit persists content without changing metadata")]
    public void PageFileEditPersistsContentWithoutChangingMetadata()
    {
        var store = new FakeOverrideStore([Override("a", sourceType: OverrideSourceType.Remote)]);
        var page = new OverridePageViewModel(overrideDeleter: new OverrideDeleter(
            overrideStore: new InMemoryOverrideStore(),
            subscriptionStore: new InMemorySubscriptionStore()), overrideStore: store);
        page.LoadOverrides(store.LoadOverrides());
        IReadOnlyList<string>? editedIds = null;
        page.OverridesEdited += (_, ids) => editedIds = ids;

        page.EditFileCommand.Execute("a");
        Assert.Equal("content-a", page.FileEditor.Content);
        page.FileEditor.Content = "mixed-port: 7890";
        page.FileEditor.ConfirmCommand.Execute(null);

        var persisted = store.LoadOverrides().Single();
        Assert.Equal("a", persisted.Name);
        Assert.Equal("a.yaml", persisted.SourceLocation);
        Assert.Equal("mixed-port: 7890", store.ReadContent("a"));
        Assert.Equal(["a"], editedIds);
    }

    [Fact(DisplayName = "Page batch update blocks duplicate request until current batch completes")]
    public async Task PageBatchUpdateBlocksDuplicateRequestUntilCurrentBatchCompletes()
    {
        var store = new FakeOverrideStore(
        [
            Override("remote-a", sourceType: OverrideSourceType.Remote),
            Override("remote-b", sourceType: OverrideSourceType.Remote),
            Override("local", sourceType: OverrideSourceType.Local)
        ]);
        var downloader = new FakeRemoteOverrideDownloader();
        var page = new OverridePageViewModel(
            overrideDeleter: new OverrideDeleter(
                overrideStore: new InMemoryOverrideStore(),
                subscriptionStore: new InMemorySubscriptionStore()),
            overrideStore: store,
            overrideUpdater: new OverrideUpdater(store, downloader, () => DateTimeOffset.UnixEpoch.AddDays(1)));
        page.LoadOverrides(store.LoadOverrides());

        var updateTask = page.UpdateAllOverridesAsync();
        await page.UpdateAllOverridesAsync();

        Assert.True(page.IsBatchUpdatingOverrides);
        Assert.Equal(["remote-a", "remote-b"], page.UpdatingOverrideIds);
        Assert.False(page.CanUpdateAllOverrides);
        Assert.Equal(2, downloader.DownloadCount);

        await updateTask;

        Assert.False(page.IsBatchUpdatingOverrides);
        Assert.Equal(["remote-a", "remote-b"], page.UpdatedOverrideIds);
        Assert.Empty(page.SkippedOverrideUpdateIds);
        Assert.DoesNotContain("local", page.UpdatingOverrideIds);
        Assert.Equal(2, downloader.DownloadCount);
    }

    [Fact(DisplayName = "Page open external editor ignores missing override")]
    public void PageOpenExternalEditorIgnoresMissingOverride()
    {
        var opener = new FakeOverrideFileOpener();
        var page = new OverridePageViewModel(overrideDeleter: new OverrideDeleter(
            overrideStore: new InMemoryOverrideStore(),
            subscriptionStore: new InMemorySubscriptionStore()), overrideFileOpener: opener);
        page.LoadOverrides([Override("a")]);

        page.OpenExternalEditorCommand.Execute("missing");
        page.OpenExternalEditorCommand.Execute("a");

        Assert.Equal(["a"], opener.OpenedOverrideIds);
    }

    [Fact(DisplayName = "Page dispose releases every language subscription")]
    public void PageDisposeReleasesEveryLanguageSubscription()
    {
        var localization = new FakeLocalizationService();
        var page = new OverridePageViewModel(overrideDeleter: new OverrideDeleter(
            overrideStore: new InMemoryOverrideStore(),
            subscriptionStore: new InMemorySubscriptionStore()), localization: localization);
        Assert.True(localization.LanguageChangedSubscriberCount > 0);

        page.Dispose();

        Assert.Equal(0, localization.LanguageChangedSubscriberCount);
    }

    [Fact(DisplayName = "Page remote override import reports success and failure toasts")]
    public async Task PageRemoteOverrideImportReportsSuccessAndFailureToasts()
    {
        var page = new OverridePageViewModel(
            overrideDeleter: new OverrideDeleter(
                overrideStore: new InMemoryOverrideStore(),
                subscriptionStore: new InMemorySubscriptionStore()),
            overrideImporter: new OverrideImporter(new FakeOverrideStore([]), new FakeRemoteOverrideDownloader()),
            localization: new FakeLocalizationService());
        var toasts = new List<(string Message, ToastType Type)>();
        page.ToastRequested += (_, toast) => toasts.Add(toast);

        await page.AddRemoteOverrideAsync(new OverrideAddRemoteRequestedEventArgs(
            "Remote",
            "https://override.example/a.yaml",
            OverrideFormat.Yaml,
            OverrideUpdateProxyMode.Direct));

        Assert.Contains(toasts, toast => toast is { Message: "远程覆写导入成功：Remote", Type: ToastType.Success });

        var failingPage = new OverridePageViewModel(
            overrideDeleter: new OverrideDeleter(
                overrideStore: new InMemoryOverrideStore(),
                subscriptionStore: new InMemorySubscriptionStore()),
            overrideImporter: new OverrideImporter(
                new FakeOverrideStore([]),
                new FakeRemoteOverrideDownloader { NextException = new InvalidOperationException("download failed") }),
            localization: new FakeLocalizationService());
        (string Message, ToastType Type)? failureToast = null;
        failingPage.ToastRequested += (_, toast) => failureToast = toast;

        var item = await failingPage.AddRemoteOverrideAsync(new OverrideAddRemoteRequestedEventArgs(
            "Remote",
            "https://override.example/a.yaml",
            OverrideFormat.Yaml,
            OverrideUpdateProxyMode.Direct));

        Assert.Null(item);
        Assert.Equal(ToastType.Error, failureToast?.Type);
        Assert.Equal("远程覆写导入失败，请稍后重试", failureToast?.Message);
    }

    [Fact(DisplayName = "Page local and blank override imports report success and failure toasts")]
    public async Task PageLocalAndBlankOverrideImportsReportSuccessAndFailureToasts()
    {
        var importer = new OverrideImporter(new FakeOverrideStore([]), new FakeRemoteOverrideDownloader());
        var page = new OverridePageViewModel(
            overrideDeleter: new OverrideDeleter(
                overrideStore: new InMemoryOverrideStore(),
                subscriptionStore: new InMemorySubscriptionStore()),
            overrideImporter: importer,
            localFileReader: new FakeLocalOverrideFileReader("mixed-port: 7890"),
            localization: new FakeLocalizationService());
        var toasts = new List<(string Message, ToastType Type)>();
        page.ToastRequested += (_, toast) => toasts.Add(toast);

        await page.AddLocalOverrideAsync(new OverrideAddLocalRequestedEventArgs(
            "Local",
            "test-data/overrides/local.yaml",
            OverrideFormat.Yaml));
        await page.CreateBlankOverrideAsync(new OverrideAddCreateBlankRequestedEventArgs("Blank", OverrideFormat.JavaScript));

        Assert.Contains(toasts, toast => toast is { Message: "本地覆写导入成功：Local", Type: ToastType.Success });
        Assert.Contains(toasts, toast => toast is { Message: "空白覆写创建成功：Blank", Type: ToastType.Success });

        var failingLocalPage = new OverridePageViewModel(
            overrideDeleter: new OverrideDeleter(
                overrideStore: new InMemoryOverrideStore(),
                subscriptionStore: new InMemorySubscriptionStore()),
            overrideImporter: importer,
            localFileReader: new FakeLocalOverrideFileReader(string.Empty, new InvalidOperationException("read failed")),
            localization: new FakeLocalizationService());
        (string Message, ToastType Type)? localFailureToast = null;
        failingLocalPage.ToastRequested += (_, toast) => localFailureToast = toast;

        var localItem = await failingLocalPage.AddLocalOverrideAsync(new OverrideAddLocalRequestedEventArgs(
            "Local",
            "test-data/overrides/local.yaml",
            OverrideFormat.Yaml));

        Assert.Null(localItem);
        Assert.Equal(ToastType.Error, localFailureToast?.Type);
        Assert.Equal("本地覆写导入失败，请稍后重试", localFailureToast?.Message);

        var failingBlankPage = new OverridePageViewModel(
            overrideDeleter: new OverrideDeleter(
                overrideStore: new InMemoryOverrideStore(),
                subscriptionStore: new InMemorySubscriptionStore()),
            overrideImporter: new OverrideImporter(
                new FakeOverrideStore([], new InvalidOperationException("save failed")),
                new FakeRemoteOverrideDownloader()),
            localization: new FakeLocalizationService());
        (string Message, ToastType Type)? blankFailureToast = null;
        failingBlankPage.ToastRequested += (_, toast) => blankFailureToast = toast;

        var blankItem = await failingBlankPage.CreateBlankOverrideAsync(new OverrideAddCreateBlankRequestedEventArgs("Blank", OverrideFormat.Yaml));

        Assert.Null(blankItem);
        Assert.Equal(ToastType.Error, blankFailureToast?.Type);
        Assert.Equal("创建空白覆写失败，请稍后重试", blankFailureToast?.Message);
    }

    [Fact(DisplayName = "Importer persists remote, local, and blank overrides")]
    public async Task ImporterPersistsRemoteLocalAndBlankOverrides()
    {
        var store = new FakeOverrideStore([]);
        var downloader = new FakeRemoteOverrideDownloader();
        var importer = new OverrideImporter(store, downloader, () => DateTimeOffset.UnixEpoch.AddHours(1));

        var remote = await importer.ImportRemoteAsync("Remote", "https://override.example/a.yaml", OverrideFormat.Yaml, OverrideUpdateProxyMode.SystemProxy);
        var local = importer.ImportLocal("Local", "test-data/overrides/local.yaml", OverrideFormat.JavaScript, "console.log('x')");
        var blank = importer.CreateBlankLocal("Blank", OverrideFormat.Yaml);

        Assert.Equal(3, store.LoadOverrides().Count);
        Assert.Equal("downloaded-" + remote.Id, store.ReadContent(remote.Id));
        Assert.Equal("console.log('x')", store.ReadContent(local.Id));
        Assert.Equal("", store.ReadContent(blank.Id));
        Assert.Equal(OverrideSourceType.Remote, remote.SourceType);
        Assert.Equal(OverrideUpdateProxyMode.SystemProxy, remote.UpdateProxyMode);
        Assert.Equal(OverrideSourceType.Local, blank.SourceType);
        Assert.Equal(1, downloader.DownloadCount);
    }

    [Fact(DisplayName = "Override updater updates remote items and skips local or missing items")]
    public async Task OverrideUpdaterUpdatesRemoteItemsAndSkipsLocalOrMissingItems()
    {
        var store = new FakeOverrideStore(
        [
            Override("remote", sourceType: OverrideSourceType.Remote),
            Override("local", sourceType: OverrideSourceType.Local)
        ]);
        var downloader = new FakeRemoteOverrideDownloader();
        var updater = new OverrideUpdater(store, downloader, () => DateTimeOffset.UnixEpoch.AddDays(1));

        var result = await updater.UpdateManyAsync(["remote", "local", "missing"]);

        Assert.Equal(["remote"], result.UpdatedOverrideIds);
        Assert.Equal(["local", "missing"], result.SkippedOverrideIds);
        Assert.Equal("downloaded-remote", store.ReadContent("remote"));
        Assert.Equal(1, downloader.DownloadCount);
    }

    [Fact(DisplayName = "Override deleter returns affected subscriptions")]
    public void OverrideDeleterReturnsAffectedSubscriptions()
    {
        var overrideStore = new FakeOverrideStore([Override("a"), Override("b")]);
        var subscriptionStore = new FakeSubscriptionStore(
        [
            new Subscription("affected", "Affected", "source", false, DateTimeOffset.UnixEpoch, OverrideIds: ["a"], OverrideSortPreference: ["a"]),
            new Subscription("clean", "Clean", "source", false, DateTimeOffset.UnixEpoch, OverrideIds: ["b"], OverrideSortPreference: ["b"])
        ]);

        var result = new OverrideDeleter(overrideStore, subscriptionStore).Delete("a");

        Assert.Equal("a", result.DeletedOverrideId);
        Assert.Equal(["affected"], result.AffectedSubscriptionIds);
        Assert.Empty(subscriptionStore.LoadSubscriptions().Single(item => item.Id == "affected").OverrideIds);
        Assert.Equal(["b"], subscriptionStore.LoadSubscriptions().Single(item => item.Id == "clean").OverrideIds);
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

    private static OverrideItemViewModel Item(string id, bool isLocal)
    {
        return new OverrideItemViewModel(id, id, $"{id}.yaml", OverrideFormat.Yaml, isLocal);
    }

    private static OverrideProfile Override(string id, OverrideSourceType sourceType = OverrideSourceType.Local)
    {
        return new OverrideProfile(id, id, sourceType, OverrideFormat.Yaml, $"{id}.yaml", DateTimeOffset.UnixEpoch);
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.ZhHans;

        public AppLanguage EffectiveLanguage => CurrentLanguage;

        private EventHandler? _languageChanged;

        public int LanguageChangedSubscriberCount { get; private set; }

        public event EventHandler? LanguageChanged
        {
            add
            {
                _languageChanged += value;
                LanguageChangedSubscriberCount++;
            }
            remove
            {
                _languageChanged -= value;
                LanguageChangedSubscriberCount--;
            }
        }

        public void SetLanguage(AppLanguage language)
        {
            CurrentLanguage = language;
            _languageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key)
        {
            return key switch
            {
                "Overrides.Toast.ImportRemoteSucceeded" => "远程覆写导入成功：{0}",
                "Overrides.Toast.ImportLocalSucceeded" => "本地覆写导入成功：{0}",
                "Overrides.Toast.CreateBlankSucceeded" => "空白覆写创建成功：{0}",
                "Overrides.Toast.ImportRemoteFailed" => "远程覆写导入失败，请稍后重试",
                "Overrides.Toast.ImportLocalFailed" => "本地覆写导入失败，请稍后重试",
                "Overrides.Toast.CreateBlankFailed" => "创建空白覆写失败，请稍后重试",
                _ => key,
            };
        }
    }

    private sealed class FakeOverrideStore(IReadOnlyList<OverrideProfile> overrides, Exception? saveException = null) : IOverrideStore
    {
        private readonly List<OverrideProfile> _overrides = overrides.ToList();
        private readonly Dictionary<string, string> _content = overrides.ToDictionary(item => item.Id, item => $"content-{item.Id}", StringComparer.Ordinal);

        public int SaveOverridesCount { get; private set; }

        public void Save(OverrideProfile overrideProfile, string content)
        {
            if (saveException is not null)
            {
                throw saveException;
            }

            var index = _overrides.FindIndex(item => item.Id == overrideProfile.Id);
            if (index >= 0)
            {
                _overrides[index] = overrideProfile;
            }
            else
            {
                _overrides.Add(overrideProfile);
            }

            _content[overrideProfile.Id] = content;
        }

        public IReadOnlyList<OverrideProfile> LoadOverrides() => _overrides.ToList();

        public string ReadContent(string overrideId) => _content[overrideId];

        public string GetContentPath(string overrideId) => $"{overrideId}.yaml";

        public void SaveOverrides(IReadOnlyList<OverrideProfile> overrides)
        {
            SaveOverridesCount++;
            _overrides.Clear();
            _overrides.AddRange(overrides);
        }

        public void Delete(string overrideId)
        {
            _overrides.RemoveAll(item => item.Id == overrideId);
            _content.Remove(overrideId);
        }
    }

    private sealed class FakeSubscriptionStore(IReadOnlyList<Subscription> subscriptions) : ISubscriptionStore
    {
        private readonly List<Subscription> _subscriptions = subscriptions.ToList();

        public void Save(Subscription subscription, string originalContent)
        {
            _subscriptions.Add(subscription);
        }

        public void UpdateSubscription(Subscription subscription)
        {
            var index = _subscriptions.FindIndex(item => item.Id == subscription.Id);
            if (index >= 0)
            {
                _subscriptions[index] = subscription;
            }
        }

        public void SaveSubscriptions(IReadOnlyList<Subscription> subscriptions)
        {
            _subscriptions.Clear();
            _subscriptions.AddRange(subscriptions);
        }

        public void SaveContent(string subscriptionId, string originalContent)
        {
        }

        public IReadOnlyList<Subscription> LoadSubscriptions() => _subscriptions.ToList();

        public string ReadContent(string subscriptionId) => string.Empty;

        public string GetContentPath(string subscriptionId) => $"{subscriptionId}.yaml";

        public void Delete(string subscriptionId)
        {
            _subscriptions.RemoveAll(item => item.Id == subscriptionId);
        }
    }

    private sealed class FakeRemoteOverrideDownloader : IRemoteOverrideDownloader
    {
        public Exception? NextException { get; init; }

        public int DownloadCount { get; private set; }

        public Task<string> DownloadAsync(OverrideProfile overrideProfile, CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            if (NextException is not null)
            {
                throw NextException;
            }

            return Task.FromResult($"downloaded-{overrideProfile.Id}");
        }
    }

    private sealed class FakeLocalOverrideFileReader(string content, Exception? exception = null) : ILocalOverrideFileReader
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

    private sealed class FakeOverrideFileOpener : IOverrideFileOpener
    {
        public List<string> OpenedOverrideIds { get; } = [];

        public void OpenOverrideFile(string overrideId)
        {
            OpenedOverrideIds.Add(overrideId);
        }
    }
}
