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
    // 中继链至少需要两个跳点；代理组只能作为首个上游跳点。
    private const int MinHopCount = 2;

    private readonly DialogCloseResetScheduler _closeReset = new();
    private readonly ILocalizationService? _localization;
    // 覆写后上下文返回内置链和候选；null 表示无覆写。
    private readonly Func<string, SubscriptionChainProxyContext>? _contextLoader;

    private readonly List<string> _builtinNames = [];
    private readonly List<string> _disabledBuiltinNames = [];
    private readonly List<SubscriptionCustomChainProxy> _customChainProxies = [];
    private readonly List<ChainProxyGroupOption> _proxyGroups = [];
    private readonly List<ChainProxyHopOption> _candidates = [];
    private readonly List<SubscriptionChainProxyHop> _draftHops = [];

    private string? _subscriptionId;
    private bool _isDialogVisible;
    private bool _isLoading;
    private string _errorMessage = string.Empty;

    private bool _isEditingDraft;
    private string? _draftId;
    private string _draftName = string.Empty;
    private ChainProxyGroupOption? _draftProxyGroup;
    private bool _hasAttemptedDraftSubmit;
    private string _draftNameErrorKey = string.Empty;
    private string _draftNodesErrorKey = string.Empty;
    private string _draftProxyGroupErrorKey = string.Empty;

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
        ToggleCustomCommand = new RelayCommand<string>(ToggleCustom);
        StartAddDraftCommand = new RelayCommand(StartAddDraft);
        EditCustomCommand = new RelayCommand<string>(EditCustom);
        RemoveCustomCommand = new RelayCommand<string>(RemoveCustom);
        SelectCandidateCommand = new RelayCommand<string>(SelectCandidate);
        MoveDraftNodeCommand = new RelayCommand<SubscriptionChainProxyMoveRequest>(MoveDraftNode);
        SaveDraftCommand = new RelayCommand(SaveDraft);
        CancelDraftCommand = new RelayCommand(CancelDraft);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
    }

    public event EventHandler<SubscriptionChainProxySaveEventArgs>? Saved;

    public event EventHandler? DialogStateChanged;

    public event EventHandler<DialogInputField>? InputFocusRequested;

    public string? DialogSubscriptionId => _subscriptionId;

    public bool IsDialogVisible => _isDialogVisible;

    public bool IsLoading => _isLoading;

    public bool IsErrorVisible => !_isLoading && !string.IsNullOrEmpty(_errorMessage);

    public string ErrorMessage => _errorMessage;

    public bool IsContentVisible => !_isLoading && !IsErrorVisible;

    public bool IsEditingDraft => _isEditingDraft;

    public bool IsListVisible => IsContentVisible && !_isEditingDraft;

    public bool IsDraftVisible => IsContentVisible && _isEditingDraft;

    public IReadOnlyList<SubscriptionChainProxyBuiltinItemViewModel> BuiltinItems => _builtinNames
        .Select(name => new SubscriptionChainProxyBuiltinItemViewModel(name, !_disabledBuiltinNames.Contains(name, StringComparer.Ordinal)))
        .ToList();

    public bool HasBuiltins => _builtinNames.Count > 0;

    public IReadOnlyList<ChainProxyGroupOption> ProxyGroups => _proxyGroups;

    public IReadOnlyList<SubscriptionChainProxyCustomItemViewModel> CustomItems => _customChainProxies
        .Select(ToCustomItem)
        .ToList();

    public bool HasCustoms => _customChainProxies.Count > 0;

    public bool CanAddDraft => IsContentVisible && _proxyGroups.Count > 0;

    public IReadOnlyList<string> DisabledBuiltinNames => _disabledBuiltinNames;

    public IReadOnlyList<SubscriptionCustomChainProxy> CustomChainProxies => _customChainProxies;

    public string DraftName
    {
        get => _draftName;
        set
        {
            if (SetProperty(ref _draftName, value) && _hasAttemptedDraftSubmit)
            {
                ValidateDraftName();
            }
        }
    }

    public ChainProxyGroupOption? DraftProxyGroup
    {
        get => _draftProxyGroup;
        set
        {
            if (!SetProperty(ref _draftProxyGroup, value))
            {
                return;
            }

            _draftHops.RemoveAll(hop => hop.Kind == SubscriptionChainProxyHopKind.ProxyGroup
                && string.Equals(hop.Name, value?.Name, StringComparison.Ordinal));
            if (_hasAttemptedDraftSubmit)
            {
                ValidateDraftProxyGroup();
                ValidateDraftNodes();
            }

            RaiseDraftGroupChanged();
        }
    }

    public IReadOnlyList<SubscriptionChainProxySlotViewModel> Slots => _isEditingDraft
        ? _draftHops
            .Select((hop, index) => new SubscriptionChainProxySlotViewModel(index, hop))
            .ToList()
        : [];

    public bool HasSelectedNodes => _isEditingDraft && _draftHops.Count > 0;

    public IReadOnlyList<SubscriptionChainProxyCandidateViewModel> Candidates => _isEditingDraft
        ? AvailableCandidates()
            .Select(candidate => new SubscriptionChainProxyCandidateViewModel(
                candidate.Key,
                candidate.Hop.Kind,
                candidate.Name,
                candidate.Type,
                _draftHops.Contains(candidate.Hop)))
            .ToList()
        : [];

    public bool HasCandidates => AvailableCandidates().Any();

    public bool CanSaveDraft => _draftHops.Count(hop => !string.IsNullOrWhiteSpace(hop.Name)) >= MinHopCount;

    public string DraftNameError => LocalizeError(_draftNameErrorKey);

    public bool IsDraftNameErrorVisible => !string.IsNullOrEmpty(_draftNameErrorKey);

    public string DraftNodesError => LocalizeError(_draftNodesErrorKey);

    public bool IsDraftNodesErrorVisible => !string.IsNullOrEmpty(_draftNodesErrorKey);

    public string DraftProxyGroupError => LocalizeError(_draftProxyGroupErrorKey);

    public bool IsDraftProxyGroupErrorVisible => !string.IsNullOrEmpty(_draftProxyGroupErrorKey);

    public ICommand ToggleBuiltinCommand { get; }

    public ICommand ToggleCustomCommand { get; }

    public ICommand StartAddDraftCommand { get; }

    public ICommand EditCustomCommand { get; }

    public ICommand RemoveCustomCommand { get; }

    public ICommand SelectCandidateCommand { get; }

    public ICommand MoveDraftNodeCommand { get; }

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
        IReadOnlyList<string> disabledBuiltinNames,
        IReadOnlyList<SubscriptionCustomChainProxy> customChainProxies)
    {
        _closeReset.Cancel();
        _subscriptionId = subscriptionId;
        _isDialogVisible = true;
        _isLoading = true;
        _errorMessage = string.Empty;
        _builtinNames.Clear();
        _proxyGroups.Clear();
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
                ? new SubscriptionChainProxyContext([], [], [])
                : await Task.Run(() => _contextLoader(subscriptionId));
            if (_subscriptionId != subscriptionId || !_isDialogVisible)
            {
                return;
            }

            _builtinNames.Clear();
            _builtinNames.AddRange(context.BuiltinChainProxyNames);
            _proxyGroups.Clear();
            _proxyGroups.AddRange(context.ProxyGroups);
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

        if (_disabledBuiltinNames.Contains(name, StringComparer.Ordinal))
        {
            _disabledBuiltinNames.Remove(name);
        }
        else
        {
            _disabledBuiltinNames.Add(name);
        }

        RaiseStateChanged();
    }

    private void ToggleCustom(string? id)
    {
        var index = _customChainProxies.FindIndex(item => item.Id == id);
        if (index < 0)
        {
            return;
        }

        var current = _customChainProxies[index];
        _customChainProxies[index] = current with { IsEnabled = !current.IsEnabled };
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
        _draftHops.Clear();
        _draftProxyGroup = _proxyGroups.FirstOrDefault();
        _isEditingDraft = true;
        ResetDraftValidation();
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
        _draftHops.Clear();
        _draftHops.AddRange(custom.Hops.Where(hop => !string.IsNullOrWhiteSpace(hop.Name)
            && !(hop.Kind == SubscriptionChainProxyHopKind.ProxyGroup
                && string.Equals(hop.Name, custom.ProxyGroupName, StringComparison.Ordinal))));
        _draftProxyGroup = _proxyGroups.FirstOrDefault(group => string.Equals(group.Name, custom.ProxyGroupName, StringComparison.Ordinal));
        _isEditingDraft = true;
        ResetDraftValidation();
        RaiseStateChanged();
    }

    private void RemoveCustom(string? id)
    {
        if (_customChainProxies.RemoveAll(item => item.Id == id) > 0)
        {
            RaiseStateChanged();
        }
    }

    private void SelectCandidate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !_isEditingDraft)
        {
            return;
        }

        var candidate = _candidates.FirstOrDefault(item => item.Key == key);
        if (candidate is null
            || (candidate.Hop.Kind == SubscriptionChainProxyHopKind.ProxyGroup
                && string.Equals(candidate.Name, _draftProxyGroup?.Name, StringComparison.Ordinal)))
        {
            return;
        }

        if (candidate.Hop.Kind == SubscriptionChainProxyHopKind.ProxyGroup)
        {
            var selectedGroup = _draftHops.FirstOrDefault(hop => hop.Kind == SubscriptionChainProxyHopKind.ProxyGroup);
            if (selectedGroup is not null)
            {
                _draftHops.Remove(selectedGroup);
            }

            if (selectedGroup?.Name != candidate.Name)
            {
                _draftHops.Insert(0, candidate.Hop);
            }
        }
        else if (!_draftHops.Remove(candidate.Hop))
        {
            _draftHops.Add(candidate.Hop);
        }

        if (_hasAttemptedDraftSubmit)
        {
            ValidateDraftNodes();
        }
        RaiseStateChanged();
    }

    private void MoveDraftNode(SubscriptionChainProxyMoveRequest? request)
    {
        if (!_isEditingDraft || request is null)
        {
            return;
        }

        var sourceIndex = _draftHops.FindIndex(hop => HopKey(hop) == request.HopKey);
        if (sourceIndex < 0)
        {
            return;
        }

        if (_draftHops[sourceIndex].Kind == SubscriptionChainProxyHopKind.ProxyGroup)
        {
            return;
        }

        var targetIndex = Math.Clamp(request.TargetIndex, 0, _draftHops.Count - 1);
        if (_draftHops.Any(hop => hop.Kind == SubscriptionChainProxyHopKind.ProxyGroup))
        {
            targetIndex = Math.Max(1, targetIndex);
        }
        if (sourceIndex == targetIndex)
        {
            return;
        }

        var hop = _draftHops[sourceIndex];
        _draftHops.RemoveAt(sourceIndex);
        _draftHops.Insert(targetIndex, hop);
        RaiseStateChanged();
    }

    private void SaveDraft()
    {
        if (!CanSaveDraft)
        {
            return;
        }

        _hasAttemptedDraftSubmit = true;
        ValidateDraftName();
        ValidateDraftProxyGroup();
        ValidateDraftNodes();
        if (IsDraftNameErrorVisible || IsDraftProxyGroupErrorVisible || IsDraftNodesErrorVisible)
        {
            FocusFirstInvalidDraftInput();
            return;
        }

        var name = _draftName.Trim();
        var hops = _draftHops.Where(hop => !string.IsNullOrWhiteSpace(hop.Name)).ToList();
        var draftId = _draftId ?? Guid.NewGuid().ToString("N");
        var existing = _customChainProxies.FirstOrDefault(item => item.Id == draftId);
        var draft = new SubscriptionCustomChainProxy(
            draftId,
            name,
            _draftProxyGroup!.Name,
            hops,
            existing?.IsEnabled ?? true);
        _customChainProxies.RemoveAll(item => item.Id == draftId);
        _customChainProxies.Add(draft);
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
        Saved?.Invoke(this, args);
        AppLogger.Info($"Subscription chain proxy save event fired: {args.SubscriptionId}");
    }

    internal void CompleteSave() => BeginClose();

    private void Cancel() => BeginClose();

    private SubscriptionChainProxyCustomItemViewModel ToCustomItem(SubscriptionCustomChainProxy custom)
    {
        var candidateKeys = _candidates.Select(candidate => candidate.Key).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        if (!_proxyGroups.Any(group => string.Equals(group.Name, custom.ProxyGroupName, StringComparison.Ordinal)))
        {
            missing.Add(custom.ProxyGroupName);
        }

        missing.AddRange(custom.Hops
            .Where(hop => !candidateKeys.Contains(HopKey(hop)))
            .Select(hop => hop.Name));
        return new SubscriptionChainProxyCustomItemViewModel(
            custom.Id,
            custom.DisplayName,
            custom.ProxyGroupName,
            string.Join(" → ", custom.Hops.Select(hop => hop.Name)),
            custom.IsEnabled,
            missing.Count > 0,
            missing.Count > 0 ? string.Format(Localize("Subscriptions.ChainProxy.MissingNodes"), string.Join(", ", missing)) : string.Empty);
    }

    private void ExitDraftState()
    {
        _isEditingDraft = false;
        _draftId = null;
        _draftName = string.Empty;
        _draftProxyGroup = null;
        _draftHops.Clear();
        ResetDraftValidation();
    }

    private void ResetDraftValidation()
    {
        _hasAttemptedDraftSubmit = false;
        _draftNameErrorKey = string.Empty;
        _draftNodesErrorKey = string.Empty;
        _draftProxyGroupErrorKey = string.Empty;
    }

    private void ValidateDraftName()
    {
        var name = _draftName.Trim();
        _draftNameErrorKey = string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            _draftNameErrorKey = "Subscriptions.ChainProxy.Error.Name";
        }
        else if (_customChainProxies.Any(item => item.Id != _draftId
            && string.Equals(item.DisplayName, name, StringComparison.Ordinal)))
        {
            _draftNameErrorKey = "Subscriptions.ChainProxy.Error.DuplicateName";
        }
        else if (_builtinNames.Contains(name, StringComparer.Ordinal)
            || _proxyGroups.Any(group => string.Equals(group.Name, name, StringComparison.Ordinal))
            || _candidates.Any(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal)))
        {
            _draftNameErrorKey = "Subscriptions.ChainProxy.Error.DuplicateName";
        }

        OnPropertyChanged(nameof(DraftNameError));
        OnPropertyChanged(nameof(IsDraftNameErrorVisible));
    }

    private void ValidateDraftNodes()
    {
        _draftNodesErrorKey = _draftHops.Skip(1).Any(hop => hop.Kind != SubscriptionChainProxyHopKind.Proxy)
            ? "Subscriptions.ChainProxy.Error.GroupPosition"
            : string.Empty;
        OnPropertyChanged(nameof(DraftNodesError));
        OnPropertyChanged(nameof(IsDraftNodesErrorVisible));
    }

    private void ValidateDraftProxyGroup()
    {
        _draftProxyGroupErrorKey = _draftProxyGroup is null
            ? "Subscriptions.ChainProxy.Error.ProxyGroup"
            : string.Empty;
        OnPropertyChanged(nameof(DraftProxyGroupError));
        OnPropertyChanged(nameof(IsDraftProxyGroupErrorVisible));
    }

    private void FocusFirstInvalidDraftInput()
    {
        if (IsDraftNameErrorVisible)
        {
            InputFocusRequested?.Invoke(this, DialogInputField.Name);
            return;
        }

        if (IsDraftProxyGroupErrorVisible)
        {
            InputFocusRequested?.Invoke(this, DialogInputField.ProxyGroup);
            return;
        }

        if (IsDraftNodesErrorVisible)
        {
            InputFocusRequested?.Invoke(this, DialogInputField.Nodes);
        }
    }

    private void Reset()
    {
        _isDialogVisible = false;
        _subscriptionId = null;
        _isLoading = false;
        _errorMessage = string.Empty;
        _builtinNames.Clear();
        _proxyGroups.Clear();
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
        OnPropertyChanged(nameof(ProxyGroups));
        OnPropertyChanged(nameof(CustomItems));
        OnPropertyChanged(nameof(HasCustoms));
        OnPropertyChanged(nameof(CanAddDraft));
        OnPropertyChanged(nameof(DraftName));
        OnPropertyChanged(nameof(DraftProxyGroup));
        OnPropertyChanged(nameof(Slots));
        OnPropertyChanged(nameof(HasSelectedNodes));
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(CanSaveDraft));
        OnPropertyChanged(nameof(DraftNameError));
        OnPropertyChanged(nameof(IsDraftNameErrorVisible));
        OnPropertyChanged(nameof(DraftNodesError));
        OnPropertyChanged(nameof(IsDraftNodesErrorVisible));
        OnPropertyChanged(nameof(DraftProxyGroupError));
        OnPropertyChanged(nameof(IsDraftProxyGroupErrorVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseDraftGroupChanged()
    {
        OnPropertyChanged(nameof(Slots));
        OnPropertyChanged(nameof(HasSelectedNodes));
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(CanSaveDraft));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs args) => RaiseStateChanged();

    private string Localize(string key) => _localization?.GetString(key) ?? key;

    private string LocalizeError(string key) => string.IsNullOrEmpty(key) ? string.Empty : Localize(key);

    private IEnumerable<ChainProxyHopOption> AvailableCandidates()
    {
        return _candidates.Where(candidate => candidate.Hop.Kind != SubscriptionChainProxyHopKind.ProxyGroup
            || !string.Equals(candidate.Name, _draftProxyGroup?.Name, StringComparison.Ordinal));
    }

    private static string HopKey(SubscriptionChainProxyHop hop) => $"{hop.Kind}:{hop.Name}";
}
