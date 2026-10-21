using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.Minecraft.Network;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>Starts the servers of one protocol version, and the proxy in front of them, when its test class starts.</summary>
public abstract class MinecraftServersFixture<TProtocolVersion> : IMinecraftServersFixture, IAsyncLifetime where TProtocolVersion : IProtocolVersionType
{
    private MinecraftServers? _servers;
    private VoidProxy? _voidProxy;
    private DateTime _voidLogWindowStartedAt;

    public ProtocolVersion ProtocolVersion => TProtocolVersion.ProtocolVersion;
    public MinecraftServers Servers => _servers ?? throw new InvalidOperationException($"{nameof(Servers)} are not started.");
    public VoidProxy VoidProxy => _voidProxy ?? throw new InvalidOperationException($"{nameof(VoidProxy)} is not started.");

    public virtual void SkipIfUnavailable()
    {
    }

    public async ValueTask InitializeAsync()
    {
        // Nothing starts for a protocol version without a game to play, whose tests skip.
        if (await StartServersAsync() is not { } servers)
            return;

        _servers = servers;
        _voidProxy = await VoidProxy.CreateAsync(Path.Combine(Path.GetTempPath(), GetType().Name, Path.GetRandomFileName()), targetServers: [.. servers.Servers.Select(server => $"localhost:{server.Port}")], cancellationToken: Timeouts.SetupTimeoutToken);
        _voidLogWindowStartedAt = DateTime.UtcNow;
    }

    public async ValueTask DisposeAsync()
    {
        if (_voidProxy is { } voidProxy)
        {
            await voidProxy.LogWriter.WriteLineAsync($"Stopping {nameof(VoidProxy)} because of {GetType().Name} disposal");
            await voidProxy.DisposeAsync();
        }

        await StopServersAsync();
        GC.SuppressFinalize(this);
        _voidProxy?.AssertNoWarningOrHigherLogsSince(_voidLogWindowStartedAt);
    }

    /// <returns>The started servers, or <see langword="null"/> when the protocol version has no game to play.</returns>
    protected abstract Task<MinecraftServers?> StartServersAsync();

    protected virtual Task StopServersAsync()
    {
        return Task.CompletedTask;
    }
}
