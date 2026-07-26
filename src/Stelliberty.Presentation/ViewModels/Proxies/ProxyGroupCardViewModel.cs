using Stelliberty.Domain.Proxies;

namespace Stelliberty.Presentation.ViewModels;

public sealed class ProxyGroupCardViewModel : ViewModelBase
{
    private ProxyGroup _group;
    private string _selectionDisplay;
    private string _nodeCountText;
    private bool _isExpanded;

    public ProxyGroupCardViewModel(ProxyGroup group, string selectionDisplay, string nodeCountText, bool isExpanded)
    {
        _group = group;
        _selectionDisplay = selectionDisplay;
        _nodeCountText = nodeCountText;
        _isExpanded = isExpanded;
    }

    public ProxyGroup Group => _group;

    public string Name => _group.Name;

    public string? IconUrl => _group.Icon;

    public bool HasIcon => !string.IsNullOrWhiteSpace(_group.Icon);

    public string SelectionDisplay
    {
        get => _selectionDisplay;
        private set => SetProperty(ref _selectionDisplay, value);
    }

    public string NodeCountText
    {
        get => _nodeCountText;
        private set => SetProperty(ref _nodeCountText, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        private set => SetProperty(ref _isExpanded, value);
    }

    public string CardAutomationId => $"Proxy.GroupCard.{Name}";

    public string ToggleAutomationId => $"Proxy.GroupCard.{Name}.Toggle";

    // 配置重建后，复用行必须接收新记录，确保派生值刷新。
    public void Update(ProxyGroup group, string selectionDisplay, string nodeCountText, bool isExpanded)
    {
        if (!ReferenceEquals(_group, group))
        {
            var nameChanged = _group.Name != group.Name;
            var iconChanged = _group.Icon != group.Icon;
            _group = group;
            OnPropertyChanged(nameof(Group));
            OnPropertyChanged(nameof(Name));
            if (nameChanged)
            {
                OnPropertyChanged(nameof(CardAutomationId));
                OnPropertyChanged(nameof(ToggleAutomationId));
            }

            if (iconChanged)
            {
                OnPropertyChanged(nameof(IconUrl));
                OnPropertyChanged(nameof(HasIcon));
            }

        }

        SelectionDisplay = selectionDisplay;
        NodeCountText = nodeCountText;
        IsExpanded = isExpanded;
    }
}
