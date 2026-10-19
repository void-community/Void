using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Void.Minecraft.Links.Extensions;
using Void.Minecraft.Mojang;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Binary;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Authentication;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Events.Proxy;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Api.Servers;
using Void.Proxy.Plugins.Common.Events;
using Void.Proxy.Plugins.Common.Extensions;
using Void.Proxy.Plugins.Common.Mojang;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;

namespace Void.Proxy.Plugins.Common.Services.Authentication;

public abstract class AbstractAuthenticationService(IEventService events, IPlayerService players, IServerService servers, IConsoleService console, IDependencyService dependencies) : IPluginCommonService
{
    public static readonly Option<string[]> OverridesOption = new("--override", "-o") { Description = "Register an additional server override to redirect players based on hostname they are connecting with.\nExample:\n--ignore-file-servers\n--server 127.0.0.1:25565\n--override vanilla.example.org=args-server-1\nIf you configured server in file:\n--override vanilla.example.org=lobby" };

    [Subscribe]
    public void OnProxyStarting(ProxyStartingEvent _)
    {
        dependencies.Register(services => services.AddSingleton<IMojangService, MojangService>());

        OverridesOption.Validators.Add(result =>
        {
            foreach (var option in result.GetValueOrDefault<string[]>())
            {
                var parts = option.Split('=');

                if (parts.Length is 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                    continue;

                result.AddError($"Override \"{option}\" must be in the format <hostname>=<server-name>.");
                return;
            }
        });

        console.EnsureOptionDiscovered(OverridesOption);
    }

    [Subscribe]
    public async ValueTask OnPlayerConnected(PlayerConnectedEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        var channel = await @event.Player.GetChannelAsync(cancellationToken);

        var handshake = await channel.ReceivePacketAsync<HandshakePacket>(cancellationToken);
        await events.ThrowAsync(new MessageReceivedEvent(Side.Client, Side.Client, Side.Proxy, Direction.Serverbound, handshake, Link: null!, @event.Player), cancellationToken);

        // Player is anonymous
        @event.Result = handshake.IsStatusQuery;
        @event.ConnectedWith = new RuntimeServer(nameof(@event.ConnectedWith), handshake.ServerAddress, handshake.ServerPort);
    }

    [Subscribe]
    public async ValueTask OnPlayerSearchServer(PlayerSearchServerEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        if (@event.Result is not null)
            return;

        var serverOverrides = servers.All.Where(server => !string.IsNullOrWhiteSpace(server.Override)).Select(server => $"{server.Override}={server.Name}");
        var optionOverrides = console.GetOptionValue(OverridesOption) ?? [];

        foreach (var value in serverOverrides.Concat(optionOverrides))
        {
            var parts = value.Split("=");

            if (parts.Length is not 2)
                continue;

            var overrideName = parts[0];
            var serverName = parts[1];

            if (!string.Equals(@event.ConnectedWith?.Host, overrideName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!servers.TryGetByName(serverName, out var server))
            {
                @event.Player.Logger.LogWarning("Failed to find server {Server} from redirection override name {Name}", serverName, overrideName);
                continue;
            }

            @event.Result = server;
            @event.Player.Logger.LogTrace("Server overridden to {Server}", server.Name);
            break;
        }
    }

    [Subscribe]
    public async ValueTask OnAuthenticationStarting(AuthenticationStartingEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        if (!await @event.Player.IsProtocolSupportedAsync(cancellationToken))
        {
            @event.Result = AuthenticationSide.Server;
            return;
        }

        @event.Result = AuthenticationSide.Proxy;

        if (!await IsPlayerAuthenticatedAsync(@event.Player, cancellationToken))
            return;

        if (await IsPlayerPlayingAsync(@event.Player, cancellationToken))
            await FinishPlayingAsync(@event.Link, cancellationToken);
    }

    [Subscribe]
    public async ValueTask OnLinkStarted(LinkStartedEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        if (!@event.IsFirstLink || !@event.IsAnonymous)
            return;

        await @event.Link.SendPacketAsync(new HandshakePacket { NextState = 1, ProtocolVersion = @event.Player.ProtocolVersion.Value, ServerAddress = @event.Link.Server.Host, ServerPort = (ushort)@event.Link.Server.Port }, cancellationToken);
        await @event.Player.SetPhaseAsync(@event.Link, Side.Server, Phase.Status, @event.Link.ServerChannel, cancellationToken);
    }

    [Subscribe]
    public async ValueTask OnPlayerKickEvent(PlayerKickEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        if (@event.Player.Phase is not Phase.Handshake)
            return;

        var channel = await @event.Player.GetChannelAsync(cancellationToken);

        if (channel.IsPausedRead)
            channel.Resume(Operation.Read);

        var handshake = await channel.ReceivePacketAsync<HandshakePacket>(cancellationToken);

        if (!handshake.IsStatusQuery)
            await @event.Player.SetPhaseAsync(link: null, Side.Client, Phase.Login, await @event.Player.GetChannelAsync(cancellationToken), cancellationToken);

        @event.Result = true;
    }

    [Subscribe]
    public async ValueTask OnAuthenticationStarted(AuthenticationStartedEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Side is AuthenticationSide.Server)
        {
            @event.Result = AuthenticationResult.Authenticated;
            return;
        }

        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        if (!await @event.Player.IsProtocolSupportedAsync(cancellationToken))
            return;

        var playerAuthenticationResult = await AuthenticatePlayerAsync(@event.Link, cancellationToken);
        var serverAuthenticationResult = playerAuthenticationResult;
        var handshakeBuildEventResult = new HandshakeBuildEventResult(NextState: 2);

        if (playerAuthenticationResult.IsAuthenticated)
        {
            handshakeBuildEventResult = await events.ThrowWithResultAsync(new HandshakeBuildEvent(@event.Player, @event.Link), cancellationToken) ?? new(NextState: 2);
            serverAuthenticationResult = await AuthenticateServerAsync(@event.Link, playerAuthenticationResult, handshakeBuildEventResult, cancellationToken);
        }

        if (serverAuthenticationResult.IsAuthenticated)
        {
            if (playerAuthenticationResult != AuthenticationResult.AlreadyAuthenticated)
                await FinishPlayerLoginAsync(@event.Link, cancellationToken);

            await FinishServerAuthenticationAsync(@event.Link, playerAuthenticationResult, handshakeBuildEventResult, cancellationToken);
        }

        @event.Result = serverAuthenticationResult;
    }

    protected bool IsAlreadyOnline(string username)
    {
        return players.All.Any(player => player.IsMinecraft && player.Profile?.Username == username);
    }

    protected async ValueTask<AuthenticationResult> AuthenticatePlayerAsync(ILink link, CancellationToken cancellationToken)
    {
        if (await IsPlayerAuthenticatedAsync(link.Player, cancellationToken))
            return AuthenticationResult.AlreadyAuthenticated;

        if (!await StartPlayerLoginAsync(link, cancellationToken))
            return AuthenticationResult.NotAuthenticatedPlayer with { Message = "You are already online on this proxy." };

        if (!await events.ThrowWithResultAsync(new PlayerVerifyingEncryptionEvent(link.Player, link), cancellationToken))
            return AuthenticationResult.NotAuthenticatedPlayer with { Message = "Your encryption cannot be verified." };

        if (!await VerifyMojangProfile(link.Player, cancellationToken))
            return AuthenticationResult.NotAuthenticatedPlayer with { Message = "Mojang session server rejected your session." };

        await events.ThrowAsync(new PlayerVerifiedEncryptionEvent(link.Player, link), cancellationToken);

        return AuthenticationResult.Authenticated;
    }

    protected async ValueTask<AuthenticationResult> AuthenticateServerAsync(ILink link, AuthenticationResult authenticationResult, HandshakeBuildEventResult handshakeBuildEventResult, CancellationToken cancellationToken)
    {
        if (!link.Player.IsMinecraft)
            return AuthenticationResult.Authenticated;

        // Server channel might be closed very early, skip authentication, ILink should stop itself as soon as it executes
        if (!link.ServerChannel.IsAlive)
            return AuthenticationResult.Authenticated;

        if (link.Player.Profile is null)
            throw new InvalidOperationException("Player should be authenticated before Server");

        await HandshakeWithServerAsync(link, handshakeBuildEventResult.Packet, handshakeBuildEventResult.NextState, cancellationToken);

        switch (handshakeBuildEventResult)
        {
            case { NextState: 2 or 3 }:
                await link.Player.SetPhaseAsync(link, Side.Server, Phase.Login, link.ServerChannel, cancellationToken);
                break;
        }

        await StartServerLoginAsync(link, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var packet = await link.ReceiveCancellablePacketAsync<IMinecraftClientboundPacket>(cancellationToken);

            if (packet is null)
                continue;

            if (packet is IMinecraftBinaryMessage message)
            {
                // TODO: What should we do with unknown messages that expect a response?
                // Client is already in the Play state, so it cannot answer these messages
                // Possible solution is to handle these packets in some "healing" service?
                // Happens when Velocity Forwarding plugin is not loaded
                await link.SendPacketAsync(message, cancellationToken);
                throw new NotSupportedException("This packet is not processed by anyone and might be waiting for a response");
                // continue;
            }

            var result = await HandleServerPacketAsync(link, packet, cancellationToken);

            if (result == AuthenticationResult.NoResult)
                continue;
            else if (result == AuthenticationResult.Authenticated)
                break;
            else
                return AuthenticationResult.NotAuthenticatedServer with { Message = "Disconnected by server." };
        }

        return AuthenticationResult.Authenticated;
    }

    protected virtual ValueTask FinishPlayingAsync(ILink link, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    protected virtual ValueTask FinishServerAuthenticationAsync(ILink link, AuthenticationResult authenticationResult, HandshakeBuildEventResult handshakeBuildEventResult, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    protected static async ValueTask<bool> VerifyMojangProfile(IPlayer player, CancellationToken cancellationToken)
    {
        if (!player.IsMinecraft)
            return false;

        var mojang = player.Context.Services.GetRequiredService<IMojangService>();

        if (await mojang.VerifyAsync(player, cancellationToken) is not { } onlineProfile)
            return false;

        player.Profile = onlineProfile;
        return true;
    }

    protected abstract bool IsSupportedVersion(ProtocolVersion version);

    protected abstract ValueTask<bool> IsPlayerAuthenticatedAsync(IPlayer player, CancellationToken cancellationToken);

    protected abstract ValueTask<bool> IsPlayerPlayingAsync(IPlayer player, CancellationToken cancellationToken);

    protected abstract ValueTask<bool> StartPlayerLoginAsync(ILink link, CancellationToken cancellationToken);

    protected abstract ValueTask FinishPlayerLoginAsync(ILink link, CancellationToken cancellationToken);

    protected abstract ValueTask HandshakeWithServerAsync(ILink link, IMinecraftServerboundPacket? packet, int nextState, CancellationToken cancellationToken);

    protected abstract ValueTask StartServerLoginAsync(ILink link, CancellationToken cancellationToken);

    protected abstract ValueTask<AuthenticationResult> HandleServerPacketAsync(ILink link, IMinecraftClientboundPacket packet, CancellationToken cancellationToken);
}
