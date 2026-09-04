using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SharpCommander.Desktop.Utilities;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can be replaced wholesale with a single Reset notification
/// and, more importantly, synchronized in place with a target list: items are matched by a key and only the
/// rows that actually changed are inserted, moved, replaced or removed. Bound list controls keep their
/// containers, selection and scroll position across a synchronization.
/// </summary>
public sealed class SyncedObservableCollection<T> : ObservableCollection<T> where T : class
{
    /// <summary>Replaces every item, raising one Reset notification instead of one event per item.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Makes this collection equal to <paramref name="target"/> (same items, same order) with the minimum of
    /// notifications. Items are identified by <paramref name="keySelector"/>; an item whose key is kept is
    /// replaced in place only when it is no longer equal (value equality) to the target item, so unchanged rows
    /// keep their instance and raise no notification at all.
    /// </summary>
    public void SyncTo(IReadOnlyList<T> target, Func<T, string> keySelector, IEqualityComparer<string> keyComparer)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(keyComparer);

        var wantedKeys = new HashSet<string>(keyComparer);
        foreach (var item in target)
        {
            wantedKeys.Add(keySelector(item));
        }

        for (var index = Count - 1; index >= 0; index--)
        {
            if (!wantedKeys.Contains(keySelector(this[index])))
            {
                RemoveAt(index);
            }
        }

        for (var index = 0; index < target.Count; index++)
        {
            var wanted = target[index];
            var wantedKey = keySelector(wanted);

            if (index < Count)
            {
                if (keyComparer.Equals(keySelector(this[index]), wantedKey))
                {
                    ReplaceIfChanged(index, wanted);
                    continue;
                }

                var current = IndexOfKey(wantedKey, index + 1, keySelector, keyComparer);
                if (current >= 0)
                {
                    Move(current, index);
                    ReplaceIfChanged(index, wanted);
                    continue;
                }
            }

            Insert(index, wanted);
        }

        while (Count > target.Count)
        {
            RemoveAt(Count - 1);
        }
    }

    private void ReplaceIfChanged(int index, T wanted)
    {
        if (!ReferenceEquals(this[index], wanted) && !EqualityComparer<T>.Default.Equals(this[index], wanted))
        {
            this[index] = wanted;
        }
    }

    private int IndexOfKey(string key, int start, Func<T, string> keySelector, IEqualityComparer<string> keyComparer)
    {
        for (var index = start; index < Count; index++)
        {
            if (keyComparer.Equals(keySelector(this[index]), key))
            {
                return index;
            }
        }

        return -1;
    }
}
