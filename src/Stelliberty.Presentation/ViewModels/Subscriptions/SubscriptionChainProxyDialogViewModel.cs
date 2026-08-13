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

    private bool _isEditingDraft;
    private string? _draftId;
    private string _draftName = string.Empty;
    private bool _hasAttemptedDraftSubmit;
    private string _draftNameErrorKey = string.Empty;
    private string _draftNodesErrorKey = string.Empty;

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

    public IReadOnlyList<SubscriptionChainProxyCustomItemViewModel> CustomItems => _customChainProxies
        .Select(ToCustomItem)
        .ToList();

    public bool HasCustoms => _customChainProxies.Count > 0;

    public bool CanAddDraft => IsContentVisible;

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

    public IReadOnlyList<SubscriptionChainProxySlotViewModel> Slots => _isEditingDraft
        ? _draftNodes
            .Select((name, index) => new SubscriptionChainProxySlotViewModel(index, name))
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

    public string DraftNameError => LocalizeError(_draftNameErrorKey);

    public bool IsDraftNameErrorVisible => !string.IsNullOrEmpty(_draftNameErrorKey);

    public string DraftNodesError => LocalizeError(_draftNodesErrorKey);

    public bool IsDraftNodesErrorVisible => !string.IsNullOrEmpty(_draftNodesErrorKey);

    public ICommand ToggleBuiltinCommand { get; }

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
        _draftNodes.Clear();
        _draftNodes.AddRange(custom.NodeNames.Where(name => !string.IsNullOrWhiteSpace(name)));
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

        var sourceIndex = _draftNodes.IndexOf(request.NodeName);
        if (sourceIndex < 0)
        {
            return;
        }

        var targetIndex = Math.Clamp(request.TargetIndex, 0, _draftNodes.Count - 1);
        if (sourceIndex == targetIndex)
        {
            return;
        }

        var nodeName = _draftNodes[sourceIndex];
        _draftNodes.RemoveAt(sourceIndex);
        _draftNodes.Insert(targetIndex, nodeName);
        RaiseStateChanged();
    }

    private void SaveDraft()
    {
        _hasAttemptedDraftSubmit = true;
        ValidateDraftName();
        ValidateDraftNodes();
        if (IsDraftNameErrorVisible || IsDraftNodesErrorVisible)
        {
            FocusFirstInvalidDraftInput();
            return;
        }

        var name = _draftName.Trim();
        var nodes = _draftNodes.Where(node => !string.IsNullOrWhiteSpace(node)).ToList();
        var draftId = _draftId ?? Guid.NewGuid().ToString("N");
        _customChainProxies.RemoveAll(item => item.Id == draftId);
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
        ResetDraftValidation();
    }

    private void ResetDraftValidation()
    {
        _hasAttemptedDraftSubmit = false;
        _draftNameErrorKey = string.Empty;
        _draftNodesErrorKey = string.Empty;
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

        OnPropertyChanged(nameof(DraftNameError));
        OnPropertyChanged(nameof(IsDraftNameErrorVisible));
    }

    private void ValidateDraftNodes()
    {
        _draftNodesErrorKey = _draftNodes.Count(node => !string.IsNullOrWhiteSpace(node)) < MinNodeCount
            ? "Subscriptions.ChainProxy.Error.Nodes"
            : string.Empty;
        OnPropertyChanged(nameof(DraftNodesError));
        OnPropertyChanged(nameof(IsDraftNodesErrorVisible));
    }

    private void FocusFirstInvalidDraftInput()
    {
        if (IsDraftNameErrorVisible)
        {
            InputFocusRequested?.Invoke(this, DialogInputField.Name);
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
        OnPropertyChanged(nameof(CustomItems));
        OnPropertyChanged(nameof(HasCustoms));
        OnPropertyChanged(nameof(CanAddDraft));
        OnPropertyChanged(nameof(DraftName));
        OnPropertyChanged(nameof(Slots));
        OnPropertyChanged(nameof(HasSelectedNodes));
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(DraftNameError));
        OnPropertyChanged(nameof(IsDraftNameErrorVisible));
        OnPropertyChanged(nameof(DraftNodesError));
        OnPropertyChanged(nameof(IsDraftNodesErrorVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs args) => RaiseStateChanged();

    private string Localize(string key) => _localization?.GetString(key) ?? key;

    private string LocalizeError(string key) => string.IsNullOrEmpty(key) ? string.Empty : Localize(key);
}
