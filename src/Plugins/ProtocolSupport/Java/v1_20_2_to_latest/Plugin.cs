using Microsoft.Extensions.DependencyInjection;
using Void.Minecraft.Network;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Plugins.Common.Crypto;
using Void.Proxy.Plugins.Common.Network.Bundles;
using Void.Proxy.Plugins.Common.Network.Channels.Services;
using Void.Proxy.Plugins.Common.Plugins;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Authentication;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Bundles;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Channels;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Commands;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Compression;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Encryption;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Lifecycle;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Registries;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Transformations;
using BundleService = Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Bundles.BundleService;

namespace Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest;

public class Plugin(IDependencyService dependencies) : IProtocolPlugin
{
    public static IEnumerable<ProtocolVersion> SupportedVersions => ProtocolVersion.Range(ProtocolVersion.MINECRAFT_1_20_2, ProtocolVersion.Latest);

    public string Name => nameof(v1_20_2_to_latest);

    [Subscribe]
    public void OnPluginLoading(PluginLoadingEvent @event)
    {
        if (@event.Plugin != this)
            return;

        Registry.Fill();

        dependencies.Register(services =>
        {
            services.AddSingleton<RegistryService>();
            services.AddSingleton<ChannelService>();
            services.AddSingleton<CompressionService>();
            services.AddSingleton<EncryptionService>();
            services.AddSingleton<AuthenticationService>();
            services.AddSingleton<TransformationService>();

            services.AddSingleton<CommandService>();
            services.AddSingleton<BundleService>();
            services.AddSingleton<LifecycleService>();

            // Each protocol plugin registers the services its players need, which another may not be loaded to register
            services.AddScoped<IChannelBuilderService, SimpleMinecraftChannelBuilderService>();
            services.AddScoped<ITokenHolder, SimpleTokenHolder>();
            services.AddScoped<IBundleService, Common.Network.Bundles.BundleService>();
        });
    }
}
