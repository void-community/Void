using Nito.AsyncEx;
using Void.Minecraft.Commands.Brigadier;
using Void.Minecraft.Commands.Brigadier.Context;
using Void.Minecraft.Commands.Brigadier.Extensions;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Plugins;

namespace Void.Proxy.Plugins;

/// <summary>
/// Lists plugin containers and loads, unloads and reloads them by name with <c>/plugins</c>. The proxy registers it rather than a plugin, so the
/// console keeps it whichever plugins are loaded.
/// </summary>
public class PluginsCommand(ILogger<PluginsCommand> logger, IPluginService plugins, ICommandService commands)
{
    private const string NameArgument = "name";

    // One operation at a time, so concurrent requests can't unload a container twice or load one while it unloads.
    private readonly AsyncLock _lock = new();

    public void Register()
    {
        commands.Register(builder => builder
            .Literal("plugins")
            .Executes(ListAsync)
            .Then(builder => builder
                .Literal("load")
                .Then(builder => builder
                    .Argument(NameArgument, Arguments.String())
                    .Executes(LoadAsync)))
            .Then(builder => builder
                .Literal("unload")
                .Then(builder => builder
                    .Argument(NameArgument, Arguments.String())
                    .Executes(UnloadAsync)))
            .Then(builder => builder
                .Literal("reload")
                .Then(builder => builder
                    .Argument(NameArgument, Arguments.String())
                    .Executes(ReloadAsync))));
    }

    private async ValueTask<int> ListAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Source is IPlayer player)
        {
            await player.SendChatMessageAsync("Loaded plugins:", cancellationToken);

            foreach (var name in plugins.Containers)
                await player.SendChatMessageAsync($"&7- &b{name}", cancellationToken);
        }
        else
        {
            logger.LogInformation("Loaded plugins:");

            foreach (var name in plugins.Containers)
                logger.LogInformation("- {Name}", name);
        }

        return 0;
    }

    private async ValueTask<int> LoadAsync(CommandContext context, CancellationToken cancellationToken)
    {
        using var _ = await _lock.LockAsync(cancellationToken);

        if (await FindAsync(context, plugins.LoadableContainers, cancellationToken) is not { } name)
            return 1;

        if (plugins.Containers.Contains(name))
        {
            await AnswerAsync(context, $"Plugin container {name} is already loaded", succeeded: false, cancellationToken);
            return 1;
        }

        logger.LogInformation("Loading {ContainerName} container requested by {Source}", name, context.Source);
        return await LoadContainerAsync(context, name, "Loaded", cancellationToken);
    }

    private async ValueTask<int> UnloadAsync(CommandContext context, CancellationToken cancellationToken)
    {
        using var _ = await _lock.LockAsync(cancellationToken);

        if (await FindAsync(context, plugins.Containers, cancellationToken) is not { } name)
            return 1;

        logger.LogInformation("Unloading {ContainerName} container requested by {Source}", name, context.Source);

        if (!await UnloadContainerAsync(context, name, cancellationToken))
            return 1;

        await AnswerAsync(context, $"Unloaded {name}", succeeded: true, cancellationToken);
        return 0;
    }

    private async ValueTask<int> ReloadAsync(CommandContext context, CancellationToken cancellationToken)
    {
        using var _ = await _lock.LockAsync(cancellationToken);

        if (await FindAsync(context, plugins.Containers, cancellationToken) is not { } name)
            return 1;

        logger.LogInformation("Reloading {ContainerName} container requested by {Source}", name, context.Source);

        if (!await UnloadContainerAsync(context, name, cancellationToken))
            return 1;

        return await LoadContainerAsync(context, name, "Reloaded", cancellationToken);
    }

    private async ValueTask<bool> UnloadContainerAsync(CommandContext context, string name, CancellationToken cancellationToken)
    {
        try
        {
            await plugins.UnloadContainerAsync(name, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Plugin container {ContainerName} couldn't be unloaded", name);
            await AnswerAsync(context, $"Plugin container {name} couldn't be unloaded: {exception.Message}", succeeded: false, cancellationToken);
            return false;
        }
    }

    private async ValueTask<int> LoadContainerAsync(CommandContext context, string name, string verb, CancellationToken cancellationToken)
    {
        try
        {
            await plugins.LoadContainerAsync(name, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Plugin container {ContainerName} couldn't be loaded", name);
            await AnswerAsync(context, $"Plugin container {name} couldn't be loaded: {exception.Message}", succeeded: false, cancellationToken);
            return 1;
        }

        // A container that holds no plugins, such as a library, isn't kept.
        if (!plugins.Containers.Contains(name))
        {
            await AnswerAsync(context, $"Plugin container {name} has no plugins to load", succeeded: false, cancellationToken);
            return 1;
        }

        await AnswerAsync(context, $"{verb} {name}", succeeded: true, cancellationToken);
        return 0;
    }

    /// <summary>
    /// Finds the container a name means: the one named exactly so, ignoring case, or else the only one whose name contains it, such as
    /// <c>essentials</c> for <c>Void.Proxy.Plugins.Essentials.dll</c>. Answers why when none or several match.
    /// </summary>
    private async ValueTask<string?> FindAsync(CommandContext context, IEnumerable<string> containers, CancellationToken cancellationToken)
    {
        var name = context.GetArgument<string>(NameArgument);
        var candidates = containers.ToArray();

        if (candidates.FirstOrDefault(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } exactMatch)
            return exactMatch;

        var matches = candidates.Where(candidate => candidate.Contains(name, StringComparison.OrdinalIgnoreCase)).ToArray();

        switch (matches.Length)
        {
            case 1:
                return matches[0];
            case 0:
                await AnswerAsync(context, $"No plugin container matches {name}", succeeded: false, cancellationToken);
                return null;
            default:
                await AnswerAsync(context, $"{name} matches several plugin containers: {string.Join(", ", matches)}", succeeded: false, cancellationToken);
                return null;
        }
    }

    private async ValueTask AnswerAsync(CommandContext context, string message, bool succeeded, CancellationToken cancellationToken)
    {
        if (context.Source is IPlayer player)
            await player.SendChatMessageAsync(message, cancellationToken);
        else if (succeeded)
            logger.LogInformation("{Message}", message);
        else
            logger.LogWarning("{Message}", message);
    }
}
