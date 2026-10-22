using System.Security.Cryptography;
using System.Text;

using Void.Client.Failures;

using File = System.IO.File;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    private static readonly string JvmAgentCacheDirectory = Path.Combine(Path.GetTempPath(), path2: "void-client", path3: "jvm-agents");

    // Native agents load as Java's -agentpath option loads them, and Java agents, which are jars, as its -javaagent option does.
    private static readonly (string Variable, string Option)[] JvmAgentVariables = [("JVM_AGENTS", "-agentpath"), ("JAVA_AGENTS", "-javaagent")];

    /// <summary>
    /// Finds or downloads the native JVM agents that <c>JVM_AGENTS</c> lists and the Java agents that <c>JAVA_AGENTS</c> lists, and returns the
    /// arguments that load them into the game.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> CreateJvmAgentArgumentsAsync(
        HttpClient httpClient,
        Func<string, string?> getVariable,
        string cacheDirectory,
        TimeSpan retryDelay,
        CancellationToken cancellationToken
    )
    {
        List<string> arguments = [];

        foreach ((string variable, string option) in JvmAgentVariables)
        {
            foreach (string agent in SplitList(getVariable(variable)))
            {
                string path = await ResolveJvmAgentAsync(httpClient, agent, cacheDirectory, retryDelay, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                arguments.Add($"--jvm-arg={option}:{path}");
            }
        }

        return arguments;
    }

    /// <summary>
    /// Returns the path of a JVM agent in the container: an absolute path as it is, or an HTTP or HTTPS URL downloaded into the cache directory,
    /// unless an earlier start already downloaded it.
    /// </summary>
    internal static async Task<string> ResolveJvmAgentAsync(HttpClient httpClient, string agent, string cacheDirectory, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        if (Path.IsPathRooted(agent))
            return File.Exists(agent) ? agent : throw JvmAgentFailure($"JVM agent {agent} doesn't exist");

        if (!Uri.TryCreate(agent, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw JvmAgentFailure($"JVM agent {agent} is neither an absolute path nor an HTTP or HTTPS URL");

        // Every URL downloads into a directory of its own, so agents with the same file name from different URLs don't replace each other.
        string directory = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        string fileName = Path.GetFileName(uri.AbsolutePath) is { Length: > 0 } name ? name : "agent";
        string path = Path.Combine(cacheDirectory, directory, fileName);

        if (File.Exists(path))
            return path;

        try
        {
            await DownloadFileAsync(httpClient, uri, path, retryDelay, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw JvmAgentFailure($"Downloading JVM agent {uri} failed: {exception.Message}", exception, IsUpstreamUnavailable(exception, cancellationToken));
        }

        return path;
    }

    /// <summary>
    /// Splits a list as itzg's image does: entries are separated by commas or newlines, anything after <c>#</c> is a comment, and entries are
    /// trimmed, with blank ones left out.
    /// </summary>
    internal static IReadOnlyList<string> SplitList(string? value)
    {
        return [.. (value ?? string.Empty).Split([',', '\n']).Select(static entry => entry.Split(separator: '#')[0].Trim()).Where(static entry => entry.Length > 0)];
    }

    private static async Task<IReadOnlyList<string>> CreateJvmAgentArgumentsAsync(CancellationToken cancellationToken)
    {
        bool hasAgents = Array.Exists(JvmAgentVariables, static agentVariable => SplitList(Environment.GetEnvironmentVariable(agentVariable.Variable)).Count > 0);

        if (!hasAgents)
            return [];

        using HttpClient httpClient = CreateUpstreamHttpClient();

        return await CreateJvmAgentArgumentsAsync(httpClient, Environment.GetEnvironmentVariable, JvmAgentCacheDirectory, UpstreamRetryDelay, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private static GameClientException JvmAgentFailure(string message, Exception? innerException = null, bool unavailable = false)
    {
        string code = unavailable ? "client.install.unavailable" : "client.install.failed";

        return new GameClientException(code, operation: "install", stage: "agent", message, innerException);
    }
}
