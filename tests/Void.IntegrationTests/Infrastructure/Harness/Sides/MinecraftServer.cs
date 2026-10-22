using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Extensions;

namespace Void.IntegrationTests.Infrastructure.Harness.Sides;

public abstract record MinecraftServer(IContainer Container, string LogFileName) : IIntegrationSide
{
    private const int MaximumStartupAttempts = 3;
    private const int StartupFailureLogLines = 200;
    private static readonly string[] TransientStartupFailureMarkers =
    [
        "DnsNameResolverTimeoutException",
        "SearchDomainUnknownHostException",
        "UnknownHostException",
        "Temporary failure in name resolution"
    ];
    private readonly DateTime _readLogsSince = DateTime.UtcNow;
    public int Port => Container.GetMappedPublicPort(containerPort: 25565);

    public IEnumerable<string> Logs => ReadLogsAsync(_readLogsSince).GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        await Container.DisposeAsync();

        GC.SuppressFinalize(this);
    }

    public async Task<IEnumerable<string>> ReadLogsAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        return await Container.ReadLogsAsync(since, cancellationToken);
    }

    public async Task ExpectTextAsync(string text, bool lookupHistory = false, CancellationToken cancellationToken = default)
    {
        if (lookupHistory)
            await Container.ExpectTextAsync(text, since: _readLogsSince, cancellationToken);
        else
            await Container.ExpectTextAsync(text, cancellationToken);
    }

    /// <summary>Starts a server container, retrying startups that failed because of a transient DNS error.</summary>
    protected static async Task<IContainer> StartContainerAsync(ContainerBuilder builder, string serverDescription, CancellationToken cancellationToken)
    {
        for (var startupAttempt = 1; startupAttempt <= MaximumStartupAttempts; startupAttempt++)
        {
            var container = builder.Build();

            try
            {
                await container.StartAsync(cancellationToken);
                return container;
            }
            catch (ContainerNotRunningException exception) when (startupAttempt < MaximumStartupAttempts && IsTransientStartupFailure(exception))
            {
                await DisposeFailedContainerAsync(container, serverDescription);
                var retryDelay = TimeSpan.FromSeconds(2 << (startupAttempt - 1));
                Console.WriteLine($"[Void.IntegrationTests] {serverDescription} startup attempt {startupAttempt} of {MaximumStartupAttempts} failed because of a transient DNS error; retrying in {retryDelay.TotalSeconds:0} seconds");
                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (Exception exception)
            {
                // The server's own output says why it didn't start, such as a mod loader installer that failed.
                var logs = await TryReadLastLogsAsync(container);
                await DisposeFailedContainerAsync(container, serverDescription);
                throw new IntegrationTestException($"{serverDescription} failed to start: {exception.Message}\nLast server logs:\n{logs}", exception);
            }
        }

        throw new InvalidOperationException($"{serverDescription} startup attempts were exhausted without returning or throwing.");
    }

    private static bool IsTransientStartupFailure(ContainerNotRunningException exception)
    {
        return Array.Exists(TransientStartupFailureMarkers, marker => exception.ToString().Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> TryReadLastLogsAsync(IContainer container)
    {
        try
        {
            var logs = await container.ReadLogsAsync(since: default, CancellationToken.None);
            return string.Join('\n', logs.TakeLast(StartupFailureLogLines));
        }
        catch (Exception exception)
        {
            return $"(the logs couldn't be read: {exception.Message})";
        }
    }

    private static async Task DisposeFailedContainerAsync(IContainer container, string serverDescription)
    {
        try
        {
            await container.DisposeAsync();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[Void.IntegrationTests] Disposing a failed {serverDescription} container also failed: {exception.Message}");
        }
    }
}
