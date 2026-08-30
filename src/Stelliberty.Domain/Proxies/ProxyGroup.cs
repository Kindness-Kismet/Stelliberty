namespace Stelliberty.Domain.Proxies;

public sealed record ProxyGroup(
    string Name,
    string Type,
    string? Now,
    IReadOnlyList<string> All,
    string? Fixed = null,
    bool IsHidden = false,
    string? Icon = null,
    int? Delay = null)
{

    public bool IsManualSelectable => ProxyGroupTypes.IsManualSelectable(Type);

    public bool RequiresDefaultSelection => ProxyGroupTypes.RequiresDefaultSelection(Type);

    public bool UsesFixedSelection => ProxyGroupTypes.UsesFixedSelection(Type);

    public string? UserSelectionName => UsesFixedSelection ? Fixed : Now;

    public string? DisplaySelectionName => string.IsNullOrWhiteSpace(UserSelectionName)
        ? Now
        : UserSelectionName;
}
