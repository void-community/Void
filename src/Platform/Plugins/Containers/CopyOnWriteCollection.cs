using System.Collections.Immutable;

namespace Void.Proxy.Plugins.Containers;

/// <summary>
/// Keeps items in an immutable snapshot that every change replaces atomically. Plugins load and unload rarely while dependency
/// resolution reads the plugin containers often and concurrently, so readers enumerate a snapshot that can never change under them
/// instead of taking a lock.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class CopyOnWriteCollection<T>
{
    private ImmutableArray<T> _items = [];

    /// <summary>
    /// Gets the items as they are now. Later changes never affect a snapshot that was already taken.
    /// </summary>
    public ImmutableArray<T> Snapshot => _items;

    public void Add(T item)
    {
        ImmutableInterlocked.Update(ref _items, static (items, item) => items.Add(item), item);
    }

    public void RemoveAll(Predicate<T> match)
    {
        ImmutableInterlocked.Update(ref _items, static (items, match) => items.RemoveAll(match), match);
    }
}
