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
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Authentication;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Channels;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Commands;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Compression;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Encryption;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Lifecycle;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Registries;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Transformations;
using BundleService = Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Bundles.BundleService;

namespace Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1;

public class Plugin(IDependencyService dependencies) : IProtocolPlugin
{
    public static IEnumerable<ProtocolVersion> SupportedVersions => ProtocolVersion.Range(ProtocolVersion.MINECRAFT_1_13, ProtocolVersion.MINECRAFT_1_20);

    public string Name => nameof(v1_13_to_1_20_1);

    [Subscribe]
    public void OnPluginLoading(PluginLoadingEvent @event)
    {
        if (@event.Plugin != this)
            return;

        Registry.Fill();

        dependencies.Register(services =>
        {
            services.AddSingleton<ChannelService>();
            services.AddSingleton<RegistryService>();
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
