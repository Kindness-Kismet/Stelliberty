using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stelliberty.Desktop.Controls;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views;

public sealed partial class RuleView : UserControl
{
    private readonly GridReorderController _reorder;
    private RulePageViewModel? _subscribedViewModel;

    public RuleView()
    {
        InitializeComponent();
        _reorder = new GridReorderController(
            RuleList,
            dataContext => (dataContext as RuleEditorRowViewModel)?.OrderId,
            container => container,
            (id, targetIndex) => (RuleList.DataContext as RulePageViewModel)?.MoveRuleCommand
                .Execute(new RuleMoveRequest(id, targetIndex)));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _reorder.Attach();
        SubscribeInputFocus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeInputFocus();
        _reorder.Detach();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnRuleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: RuleEditorRowViewModel row }
            && RulePageRoot.DataContext is RulePageViewModel viewModel
            && !row.IsBuiltIn)
        {
            viewModel.EditRuleCommand.Execute(row);
        }
    }

    private void SubscribeInputFocus()
    {
        if (RulePageRoot.DataContext is not RulePageViewModel viewModel
            || ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        UnsubscribeInputFocus();
        _subscribedViewModel = viewModel;
        _subscribedViewModel.InputFocusRequested += OnInputFocusRequested;
    }

    private void UnsubscribeInputFocus()
    {
        if (_subscribedViewModel is null)
        {
            return;
        }

        _subscribedViewModel.InputFocusRequested -= OnInputFocusRequested;
        _subscribedViewModel = null;
    }

    private void OnInputFocusRequested(object? sender, DialogInputField field)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var automationId = field switch
            {
                DialogInputField.Payload => "Rules.EditorDialog.PayloadBox",
                DialogInputField.TemplateName => "Rules.TemplateDialog.NameBox",
                _ => string.Empty,
            };
            var target = this.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
            target?.BringIntoView();
            target?.Focus();
        }, DispatcherPriority.Input);
    }
}
