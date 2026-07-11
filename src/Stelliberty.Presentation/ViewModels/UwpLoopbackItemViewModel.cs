using Stelliberty.Application.Platform;

namespace Stelliberty.Presentation.ViewModels;

// UWP 回环项：IsSelected 是待提交状态，保存前不触碰系统。
public sealed class UwpLoopbackItemViewModel : ViewModelBase
{
    private bool _isSelected;

    public UwpLoopbackItemViewModel(UwpLoopbackPackage package)
    {
        PackageFamilyName = package.PackageFamilyName;
        DisplayName = package.DisplayName;
        AppContainerName = package.AppContainerName;
        Sid = package.Sid;
        _isSelected = package.IsLoopbackEnabled;
    }

    public string PackageFamilyName { get; }

    public string DisplayName { get; }

    public string AppContainerName { get; }

    public string Sid { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool HasAppContainerName => !string.IsNullOrWhiteSpace(AppContainerName);

    // 显示名已经回退为包名时，隐藏包名。
    public bool ShowPackageFamilyName => !string.Equals(PackageFamilyName, DisplayName, StringComparison.Ordinal);

    // AppContainer 名只有在补充包名或显示名信息时才显示。
    public bool ShowAppContainerName => HasAppContainerName
        && !string.Equals(AppContainerName, PackageFamilyName, StringComparison.Ordinal)
        && !string.Equals(AppContainerName, DisplayName, StringComparison.Ordinal);

    public bool HasSid => !string.IsNullOrWhiteSpace(Sid);

    // 搜索按名称或包族名匹配，忽略大小写。
    public bool Matches(string keyword)
    {
        return DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || PackageFamilyName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
