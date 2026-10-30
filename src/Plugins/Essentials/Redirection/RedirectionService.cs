using Microsoft.Extensions.Logging;
using Void.Minecraft.Commands.Brigadier;
using Void.Minecraft.Commands.Brigadier.Context;
using Void.Minecraft.Commands.Brigadier.Extensions;
using Void.Minecraft.Commands.Brigadier.Suggestion;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Api.Servers;
using Void.Proxy.Plugins.Common.Services;

namespace Void.Proxy.Plugins.Essentials.Redirection;

public class RedirectionService(Plugin plugin, IServerService servers, ICommandService commands, ILinkService links) : IPluginCommonService
{
    [Subscribe]
    public void OnPluginLoading(PluginLoadingEvent @event)
    {
        if (@event.Plugin != plugin)
            return;

        commands.Register(builder => builder
            .Literal("server")
            .Then(builder => builder
                .Argument("server", Arguments.GreedyString())
                .Suggests(SuggestServer)
                .Executes(ChangeServerAsync))
            .Executes(ChangeServerAsync));
    }

    public async ValueTask<int> ChangeServerAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Source is not IPlayer player)
        {
            await context.Source.SendMessageAsync("This command can be executed only by a player", cancellationToken);
            return 1;
        }

        var previousServer = player.Server;
        var targetServer = context.TryGetArgument<string>("server", out var serverText) switch
        {
            true when servers.All.FirstOrDefault(server => server.Name.Equals(serverText, StringComparison.CurrentCultureIgnoreCase)) is { } found => found,
            _ => servers.All.Except([previousServer]).ElementAt(Random.Shared.Next(servers.All.Count() - 1))
        };

        if (targetServer is null)
            return 1;

        var result = await links.ConnectAsync(player, targetServer, cancellationToken);

        if (result is ConnectionResult.NotConnected)
            return 1;

        var currentServer = player.Link?.Server ?? throw new InvalidOperationException("Player is not linked to any server after successful redirection.");

        if (previousServer == currentServer)
            return 0;

        player.Context.Logger.LogInformation("Redirected from server {PreviousServer} to server {CurrentServer}", previousServer, currentServer);

        return 0;
    }

    private Suggestions SuggestServer(CommandContext context, SuggestionsBuilder builder)
    {
        return servers.All
            .Select(server => server.Name)
            .Aggregate(builder, (suggestionsBuilder, name) => suggestionsBuilder.Suggest(name))
            .Build();
    }
}
