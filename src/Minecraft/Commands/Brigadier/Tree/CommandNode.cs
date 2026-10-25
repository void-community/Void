using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Void.Minecraft.Commands.Brigadier.Builder;
using Void.Minecraft.Commands.Brigadier.Context;
using Void.Minecraft.Commands.Brigadier.Suggestion;
using Void.Minecraft.Commands.Brigadier.Tree.Nodes;
using Void.Proxy.Api.Commands;

namespace Void.Minecraft.Commands.Brigadier.Tree;

/// <summary>Provides shared storage, traversal, access checks, and ambiguity analysis for command-tree nodes.</summary>
/// <param name="executor">The executor selected when parsing ends at this node.</param>
/// <param name="requirement">The source access requirement.</param>
/// <param name="redirectTarget">The redirect target.</param>
/// <param name="redirectModifier">The redirected-source modifier.</param>
/// <param name="isForks">Whether redirection forks execution.</param>
public abstract class CommandNode(CommandExecutor? executor = null, CommandRequirement? requirement = null, CommandNode? redirectTarget = null, RedirectModifier? redirectModifier = null, bool isForks = false) : ICommandNode
{
    private readonly Dictionary<string, CommandNode> _children = [];
    private readonly Dictionary<string, ArgumentCommandNode> _arguments = [];
    private readonly Dictionary<string, LiteralCommandNode> _literals = [];

    /// <summary>Gets or sets whether redirection forks execution.</summary>
    public bool IsForks { get; set; } = isForks;
    /// <summary>Gets or sets the source access requirement.</summary>
    public CommandRequirement? Requirement { get; set; } = requirement;
    /// <summary>Gets or sets the command executor.</summary>
    public CommandExecutor? Executor { get; set; } = executor;
    /// <summary>Gets or sets the redirect target.</summary>
    public CommandNode? RedirectTarget { get; set; } = redirectTarget;
    /// <summary>Gets or sets the redirected-source modifier.</summary>
    public RedirectModifier? RedirectModifier { get; set; } = redirectModifier;
    /// <summary>Gets the child nodes in dictionary enumeration order.</summary>
    public IEnumerable<CommandNode> Children => _children.Values;
    /// <summary>Gets the name used to index this node under its parent.</summary>
    public abstract string Name { get; }
    /// <summary>Gets the fragment displayed in command usage.</summary>
    public abstract string UsageText { get; }
    /// <summary>Gets representative inputs used for ambiguity analysis.</summary>
    public abstract IEnumerable<string> Examples { get; }
    /// <summary>Gets the key used when ordering nodes.</summary>
    protected abstract string SortedKey { get; }

    /// <summary>Adds or merges a child by name.</summary>
    /// <param name="node">The child to add.</param>
    /// <remarks>When a same-name child exists, a non-null executor and all grandchildren are merged into that existing node; other metadata is retained.</remarks>
    /// <exception cref="InvalidOperationException"><paramref name="node"/> is a root node.</exception>
    public void AddChild(CommandNode node)
    {
        if (node is RootCommandNode)
            throw new InvalidOperationException("Cannot add root node as child.");

        if (_children.TryGetValue(node.Name, out var child))
        {
            if (node.Executor is not null)
                child.Executor = node.Executor;

            foreach (var grandChild in node.Children)
                child.AddChild(grandChild);
        }
        else
        {
            _children[node.Name] = node;

            if (node is LiteralCommandNode literal)
                _literals[node.Name] = literal;
            else if (node is ArgumentCommandNode argument)
                _arguments[node.Name] = argument;
        }
    }

    /// <summary>Evaluates this node's access requirement.</summary>
    /// <param name="source">The command source.</param>
    /// <param name="cancellationToken">A token that may cancel the requirement.</param>
    /// <returns><see langword="true"/> when no requirement exists or it permits the source.</returns>
    public async ValueTask<bool> CanUseAsync(ICommandSource source, CancellationToken cancellationToken)
    {
        if (Requirement is null)
            return true;

        return await Requirement(source, cancellationToken);
    }

    /// <summary>Recursively reports sibling pairs whose examples are accepted by one another.</summary>
    /// <param name="consumer">The callback receiving the parent, child pair, and overlapping examples.</param>
    public void FindAmbiguities(AmbiguousConsumer consumer)
    {
        var matches = new HashSet<string>();

        foreach (var child in Children)
        {
            foreach (var sibling in Children)
            {
                if (child == sibling)
                    continue;

                foreach (var input in child.Examples)
                {
                    if (sibling.IsValidInput(input))
                        matches.Add(input);
                }

                if (matches.Count > 0)
                {
                    consumer(this, child, sibling, matches);
                    matches.Clear();
                }
            }

            child.FindAmbiguities(consumer);
        }
    }

    /// <summary>Gets a child by exact name.</summary>
    /// <param name="name">The child name.</param>
    /// <returns>The child node.</returns>
    /// <exception cref="KeyNotFoundException">No child has that name.</exception>
    public CommandNode GetChild(string name)
    {
        return _children[name];
    }

    /// <summary>Removes a child, which parsing then no longer matches.</summary>
    /// <param name="name">The child name.</param>
    /// <returns>Whether the node had a child of that name.</returns>
    public bool RemoveChild(string name)
    {
        _literals.Remove(name);
        _arguments.Remove(name);

        return _children.Remove(name);
    }

    /// <summary>Selects a same-text literal when present, otherwise returns argument children.</summary>
    /// <param name="reader">The reader used to inspect the next space-delimited token; its cursor is restored.</param>
    /// <returns>The literal singleton or all argument nodes.</returns>
    public IEnumerable<CommandNode> GetRelevantNodes(StringReader reader)
    {
        if (_literals.Count > 0)
        {
            var cursor = reader.Cursor;

            while (reader.CanRead && reader.Peek != ' ')
                reader.Skip();

            var text = reader.Source[cursor..reader.Cursor];
            reader.Cursor = cursor;

            if (_literals.TryGetValue(text, out var literal))
                return [literal];
            else
                return _arguments.Values;
        }
        else
        {
            return _arguments.Values;
        }
    }

    /// <summary>Creates a builder initialized from this node.</summary>
    /// <returns>The configured builder.</returns>
    public abstract IArgumentBuilder<CommandNode> CreateBuilder();
    /// <summary>Determines whether this node can consume a complete sample input.</summary>
    /// <param name="input">The sample input.</param>
    /// <returns>Whether the input is valid.</returns>
    public abstract bool IsValidInput(string input);
    /// <summary>Lists completions for this node.</summary>
    /// <param name="context">The parsed context.</param>
    /// <param name="builder">The suggestion builder.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The suggestions.</returns>
    public abstract ValueTask<Suggestions> ListSuggestionsAsync(CommandContext context, SuggestionsBuilder builder, CancellationToken cancellationToken);
    /// <summary>Parses this node and records its result in a context builder.</summary>
    /// <param name="reader">The input reader.</param>
    /// <param name="context">The parse context builder.</param>
    public abstract void Parse(StringReader reader, CommandContextBuilder context);
}
