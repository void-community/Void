using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Void.Minecraft.Components.Text;
using Void.Minecraft.Events;
using Void.Minecraft.Events.Chat;
using Void.Minecraft.Network;
using Void.Minecraft.Players;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;
using Void.Proxy.Api.Servers;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Services.Lifecycle;
using Xunit;
using static Void.UnitTests.Players.PlayerServiceDisconnectTests;

namespace Void.UnitTests.Services.Lifecycle;

public class ServerKickTests
{
    [Fact]
    public async Task PlayerThatItsServerKicksReadsWhyOnTheNextServer()
    {
        var test = new Proxy();
        var kick = test.ServerKick("Kicked by an operator");

        await test.Service.OnMessageReceived(kick, CancellationToken.None);
        await test.Service.OnPlayerJoinedServer(new PlayerJoinedServerEvent(test.Player, test.Server, test.Link, IsRedirected: true), CancellationToken.None);

        Assert.True(kick.Result);
        Assert.Equal("You were kicked from lobby: Kicked by an operator", Assert.Single(test.ChatMessages).AsText);
        Assert.Empty(test.Kicks);
    }

    [Fact]
    public async Task PlayerThatNoOtherServerTakesIsKickedForTheReasonItsServerGave()
    {
        var test = new Proxy();

        await test.Service.OnMessageReceived(test.ServerKick("Kicked by an operator"), CancellationToken.None);
        await test.Service.OnPlayerKickEvent(new PlayerKickEvent(test.Player, "No available servers to connect you to."), CancellationToken.None);

        Assert.Equal("Kicked by an operator", Assert.Single(test.Kicks).AsText);
        Assert.Empty(test.ChatMessages);
    }

    [Fact]
    public async Task ServerThatRefusesAJoiningPlayerLeavesTheKickToLogin()
    {
        var test = new Proxy { LinkAlive = false };
        var refusal = test.ServerKick("You are banned from this server");

        await test.Service.OnMessageReceived(refusal, CancellationToken.None);
        await test.Service.OnPlayerKickEvent(new MinecraftPlayerKickEvent(test.Player, "You are banned from this server"), CancellationToken.None);

        Assert.False(refusal.Result);
        Assert.Equal("You are banned from this server", Assert.Single(test.Kicks).AsText);
    }

    [Fact]
    public async Task PlayerThatJoinsAnotherServerWithoutAKickReadsNothing()
    {
        var test = new Proxy();

        await test.Service.OnPlayerJoinedServer(new PlayerJoinedServerEvent(test.Player, test.Server, test.Link, IsRedirected: true), CancellationToken.None);

        Assert.Empty(test.ChatMessages);
    }

    private sealed class Proxy
    {
        public Proxy()
        {
            var events = StandIn.Create<IEventService>((_, arguments) =>
            {
                if (arguments is [ChatMessageSendEvent chatMessage, ..])
                    ChatMessages.Add(chatMessage.Text);

                return null;
            });

            var links = StandIn.Create<ILinkService>((method, arguments) =>
            {
                if (method.Name is not nameof(ILinkService.TryGetLink))
                    return null;

                arguments[1] = Link;
                return true;
            });

            var services = StandIn.Create<IServiceProvider>((_, arguments) => arguments[0] switch
            {
                Type type when type == typeof(IEventService) => events,
                Type type when type == typeof(ILinkService) => links,
                Type type when type == typeof(ILoggerFactory) => NullLoggerFactory.Instance,
                _ => null
            });

            var context = StandIn.Create<IPlayerContext>((method, _) => method.Name is "get_Services" ? services : null);
            Player = new MinecraftPlayer(new TcpClient(), context, remoteEndPoint: "127.0.0.1:50000", DateTimeOffset.UtcNow, ProtocolVersion.MINECRAFT_1_21_4) { Phase = Phase.Play };
            Server = StandIn.Create<IServer>((method, _) => method.Name is "get_Name" ? "lobby" : null);
            Link = StandIn.Create<ILink>((method, _) => method.Name switch
            {
                "get_Player" => Player,
                "get_Server" => Server,
                "get_IsAlive" => LinkAlive,
                _ => null
            });

            Service = new RecordingLifecycleService(events, Kicks);
        }

        public List<Component> ChatMessages { get; } = [];
        public List<Component> Kicks { get; } = [];
        public bool LinkAlive { get; init; } = true;
        public MinecraftPlayer Player { get; }
        public IServer Server { get; }
        public ILink Link { get; }
        public AbstractLifecycleService Service { get; }

        public MessageReceivedEvent ServerKick(string reason)
        {
            return new MessageReceivedEvent(Side.Server, Side.Server, Side.Proxy, Direction.Clientbound, new JsonDisconnectPacket { Reason = reason }, Link, Player);
        }
    }

    private sealed class RecordingLifecycleService(IEventService events, List<Component> kicks) : AbstractLifecycleService(NullLogger.Instance, events)
    {
        protected override ValueTask EnableCompressionAsync(ILink link, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        protected override ValueTask<bool> KickPlayerAsync(IPlayer player, Component reason, CancellationToken cancellationToken)
        {
            kicks.Add(reason);
            return ValueTask.FromResult(true);
        }

        protected override ValueTask<bool> SendChatMessageAsync(IPlayer player, Component text, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(true);
        }

        protected override bool IsSupportedVersion(ProtocolVersion version)
        {
            return true;
        }
    }
}
