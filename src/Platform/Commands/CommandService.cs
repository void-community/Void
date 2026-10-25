using System.Reflection;
using Void.Minecraft.Commands.Brigadier;
using Void.Minecraft.Commands.Brigadier.Context;
using Void.Minecraft.Commands.Brigadier.Exceptions;
using Void.Minecraft.Commands.Brigadier.Suggestion;
using Void.Minecraft.Commands.Brigadier.Tree;
using Void.Minecraft.Commands.Brigadier.Tree.Nodes;
using Void.Minecraft.Events.Chat;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Extensions.Reflection;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Players;
using StringReader = Void.Minecraft.Commands.Brigadier.StringReader;

namespace Void.Proxy.Commands;

public class CommandService(ILogger<ICommandService> logger, IEventService events) : ICommandService, IEventListener
{
    private readonly CommandDispatcher _dispatcher = new();

    public ICommandDispatcher Dispatcher => _dispatcher;

    public async ValueTask<CommandExecutionResult> ExecuteAsync(ICommandSource source, string command, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(source, command, Side.Proxy, cancellationToken);
    }

    public async ValueTask<CommandExecutionResult> ExecuteAsync(ICommandSource source, string command, Side origin, CancellationToken cancellationToken = default)
    {
        if (command.StartsWith('/'))
            command = command[1..];

        try
        {
            var player = source as IPlayer;
            player?.Logger.LogInformation("Entered command: {Command}", command);

            var results = await _dispatcher.ParseAsync(new StringReader(command), source, cancellationToken);

            if (results.Reader.CanRead || ContextChain.TryFlatten(results.Context.Build(results.Reader.Source)) is null)
            {
                if (player is null)
                    return CommandExecutionResult.Executed;

                await events.ThrowAsync(new ChatCommandSendEvent(player, command, origin), cancellationToken);
                return CommandExecutionResult.Forwarded;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // TODO: Handle error codes appropriately
                    _ = await _dispatcher.ExecuteAsync(results, cancellationToken);
                }
                catch (StreamException)
                {
                    // Ignore
                }
                catch (Exception exception)
                {
                    logger.LogCritical(exception, "An exception occurred while executing command {Command} from {Source}", command, source);
                }
            }, cancellationToken);

            return CommandExecutionResult.Executed;
        }
        catch (CommandSyntaxException exception)
        {
            if (source is IPlayer player)
                await player.SendChatMessageAsync(exception.Message, cancellationToken);

            return CommandExecutionResult.Exception;
        }
    }

    public async ValueTask<IEnumerable<ICommandSuggestion>> SuggestAsync(string input, ICommandSource source, CancellationToken cancellationToken = default)
    {
        var suggestions = await _dispatcher.SuggestAsync(input, source, cancellationToken);
        return suggestions.All.Select(suggestion => new CommandSuggestion(suggestion.Range.Start, suggestion.Text, suggestion.Tooltip?.Value));
    }

    public async ValueTask CopyToAsync(ICommandNode commandNode, ICommandSource commandSource, CancellationToken cancellationToken = default)
    {
        if (commandNode is not RootCommandNode destinationRoot)
            throw new ArgumentException(@"Command node must be a root node", nameof(commandNode));

        foreach (var node in _dispatcher.Root.Children)
        {
            if (!await node.CanUseAsync(commandSource, cancellationToken))
                continue;

            destinationRoot.RemoveChild(node.Name);
            destinationRoot.AddChild(node);
        }
    }

    [Subscribe]
    public void OnPluginUnloading(PluginUnloadingEvent @event)
    {
        lock (this)
        {
            ClearByAssembly(@event.Plugin.GetType().Assembly);
            RemoveUnusableNodes(_dispatcher.Root);
        }
    }

    // A command left with nothing to run once its plugin's handlers are gone is removed, so the plugin registers it anew as it loads again,
    // with its suggestions, rather than merging into the remains of the old one.
    private static bool RemoveUnusableNodes(CommandNode node)
    {
        foreach (var child in node.Children.ToArray())
        {
            if (!RemoveUnusableNodes(child))
                node.RemoveChild(child.Name);
        }

        return node.Executor is not null || node.RedirectTarget is not null || node.Children.Any();
    }

    private void ClearByAssembly(Assembly assembly)
    {
        foreach (var node in _dispatcher.All())
        {
            if (node.Executor is not null)
            {
                foreach (var invocation in node.Executor.GetInvocationList().Cast<CommandExecutor>())
                {
                    Delegate command = invocation.Target?.GetFieldValue("command") switch
                    {
                        CommandExecutorSync value => value,
                        CommandExecutor value => value,
                        _ => invocation
                    };

                    if (!IsFromAssembly(command, assembly))
                        continue;

                    node.Executor -= invocation;
                }
            }

            if (node is ArgumentCommandNode { CustomSuggestions: not null } argumentCommandNode)
            {
                foreach (var invocation in argumentCommandNode.CustomSuggestions.GetInvocationList().Cast<SuggestionProvider>())
                {
                    Delegate provider = invocation.Target?.GetFieldValue("provider") switch
                    {
                        SuggestionProviderSync value => value,
                        SuggestionProvider value => value,
                        _ => invocation
                    };

                    if (!IsFromAssembly(provider, assembly))
                        continue;

                    argumentCommandNode.CustomSuggestions -= invocation;
                }
            }
        }
    }

    // A static method has no target, so its declaring type tells where it's from, as it does for a lambda's closure.
    private static bool IsFromAssembly(Delegate value, Assembly assembly)
    {
        return value.Target?.GetType().Assembly == assembly || value.Method.DeclaringType?.Assembly == assembly;
    }

    private record CommandSuggestion(int Start, string Text, string? Tooltip) : ICommandSuggestion;
}
