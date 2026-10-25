using System.CommandLine;
using System.Text;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Void.Proxy.Api;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Configurations;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Crypto;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Extensions;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Logging;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Plugins;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Api.Servers;
using Void.Proxy.Commands;
using Void.Proxy.Configurations;
using Void.Proxy.Console;
using Void.Proxy.Crypto;
using Void.Proxy.Events;
using Void.Proxy.Extensions;
using Void.Proxy.Links;
using Void.Proxy.Logging;
using Void.Proxy.Players;
using Void.Proxy.Plugins;
using Void.Proxy.Plugins.Dependencies;
using Void.Proxy.Plugins.Dependencies.Embedded;
using Void.Proxy.Plugins.Dependencies.Extensions;
using Void.Proxy.Plugins.Dependencies.File;
using Void.Proxy.Plugins.Dependencies.Nuget;
using Void.Proxy.Plugins.Retention;
using Void.Proxy.Servers;
using Logger = Serilog.Core.Logger;

namespace Void.Proxy;

public static class VoidEntryPoint
{
    public record RunOptions : IRunOptions
    {
        public static RunOptions Default { get; } = new();

        public string WorkingDirectory
        {
            get;
            init => field = Path.EndsInDirectorySeparator(value) ? value : value + Path.DirectorySeparatorChar;
        } = AppContext.BaseDirectory;
        public string[] Arguments { get; init; } = [];
        public TextWriter? LogWriter { get; init; } = null;
    }

    /// <param name="Services">The services of the running proxy, such as its <see cref="IPluginService" />, until it stops.</param>
    public record RunResult(Task<int> CompletionTask, int ListeningPort, IServiceProvider Services) : IAsyncDisposable
    {
        public int ExitCode => !CompletionTask.IsCompleted
                ? throw new InvalidOperationException("Cannot get exit code before completion.")
                : !CompletionTask.IsCompletedSuccessfully
                ? throw new InvalidOperationException("Cannot get exit code from a failed task.", CompletionTask.Exception)
                : CompletionTask.Result;

        public async ValueTask DisposeAsync()
        {
            await CompletionTask;
            GC.SuppressFinalize(this);
        }

        protected virtual bool PrintMembers(StringBuilder builder)
        {
            builder.Append(nameof(CompletionTask));
            builder.Append(" = ");
            builder.Append(CompletionTask);
            builder.Append(", ");
            builder.Append(nameof(ListeningPort));
            builder.Append(" = ");
            builder.Append(ListeningPort);
            return true;
        }
    }

    static VoidEntryPoint() => System.Console.Title = nameof(Void);

    private static async Task<int> Main(string[] args)
    {
        // The proxy starts its own executable to analyze a dump of itself, as it does for a plugin that refuses to unload.
        if (PluginRetentionInspector.IsAnalyzer(args))
            return PluginRetentionInspector.Analyze(args);

        var options = new RunOptions { Arguments = args };
        var result = await RunAsync(options);

        return await result.CompletionTask;
    }

    public static async Task<RunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        return await RunAsync(RunOptions.Default, cancellationToken);
    }

    public static async Task<RunResult> RunAsync(RunOptions options, CancellationToken cancellationToken = default)
    {
        // If you set a custom working directory, you are responsible for ensuring everything follows it.
        // We are using the default working directory only for normal runs.
        // This was done to allow Tests to not conflict when running in parallel.

        if (options.WorkingDirectory.Equals(RunOptions.Default.WorkingDirectory, StringComparison.OrdinalIgnoreCase))
            Directory.SetCurrentDirectory(options.WorkingDirectory);

        var command = new RootCommand("Runs the proxy");
        var loggingLevelSwitch = new LoggingLevelSwitch();

        var logger = CreateLogger(loggingLevelSwitch, options.LogWriter);
        var host = new HostBuilder()
            .UseServiceProviderFactory(new DryIocServiceProviderFactory(new Container(DryIocAdapter.MicrosoftDependencyInjectionRules)))
            .UseConsoleLifetime(options => options.SuppressStatusMessages = true)
            .ConfigureServices((context, services) => services
                .AddSerilog(logger, dispose: false)
                .AddJsonOptions()
                .AddHttpClient()
                .AddSettings()
                .AddSingleton(loggingLevelSwitch)
                .AddSingleton(new ConsoleConfiguration(options.LogWriter is null, command))
                .AddSingleton<ILogLevelSwitch, LogLevelSwitch>()
                .AddSingleton<IRunOptions>(options)
                .AddSingletonAndListen<ICryptoService, RsaCryptoService>()
                .AddSingletonAndListen<IEventService, EventService>()
                .AddSingletonAndListen<IPluginService, PluginService>()
                .AddSingletonAndListen<IPlayerService, PlayerService>()
                .AddSingletonAndListen<IServerService, ServerService>()
                .AddSingletonAndListen<ILinkService, LinkService>()
                .AddSingletonAndListen<IConsoleService, ConsoleService>()
                .AddSingletonAndListen<ICommandService, CommandService>()
                .AddSingletonAndListen<IConfigurationService, ConfigurationService>()
                .AddSingletonAndListen<IDependencyService, DependencyService>()
                .AddSingletonAndListen<IProxy, Platform>()
                .AddSingletonAndListen<INuGetDependencyResolver, DependencyResolver>()
                .AddSingleton<IFileDependencyResolver, FileDependencyResolver>()
                .AddSingleton<IEmbeddedDependencyResolver, EmbeddedDependencyResolver>()
                .AddHostedService(services => services.GetRequiredService<IConfigurationService>())
                .AddHostedService(services => (Platform)services.GetRequiredService<IProxy>()))
            .Build();

        await host.StartAsync(cancellationToken);
        command.SetAction(async (result, cancellationToken) =>
        {
            var console = host.Services.GetRequiredService<IConsoleService>();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

            var token = lifetime.ApplicationStopping;
            await using var registration = cancellationToken.Register(lifetime.StopApplication);

            try
            {
                while (!token.IsCancellationRequested)
                    await console.HandleCommandsAsync(token);
            }
            catch (Exception exception) when (exception is TaskCanceledException or OperationCanceledException)
            {
                // Ignore
            }
        });

        var proxy = host.Services.GetRequiredService<IProxy>();
        var port = proxy.Port;

        return new RunResult(CompletionTask: Task.Run(async () =>
        {
            try
            {
                return await command.Parse(options.Arguments).InvokeAsync(cancellationToken: cancellationToken);
            }
            finally
            {
                await host.StopAsync(cancellationToken.IsCancellationRequested ? default : cancellationToken);

                host.Dispose();
                logger.Dispose();
            }
        }), ListeningPort: port, Services: host.Services);
    }

    private static Logger CreateLogger(LoggingLevelSwitch loggingLevelSwitch, TextWriter? logWriter)
    {
        var configuration = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.ControlledBy(loggingLevelSwitch)
            .MinimumLevel.Override(nameof(Microsoft), LogEventLevel.Information)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning);

        const string template = "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj} {NewLine}{Exception}";

        if (logWriter is null)
            configuration = configuration.WriteTo.Console(outputTemplate: template, theme: AnsiConsoleTheme.Sixteen);
        else
            configuration = configuration.WriteTo.TextWriter(logWriter, outputTemplate: template);

        return configuration.CreateLogger();
    }
}
