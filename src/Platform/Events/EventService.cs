using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using DryIoc;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Plugins.Dependencies;

namespace Void.Proxy.Events;

public partial class EventService(ILogger<EventService> logger, IContainer container) : IEventService
{
    private readonly ConcurrentDictionary<Func<IEvent, bool>, TaskCompletionSource> _waiters = [];

    // Events read the entries on every thread without waiting, so registering and unregistering publish a new array rather than change it
    private ImmutableArray<Entry> _entries = [];

    public IEnumerable<IEventListener> Listeners => [.. _entries.Select(entry => entry.Listener)];

    public async ValueTask ThrowAsync<T>(CancellationToken cancellationToken = default) where T : IEvent, new()
    {
        await ThrowAsync(new T(), cancellationToken);
    }

    public async ValueTask<TResult?> ThrowWithResultAsync<TResult>(IEventWithResult<TResult> @event, CancellationToken cancellationToken = default)
    {
        await ThrowAsync(@event, cancellationToken);
        return @event.Result;
    }

    public async ValueTask<TResult?> ThrowWithResultAsync<T, TResult>(CancellationToken cancellationToken = default) where T : IEventWithResult<TResult?>, new()
    {
        var @event = new T();
        await ThrowAsync(@event, cancellationToken);
        return @event.Result;
    }

