using System.Windows.Input;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SettingsAppLogViewModel : ViewModelBase, IDisposable
{
    private const int MaxVisibleRows = 60;
    private const int MaxLoadedEntries = 2000;

    private readonly ILocalizationService _localization;
    private readonly IAppLogReader? _reader;
    private readonly IAppLogExporter? _exporter;
    private CancellationTokenSource? _refreshCancellation;
    private IReadOnlyList<AppLogEntry> _logs = [];
    private IReadOnlyList<AppLogEntry> _filteredLogs = [];
    private IReadOnlyList<AppLogRowViewModel> _filteredLogRows = [];
    private AppLogLevel? _filterLevel;
    private AppLogDateRange _dateRange = AppLogDateRange.All;
    private string _searchKeyword = string.Empty;
    private string _statusText = string.Empty;
    private int _refreshRequestId;
    private bool _isLoading;
    private bool _isDisposed;

    public SettingsAppLogViewModel(ILocalizationService localization, IAppLogReader? reader = null, IAppLogExporter? exporter = null)
    {
        _localization = localization;
        _reader = reader;
        _exporter = exporter;
        RefreshCommand = new RelayCommand(Refresh);
        _localization.LanguageChanged += OnLanguageChanged;
        RebuildFilteredLogs();
    }

    public IReadOnlyList<AppLogEntry> Logs => _logs;

    public IReadOnlyList<AppLogEntry> FilteredLogs => _filteredLogs;

    public IReadOnlyList<AppLogRowViewModel> FilteredLogRows => _filteredLogRows;

    public int TotalLogCount => Logs.Count;

    public int FilteredLogCount => FilteredLogs.Count;

    public int WarningLogCount => Logs.Count(log => log.Level == AppLogLevel.Warning);

    public int ErrorLogCount => Logs.Count(log => log.Level == AppLogLevel.Error);

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmptyVisible));
                OnPropertyChanged(nameof(IsLogListVisible));
            }
        }
    }

    // 等级、日期下拉项随语言重建以刷新显示名；选中项按存储值回查。
    public IReadOnlyList<SelectionOption<AppLogLevel?>> LevelOptions =>
    [
        new(null, _localization.GetString("Common.Filter.All")),
        new(AppLogLevel.Debug, _localization.GetString("AppLogs.Level.Debug")),
        new(AppLogLevel.Info, _localization.GetString("AppLogs.Level.Info")),
        new(AppLogLevel.Warning, _localization.GetString("AppLogs.Level.Warning")),
        new(AppLogLevel.Error, _localization.GetString("AppLogs.Level.Error")),
    ];

    public SelectionOption<AppLogLevel?> SelectedLevelOption
    {
        get => LevelOptions.FirstOrDefault(option => option.Value == _filterLevel) ?? LevelOptions[0];
        set
        {
            if (value is not null)
            {
                SetFilterLevel(value.Value);
            }
        }
    }

    public IReadOnlyList<SelectionOption<AppLogDateRange>> DateRangeOptions =>
    [
        new(AppLogDateRange.All, _localization.GetString("AppLogs.Date.All")),
        new(AppLogDateRange.Today, _localization.GetString("AppLogs.Date.Today")),
        new(AppLogDateRange.Last7Days, _localization.GetString("AppLogs.Date.Last7Days")),
        new(AppLogDateRange.Last30Days, _localization.GetString("AppLogs.Date.Last30Days")),
    ];

    public SelectionOption<AppLogDateRange> SelectedDateRangeOption
    {
        get => DateRangeOptions.FirstOrDefault(option => option.Value == _dateRange) ?? DateRangeOptions[0];
        set
        {
            if (value is not null)
            {
                SetDateRange(value.Value);
            }
        }
    }

    public bool IsEmptyVisible => !IsLoading && FilteredLogs.Count == 0;

    public bool IsLogListVisible => !IsLoading && FilteredLogs.Count > 0;

    public string EmptyText => Logs.Count == 0
        ? _localization.GetString("AppLogs.Empty.NoLogs")
        : _localization.GetString("AppLogs.Empty.NoMatches");

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(IsStatusVisible));
            }
        }
    }

    public bool IsStatusVisible => !string.IsNullOrWhiteSpace(StatusText);

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                RaiseLogStateChanged();
            }
        }
    }

    public ICommand RefreshCommand { get; }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _refreshRequestId++;
        var cancellation = _refreshCancellation;
        _refreshCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    public void Refresh()
    {
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var reader = _reader ?? throw new InvalidOperationException("App log reader is not initialized");
        var requestId = ++_refreshRequestId;
        try
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _refreshCancellation = cancellation;
            IsLoading = true;
            await Task.Yield();

            var logs = await Task.Run(() => reader.ReadEntries(MaxLoadedEntries, cancellation.Token), cancellation.Token);
            if (IsStaleRefresh(requestId, cancellation))
            {
                return;
            }

            _logs = logs;
            StatusText = string.Empty;
            IsLoading = false;
            RaiseLogStateChanged();
        }
        catch (OperationCanceledException)
        {
            if (requestId == _refreshRequestId)
            {
                IsLoading = false;
            }
        }
        catch (Exception exception)
        {
            if (_isDisposed || requestId != _refreshRequestId)
            {
                return;
            }

            _logs = [];
            StatusText = exception.Message;
            IsLoading = false;
            RaiseLogStateChanged();
        }
    }

    public async Task ExportToFileAsync(string exportPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            return;
        }
        var exporter = _exporter ?? throw new InvalidOperationException("App log exporter is not initialized");

        try
        {
            await exporter.ExportAsync(exportPath, cancellationToken);
            ReportExport(true, exportPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "App log export failed");
            ReportExport(false, exception.Message);
        }
    }

    public void ReportExport(bool success, string detail)
    {
        StatusText = success
            ? string.Format(_localization.GetString("AppLogs.Export.Success"), detail)
            : string.Format(_localization.GetString("AppLogs.Export.Failed"), detail);
    }

    private void SetFilterLevel(AppLogLevel? level)
    {
        if (_filterLevel == level)
        {
            return;
        }

        _filterLevel = level;
        RaiseLogStateChanged();
    }

    private void SetDateRange(AppLogDateRange range)
    {
        if (_dateRange == range)
        {
            return;
        }

        _dateRange = range;
        RaiseLogStateChanged();
    }

    private bool MatchesFilter(AppLogEntry log)
    {
        if (_filterLevel is not null && log.Level != _filterLevel)
        {
            return false;
        }

        if (!MatchesDateRange(log))
        {
            return false;
        }

        var keyword = _searchKeyword.Trim();
        return string.IsNullOrEmpty(keyword)
            || log.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || log.Format().Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    // 按本地时区日界比较；近 7/30 天包含当天。
    private bool MatchesDateRange(AppLogEntry log)
    {
        if (_dateRange == AppLogDateRange.All)
        {
            return true;
        }

        var localDate = log.Timestamp.ToLocalTime().Date;
        var today = DateTime.Now.Date;
        return _dateRange switch
        {
            AppLogDateRange.Today => localDate == today,
            AppLogDateRange.Last7Days => localDate >= today.AddDays(-6),
            AppLogDateRange.Last30Days => localDate >= today.AddDays(-29),
            _ => true
        };
    }

    private void RebuildFilteredLogs()
    {
        _filteredLogs = _logs.Where(MatchesFilter).ToList();
        _filteredLogRows = _filteredLogs
            .Reverse()
            .Take(MaxVisibleRows)
            .Select((log, index) => new AppLogRowViewModel(index + 1, log, _localization))
            .ToList();
    }

    private bool IsStaleRefresh(int requestId, CancellationTokenSource cancellation)
    {
        return _isDisposed
            || requestId != _refreshRequestId
            || cancellation.IsCancellationRequested
            || !ReferenceEquals(_refreshCancellation, cancellation);
    }

    private void RaiseLogStateChanged()
    {
        RebuildFilteredLogs();
        OnPropertyChanged(nameof(Logs));
        OnPropertyChanged(nameof(FilteredLogs));
        OnPropertyChanged(nameof(FilteredLogRows));
        OnPropertyChanged(nameof(TotalLogCount));
        OnPropertyChanged(nameof(FilteredLogCount));
        OnPropertyChanged(nameof(WarningLogCount));
        OnPropertyChanged(nameof(ErrorLogCount));
        OnPropertyChanged(nameof(SelectedLevelOption));
        OnPropertyChanged(nameof(SelectedDateRangeOption));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(IsLogListVisible));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsStatusVisible));
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(LevelOptions));
        OnPropertyChanged(nameof(DateRangeOptions));
        RaiseLogStateChanged();
    }
}
