using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>The proxy in front of the servers of a test collection, whichever server type they run.</summary>
public class VoidFixture<TServersFixture>(TServersFixture serversFixture) : IVoidFixture, IAsyncLifetime where TServersFixture : IMinecraftServersFixture
{
    private VoidProxy? _voidProxy;
    private DateTime _voidLogWindowStartedAt;

    public VoidProxy VoidProxy => _voidProxy ?? throw new InvalidOperationException($"{nameof(VoidProxy)} is not initialized.");

    public async ValueTask InitializeAsync()
    {
        // Servers that didn't start leave their tests to skip, with nothing to proxy.
        if (!serversFixture.IsAvailable)
            return;

        _voidProxy = await VoidProxy.CreateAsync(Path.Combine(Path.GetTempPath(), nameof(VoidFixture<>), Path.GetRandomFileName()), targetServers: [.. serversFixture.Servers.Servers.Select(server => $"localhost:{server.Port}")], cancellationToken: Timeouts.SetupTimeoutToken);
        _voidLogWindowStartedAt = DateTime.UtcNow;
    }

    public async ValueTask DisposeAsync()
    {
        if (_voidProxy is { } voidProxy)
        {
            await voidProxy.LogWriter.WriteLineAsync($"Stopping {nameof(VoidProxy)} because of {nameof(VoidFixture<>)} disposal");
            await voidProxy.DisposeAsync();
            voidProxy.AssertNoWarningOrHigherLogsSince(_voidLogWindowStartedAt);
        }

        GC.SuppressFinalize(this);
    }
}
