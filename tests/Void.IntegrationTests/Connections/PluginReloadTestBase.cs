using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.Minecraft.Network;
using Void.Proxy.Api.Plugins;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// Unloads, loads and reloads the plugin containers of the collection's proxy, which every test leaves with the containers it started with,
/// so the tests that run after it play on the proxy as it started.
/// </summary>
public abstract class PluginReloadTestBase(ConnectionsFixture fixture) : IntegrationUnitBase
{
    // The proxy waits as long for an unloaded container to be collected.
    private static readonly TimeSpan CollectionTimeout = TimeSpan.FromSeconds(10);

    protected ConnectionsFixture Fixture => fixture;

    protected VoidProxy Proxy => fixture.VoidProxy;

    protected IPluginService Plugins => Proxy.Plugins;

    protected EndPoint ProxyEndPoint => new IPEndPoint(IPAddress.Loopback, Proxy.Port);

    /// <summary>The containers the proxy loaded as it started, which tests unload and load, and leave loaded.</summary>
    protected IReadOnlyList<string> StartupContainers => Proxy.StartupContainers;

    /// <summary>Runs a test on the collection's proxy, and loads the containers it left unloaded afterwards.</summary>
    protected Task RunReloadingAsync(Func<Task> function)
    {
        return RunReloadingAsync(function, LogLevel.Warning);
    }

    /// <inheritdoc cref="RunReloadingAsync(Func{Task})"/>
    protected async Task RunReloadingAsync(Func<Task> function, LogLevel minimumFailureLogLevel)
    {
        fixture.SkipIfUnavailable();

        await LoggedExecutorAsync(async () =>
        {
            try
            {
                await function();
            }
            finally
            {
                await RestoreAsync();
            }
        }, minimumFailureLogLevel, fixture.Game1, Proxy, fixture.Server1, fixture.Server2);
    }

    /// <summary>The container of the protocol plugin that the games of the collection's protocol version play with.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected string FindProtocolContainer()
    {
        // Each container loads its own copy of the protocol plugins' interface, so the plugin is found by the versions it supports.
        foreach (var plugin in Plugins.All)
        {
            var pluginType = plugin.GetType();

            if (pluginType.GetProperty("SupportedVersions", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is IEnumerable<ProtocolVersion> versions && versions.Contains(fixture.ProtocolVersion))
                return AssemblyLoadContext.GetLoadContext(pluginType.Assembly)?.Name ?? throw new IntegrationTestException($"Plugin {plugin.Name} has no named load context");
        }

        throw new IntegrationTestException($"No loaded plugin supports {fixture.ProtocolVersion}");
    }

    /// <summary>The startup containers other than the one of the collection's protocol version, which a playing game doesn't need to stay.</summary>
    protected IReadOnlyList<string> FindContainersBesidesProtocol()
    {
        var protocolContainer = FindProtocolContainer();
        return [.. StartupContainers.Where(name => name != protocolContainer)];
    }

    /// <summary>Unloads a container, and fails unless its load context is collected afterwards.</summary>
    protected async Task UnloadAsync(string name, CancellationToken cancellationToken = default)
    {
        var context = TrackContext(name);

        await Plugins.UnloadContainerAsync(name, cancellationToken);

        Assert.DoesNotContain(name, Plugins.Containers);
        await AssertCollectedAsync(name, context, cancellationToken);
    }

    /// <summary>Loads a container, and fails unless it's loaded afterwards.</summary>
    protected async Task LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        await Plugins.LoadContainerAsync(name, cancellationToken);
        Assert.Contains(name, Plugins.Containers);
    }

    protected async Task ReloadAsync(string name, CancellationToken cancellationToken = default)
    {
        await UnloadAsync(name, cancellationToken);
        await LoadAsync(name, cancellationToken);
    }

    /// <summary>Loads the startup containers that aren't loaded, in the order the proxy loaded them as it started.</summary>
    protected async Task RestoreAsync()
    {
        foreach (var name in StartupContainers.Except(Plugins.Containers).ToArray())
            await Plugins.LoadContainerAsync(name, Timeouts.StepTimeoutToken);

        Assert.Equal(StartupContainers.Order(), Plugins.Containers.Order());
    }

    /// <summary>A weak reference to the load context of a loaded container, which doesn't keep the context alive.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected WeakReference TrackContext(string name)
    {
        foreach (var plugin in Plugins.All)
        {
            if (AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly) is { } context && context.Name == name)
                return new WeakReference(context);
        }

        throw new IntegrationTestException($"Plugin container {name} has no loaded plugins");
    }

    /// <summary>Fails unless the load context of an unloaded container is collected in the time the proxy gives it.</summary>
    protected static async Task AssertCollectedAsync(string name, WeakReference context, CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();

        while (context.IsAlive && Stopwatch.GetElapsedTime(startedAt) < CollectionTimeout)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();

            if (context.IsAlive)
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        Assert.False(context.IsAlive, $"Load context of plugin container {name} is still alive after it unloaded");
    }

    /// <summary>
    /// Plays a game through the proxy: joins the first server, chats there, and leaves. Each message reaches the server exactly once, which a
    /// packet handler registered twice would break.
    /// </summary>
    protected Task PlayAsync(string testName, bool redirect = false)
    {
        return PlayAsync(testName, Proxy, redirect);
    }

    /// <inheritdoc cref="PlayAsync(string, bool)"/>
    protected async Task PlayAsync(string testName, VoidProxy proxy, bool redirect = false)
    {
        var firstMessage = $"server1-{Guid.NewGuid():N}";
        var secondMessage = $"server2-{Guid.NewGuid():N}";
        Task playerDisconnectionTask;

        await using (var game = await fixture.Game1.BeginSessionAsync(testName, [proxy, fixture.Server1, fixture.Server2], Timeouts.SetupTimeoutToken))
        {
            playerDisconnectionTask = proxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

            await game.JoinServerAsync(new IPEndPoint(IPAddress.Loopback, proxy.Port), Timeouts.SetupTimeoutToken);
            await fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
            await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

            await game.SendTextMessageAsync(firstMessage, Timeouts.StepTimeoutToken);
            await fixture.Server1.ExpectTextAsync(firstMessage, lookupHistory: true, Timeouts.StepTimeoutToken);

            var joinedServer = fixture.Server1;

            if (redirect)
            {
                var secondServerReadyTask = proxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-2", Timeouts.StepTimeoutToken);
                await game.SendTextMessageAsync("/server args-server-2", Timeouts.StepTimeoutToken);
                await secondServerReadyTask;

                await game.SendTextMessageAsync(secondMessage, Timeouts.StepTimeoutToken);
                await fixture.Server2.ExpectTextAsync(secondMessage, lookupHistory: true, Timeouts.StepTimeoutToken);

                joinedServer = fixture.Server2;
            }

            await game.EndAsync(joinedServer, Timeouts.StepTimeoutToken);
        }

        await playerDisconnectionTask;

        Assert.Single(fixture.Server1.Logs, line => line.Contains(firstMessage, StringComparison.Ordinal));

        if (redirect)
            Assert.Single(fixture.Server2.Logs, line => line.Contains(secondMessage, StringComparison.Ordinal));
    }
}
