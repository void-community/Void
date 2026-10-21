using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>The Paper servers every Paper collection plays on. They start when the first one plays, so a shard with none starts no Paper server.</summary>
public class PaperFixture : IAsyncLifetime
{
    private readonly Lazy<Task<PaperServer[]>> _servers = new(StartServersAsync);

    public async Task<IReadOnlyList<PaperServer>> GetServersAsync()
    {
        return await _servers.Value;
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_servers.IsValueCreated)
        {
            // Servers that failed to start were stopped as they failed.
            var serversTask = _servers.Value;
            await ((Task)serversTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (serversTask.IsCompletedSuccessfully)
                await Task.WhenAll(serversTask.Result.Select(server => server.DisposeAsync().AsTask()));
        }

        GC.SuppressFinalize(this);
    }

    private static async Task<PaperServer[]> StartServersAsync()
    {
        var paperServer1 = PaperServer.CreateAsync("server-1.log", Timeouts.SetupTimeoutToken);
        var paperServer2 = PaperServer.CreateAsync("server-2.log", Timeouts.SetupTimeoutToken);

        try
        {
            return await Task.WhenAll(paperServer1, paperServer2);
        }
        catch
        {
            await DisposeCreatedServersAsync(paperServer1, paperServer2);
            throw;
        }
    }

    private static async Task DisposeCreatedServersAsync(params Task<PaperServer>[] serverTasks)
    {
        await Task.WhenAll(serverTasks.Where(task => task.IsCompletedSuccessfully).Select(task => task.Result.DisposeAsync().AsTask()));
    }
}
