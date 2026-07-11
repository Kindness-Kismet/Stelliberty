namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SettingsCoreConfigViewModel
{
    public string PerformanceGeoLoaderText => _localization.GetString("Settings.Performance.GeoLoader");
    public string PerformanceFindProcessText => _localization.GetString("Settings.Performance.FindProcess");
    public string PerformanceKeepAliveText => _localization.GetString("Settings.Performance.KeepAlive");
    public string PerformanceKeepAliveIntervalText => _localization.GetString("Settings.Performance.KeepAliveInterval");

    public IReadOnlyList<string> PerformanceItems =>
    [
        PerformanceGeoLoaderText,
        PerformanceFindProcessText,
        PerformanceKeepAliveText
    ];

    public IReadOnlyList<SelectionOption<string>> GeoDataLoaderOptions =>
    [
        new("standard", _localization.GetString("Settings.Performance.GeoLoader.Standard")),
        new("memconservative", _localization.GetString("Settings.Performance.GeoLoader.MemConservative"))
    ];

    public SelectionOption<string> SelectedGeoDataLoaderOption
    {
        get => GeoDataLoaderOptions.FirstOrDefault(option => option.Value == _settings.GeoDataLoader)
            ?? GeoDataLoaderOptions[0];
        set => SetWithArea(_settings.GeoDataLoader, value.Value, next => _settings.GeoDataLoader = next, "Performance");
    }

    public IReadOnlyList<SelectionOption<string>> FindProcessModeOptions =>
    [
        new("off", _localization.GetString("Settings.Performance.FindProcess.Off")),
        new("strict", _localization.GetString("Settings.Performance.FindProcess.Strict")),
        new("always", _localization.GetString("Settings.Performance.FindProcess.Always"))
    ];

    public SelectionOption<string> SelectedFindProcessModeOption
    {
        get => FindProcessModeOptions.FirstOrDefault(option => option.Value == _settings.FindProcessMode)
            ?? FindProcessModeOptions[0];
        set => SetWithArea(_settings.FindProcessMode, value.Value, next => _settings.FindProcessMode = next, "Performance");
    }

    public string GeoDataLoader
    {
        get => _settings.GeoDataLoader;
        set => SetWithArea(_settings.GeoDataLoader, value, next => _settings.GeoDataLoader = next, "Performance");
    }

    public string FindProcessMode
    {
        get => _settings.FindProcessMode;
        set => SetWithArea(_settings.FindProcessMode, value, next => _settings.FindProcessMode = next, "Performance");
    }

    public bool IsTcpKeepAliveEnabled
    {
        get => _settings.IsTcpKeepAliveEnabled;
        set => SetWithArea(_settings.IsTcpKeepAliveEnabled, value, next => _settings.IsTcpKeepAliveEnabled = next, "Performance");
    }

    public string TcpKeepAliveIntervalText
    {
        get => _tcpKeepAliveIntervalText;
        set => SetIntWithArea(_settings.TcpKeepAliveInterval, value, next => _settings.TcpKeepAliveInterval = next, "Performance", next => _tcpKeepAliveIntervalText = next);
    }
}
