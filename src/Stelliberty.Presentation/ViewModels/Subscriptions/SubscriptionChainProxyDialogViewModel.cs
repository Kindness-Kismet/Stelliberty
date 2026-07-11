using System.Windows.Input;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionChainProxySaveEventArgs(
    string SubscriptionId,
    IReadOnlyList<string> DisabledBuiltinNames,
    IReadOnlyList<SubscriptionCustomChainProxy> CustomChainProxies);

public sealed class SubscriptionChainProxyDialogViewModel : ViewModelBase, IDisposable
{
    // 中继链至少需要两个节点；点击顺序决定跳点顺序。
    private const int MinNodeCount = 2;

    private readonly DialogCloseResetScheduler _closeReset = new();
    private readonly ILocalizationService? _localization;
    // 覆写后上下文返回内置链和候选；null 表示无覆写。
    private readonly Func<string, SubscriptionChainProxyContext>? _contextLoader;

    private readonly List<string> _builtinNames = [];
    private readonly List<string> _disabledBuiltinNames = [];
    private readonly List<SubscriptionCustomChainProxy> _customChainProxies = [];
    private readonly List<ChainProxyNodeOption> _candidates = [];
    private readonly List<string> _draftNodes = [];

    private string? _subscriptionId;
    private bool _isDialogVisible;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private string _title = string.Empty;

    private bool _isEditingDraft;
    private string? _draftId;
    private string _draftName = string.Empty;
    private string _draftError = string.Empty;

    public SubscriptionChainProxyDialogViewModel(
        ILocalizationService? localization = null,
        Func<string, SubscriptionChainProxyContext>? contextLoader = null)
    {
        _localization = localization;
        _contextLoader = contextLoader;
        if (_localization is not null)
        {
            _localization.LanguageChanged += OnLanguageChanged;
        }

        ToggleBuiltinCommand = new RelayCommand<string>(ToggleBuiltin);
        StartAddDraftCommand = new RelayCommand(StartAddDraft);
        EditCustomCommand = new RelayCommand<string>(EditCustom);
        RemoveCustomCommand = new RelayCommand<string>(RemoveCustom);
        SelectCandidateCommand = new RelayCommand<string>(SelectCandidate);
        SaveDraftCommand = new RelayCommand(SaveDraft);
        CancelDraftCommand = new RelayCommand(CancelDraft);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
    }

    public event EventHandler<SubscriptionChainProxySaveEventArgs>? Saved;

    public event EventHandler? DialogStateChanged;

    public string? DialogSubscriptionId => _subscriptionId;

    public string DialogTitle => _title;

    public bool IsDialogVisible => _isDialogVisible;

    public bool IsLoading => _isLoading;

    public bool IsErrorVisible => !_isLoading && !string.IsNullOrEmpty(_errorMessage);

    public string ErrorMessage => _errorMessage;

    public bool IsContentVisible => !_isLoading && !IsErrorVisible;

    public bool IsEditingDraft => _isEditingDraft;

    public bool IsListVisible => IsContentVisible && !_isEditingDraft;

    public bool IsDraftVisible => IsContentVisible && _isEditingDraft;

    public IReadOnlyList<SubscriptionChainProxyBuiltinItemViewModel> BuiltinItems => _builtinNames
        .Select(name => new SubscriptionChainProxyBuiltinItemViewModel(name, !_disabledBuiltinNames.Contains(name, StringComparer.Ordinal), _localization))
        .ToList();

    public bool HasBuiltins => _builtinNames.Count > 0;

    public IReadOnlyList<SubscriptionChainProxyCustomItemViewModel> CustomItems => _customChainProxies
        .Select(ToCustomItem)
        .ToList();

    public bool HasCustoms => _customChainProxies.Count > 0;

    public bool IsEmptyVisible => IsListVisible && _builtinNames.Count == 0 && _customChainProxies.Count == 0;

    public bool CanAddDraft => IsContentVisible;

    public IReadOnlyList<string> DisabledBuiltinNames => _disabledBuiltinNames;

    public IReadOnlyList<SubscriptionCustomChainProxy> CustomChainProxies => _customChainProxies;

    public string DraftName
    {
        get => _draftName;
        set => SetProperty(ref _draftName, value);
    }

    public IReadOnlyList<SubscriptionChainProxySlotViewModel> Slots => _isEditingDraft
        ? _draftNodes
            .Select((name, index) => new SubscriptionChainProxySlotViewModel(index, name, _localization))
            .ToList()
        : [];

