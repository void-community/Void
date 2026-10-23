using Microsoft.Extensions.DependencyInjection;
using Void.Minecraft.Network;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Plugins.Common.Plugins;
using Void.Proxy.Plugins.Essentials.Debugging;
using Void.Proxy.Plugins.Essentials.Moderation;
using Void.Proxy.Plugins.Essentials.Platform;
using Void.Proxy.Plugins.Essentials.Redirection;

namespace Void.Proxy.Plugins.Essentials;

public class Plugin(IDependencyService dependencies, IConsoleService console) : IProtocolPlugin
{
    public static IEnumerable<ProtocolVersion> SupportedVersions => ProtocolVersion.Range(ProtocolVersion.MINECRAFT_1_20_2, ProtocolVersion.Latest);

    public string Name => nameof(Essentials);

    [Subscribe]
    public void OnPluginLoading(PluginLoadingEvent @event)
    {
        if (@event.Plugin != this)
            return;

        dependencies.Register(services =>
        {
            services.AddSingleton<RedirectionService>();
            services.AddSingleton<ModerationService>();
            services.AddSingleton<PlatformService>();
            services.AddSingleton<TraceService>();
            services.AddScoped<FailoverService>();
        });
    }
}
