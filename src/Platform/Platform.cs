using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Void.Proxy.Api;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events.Proxy;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Logging;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Plugins;
using Void.Proxy.Api.Settings;

namespace Void.Proxy;

public class Platform(
    ILogger<Platform> logger,
    ILogLevelSwitch logLevelSwitch,
    IRunOptions runOptions,
    IConsoleService console,
    IPluginService plugins,
    IEventService events,
    IPlayerService players,
    ISettings settings,
    IHostApplicationLifetime hostApplicationLifetime) : IProxy, IHostedService
{
    private readonly Option<string> _interfaceOption = new("--interface") { Description = "Sets the listening network interface" };

    private readonly Option<LogLevel> _loggingOption = new("--logging") { Description = "Sets the logging level" };

    private readonly Option<bool> _offlineOption = new("--offline") { Description = "Allows players to connect without Mojang authorization" };

    private readonly Option<int> _portOption = new("--port") { Description = "Sets the listening port" };

    private Task? _backgroundTask;
    private TcpListener? _listener;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
#if RELEASE
        logLevelSwitch.Level = settings.LogLevel;
#else
        logLevelSwitch.Level = LogLevel.Debug;
#endif

        if (console.TryGetOptionValue(_loggingOption, out var loggingOptionValue))
            logLevelSwitch.Level = loggingOptionValue;

        logger.LogInformation("Starting {Name} {Version} proxy", nameof(Void), $"v{GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        var startTime = Stopwatch.GetTimestamp();

        logger.LogTrace("Working directory is {Path}", runOptions.WorkingDirectory);

        if (bool.TryParse(Environment.GetEnvironmentVariable("VOID_OFFLINE"), out var offlineVariable))
            settings.Offline = offlineVariable;

        if (console.TryGetOptionValue(_offlineOption, out var offlineOptionValue))
            settings.Offline = offlineOptionValue;

        if (settings.Offline)
            logger.LogWarning("Offline mode is enabled. Players will be able to connect without Mojang authentication.");

        logger.LogInformation("Loading plugins");
        await plugins.LoadPluginsAsync(cancellationToken: cancellationToken);

        await events.ThrowAsync<ProxyStartingEvent>(cancellationToken);

        logger.LogDebug("Starting connection listener");
        _listener = new TcpListener(Interface.Equals(IPAddress.Any) ? IPAddress.IPv6Any : Interface, Port);

        if (_listener.Server.AddressFamily is AddressFamily.InterNetworkV6)
            _listener.Server.DualMode = true;

        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        await StartAcceptingConnectionsAsync(cancellationToken);

        logger.LogInformation("Connection listener started on address {EndPoint}", _listener.Server.LocalEndPoint);

        _backgroundTask = ExecuteAsync(hostApplicationLifetime.ApplicationStopping);

        var totalTime = Stopwatch.GetElapsedTime(startTime);
        logger.LogInformation("Proxy started in {StartTimeSeconds} seconds!", totalTime.TotalSeconds.ToString("F"));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Status = ProxyStatus.Stopping;

        logger.LogInformation("Stopping proxy");
        await events.ThrowAsync<ProxyStoppingEvent>(cancellationToken);

        await players.ForEachAsync(async (player, cancellationToken) =>
        {
            try
            {
                await players.KickPlayerAsync(player, "Proxy is shutting down", cancellationToken);
            }
            catch (StreamClosedException)
            {
                // Player disconnected before we could kick them, ignore
            }
        }, cancellationToken);

        logger.LogInformation("Awaiting completion of connection listener");
        if (_backgroundTask is not null)
            await _backgroundTask;

        _listener?.Stop();

        await events.ThrowAsync<ProxyStoppedEvent>(cancellationToken);

        logger.LogInformation("Unloading plugins");
        await plugins.UnloadContainersAsync(cancellationToken);

        logger.LogInformation("Proxy stopped!");
    }

    public ProxyStatus Status
    {
        get;
        private set
        {
            logger.LogInformation("Proxy is {Status}", value);
            field = value;
        }
    }

    public IPAddress Interface => console.TryGetOptionValue(_interfaceOption, out var value) && IPAddress.TryParse(value, out var address) ? address : settings.Address;

    public int Port => _listener switch
    {
        { LocalEndpoint: IPEndPoint ipEndPoint } => ipEndPoint.Port,
        { LocalEndpoint: DnsEndPoint dnsEndPoint } => dnsEndPoint.Port,
        _ when console.TryGetOptionValue(_portOption, out var value) => value,
        _ => settings.Port
    };

    public async ValueTask StartAcceptingConnectionsAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
            throw new InvalidOperationException("Listener is not created yet.");

        if (Status is ProxyStatus.Stopping)
            throw new InvalidOperationException("Proxy is stopping.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _listener.Start();
                break;
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode is not SocketError.AddressAlreadyInUse)
                    throw;

                logger.LogError("Address {Address}:{Port} already in use by another process. Retrying in 5 seconds ...", Interface, Port);
                await Task.Delay(5_000, cancellationToken);
            }
        }

        Status = ProxyStatus.Alive;
    }

    public void PauseAcceptingConnections()
    {
        if (_listener is null)
            throw new InvalidOperationException("Listener is not created yet.");

        if (Status is ProxyStatus.Stopping)
            throw new InvalidOperationException("Proxy is stopping.");

        Status = ProxyStatus.Paused;
        _listener.Stop();
    }

    public void Stop(bool waitOnlinePlayers = false)
    {
        if (waitOnlinePlayers)
        {
            _ = Task.Run(async () =>
            {
                while (players.All.Any())
                {
                    logger.LogInformation("Waiting for {Count} players to disconnect ...", players.All.Count());
                    await Task.Delay(5_000);
                }

                hostApplicationLifetime.StopApplication();
            });
        }
        else
        {
            hostApplicationLifetime.StopApplication();
        }
    }

    private async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(_listener);
        await events.ThrowAsync<ProxyStartedEvent>(cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Status is ProxyStatus.Stopping)
                    break;

                if (Status is ProxyStatus.Paused)
                {
                    await Task.Delay(1_000, cancellationToken);
                    continue;
                }

                var client = await _listener.AcceptTcpClientAsync(cancellationToken);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await players.AcceptPlayerAsync(client, cancellationToken);
                    }
                    catch (Exception exception) when (IsIgnored(exception))
                    {
                        // Ignored
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "An error occurred while accepting a player connection");
                    }
                }, cancellationToken);
            }
        }
        catch (Exception exception) when (IsIgnored(exception))
        {
            // Ignored
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Platform background task completed with exception");
            throw;
        }

        return;

        static bool IsIgnored(Exception exception) => exception switch
        {
            OperationCanceledException or SocketException { SocketErrorCode: SocketError.OperationAborted } => true,
            _ => false
        };
    }
}
