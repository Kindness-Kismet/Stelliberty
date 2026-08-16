using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Desktop.Controls;
using Stelliberty.Desktop.Localization;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views;

public sealed partial class OverrideView : UserControl
{
    private readonly GridReorderController _reorder;
    private OverrideAddDialogViewModel? _subscribedAddDialog;
    private OverrideEditDialogViewModel? _subscribedEditDialog;

    public OverrideView()
    {
        InitializeComponent();

        _reorder = new GridReorderController(
            OverrideList,
            dataContext => (dataContext as OverrideItemViewModel)?.Id,
            container => container,
            (id, targetIndex) => (DataContext as OverridePageViewModel)?.MoveOverrideCommand
                .Execute(new OverrideMoveRequest(id, targetIndex)));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _reorder.Attach();
        SubscribeAddDialog();
        SubscribeEditDialog();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeAddDialog();
        UnsubscribeEditDialog();
        _reorder.Detach();
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeAddDialog()
    {
        if (OverridePageRoot.DataContext is not OverridePageViewModel viewModel
            || ReferenceEquals(_subscribedAddDialog, viewModel.AddDialog))
        {
            return;
        }

        UnsubscribeAddDialog();
        _subscribedAddDialog = viewModel.AddDialog;
        _subscribedAddDialog.DialogStateChanged += OnAddDialogStateChanged;
        _subscribedAddDialog.InputFocusRequested += OnInputFocusRequested;
    }

    private void UnsubscribeAddDialog()
    {
        if (_subscribedAddDialog is null)
        {
            return;
        }

        _subscribedAddDialog.DialogStateChanged -= OnAddDialogStateChanged;
        _subscribedAddDialog.InputFocusRequested -= OnInputFocusRequested;
        _subscribedAddDialog = null;
    }

    private void SubscribeEditDialog()
    {
        if (OverridePageRoot.DataContext is not OverridePageViewModel viewModel
            || ReferenceEquals(_subscribedEditDialog, viewModel.EditDialog))
        {
            return;
        }

        UnsubscribeEditDialog();
        _subscribedEditDialog = viewModel.EditDialog;
        _subscribedEditDialog.InputFocusRequested += OnInputFocusRequested;
    }

    private void UnsubscribeEditDialog()
    {
        if (_subscribedEditDialog is null)
        {
            return;
        }

        _subscribedEditDialog.InputFocusRequested -= OnInputFocusRequested;
        _subscribedEditDialog = null;
    }

    private async void OnAddDialogStateChanged(object? sender, EventArgs args)
    {
        await RefreshOverrideUrlPasteAvailabilityAsync();
    }

    private void OnInputFocusRequested(object? sender, DialogInputField field)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var prefix = sender is OverrideEditDialogViewModel ? "Overrides.EditDialog" : "Overrides.Dialog";
            var automationId = field switch
            {
                DialogInputField.Name => $"{prefix}.NameBox",
                DialogInputField.Source => $"{prefix}.PathBox",
                DialogInputField.LocalFile => "Overrides.Dialog.ChooseLocalFileButton",
                _ => string.Empty,
            };
            var target = this.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
            target?.BringIntoView();
            target?.Focus();
        }, DispatcherPriority.Input);
    }

    private async void OnOverrideUrlBoxGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        await RefreshOverrideUrlPasteAvailabilityAsync();
    }

    private async void OnOverrideUrlBoxTextChanged(object? sender, TextChangedEventArgs args)
    {
        await RefreshOverrideUrlPasteAvailabilityAsync();
    }

    private async void OnPasteOverrideUrlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try
        {
            var text = await ReadClipboardTextAsync();
            if (OverridePageRoot.DataContext is OverridePageViewModel viewModel)
            {
                viewModel.AddDialog.PasteUrl(text);
                viewModel.AddDialog.SetClipboardTextAvailable(!string.IsNullOrWhiteSpace(text));
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Override URL paste failed: {exception.Message}");
        }
    }

    private async Task RefreshOverrideUrlPasteAvailabilityAsync()
    {
        try
        {
            if (OverridePageRoot.DataContext is not OverridePageViewModel viewModel)
            {
                return;
            }

            var text = viewModel.AddDialog.IsUrlPasteButtonVisible ? await ReadClipboardTextAsync() : string.Empty;
            viewModel.AddDialog.SetClipboardTextAvailable(!string.IsNullOrWhiteSpace(text));
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Override URL paste state refresh failed: {exception.Message}");
        }
    }

    private async Task<string> ReadClipboardTextAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        return clipboard is null ? string.Empty : await clipboard.TryGetTextAsync() ?? string.Empty;
    }

    // async void 异常会终止进程，所以在这里处理选择器错误。
    private async void OnChooseLocalFileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try
        {
            if (sender is not Button button || button.DataContext is not OverridePageViewModel viewModel)
            {
                return;
            }

            if (TopLevel.GetTopLevel(button) is not { } topLevel)
            {
                return;
            }

            var filePath = await LocalFilePicker.PickFileAsync(
                topLevel,
                Localize("Overrides.FilePicker.Title"),
                Localize("Overrides.FilePicker.Filter"),
                ["*.yaml", "*.yml", "*.js"]);
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                viewModel.AddDialog.SourceLocation = filePath;
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Override file picker failed");
        }
    }

    private static string Localize(string key) => LocalizationManager.Translate(key);
}
