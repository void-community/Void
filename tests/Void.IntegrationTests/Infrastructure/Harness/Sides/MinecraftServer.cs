using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Extensions;

namespace Void.IntegrationTests.Infrastructure.Harness.Sides;

public abstract record MinecraftServer(IContainer Container, string LogFileName) : IIntegrationSide
{
    private const int MaximumStartupAttempts = 3;
    private const int StartupFailureLogLines = 200;
    // The image downloads the server and its mods while it starts, and lookups and downloads can fail for a moment.
    private static readonly string[] TransientStartupFailureMarkers =
    [
        "DnsNameResolverTimeoutException",
        "SearchDomainUnknownHostException",
        "UnknownHostException",
        "Temporary failure in name resolution",
        "SslHandshakeTimeoutException",
        "ConnectTimeoutException",
        "ReadTimeoutException",
        "PrematureCloseException",
        "Connection reset",
        // Paper's launcher checks the server jar it downloads from Mojang
        "Hash check failed for downloaded file"
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

    /// <summary>
    /// Waits until a player has logged in since a time. Every server logs the address the player came from right after its name then, while old
    /// Bukkit-based servers never log that it joined the game.
    /// </summary>
    public async Task ExpectLoginAsync(string username, DateTime since, CancellationToken cancellationToken = default)
    {
        await Container.ExpectTextAsync($"{username}[/", since, cancellationToken);
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
            catch (Exception exception) when (startupAttempt < MaximumStartupAttempts && IsTransientStartupFailure(exception))
            {
                await DisposeFailedContainerAsync(container, serverDescription);
                var retryDelay = TimeSpan.FromSeconds(2 << (startupAttempt - 1));
                Console.WriteLine($"[Void.IntegrationTests] {serverDescription} startup attempt {startupAttempt} of {MaximumStartupAttempts} failed because of a transient network error; retrying in {retryDelay.TotalSeconds:0} seconds: {exception.Message}");
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

    private static bool IsTransientStartupFailure(Exception exception)
    {
        return exception switch
        {
            ContainerNotRunningException => Array.Exists(TransientStartupFailureMarkers, marker => exception.ToString().Contains(marker, StringComparison.OrdinalIgnoreCase)),

            // Files Testcontainers downloads into the container, such as LegacyForgePatcher
            HttpRequestException { StatusCode: null or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError } => true,
            TaskCanceledException { InnerException: TimeoutException } => true,

            // An image pull that a registry failed, such as with a 502, leaves no image to create the container from.
            DockerImageNotFoundException => true,
            _ => false
        };
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
