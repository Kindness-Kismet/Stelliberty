namespace Stelliberty.Application.Updates;

public sealed class UpdateOperationState
{
    private readonly List<string> _updatedItemIds = [];
    private readonly List<string> _updatingItemIds = [];
    private readonly List<string> _batchUpdatingItemIds = [];
    private readonly List<string> _skippedItemIds = [];

    public IReadOnlyList<string> UpdatedItemIds => _updatedItemIds;

    public IReadOnlyList<string> UpdatingItemIds => _updatingItemIds;

    public IReadOnlyList<string> SkippedItemIds => _skippedItemIds;

    public bool HasUpdatedAllItems { get; private set; }

    public bool IsBatchUpdating { get; private set; }

    public void StartBatchUpdate(IReadOnlyList<UpdateOperationItem> items)
    {
        HasUpdatedAllItems = true;
        IsBatchUpdating = true;
        _batchUpdatingItemIds.Clear();
        foreach (var item in items)
        {
            if (!item.CanUpdate)
            {
                MarkItemSkipped(item.Id);
                continue;
            }

            if (_updatingItemIds.Contains(item.Id))
            {
                continue;
            }

            _updatingItemIds.Add(item.Id);
            _batchUpdatingItemIds.Add(item.Id);
            _skippedItemIds.Remove(item.Id);
        }
    }

    public UpdateStartResult TryStartBatchUpdate(IReadOnlyList<UpdateOperationItem> items)
    {
        if (IsBatchUpdating || !items.Any(item => item.CanUpdate && !_updatingItemIds.Contains(item.Id)))
        {
            return UpdateStartResult.Skipped;
        }

        StartBatchUpdate(items);
        return UpdateStartResult.Started;
    }

    public void CompleteBatchUpdate()
    {
        foreach (var itemId in _batchUpdatingItemIds)
        {
            _updatingItemIds.Remove(itemId);
        }

        _batchUpdatingItemIds.Clear();
        IsBatchUpdating = false;
    }

    public UpdateStartResult TryStartItemUpdate(UpdateOperationItem item)
    {
        if ((IsBatchUpdating || _updatingItemIds.Contains(item.Id)) && item.CanUpdate)
        {
            return UpdateStartResult.Skipped;
        }

        if (!item.CanUpdate)
        {
            MarkItemSkipped(item.Id);
            return UpdateStartResult.Skipped;
        }

        _updatingItemIds.Add(item.Id);
        _skippedItemIds.Remove(item.Id);
        return UpdateStartResult.Started;
    }

    public void CompleteItemUpdate(string itemId, bool isUpdated = false)
    {
        _updatingItemIds.Remove(itemId);
        _batchUpdatingItemIds.Remove(itemId);
        if (isUpdated && !_updatedItemIds.Contains(itemId))
        {
            _updatedItemIds.Add(itemId);
            _skippedItemIds.Remove(itemId);
        }
    }

    public void MarkItemSkipped(string itemId)
    {
        if (!_skippedItemIds.Contains(itemId))
        {
            _skippedItemIds.Add(itemId);
        }
    }

    public bool IsUpdating(string itemId)
    {
        return _updatingItemIds.Contains(itemId);
    }

    public bool IsBatchUpdatingItem(string itemId)
    {
        return _batchUpdatingItemIds.Contains(itemId);
    }
}
