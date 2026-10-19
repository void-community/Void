using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Void.Minecraft.Network;
using Void.Proxy.Api.Configurations;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Plugins.Common.Plugins;
using Void.Proxy.Plugins.ModsSupport.SlowJoin.Services;

namespace Void.Proxy.Plugins.ModsSupport.SlowJoin;

/// <summary>
/// Keeps players connected while their client is still busy loading after joining a server, which large modpacks need for longer
/// than a backend waits for a keep-alive answer.
/// </summary>
public class Plugin(IDependencyService dependencies, IConfigurationService configs, IConsoleService console) : IProtocolPlugin
{
    public readonly Option<int> SlowJoinTimeoutOption = new("--slow-join-timeout")
    {
        Description = "Sets how long a player may take to answer the first keep-alive after joining a server, in seconds"
    };

    public static IEnumerable<ProtocolVersion> SupportedVersions => ProtocolVersion.Range();

    public string Name => nameof(SlowJoin);

    [Subscribe]
    public async ValueTask OnPluginLoading(PluginLoadingEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Plugin != this)
            return;

        var settings = await configs.GetAsync<Settings>(cancellationToken);

        dependencies.Register(services =>
        {
            services.AddSingleton(settings);
            services.AddScoped<SlowJoinService>();
        });

        console.EnsureOptionDiscovered(SlowJoinTimeoutOption);
    }
}
