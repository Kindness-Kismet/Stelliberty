using System.Collections.ObjectModel;
using System.Windows.Input;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Rules;
using Stelliberty.Domain.Rules;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class RulePageViewModel : ViewModelBase, IDisposable
{
    private readonly RuleOverrideService? _overrideService;
    private readonly RuleListLoader? _loader;
    private readonly ILocalizationService? _localization;
    private readonly RuleSearch _search = new();
    private bool _isCoreRunning = true;
    private bool _hasRequestedRefresh;
    private bool _isEditorDialogVisible;
    private bool _isTemplateDialogVisible;
    private bool _isTemplateSelectMode;
    private string _type = "DOMAIN-SUFFIX";
    private string _payload = string.Empty;
    private string _proxy = "DIRECT";
    private string _options = string.Empty;
    private string _templateName = string.Empty;
    private string _errorText = string.Empty;
    private RuleTemplateOptionViewModel? _selectedTemplate;
    private RuleEditorRowViewModel? _editingRule;
    private RuleEditorRowViewModel? _deleteCandidate;
    private RuleEditorSnapshot _snapshot = new(string.Empty, [], [], false);
    private string _searchKeyword = string.Empty;
    private RuleTypeBucket _typeBucket = RuleTypeBucket.All;
    private IReadOnlyList<RuleItem> _rules = [];
    private IReadOnlyList<RuleItem> _filteredRules = [];
    private IReadOnlyList<RuleRowViewModel> _filteredRuleRows = [];

    public RulePageViewModel(RuleOverrideService? overrideService = null, ILocalizationService? localization = null)
    {
        _overrideService = overrideService;
        _localization = localization;
        _localization?.LanguageChanged += OnLanguageChanged;
        RefreshRulesCommand = new RelayCommand(RequestRefresh);
        ShowAllTypesCommand = new RelayCommand(() => SetTypeBucket(RuleTypeBucket.All));
        ShowDomainRulesCommand = new RelayCommand(() => SetTypeBucket(RuleTypeBucket.Domain));
        ShowIpRulesCommand = new RelayCommand(() => SetTypeBucket(RuleTypeBucket.Ip));
        ShowRuleSetRulesCommand = new RelayCommand(() => SetTypeBucket(RuleTypeBucket.RuleSet));
        ShowOtherRulesCommand = new RelayCommand(() => SetTypeBucket(RuleTypeBucket.Other));
        AddRuleCommand = new RelayCommand(() => OpenEditor(null));
        EditRuleCommand = new RelayCommand<RuleEditorRowViewModel>(row => OpenEditor(row));
        DeleteRuleCommand = new RelayCommand<RuleEditorRowViewModel>(ShowDeleteRuleDialog);
        MoveRuleCommand = new RelayCommand<RuleMoveRequest>(MoveRuleToIndex);
        SaveRuleCommand = new RelayCommand(SaveRule);
        SaveChangesCommand = new RelayCommand(SaveChanges);
        CancelEditorCommand = new RelayCommand(CloseEditor);
        OpenTemplateCommand = new RelayCommand(OpenTemplateSelector);
        OpenCreateTemplateCommand = new RelayCommand(OpenTemplateCreator);
        SaveTemplateCommand = new RelayCommand(SaveTemplate, () => CanSaveTemplate);
        ApplyTemplateCommand = new RelayCommand(ApplyTemplate, () => SelectedTemplate is not null);
        DeleteSingleTemplateCommand = new RelayCommand<RuleTemplateOptionViewModel>(DeleteSingleTemplate);
        CancelTemplateCommand = new RelayCommand(() => IsTemplateDialogVisible = false);
        ConfirmDeleteRuleCommand = new RelayCommand(ConfirmDeleteRule);
        DeleteEditingRuleCommand = new RelayCommand(DeleteEditingRule);
        CancelDeleteRuleCommand = new RelayCommand(() => DeleteCandidate = null);
    }

    public RulePageViewModel(RuleListLoader loader, ILocalizationService? localization = null)
        : this(localization: localization)
    {
        _loader = loader;
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? RuntimeRefreshRequested;

    public ObservableCollection<RuleEditorRowViewModel> BuiltinRules { get; } = [];
    public ObservableCollection<RuleEditorRowViewModel> CustomRules { get; } = [];
    public ObservableCollection<RuleEditorRowViewModel> VisibleRules { get; } = [];
    public IReadOnlyList<string> RuleTypes { get; } = ["DOMAIN", "DOMAIN-SUFFIX", "DOMAIN-KEYWORD", "IP-CIDR", "IP-CIDR6", "GEOIP", "GEOSITE", "RULE-SET", "PROCESS-NAME", "PROCESS-PATH", "DST-PORT", "SRC-IP-CIDR", "MATCH"];
    public IReadOnlyList<RuleTemplateOptionViewModel> Templates => _snapshot.Templates.Select(template => new RuleTemplateOptionViewModel(template)).ToList();
    public IReadOnlyList<RuleItem> Rules => _rules;
    public IReadOnlyList<RuleItem> FilteredRules => _filteredRules;
    public IReadOnlyList<RuleRowViewModel> FilteredRuleRows => _filteredRuleRows;
    public RuleTypeBucket TypeBucket => _typeBucket;
    public bool IsAllTypesSelected => _typeBucket == RuleTypeBucket.All;
    public bool IsDomainRulesSelected => _typeBucket == RuleTypeBucket.Domain;
    public bool IsIpRulesSelected => _typeBucket == RuleTypeBucket.Ip;
    public bool IsRuleSetRulesSelected => _typeBucket == RuleTypeBucket.RuleSet;
    public bool IsOtherRulesSelected => _typeBucket == RuleTypeBucket.Other;
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (SetProperty(ref _searchKeyword, value))
            {
                RebuildFilteredRows();
                RebuildVisibleRules();
            }
        }
    }
    public bool HasSubscription => _snapshot.HasSubscription;
    public bool IsCoreRunning => _isCoreRunning;
    public bool HasRequestedRefresh => _hasRequestedRefresh;
    public bool IsEmptyVisible => _overrideService is null
        ? !_isCoreRunning || _filteredRules.Count == 0
        : !HasSubscription;
    public bool IsCustomRulesEmpty => CustomRules.Count == 0;
    public bool HasCustomRules => CustomRules.Count > 0;
    public string CurrentSectionHint => Localize("Rules.Section.MixedHint");
    public bool IsTemplateSelectMode => _isTemplateSelectMode;
    public bool IsTemplateCreateMode => !_isTemplateSelectMode;
    public string TemplateDialogTitle => Localize(IsTemplateSelectMode ? "Rules.Dialog.Template.SelectTitle" : "Rules.Dialog.Template.CreateTitle");
    public bool IsVisibleRulesEmpty => VisibleRules.Count == 0;
    public bool IsNoMatchesVisible => HasSubscription && IsVisibleRulesEmpty;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool HasSelectedTemplate => SelectedTemplate is not null;
    public bool CanSaveTemplate => HasCustomRules && !string.IsNullOrWhiteSpace(TemplateName);
    public string EmptyText => _overrideService is null
        ? !_isCoreRunning
            ? Localize("Rules.Empty.CoreStopped")
            : _rules.Count == 0
                ? Localize("Rules.Empty.NoRules")
                : Localize("Rules.Empty.NoMatches")
        : Localize("Rules.Empty.NoSubscription");
    public string MonitorStateText => _isCoreRunning ? Localize("Rules.State.Monitoring") : Localize("Rules.State.CoreStopped");
    public string MonitorSignalTag => _isCoreRunning ? "ok" : "warning";

    public bool IsEditorDialogVisible { get => _isEditorDialogVisible; private set { if (SetProperty(ref _isEditorDialogVisible, value)) OnPropertyChanged(nameof(IsDialogOverlayVisible)); } }
    public bool IsTemplateDialogVisible { get => _isTemplateDialogVisible; private set { if (SetProperty(ref _isTemplateDialogVisible, value)) OnPropertyChanged(nameof(IsDialogOverlayVisible)); } }
    public bool IsDeleteDialogVisible => DeleteCandidate is not null;
    public bool IsDialogOverlayVisible => IsEditorDialogVisible || IsTemplateDialogVisible || IsDeleteDialogVisible;
    public bool IsEditingExisting => _editingRule is not null;
    public string EditorTitle => IsEditingExisting ? Localize("Rules.Dialog.Edit.Title") : Localize("Rules.Dialog.Add.Title");
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public string Payload { get => _payload; set => SetProperty(ref _payload, value); }
    public string Proxy { get => _proxy; set => SetProperty(ref _proxy, value); }
    public string Options { get => _options; set => SetProperty(ref _options, value); }
    public string TemplateName { get => _templateName; set { if (SetProperty(ref _templateName, value)) { OnPropertyChanged(nameof(CanSaveTemplate)); ((RelayCommand)SaveTemplateCommand).RaiseCanExecuteChanged(); } } }
    public RuleEditorRowViewModel? DeleteCandidate
    {
        get => _deleteCandidate;
        private set
        {
            if (SetProperty(ref _deleteCandidate, value))
            {
                OnPropertyChanged(nameof(IsDeleteDialogVisible));
                OnPropertyChanged(nameof(IsDialogOverlayVisible));
            }
        }
    }
    public string ErrorText { get => _errorText; private set { if (SetProperty(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); } }
    public RuleTemplateOptionViewModel? SelectedTemplate { get => _selectedTemplate; set { if (SetProperty(ref _selectedTemplate, value)) { OnPropertyChanged(nameof(HasSelectedTemplate)); ((RelayCommand)ApplyTemplateCommand).RaiseCanExecuteChanged(); } } }

    public ICommand RefreshRulesCommand { get; }
    public ICommand ShowAllTypesCommand { get; }
    public ICommand ShowDomainRulesCommand { get; }
    public ICommand ShowIpRulesCommand { get; }
    public ICommand ShowRuleSetRulesCommand { get; }
    public ICommand ShowOtherRulesCommand { get; }
    public ICommand AddRuleCommand { get; }
    public ICommand EditRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand MoveRuleCommand { get; }
    public ICommand SaveRuleCommand { get; }
    public ICommand SaveChangesCommand { get; }
    public ICommand CancelEditorCommand { get; }
    public ICommand OpenTemplateCommand { get; }
    public ICommand OpenCreateTemplateCommand { get; }
    public ICommand SaveTemplateCommand { get; }
    public ICommand ApplyTemplateCommand { get; }
    public ICommand DeleteSingleTemplateCommand { get; }
    public ICommand CancelTemplateCommand { get; }
    public ICommand ConfirmDeleteRuleCommand { get; }
    public ICommand DeleteEditingRuleCommand { get; }
    public ICommand CancelDeleteRuleCommand { get; }

    private void OpenTemplateSelector()
    {
        ErrorText = string.Empty;
        _isTemplateSelectMode = true;
        SelectedTemplate = null;
        OnPropertyChanged(nameof(IsTemplateSelectMode));
        OnPropertyChanged(nameof(IsTemplateCreateMode));
        OnPropertyChanged(nameof(TemplateDialogTitle));
        IsTemplateDialogVisible = true;
    }

    private void DeleteSingleTemplate(RuleTemplateOptionViewModel? template)
    {
        if (template is null || _overrideService is null) return;
        _overrideService.DeleteTemplate(template.Id);
        if (ReferenceEquals(SelectedTemplate, template) || SelectedTemplate?.Id == template.Id)
        {
            SelectedTemplate = null;
        }
        LoadEditorSnapshot();
    }

    private void OpenTemplateCreator()
    {
        ErrorText = string.Empty;
        _isTemplateSelectMode = false;
        OnPropertyChanged(nameof(IsTemplateSelectMode));
        OnPropertyChanged(nameof(IsTemplateCreateMode));
        OnPropertyChanged(nameof(TemplateDialogTitle));
        IsTemplateDialogVisible = true;
    }

    public void LoadEditorSnapshot()
    {
        if (_overrideService is null)
        {
            return;
        }

        _snapshot = _overrideService.LoadCurrent();
        BuiltinRules.Clear();
        CustomRules.Clear();
        foreach (var item in _snapshot.Items.Where(item => item.IsBuiltIn))
        {
            var row = new RuleEditorRowViewModel(item);
            row.StateChanged += OnRuleStateChanged;
            BuiltinRules.Add(row);
        }
        foreach (var item in _snapshot.Items.Where(item => !item.IsBuiltIn))
        {
            var row = new RuleEditorRowViewModel(item);
            row.StateChanged += OnRuleStateChanged;
            CustomRules.Add(row);
        }

        _rules = _snapshot.Items.Select(item => new RuleItem(item.Type, item.Payload, item.Proxy, item.Options, item.Source, item.RuleCount)).ToList();
        RebuildFilteredRows();

        OnPropertyChanged(nameof(Templates));
        OnPropertyChanged(nameof(HasSubscription));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(IsCustomRulesEmpty));
        OnPropertyChanged(nameof(HasCustomRules));
        OnPropertyChanged(nameof(IsNoMatchesVisible));
        OnPropertyChanged(nameof(CanSaveTemplate));
        ((RelayCommand)SaveTemplateCommand).RaiseCanExecuteChanged();
        RebuildVisibleRules();
    }

    public void ApplyCoreRunning(bool isRunning)
    {
        if (SetProperty(ref _isCoreRunning, isRunning))
        {
            if (!isRunning && _overrideService is null)
            {
                _searchKeyword = string.Empty;
                _typeBucket = RuleTypeBucket.All;
                RebuildFilteredRows();
                OnPropertyChanged(nameof(SearchKeyword));
                OnPropertyChanged(nameof(TypeBucket));
            }
            OnPropertyChanged(nameof(IsEmptyVisible));
            OnPropertyChanged(nameof(EmptyText));
            OnPropertyChanged(nameof(IsNoMatchesVisible));
            OnPropertyChanged(nameof(MonitorStateText));
            OnPropertyChanged(nameof(MonitorSignalTag));
        }
    }

    private void RequestRefresh()
    {
        _hasRequestedRefresh = true;
        if (_overrideService is not null)
        {
            LoadEditorSnapshot();
        }
        else if (_loader is not null)
        {
            LoadRules(_loader.LoadRules());
        }
        OnPropertyChanged(nameof(HasRequestedRefresh));
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenEditor(RuleEditorRowViewModel? row)
    {
        _editingRule = row;
        Type = row?.Type ?? "DOMAIN-SUFFIX";
        Payload = row?.Item.Payload ?? string.Empty;
        Proxy = row?.Proxy ?? "DIRECT";
        Options = row?.Options ?? string.Empty;
        ErrorText = string.Empty;
        IsEditorDialogVisible = true;
        OnPropertyChanged(nameof(IsEditingExisting));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(IsDialogOverlayVisible));
    }

    private void CloseEditor()
    {
        IsEditorDialogVisible = false;
        _editingRule = null;
        OnPropertyChanged(nameof(IsEditingExisting));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(IsDialogOverlayVisible));
    }

    private void SaveRule()
    {
        if (_overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId))
        {
            return;
        }

        var id = _editingRule?.Id ?? $"local-{Guid.NewGuid():N}";
        var rule = new EditableRule(id, Type, Payload, Proxy, Options);
        var custom = CustomRules.Select(row => row.ToEditableRule()).ToList();
        if (_editingRule is not null)
        {
            var index = custom.FindIndex(item => item.Id == _editingRule.Id);
            custom[index] = rule;
        }
        else
        {
            custom.Add(rule);
        }

        try
        {
            SaveCurrentRules(custom);
            CloseEditor();
            LoadEditorSnapshot();
            RuntimeRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (RuleOverrideException exception)
        {
            ErrorText = LocalizeRuleError(exception.Error);
        }
    }

    private void SaveChanges()
    {
        if (_overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId)) return;
        try
        {
            SaveCurrentRules(CustomRules.Select(row => row.ToEditableRule()).ToList());
            RuntimeRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (RuleOverrideException exception) { ErrorText = LocalizeRuleError(exception.Error); }
    }

    private void SaveCurrentRules(IReadOnlyList<EditableRule> customRules, IReadOnlyList<string>? ruleOrder = null)
    {
        if (_overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId)) return;
        _overrideService.Save(
            _snapshot.SubscriptionId,
            customRules,
            BuiltinRules.Where(row => !row.IsEnabled).Select(row => row.Item.Key).ToHashSet(StringComparer.Ordinal),
            ruleOrder ?? VisibleRules.Select(row => row.OrderId).ToList());
    }

    private void DeleteRule(RuleEditorRowViewModel? row)
    {
        if (row is null || _overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId)) return;
        var custom = CustomRules.Where(item => item.OrderId != row.OrderId).Select(item => item.ToEditableRule()).ToList();
        SaveCurrentRules(custom, VisibleRules.Where(item => item.OrderId != row.OrderId).Select(item => item.OrderId).ToList());
        LoadEditorSnapshot();
        RuntimeRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ShowDeleteRuleDialog(RuleEditorRowViewModel? row)
    {
        if (row is not null)
        {
            DeleteCandidate = row;
        }
    }

    private void ConfirmDeleteRule()
    {
        var row = DeleteCandidate;
        DeleteCandidate = null;
        DeleteRule(row);
    }

    private void DeleteEditingRule()
    {
        var row = _editingRule;
        CloseEditor();
        ShowDeleteRuleDialog(row);
    }

    public void EditRule(RuleEditorRowViewModel row) => OpenEditor(row);

    private void MoveRuleToIndex(RuleMoveRequest? request)
    {
        if (request is null || !string.IsNullOrWhiteSpace(SearchKeyword) || _typeBucket != RuleTypeBucket.All) return;
        var source = VisibleRules.FirstOrDefault(item => item.OrderId == request.RuleId);
        if (source is null) return;
        var sourceIndex = VisibleRules.IndexOf(source);
        var targetIndex = Math.Clamp(request.TargetIndex, 0, VisibleRules.Count - 1);
        if (sourceIndex < 0 || targetIndex == sourceIndex) return;

        VisibleRules.RemoveAt(sourceIndex);
        VisibleRules.Insert(targetIndex, source);
        ReindexVisibleRules();
        try
        {
            SaveCurrentRules(CustomRules.Select(row => row.ToEditableRule()).ToList());
            RuntimeRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (RuleOverrideException exception) { ErrorText = LocalizeRuleError(exception.Error); }
    }

    private void SaveTemplate()
    {
        if (_overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId) || string.IsNullOrWhiteSpace(TemplateName)) return;
        var existing = _snapshot.Templates.FirstOrDefault(template => string.Equals(template.Name, TemplateName.Trim(), StringComparison.OrdinalIgnoreCase));
        var template = new RuleTemplate(existing?.Id ?? $"template-{Guid.NewGuid():N}", TemplateName.Trim(), CustomRules.Select(row => row.ToEditableRule()).ToList());
        _overrideService.UpsertTemplate(template);
        TemplateName = string.Empty;
        IsTemplateDialogVisible = false;
        LoadEditorSnapshot();
    }

    private void ApplyTemplate()
    {
        if (SelectedTemplate is null || _overrideService is null || string.IsNullOrWhiteSpace(_snapshot.SubscriptionId)) return;
        var custom = CustomRules.Select(row => row.ToEditableRule()).Concat(SelectedTemplate.Template.Rules).GroupBy(rule => rule.Key, StringComparer.Ordinal).Select(group => group.First()).ToList();
        try
        {
            SaveCurrentRules(custom);
            IsTemplateDialogVisible = false;
            SelectedTemplate = null;
            LoadEditorSnapshot();
            RuntimeRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (RuleOverrideException exception) { ErrorText = LocalizeRuleError(exception.Error); }
    }

    private string Localize(string key) => _localization?.GetString(key) ?? key;
    private string LocalizeRuleError(RuleOverrideError error) => Localize(error switch
    {
        RuleOverrideError.DuplicateCustomRule => "Rules.Error.DuplicateCustom",
        RuleOverrideError.DuplicateBuiltinRule => "Rules.Error.DuplicateBuiltin",
        RuleOverrideError.SubscriptionNotFound => "Rules.Error.SubscriptionNotFound",
        _ => "Rules.Error.InvalidRule",
    });
    private void OnRuleStateChanged(object? sender, EventArgs args) => SaveChanges();

    public void SetTypeBucket(RuleTypeBucket bucket)
    {
        if (_typeBucket == bucket) return;
        _typeBucket = bucket;
        OnPropertyChanged(nameof(TypeBucket));
        OnPropertyChanged(nameof(IsAllTypesSelected));
        OnPropertyChanged(nameof(IsDomainRulesSelected));
        OnPropertyChanged(nameof(IsIpRulesSelected));
        OnPropertyChanged(nameof(IsRuleSetRulesSelected));
        OnPropertyChanged(nameof(IsOtherRulesSelected));
        RebuildFilteredRows();
        RebuildVisibleRules();
    }

    public void LoadRules(IReadOnlyList<RuleItem> rules)
    {
        _rules = rules;
        RebuildFilteredRows();
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(IsNoMatchesVisible));
    }

    private void RebuildFilteredRows()
    {
        _filteredRules = _search.Filter(_rules, _searchKeyword)
            .Where(rule => RuleTypeClassifier.MatchesBucket(rule.Type, _typeBucket))
            .ToList();
        _filteredRuleRows = _filteredRules.Select((rule, index) => new RuleRowViewModel(index + 1, rule, _localization)).ToList();
        OnPropertyChanged(nameof(Rules));
        OnPropertyChanged(nameof(FilteredRules));
        OnPropertyChanged(nameof(FilteredRuleRows));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(IsNoMatchesVisible));
    }

    private void RebuildVisibleRules()
    {
        VisibleRules.Clear();
        var order = _snapshot.Items.Select(item => item.OrderId).ToList();
        var source = BuiltinRules.Concat(CustomRules)
            .OrderBy(row => OrderIndex(order, row.OrderId))
            .ToList();
        ReindexRows(source);

        var keyword = _searchKeyword.Trim();
        foreach (var row in source)
        {
            if (!RuleTypeClassifier.MatchesBucket(row.Type, _typeBucket))
            {
                continue;
            }

            if (keyword.Length > 0
                && !string.Join(' ', row.Type, row.Payload, row.Proxy, row.Options)
                    .Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            VisibleRules.Add(row);
        }

        OnPropertyChanged(nameof(CurrentSectionHint));
        OnPropertyChanged(nameof(IsVisibleRulesEmpty));
        OnPropertyChanged(nameof(IsNoMatchesVisible));
    }

    private static int OrderIndex(IReadOnlyList<string> order, string orderId)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (string.Equals(order[index], orderId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static void ReindexRows(IReadOnlyList<RuleEditorRowViewModel> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            rows[index].SequenceNumber = index + 1;
        }
    }

    private void ReindexVisibleRules() => ReindexRows(VisibleRules.ToList());

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(MonitorStateText));
        OnPropertyChanged(nameof(EditorTitle));
    }

    public void Dispose() => _localization?.LanguageChanged -= OnLanguageChanged;
}
