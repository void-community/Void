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

    /// <summary>
    /// Adds an item unless the collection holds one that conflicts with it, checked and added as one change.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <param name="conflicts">Whether an item of the collection, the first argument, conflicts with the added one.</param>
    /// <returns><see langword="true" /> when the item was added; <see langword="false" /> when a conflicting item kept it out.</returns>
    public bool TryAdd(T item, Func<T, T, bool> conflicts)
    {
        var added = false;

        ImmutableInterlocked.Update(ref _items, (items, item) =>
        {
            added = !items.Any(existing => conflicts(existing, item));
            return added ? items.Add(item) : items;
        }, item);

        return added;
    }

    /// <summary>
    /// Removes the items that match, as one change.
    /// </summary>
    /// <returns>How many items this change removed, which concurrent removals of the same item never count twice.</returns>
    public int RemoveAll(Predicate<T> match)
    {
        var removed = 0;

        ImmutableInterlocked.Update(ref _items, (items, match) =>
        {
            var remaining = items.RemoveAll(match);
            removed = items.Length - remaining.Length;
            return remaining;
        }, match);

        return removed;
    }
}
