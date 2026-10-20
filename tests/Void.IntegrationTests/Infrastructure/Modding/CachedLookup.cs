using System;
using System.Threading;
using System.Threading.Tasks;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>Runs a lookup once and shares its result, unless it failed, so a later test tries again.</summary>
internal sealed class CachedLookup<T>(Func<Task<T>> lookUpAsync)
{
    private readonly Lock _lock = new();
    private Task<T>? _lookup;

    public Task<T> GetAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_lookup is null || _lookup.IsFaulted || _lookup.IsCanceled)
                _lookup = lookUpAsync();

            return _lookup.WaitAsync(cancellationToken);
        }
    }
}
