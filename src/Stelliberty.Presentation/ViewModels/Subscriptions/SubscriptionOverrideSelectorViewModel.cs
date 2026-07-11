using System.Windows.Input;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionOverrideSelectionSaveRequestedEventArgs(
    string SubscriptionId,
    IReadOnlyList<string> SelectedOverrideIds,
    IReadOnlyList<string> OverrideSortPreference);

public sealed class SubscriptionOverrideSelectorViewModel : ViewModelBase
{
    private readonly DialogCloseResetScheduler _closeReset = new();
    private readonly List<SubscriptionOverrideOptionViewModel> _availableOverrides = [];
    private readonly List<string> _selectedOverrideIds = [];
    private readonly List<string> _sortPreference = [];
    private readonly List<string> _savedSelectionIds = [];
    private readonly List<string> _savedSortPreference = [];
    private string? _subscriptionId;
    private bool _isDialogVisible;

    public SubscriptionOverrideSelectorViewModel()
    {
        ToggleSelectionCommand = new RelayCommand<string>(ToggleSelection);
        MoveCommand = new RelayCommand<SubscriptionOverrideMoveRequest>(Move);
        MoveUpCommand = new RelayCommand<string>(MoveUp);
        MoveDownCommand = new RelayCommand<string>(MoveDown);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Close);
    }

    public event EventHandler<SubscriptionOverrideSelectionSaveRequestedEventArgs>? SaveRequested;

    public event EventHandler? DialogStateChanged;

    public string? DialogSubscriptionId => _subscriptionId;

    public bool IsDialogVisible => _isDialogVisible;

    public IReadOnlyList<SubscriptionOverrideOptionViewModel> AvailableOverrides => _availableOverrides
        .OrderBy(item => _sortPreference.IndexOf(item.Id) < 0 ? int.MaxValue : _sortPreference.IndexOf(item.Id))
        .ThenBy(item => item.Name, StringComparer.Ordinal)
        .Select(item => item with { IsSelected = _selectedOverrideIds.Contains(item.Id) })
        .ToList();

    public IReadOnlyList<string> SelectedOverrideIds => _selectedOverrideIds;

    public IReadOnlyList<string> OverrideSortPreference => _sortPreference;

    public IReadOnlyList<string> SavedOverrideSelectionIds => _savedSelectionIds;

    public IReadOnlyList<string> SavedOverrideSortPreference => _savedSortPreference;

    public ICommand ToggleSelectionCommand { get; }

    public ICommand MoveCommand { get; }

    public ICommand MoveUpCommand { get; }

    public ICommand MoveDownCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    public void Open(string subscriptionId)
    {
        _closeReset.Cancel();
        _subscriptionId = subscriptionId;
        _isDialogVisible = true;
        RaiseDialogStateChanged();
    }

    public void Close()
    {
        BeginClose(restoreSavedSelection: true);
    }

    private void ResetAfterClose(bool restoreSavedSelection)
    {
        _isDialogVisible = false;
        if (restoreSavedSelection)
        {
            RestoreSavedSelection();
            RaiseSelectionStateChanged();
        }

        _subscriptionId = null;
        RaiseDialogStateChanged();
    }

    public void ClearForSubscription(string subscriptionId)
    {
        if (_subscriptionId == subscriptionId)
        {
            BeginClose(restoreSavedSelection: true);
        }
    }

    public void LoadAvailable(IReadOnlyList<SubscriptionOverrideOptionViewModel> overrides)
    {
        _availableOverrides.Clear();
        _availableOverrides.AddRange(overrides);
        foreach (var overrideId in overrides.Select(item => item.Id).Where(overrideId => !_sortPreference.Contains(overrideId, StringComparer.Ordinal)))
        {
            _sortPreference.Add(overrideId);
        }

        _sortPreference.RemoveAll(overrideId => overrides.All(item => item.Id != overrideId));
        RaiseSelectionStateChanged();
    }

    public void ApplySaved(Subscription subscription)
    {
        _selectedOverrideIds.Clear();
        _selectedOverrideIds.AddRange(subscription.OverrideIds);
        _sortPreference.Clear();
        _sortPreference.AddRange(subscription.OverrideSortPreference);
        _savedSelectionIds.Clear();
        _savedSelectionIds.AddRange(subscription.OverrideIds);
        _savedSortPreference.Clear();
        _savedSortPreference.AddRange(subscription.OverrideSortPreference);
        RaiseSelectionStateChanged();
    }

    private void ToggleSelection(string? overrideId)
    {
        if (string.IsNullOrWhiteSpace(overrideId) || _availableOverrides.All(item => item.Id != overrideId))
        {
            return;
        }

        if (!_selectedOverrideIds.Remove(overrideId))
        {
            _selectedOverrideIds.Add(overrideId);
        }

        RaiseSelectionStateChanged();
    }

    private void Move(SubscriptionOverrideMoveRequest? request)
    {
        if (request is null)
        {
            return;
        }

        MoveTo(request.OverrideId, request.TargetIndex);
    }

    private void MoveUp(string? overrideId)
    {
        var index = _sortPreference.IndexOf(overrideId ?? string.Empty);
        if (index <= 0)
        {
            return;
        }

        MoveTo(overrideId, index - 1);
    }

    private void MoveDown(string? overrideId)
    {
        var index = _sortPreference.IndexOf(overrideId ?? string.Empty);
        if (index < 0 || index >= _sortPreference.Count - 1)
        {
            return;
        }

        MoveTo(overrideId, index + 1);
    }

    private void MoveTo(string? overrideId, int targetIndex)
    {
        var index = _sortPreference.IndexOf(overrideId ?? string.Empty);
        if (index < 0)
        {
            return;
        }

        var selectedOverrideIds = _selectedOverrideIds.ToHashSet(StringComparer.Ordinal);
        var selectedOverrideId = _sortPreference[index];
        _sortPreference.RemoveAt(index);
        _sortPreference.Insert(Math.Clamp(targetIndex, 0, _sortPreference.Count), selectedOverrideId);
        _selectedOverrideIds.Clear();
        _selectedOverrideIds.AddRange(_sortPreference.Where(selectedOverrideIds.Contains));
        RaiseSelectionStateChanged();
    }

    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(_subscriptionId))
        {
            SaveRequested?.Invoke(this, new SubscriptionOverrideSelectionSaveRequestedEventArgs(
                _subscriptionId,
                _sortPreference.Where(_selectedOverrideIds.Contains).ToList(),
                _sortPreference.ToList()));
            return;
        }

        var selectedOverrideIds = _selectedOverrideIds.ToHashSet(StringComparer.Ordinal);
        _selectedOverrideIds.Clear();
        _selectedOverrideIds.AddRange(_sortPreference.Where(selectedOverrideIds.Contains));
        _savedSelectionIds.Clear();
        _savedSelectionIds.AddRange(_selectedOverrideIds);
        _savedSortPreference.Clear();
        _savedSortPreference.AddRange(_sortPreference);
        RaiseSelectionStateChanged();
    }

    private void BeginClose(bool restoreSavedSelection)
    {
        if (!_isDialogVisible)
        {
            return;
        }

        _isDialogVisible = false;
        RaiseDialogStateChanged();
        _closeReset.Run(() => !_isDialogVisible, () => ResetAfterClose(restoreSavedSelection));
    }

    private void RaiseDialogStateChanged()
    {
        OnPropertyChanged(nameof(DialogSubscriptionId));
        OnPropertyChanged(nameof(IsDialogVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseSelectionStateChanged()
    {
        OnPropertyChanged(nameof(AvailableOverrides));
        OnPropertyChanged(nameof(SelectedOverrideIds));
        OnPropertyChanged(nameof(OverrideSortPreference));
        OnPropertyChanged(nameof(SavedOverrideSelectionIds));
        OnPropertyChanged(nameof(SavedOverrideSortPreference));
    }

    private void RestoreSavedSelection()
    {
        _selectedOverrideIds.Clear();
        _selectedOverrideIds.AddRange(_savedSelectionIds);
        _sortPreference.Clear();
        _sortPreference.AddRange(_savedSortPreference);
    }
}