    public async ValueTask ThrowAsync<T>(T @event, CancellationToken cancellationToken = default) where T : IEvent
    {
        var eventType = @event.GetType();

        var invoked = new HashSet<InvokedListenerMethod>(InvokedListenerMethodComparer.Instance);
        var firstParameterTypeCache = new Dictionary<MethodInfo, Type>(ReferenceEqualityComparer.Instance);

        // Listeners are often registered while the event is invoked, such as by the listeners it invokes, so the entries are read again until
        // they have no more candidates
        while (FindInvocationCandidates(_entries) is { Count: > 0 } invocationCandidates)
        {
            invocationCandidates.Sort(static (left, right) =>
            {
                var orderComparison = left.Order.CompareTo(right.Order);

                if (orderComparison is not 0)
                    return orderComparison;

                return left.Sequence.CompareTo(right.Sequence);
            });

            var toInvoke = new WeakEntry[invocationCandidates.Count];

            for (var candidateIndex = 0; candidateIndex < invocationCandidates.Count; candidateIndex++)
                toInvoke[candidateIndex] = invocationCandidates[candidateIndex].WeakEntry;

            await ThrowOnceAsync(toInvoke, @event, cancellationToken);
        }

        if (_waiters.IsEmpty)
            return;

        foreach (var (condition, taskCompletionSource) in _waiters.ToArray())
        {
            if (!condition(@event))
                continue;

            if (_waiters.TryRemove(condition, out _))
                taskCompletionSource.SetResult();
        }

        return;

        List<InvocationCandidate> FindInvocationCandidates(ImmutableArray<Entry> entries)
        {
            var candidates = new List<InvocationCandidate>();

            for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                var entry = entries[entryIndex];
                var invokedKey = new InvokedListenerMethod(entry.Listener, entry.Method);

                if (invoked.Contains(invokedKey))
                    continue;

                if (!firstParameterTypeCache.TryGetValue(entry.Method, out var firstParameterType))
                {
                    firstParameterType = entry.Method.GetParameters()[0].ParameterType;
                    firstParameterTypeCache.Add(entry.Method, firstParameterType);
                }

                if (!eventType.IsAssignableTo(firstParameterType))
                    continue;

                invoked.Add(invokedKey);

                candidates.Add(new InvocationCandidate(
                    entry.Order,
                    entryIndex,
                    new WeakEntry(
                        new WeakReference<IEventListener>(entry.Listener),
                        new WeakReference<MethodInfo>(entry.Method),
                        entry.Order,
                        entry.BypassScopedFilter,
                        entry.CancellationToken)));
            }

            return candidates;
        }
    }

    public async ValueTask WaitAsync(Func<IEvent, bool> condition, CancellationToken cancellationToken = default)
    {
        var taskCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[condition] = taskCompletionSource;

        await using var registration = cancellationToken.Register(() =>
        {
            if (!_waiters.Remove(condition, out _))
                return;

            taskCompletionSource.SetCanceled(cancellationToken);
        });

        await taskCompletionSource.Task;
    }

    public void RegisterListeners(IEnumerable<IEventListener> listeners, CancellationToken cancellationToken = default)
    {
        foreach (var listener in listeners)
            RegisterListeners(cancellationToken, listener);
    }

    public void RegisterListeners(params IEventListener[] listeners)
    {
        foreach (var listener in listeners)
            RegisterListeners(CancellationToken.None, listener);
    }

    public void RegisterListeners(CancellationToken cancellationToken = default, params IEventListener[] listeners)
    {
        foreach (var listener in listeners)
        {
            if (IsRegistered(_entries, listener))
                continue;

            LogRegisteringTypeEventListener(listener);

            var listenerEntries = new List<Entry>();
            var type = listener.GetType();
            while (type != null)
            {
                var methods = type
                    .GetMethods(
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Static |
                        BindingFlags.Instance |
                        BindingFlags.DeclaredOnly)
                    .Where(method => Attribute.IsDefined(method, typeof(SubscribeAttribute)));

                foreach (var method in methods)
                {
                    LogRegisteringTypeEventListenerMethodName(listener, method.Name);
                    SubscribeAttribute.SanityChecks(method);

                    var attribute = method.GetCustomAttribute<SubscribeAttribute>()
                                    ?? throw new InvalidOperationException($"Method {method.Name} does not define {nameof(SubscribeAttribute)}.");
                    listenerEntries.Add(new Entry(listener, method, attribute.Order, attribute.BypassScopedFilter, cancellationToken));
                }

                type = type.BaseType;
            }

            // Another thread may have registered the same listener meanwhile, whose entries then stay the only ones
            ImmutableInterlocked.Update(ref _entries, static (entries, registration) => IsRegistered(entries, registration.Listener) ? entries : entries.AddRange(registration.Entries), (Listener: listener, Entries: listenerEntries));
        }
    }

    public void UnregisterListeners(IEnumerable<IEventListener> listeners)
    {
        foreach (var listener in listeners)
            UnregisterListeners(listener);
    }

    public void UnregisterListeners(params IEventListener[] listeners)
    {
        foreach (var listener in listeners)
            LogUnregisteringListenerTypeEventListener(listener);

        ImmutableInterlocked.Update(ref _entries, static (entries, removedListeners) => entries.RemoveAll(entry => Array.Exists(removedListeners, listener => listener == entry.Listener)), listeners);
    }

    private static bool IsRegistered(ImmutableArray<Entry> entries, IEventListener listener)
    {
        return entries.Any(entry => entry.Listener == listener);
    }

    private async ValueTask ThrowOnceAsync<T>(WeakEntry[] entries, T @event, CancellationToken cancellationToken = default) where T : IEvent
    {
        var dependencies = container.Resolve<IDependencyService>();

        var eventType = @event.GetType();
        LogInvokingTypeEvent(eventType.Name);

        foreach (var entry in entries)
        {
            var listener = entry.Listener;
            var method = entry.Method;

            if (listener is null || method is null)
                continue;

            if (!entry.IsCompatible(eventType))
                continue;

            await Task.Yield();

            if (!entry.BypassScopedFilter && @event is IScopedEvent scopedEvent)
            {
                var serviceType = listener.GetType();

                // Listener should be exactly Scoped
                if (dependencies.TryGetServiceReuse(serviceType, out var lifetime) && lifetime is ServiceLifetime.Scoped)
                {
                    if (!dependencies.IsInPlayerScope(scopedEvent.Player, listener))
                        continue;
                }
            }

            var parameters = method.GetParameters();

            try
            {
                using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entry.CancellationToken);
                var value = method.Invoke(listener, parameters.Length == 1 ? [@event] : [@event, cancellationTokenSource.Token]);
                var handle = value switch
                {
                    Task task => new ValueTask(task),
                    ValueTask task => task,
                    _ => ValueTask.CompletedTask
                };

                await handle;
            }
            catch (TargetInvocationException exception)
            {
                logger.LogError(exception.InnerException, "{EventName} cannot be invoked on {ListenerName}", eventType.Name, entry.Listener);
            }
        }

        LogCompletedInvokingTypeEvent(eventType.Name);
    }

    [LoggerMessage(LogLevel.Trace, "Invoking {TypeName} event")]
    partial void LogInvokingTypeEvent(string typeName);

    [LoggerMessage(LogLevel.Trace, "Completed invoking {TypeName} event")]
    partial void LogCompletedInvokingTypeEvent(string typeName);

    [LoggerMessage(LogLevel.Trace, "Registering {Type} event listener")]
    partial void LogRegisteringTypeEventListener(IEventListener type);

    [LoggerMessage(LogLevel.Trace, "Registering {Type} event listener method {Name}")]
    partial void LogRegisteringTypeEventListenerMethodName(IEventListener type, string name);

    [LoggerMessage(LogLevel.Trace, "Unregistering {ListenerType} event listener")]
    partial void LogUnregisteringListenerTypeEventListener(IEventListener listenerType);

    [LoggerMessage(LogLevel.Error, "Failed to resolve {ServiceType} for scoped event {EventType}. Reason:")]
    partial void LogFailedToResolveServiceTypeForScopedEventEventTypeReason(string? serviceType, string? eventType, Exception exception);

    private record WeakEntry(WeakReference<IEventListener> ListenerReference, WeakReference<MethodInfo> MethodReference, PostOrder Order, bool BypassScopedFilter, CancellationToken CancellationToken)
    {
        public IEventListener? Listener => ListenerReference.TryGetTarget(out var listener) ? listener : null;
        public MethodInfo? Method => MethodReference.TryGetTarget(out var method) ? method : null;

        public bool IsCompatible(Type eventType) => MethodReference.TryGetTarget(out var method) && eventType.IsAssignableTo(method.GetParameters()[0].ParameterType);
    }

    private record Entry(IEventListener Listener, MethodInfo Method, PostOrder Order, bool BypassScopedFilter, CancellationToken CancellationToken);

    private readonly record struct InvocationCandidate(PostOrder Order, int Sequence, WeakEntry WeakEntry);

    private readonly record struct InvokedListenerMethod(IEventListener Listener, MethodInfo Method);

    private sealed class InvokedListenerMethodComparer : IEqualityComparer<InvokedListenerMethod>
    {
        public static readonly InvokedListenerMethodComparer Instance = new();

        public bool Equals(InvokedListenerMethod left, InvokedListenerMethod right)
        {
            return ReferenceEquals(left.Listener, right.Listener) && ReferenceEquals(left.Method, right.Method);
        }

        public int GetHashCode(InvokedListenerMethod value)
        {
            return HashCode.Combine(RuntimeHelpers.GetHashCode(value.Listener), RuntimeHelpers.GetHashCode(value.Method));
        }
    }
}
