using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.Minecraft.Network;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>
/// What the tests of one server type and protocol version play on, one test after another: two servers, the proxy in front of them, and two
/// games. Starting each once lets a test cost only what it plays. The games launch with the first test that plays them, so a collection whose
/// servers don't start, and whose tests skip, launches none.
/// </summary>
public abstract class ConnectionsFixture(ProtocolVersion protocolVersion) : IAsyncLifetime
{
    private VoidProxy? _voidProxy;
    private DateTime _voidLogWindowStartedAt;
    private ReusableGame? _game1;
    private ReusableGame? _game2;

    public ProtocolVersion ProtocolVersion => protocolVersion;

    /// <summary>Whether the servers started, which they don't for a protocol version without a game to play.</summary>
    public abstract bool IsAvailable { get; }

    public abstract IReadOnlyList<MinecraftServer> Servers { get; }

    public MinecraftServer Server1 => Servers[0];

    public MinecraftServer Server2 => Servers[1];

    /// <summary>The modded game the servers run, which clients start to play on them, or <see langword="null"/> when clients play vanilla Minecraft of their own protocol version.</summary>
    public abstract GameSetup? Setup { get; }

    public VoidProxy VoidProxy => _voidProxy ?? throw new InvalidOperationException($"{nameof(VoidProxy)} is not started.");

    /// <summary>The game every test plays.</summary>
    public ReusableGame Game1 => _game1 ?? throw new InvalidOperationException($"{nameof(Game1)} is not available.");

    /// <summary>The game a test with a second player plays.</summary>
    public ReusableGame Game2 => _game2 ?? throw new InvalidOperationException($"{nameof(Game2)} is not available.");

    /// <summary>Skips the test when there is no game for the protocol version to play.</summary>
    public virtual void SkipIfUnavailable()
    {
    }

    public async ValueTask InitializeAsync()
    {
        await StartServersAsync();

        // Servers that didn't start leave their tests to skip, with nothing to play or proxy.
        if (!IsAvailable)
            return;

        _game1 = new ReusableGame(protocolVersion, Setup);
        _game2 = new ReusableGame(protocolVersion, Setup);
        _voidProxy = await VoidProxy.CreateAsync(Path.Combine(Path.GetTempPath(), nameof(ConnectionsFixture), Path.GetRandomFileName()), targetServers: [.. Servers.Select(server => $"localhost:{server.Port}")], cancellationToken: Timeouts.SetupTimeoutToken);
        _voidLogWindowStartedAt = DateTime.UtcNow;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Task.WhenAll(new[] { _game1, _game2 }.OfType<ReusableGame>().Select(game => game.DisposeAsync().AsTask()));
        }
        finally
        {
            try
            {
                if (_voidProxy is { } voidProxy)
                {
                    await voidProxy.LogWriter.WriteLineAsync($"Stopping {nameof(VoidProxy)} because of {nameof(ConnectionsFixture)} disposal");
                    await voidProxy.DisposeAsync();

                    // Each test checks what the proxy logged while it played, and this also covers what it logged between and after them.
                    voidProxy.AssertNoWarningOrHigherLogsSince(_voidLogWindowStartedAt);
                }
            }
            finally
            {
                await DisposeServersAsync();
            }
        }

        GC.SuppressFinalize(this);
    }

    protected abstract Task StartServersAsync();

    protected abstract Task DisposeServersAsync();
}
