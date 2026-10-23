using System;
using System.Collections.Generic;
using System.IO;
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
using Void.Proxy.Api.Network.Messages;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;
using Void.Proxy.Api.Servers;
using Void.Proxy.Plugins.Common.Network.Messages.Binary;
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
        test.Service.OnPlayerJoinedServer(new PlayerJoinedServerEvent(test.Player, test.Server, test.NextLink, IsRedirected: true));
        Assert.Empty(test.ChatMessages);

        await test.NextServerSendsAsync(JoinGameId);

        Assert.True(kick.Result);
        Assert.Equal("You were kicked from lobby: Kicked by an operator", Assert.Single(test.ChatMessages).AsText);
        Assert.Empty(test.Kicks);
    }

    [Fact]
    public async Task PlayerThatGetsTheWorldAfterJoiningReadsWhyOnceItHasTheWorld()
    {
        // From 1.20.2 the player joins as the proxy forwards the end of configuration, and servers such as NeoForge's send payloads before
        // the Join Game that gives the game a world to show chat in.
        var test = new Proxy { WorldMessageId = JoinGameId };

        await test.Service.OnMessageReceived(test.ServerKick("Kicked by an operator"), CancellationToken.None);
        test.Service.OnPlayerJoinedServer(new PlayerJoinedServerEvent(test.Player, test.Server, test.NextLink, IsRedirected: true));
        await test.NextServerSendsAsync(PayloadId);
        Assert.Empty(test.ChatMessages);

        await test.NextServerSendsAsync(JoinGameId);

        Assert.Equal("You were kicked from lobby: Kicked by an operator", Assert.Single(test.ChatMessages).AsText);
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

        test.Service.OnPlayerJoinedServer(new PlayerJoinedServerEvent(test.Player, test.Server, test.NextLink, IsRedirected: true));
        await test.NextServerSendsAsync(JoinGameId);

        Assert.Empty(test.ChatMessages);
    }

    [Fact]
    public async Task PlayerThatAnotherServerTookIsKickedForTheProxysOwnReason()
    {
        var test = new Proxy();

        await test.Service.OnMessageReceived(test.ServerKick("Kicked by an operator"), CancellationToken.None);
        test.Service.OnPlayerJoinedServer(new PlayerJoinedServerEvent(test.Player, test.Server, test.NextLink, IsRedirected: true));
        await test.Service.OnPlayerKickEvent(new PlayerKickEvent(test.Player, "The proxy is restarting"), CancellationToken.None);

        Assert.Equal("The proxy is restarting", Assert.Single(test.Kicks).AsText);
    }

    private const int JoinGameId = 0x2B;
    private const int PayloadId = 0x18;

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

            NextLink = StandIn.Create<ILink>((method, _) => method.Name switch
            {
                "get_Player" => Player,
                "get_IsAlive" => true,
                _ => null
            });

            Service = new RecordingLifecycleService(events, Kicks, message => WorldMessageId is not { } id || message is MinecraftBinaryPacket { Id: var messageId } && messageId == id);
        }

        public List<Component> ChatMessages { get; } = [];
        public List<Component> Kicks { get; } = [];
        public bool LinkAlive { get; init; } = true;
        public int? WorldMessageId { get; init; }
        public MinecraftPlayer Player { get; }
        public IServer Server { get; }
        public ILink Link { get; }
        public ILink NextLink { get; }
        public AbstractLifecycleService Service { get; }

        public MessageReceivedEvent ServerKick(string reason)
        {
            return new MessageReceivedEvent(Side.Server, Side.Server, Side.Proxy, Direction.Clientbound, new JsonDisconnectPacket { Reason = reason }, Link, Player);
        }

        public async Task NextServerSendsAsync(int id)
        {
            var message = new MinecraftBinaryPacket(id, new MemoryStream([1, 2, 3]));

            await Service.OnMessageReceived(new MessageReceivedEvent(Side.Server, Side.Server, Side.Proxy, Direction.Clientbound, message, NextLink, Player), CancellationToken.None);
            await Service.OnMessageSent(new MessageSentEvent(Side.Server, Side.Proxy, Side.Client, Direction.Clientbound, message, NextLink, Player), CancellationToken.None);
        }
    }

    private sealed class RecordingLifecycleService(IEventService events, List<Component> kicks, Func<INetworkMessage, bool> givesWorld) : AbstractLifecycleService(NullLogger.Instance, events)
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

        protected override bool GivesWorld(INetworkMessage message)
        {
            return givesWorld(message);
        }
    }
}
