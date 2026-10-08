using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Prompuff.App.ViewModels;

/// <summary>
/// An observable list that can swap its whole contents with one notification. Adding 10,000 items one by one raises
/// 10,000 events, and every list bound to it would react to each.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        var list = (List<T>)Items;
        list.Clear();
        list.AddRange(items);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
