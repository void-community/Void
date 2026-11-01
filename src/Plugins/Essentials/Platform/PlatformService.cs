using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Void.Minecraft.Commands.Brigadier.Context;
using Void.Minecraft.Commands.Brigadier.Extensions;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Plugins.Common.Services;

namespace Void.Proxy.Plugins.Essentials.Platform;

public class PlatformService(ILogger<PlatformService> logger, IHostApplicationLifetime hostApplicationLifetime, ICommandService commands, Plugin plugin) : IPluginCommonService
{
    [Subscribe]
    public void OnPlayerJoinedServer(PlayerJoinedServerEvent @event)
    {
        logger.LogInformation("Player {Player} connected to {Server} ({ProtocolVersion})", @event.Player, @event.Link.Server, @event.Player.ProtocolVersion);
    }

    [Subscribe]
    public void OnPluginLoading(PluginLoadingEvent @event)
    {
        if (@event.Plugin != plugin)
            return;

        commands.Register(builder => builder
            .Literal("stop")
            .Executes(StopServerAsync));
    }

    private async ValueTask<int> StopServerAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await context.Source.SendMessageAsync("Stopping proxy...", cancellationToken);
        hostApplicationLifetime.StopApplication();
        return 0;
    }
}
