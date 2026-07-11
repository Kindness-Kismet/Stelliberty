using Avalonia;
using Avalonia.Data;

namespace Stelliberty.Desktop.Localization;

public sealed class LocExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider)
        => LocalizationManager.Observe(Key).ToBinding();
}
