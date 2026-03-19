using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace LocalWealthTracker.Helpers;

/// <summary>
/// ObservableCollection that supports adding multiple items
/// with a single CollectionChanged notification.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    /// <summary>
    /// Replaces all items with a single UI notification.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        _suppressNotification = true;

        Clear();
        foreach (var item in items)
            Items.Add(item);

        _suppressNotification = false;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
            base.OnCollectionChanged(e);
    }
}