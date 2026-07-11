using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Presentation.ViewModels;

public sealed class ProxyGroupButtonViewModel : ViewModelBase
{
    private ProxyGroup _group;
    private bool _isSelected;

    public ProxyGroupButtonViewModel(ProxyGroup group, bool isSelected = false)
    {
        _group = group;
        _isSelected = isSelected;
    }

    public ProxyGroup Group => _group;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Name => _group.Name;

    public string AutomationId => $"Proxy.Group.{Name}.Button";

    // 配置重建后，复用行必须接收新记录，否则派生值会停留。
    public void Update(ProxyGroup group, bool isSelected)
    {
        if (!ReferenceEquals(_group, group))
        {
            _group = group;
            OnPropertyChanged(nameof(Group));
        }

        IsSelected = isSelected;
    }
}