    public bool HasSelectedNodes => _isEditingDraft && _draftNodes.Count > 0;

    public IReadOnlyList<SubscriptionChainProxyCandidateViewModel> Candidates => _isEditingDraft
        ? _candidates
            .Select(candidate => new SubscriptionChainProxyCandidateViewModel(
                candidate.Name,
                candidate.Type,
                _draftNodes.Contains(candidate.Name, StringComparer.Ordinal)))
            .ToList()
        : [];

    public bool HasCandidates => _candidates.Count > 0;

    public string DraftError => _draftError;

    public bool IsDraftErrorVisible => !string.IsNullOrEmpty(_draftError);

    public ICommand ToggleBuiltinCommand { get; }

    public ICommand StartAddDraftCommand { get; }

    public ICommand EditCustomCommand { get; }

    public ICommand RemoveCustomCommand { get; }

    public ICommand SelectCandidateCommand { get; }

    public ICommand SaveDraftCommand { get; }

    public ICommand CancelDraftCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    public void Dispose()
    {
        _isDialogVisible = false;
        _subscriptionId = null;
        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }
    }

    public void Open(
        string subscriptionId,
        string title,
        IReadOnlyList<string> disabledBuiltinNames,
        IReadOnlyList<SubscriptionCustomChainProxy> customChainProxies)
    {
        _closeReset.Cancel();
        _subscriptionId = subscriptionId;
        _title = title;
        _isDialogVisible = true;
        _isLoading = true;
        _errorMessage = string.Empty;
        _builtinNames.Clear();
        _candidates.Clear();
        _disabledBuiltinNames.Clear();
        _disabledBuiltinNames.AddRange(disabledBuiltinNames);
        _customChainProxies.Clear();
        _customChainProxies.AddRange(customChainProxies);
        ExitDraftState();
        RaiseStateChanged();
        _ = LoadContextAsync(subscriptionId);
    }

    public void Close() => BeginClose();

    public void ClearForSubscription(string subscriptionId)
    {
        if (_subscriptionId == subscriptionId)
        {
            BeginClose();
        }
    }

    // 后台覆写预览在订阅或对话框变化时丢弃。
    private async Task LoadContextAsync(string subscriptionId)
    {
        try
        {
            var context = _contextLoader is null
                ? new SubscriptionChainProxyContext([], [])
                : await Task.Run(() => _contextLoader(subscriptionId));
            if (_subscriptionId != subscriptionId || !_isDialogVisible)
            {
                return;
            }

            _builtinNames.Clear();
            _builtinNames.AddRange(context.BuiltinChainProxyNames);
            _candidates.Clear();
            _candidates.AddRange(context.Candidates);
            _isLoading = false;
            _errorMessage = string.Empty;
            RaiseStateChanged();
        }
        catch (Exception exception)
        {
            if (_subscriptionId != subscriptionId || !_isDialogVisible)
            {
                return;
            }

            _isLoading = false;
            _errorMessage = exception.Message;
            AppLogger.Warning($"Chain proxy override preview failed: {exception.Message}");
            RaiseStateChanged();
        }
    }

    private void ToggleBuiltin(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_builtinNames.Contains(name, StringComparer.Ordinal))
        {
            return;
        }

        if (!_disabledBuiltinNames.Remove(name))
        {
            _disabledBuiltinNames.Add(name);
        }

        RaiseStateChanged();
    }

    private void StartAddDraft()
    {
        if (!IsContentVisible)
        {
            return;
        }

        _draftId = Guid.NewGuid().ToString("N");
        _draftName = string.Empty;
        _draftNodes.Clear();
        _draftError = string.Empty;
        _isEditingDraft = true;
        RaiseStateChanged();
    }

    private void EditCustom(string? id)
    {
        var custom = _customChainProxies.FirstOrDefault(item => item.Id == id);
        if (custom is null)
        {
            return;
        }

        _draftId = custom.Id;
        _draftName = custom.DisplayName;
        _draftNodes.Clear();
        _draftNodes.AddRange(custom.NodeNames.Where(name => !string.IsNullOrWhiteSpace(name)));
        _draftError = string.Empty;
        _isEditingDraft = true;
        RaiseStateChanged();
    }

    private void RemoveCustom(string? id)
    {
        if (_customChainProxies.RemoveAll(item => item.Id == id) > 0)
        {
            RaiseStateChanged();
        }
    }

    private void SelectCandidate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_isEditingDraft)
        {
            return;
        }

        // 点击已选节点会移除；新节点追加到链尾。
        if (!_draftNodes.Remove(name))
        {
            _draftNodes.Add(name);
        }

        _draftError = string.Empty;
        RaiseStateChanged();
    }

    private void SaveDraft()
    {
        var name = _draftName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _draftError = Localize("Subscriptions.ChainProxy.Error.Name");
            RaiseStateChanged();
            return;
        }

        var nodes = _draftNodes.Where(node => !string.IsNullOrWhiteSpace(node)).ToList();
        if (nodes.Count < MinNodeCount)
        {
            _draftError = Localize("Subscriptions.ChainProxy.Error.Nodes");
            RaiseStateChanged();
            return;
        }

        var draftId = _draftId ?? Guid.NewGuid().ToString("N");
        _customChainProxies.RemoveAll(item => item.Id == draftId || string.Equals(item.DisplayName, name, StringComparison.Ordinal));
        _customChainProxies.Add(new SubscriptionCustomChainProxy(draftId, name, nodes));
        ExitDraftState();
        RaiseStateChanged();
    }

    private void CancelDraft()
    {
        ExitDraftState();
        RaiseStateChanged();
    }

    private void Save()
    {
        if (_subscriptionId is null)
        {
            return;
        }

        var args = new SubscriptionChainProxySaveEventArgs(_subscriptionId, _disabledBuiltinNames.ToList(), _customChainProxies.ToList());
        BeginClose();
        Saved?.Invoke(this, args);
        AppLogger.Info($"Subscription chain proxy save event fired: {args.SubscriptionId}");
    }

    private void Cancel() => BeginClose();

    private SubscriptionChainProxyCustomItemViewModel ToCustomItem(SubscriptionCustomChainProxy custom)
    {
        var candidateNames = _candidates.Select(candidate => candidate.Name).ToHashSet(StringComparer.Ordinal);
        var missing = custom.NodeNames.Where(node => !candidateNames.Contains(node)).ToList();
        return new SubscriptionChainProxyCustomItemViewModel(
            custom.Id,
            custom.DisplayName,
            string.Join(" → ", custom.NodeNames),
            missing.Count > 0,
            missing.Count > 0 ? string.Format(Localize("Subscriptions.ChainProxy.MissingNodes"), string.Join(", ", missing)) : string.Empty);
    }

    private void ExitDraftState()
    {
        _isEditingDraft = false;
        _draftId = null;
        _draftName = string.Empty;
        _draftNodes.Clear();
        _draftError = string.Empty;
    }

    private void Reset()
    {
        _isDialogVisible = false;
        _subscriptionId = null;
        _title = string.Empty;
        _isLoading = false;
        _errorMessage = string.Empty;
        _builtinNames.Clear();
        _disabledBuiltinNames.Clear();
        _customChainProxies.Clear();
        _candidates.Clear();
        ExitDraftState();
        RaiseStateChanged();
    }

    private void BeginClose()
    {
        if (!_isDialogVisible)
        {
            return;
        }

        _isDialogVisible = false;
        RaiseStateChanged();
        _closeReset.Run(() => !_isDialogVisible, Reset);
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(DialogSubscriptionId));
        OnPropertyChanged(nameof(DialogTitle));
        OnPropertyChanged(nameof(IsDialogVisible));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsErrorVisible));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(IsContentVisible));
        OnPropertyChanged(nameof(IsEditingDraft));
        OnPropertyChanged(nameof(IsListVisible));
        OnPropertyChanged(nameof(IsDraftVisible));
        OnPropertyChanged(nameof(BuiltinItems));
        OnPropertyChanged(nameof(HasBuiltins));
        OnPropertyChanged(nameof(CustomItems));
        OnPropertyChanged(nameof(HasCustoms));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(CanAddDraft));
        OnPropertyChanged(nameof(DraftName));
        OnPropertyChanged(nameof(Slots));
        OnPropertyChanged(nameof(HasSelectedNodes));
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(DraftError));
        OnPropertyChanged(nameof(IsDraftErrorVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs args) => RaiseStateChanged();

    private string Localize(string key) => _localization?.GetString(key) ?? key;
}
