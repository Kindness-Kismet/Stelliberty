using System.Collections;
using System.Collections.Specialized;

namespace Stelliberty.Presentation.Collections;

// 仅在索引访问时创建行，供虚拟化面板按当前视口实体化数据模型。
public sealed class LazyRowList<TSource, TRow> : IList, IReadOnlyList<TRow>, INotifyCollectionChanged
    where TRow : class
{
    private readonly Func<TSource, int, TRow> _rowFactory;
    private IReadOnlyList<TSource> _source = [];
    private TRow?[] _rows = [];

    public LazyRowList(Func<TSource, int, TRow> rowFactory)
    {
        _rowFactory = rowFactory;
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int Count => _source.Count;

    public int RealizedCount => _rows.Count(row => row is not null);

    public TRow this[int index] => _rows[index] ??= _rowFactory(_source[index], index);

    public void Replace(IReadOnlyList<TSource> source)
    {
        _source = source;
        _rows = source.Count == 0 ? [] : new TRow?[source.Count];
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void UpdateInPlace(IReadOnlyList<TSource> source, Action<TRow, TSource, int> update)
    {
        _source = source;
        for (var index = 0; index < _rows.Length; index++)
        {
            if (_rows[index] is { } row)
            {
                update(row, source[index], index);
            }
        }
    }

    public void Release() => Replace([]);

    public TRow? GetRealizedRow(int index)
        => index >= 0 && index < _rows.Length ? _rows[index] : null;

    public IEnumerator<TRow> GetEnumerator()
    {
        for (var index = 0; index < _source.Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool IList.IsFixedSize => false;
    bool IList.IsReadOnly => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
    bool IList.Contains(object? value) => ((IList)this).IndexOf(value) >= 0;

    int IList.IndexOf(object? value)
    {
        if (value is not TRow)
        {
            return -1;
        }

        for (var index = 0; index < _rows.Length; index++)
        {
            if (ReferenceEquals(_rows[index], value))
            {
                return index;
            }
        }

        return -1;
    }

    void ICollection.CopyTo(Array array, int index)
    {
        for (var sourceIndex = 0; sourceIndex < _source.Count; sourceIndex++)
        {
            array.SetValue(this[sourceIndex], index + sourceIndex);
        }
    }
}
