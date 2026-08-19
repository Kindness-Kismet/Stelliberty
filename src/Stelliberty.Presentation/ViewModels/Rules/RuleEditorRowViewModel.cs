using Stelliberty.Application.Rules;
using Stelliberty.Domain.Rules;

namespace Stelliberty.Presentation.ViewModels;

public sealed class RuleEditorRowViewModel : ViewModelBase
{
    private bool _isEnabled;
    private int _sequenceNumber;

    public RuleEditorRowViewModel(RuleEditorItem item)
    {
        Item = item;
        _isEnabled = item.IsEnabled;
    }

    public RuleEditorItem Item { get; }
    public event EventHandler? StateChanged;

    public string Id => Item.Id;
    public string OrderId => Item.OrderId;
    public string Type => Item.Type;
    public string Payload => string.IsNullOrWhiteSpace(Item.Payload) ? "-" : Item.Payload;
    public string Proxy => Item.Proxy;
    public string Options => Item.Options;
    public string Source => Item.Source;
    public bool IsBuiltIn => Item.IsBuiltIn;
    public string SourceTag => IsBuiltIn ? "subscription" : "custom";
    public string RuleText => Item.ToEditableRule().Render();
    public string AutomationId => $"Rules.Editor.{(IsBuiltIn ? "Builtin" : "Custom")}.{Id}";
    public string ToggleAutomationId => $"{AutomationId}.Toggle";
    public string EditAutomationId => $"{AutomationId}.Edit";
    public string DeleteAutomationId => $"{AutomationId}.Delete";
    public string DuplicateText => Item.RuleCount > 1 ? $"x{Item.RuleCount}" : string.Empty;
    public int SequenceNumber
    {
        get => _sequenceNumber;
        set => SetProperty(ref _sequenceNumber, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public EditableRule ToEditableRule() => Item.ToEditableRule() with { IsEnabled = IsEnabled };
}
