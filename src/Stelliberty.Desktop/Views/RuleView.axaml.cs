using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stelliberty.Desktop.Controls;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views;

public sealed partial class RuleView : UserControl
{
    private readonly GridReorderController _reorder;

    public RuleView()
    {
        InitializeComponent();
        _reorder = new GridReorderController(
            RuleList,
            dataContext => (dataContext as RuleEditorRowViewModel)?.OrderId,
            (id, targetIndex) => (RuleList.DataContext as RulePageViewModel)?.MoveRuleCommand
                .Execute(new RuleMoveRequest(id, targetIndex)));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _reorder.Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
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
}
