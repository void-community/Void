using Microsoft.Extensions.Logging;
using Void.Minecraft.Commands.Brigadier.Tree.Nodes;
using Void.Minecraft.Components.Text;
using Void.Minecraft.Components.Text.Properties.Content;
using Void.Minecraft.Events.Chat;
using Void.Minecraft.Network;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Commands;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Players;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;

namespace Void.Proxy.Plugins.Common.Services.Commands;

public abstract class AbstractCommandService(ILogger logger, IEventService events, ICommandService commands) : IPluginCommonService
{
    [Subscribe]
    public async ValueTask OnCommandsMessageReceived(MessageReceivedEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        switch (@event.Message)
        {
            case CommandsPacket packet:
                if (packet.RootNode is null)
                {
                    logger.LogWarning(packet.FallbackReason, "Player {Player} will have no commands suggestions due to inability to parse the command tree", @event.Player);
                    
                    var unavailableCommandsEvent = new UnavailableCommandsEvent(@event.Link, @event.Player, packet.FallbackReason);
                    await events.ThrowAsync(unavailableCommandsEvent, cancellationToken);
                    
                    if (unavailableCommandsEvent.CustomCommandNode is not null)
                        packet.RootNode = unavailableCommandsEvent.CustomCommandNode;
                }
                else
                {
                    foreach (var (node, reason) in CommandsPacket.RemoveUnserializableArguments(packet.RootNode, @event.Player.ProtocolVersion))
                        logger.LogWarning(reason, "Player {Player} will not see command argument {Argument} because it can't be encoded for their protocol version", @event.Player, node.Name);

                    await events.ThrowAsync(new AvailableCommandsEvent(@event.Link, @event.Player, packet.RootNode), cancellationToken);
                }

                break;
            
            case CommandSuggestionsRequestPacket packet:
                // TODO: Suggest player names if completing not command
                var isCommand = packet.AssumeCommand == true || packet.Command.StartsWith('/');

                var suggestions = await commands.SuggestAsync(packet.Command.TrimStart('/'), @event.Player, cancellationToken);
                var materialized = suggestions.ToArray();
                
                var startPosition = materialized.Select(suggestion => suggestion.Start).DefaultIfEmpty(-1).Min();
                var length = packet.Command.Length - startPosition - 1;
                var offers = materialized.ToDictionary(suggestion => suggestion.Text, suggestion => string.IsNullOrWhiteSpace(suggestion.Tooltip) ? null : Component.Default with { Content = new TextContent(suggestion.Tooltip)});
                
                if (offers.Count is 0)
                    return;

                @event.Cancel();
                
                await @event.Player.SendPacketAsync(new CommandSuggestionsResponsePacket
                {
                    TransactionId = packet.TransactionId,
                    Start = startPosition + 1,
                    Length = length,
                    Offers = offers
                }, cancellationToken);
                break;
        }
    }

    [Subscribe]
    public async ValueTask OnAvailableCommands(AvailableCommandsEvent @event, CancellationToken cancellationToken)
    {
        await commands.CopyToAsync(@event.Node, @event.Player, cancellationToken);
    }

    public async ValueTask<bool> HandleCommandAsync(ILink link, string command, bool isSigned, CancellationToken cancellationToken)
    {
        var cancelled = await events.ThrowWithResultAsync(new ChatCommandEvent(link, link.Player, command, isSigned), cancellationToken);

        if (isSigned && cancelled)
            logger.LogDebug("Signed command cannot be canceled: {Command}", command);

        return cancelled;
    }

    [Subscribe]
    public async ValueTask OnChatCommand(ChatCommandEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        @event.Result = await commands.ExecuteAsync(@event.Player, @event.Command, Side.Client, cancellationToken) is CommandExecutionResult.Executed;
    }

    [Subscribe]
    public async ValueTask OnChatMessageSend(ChatCommandSendEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Origin is not Side.Proxy)
            return;

        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        await SendChatCommandAsync(@event.Player, @event.Command, cancellationToken);
    }

    protected abstract ValueTask<bool> SendChatCommandAsync(IPlayer player, string command, CancellationToken cancellationToken);
    protected abstract bool IsSupportedVersion(ProtocolVersion version);
}
