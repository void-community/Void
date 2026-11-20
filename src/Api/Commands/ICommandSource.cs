namespace Void.Proxy.Api.Commands;

/// <summary>
/// Represents an object that can act as the source of command execution and command completion requests.
/// </summary>
/// <remarks>
/// Implementations are used by the command pipeline to evaluate permissions, execute handlers, and produce suggestions.
/// </remarks>
public interface ICommandSource
{
    /// <summary>
    /// Sends a message to the source where it reads it, such as the chat of a player or the log of the console.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Token used to cancel sending.</param>
    /// <returns>A task that completes when the message is sent.</returns>
    public ValueTask SendMessageAsync(string message, CancellationToken cancellationToken = default);
}
