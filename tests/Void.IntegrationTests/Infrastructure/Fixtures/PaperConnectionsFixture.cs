using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.Minecraft;
using Void.Minecraft.Network;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>
/// Plays a protocol version on servers of its own: Paper's for the newest release of the version that Paper built, or vanilla Minecraft's for
/// the newest release when Paper built none, so that every version is played on the server it was made for.
/// </summary>
public abstract class PaperConnectionsFixture(ProtocolVersion protocolVersion) : ConnectionsFixture(protocolVersion)
{
    private MinecraftServer[]? _servers;

    public override bool IsAvailable => _servers is not null;

    public override IReadOnlyList<MinecraftServer> Servers => _servers ?? throw new InvalidOperationException($"{nameof(Servers)} are not started.");

    public override GameSetup? Setup => null;

    protected override async Task StartServersAsync()
    {
        var releases = ProtocolVersion.Releases.OrderDescending().ToArray();

        foreach (var release in releases)
        {
            try
            {
                await StartServersAsync(release, vanilla: false);
                return;
            }
            catch (IntegrationTestException exception) when (exception.Message.Contains(PaperServer.MissingBuildMessage, StringComparison.Ordinal) || exception.Message.Contains(PaperServer.UnusableBuildMessage, StringComparison.Ordinal))
            {
                Console.WriteLine($"[Void.IntegrationTests] Paper has no build for Minecraft {release} that starts");
            }
        }

        await StartServersAsync(releases[0], vanilla: true);
    }

    private async Task StartServersAsync(ReleaseVersion release, bool vanilla)
    {
        Task<PaperServer>[] serverTasks = [PaperServer.CreateAsync(release, vanilla, "server-1.log", TestContext.Current.CancellationToken), PaperServer.CreateAsync(release, vanilla, "server-2.log", TestContext.Current.CancellationToken)];

        try
        {
            _servers = await Task.WhenAll(serverTasks);
        }
        catch
        {
            await Task.WhenAll(serverTasks.Where(task => task.IsCompletedSuccessfully).Select(task => task.Result.DisposeAsync().AsTask()));
            throw;
        }
    }

    protected override async Task DisposeServersAsync()
    {
        if (_servers is { } servers)
            await Task.WhenAll(servers.Select(server => server.DisposeAsync().AsTask()));
    }
}
