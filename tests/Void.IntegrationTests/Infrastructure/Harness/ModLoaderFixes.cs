using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Void.IntegrationTests.Infrastructure.Exceptions;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>
/// The JVM agent from void-community/ModLoaderFixes, which fixes the bugs of Forge, NeoForge and Minecraft itself that games and servers
/// hit while they start, log players in or move between servers, such as Forge for Minecraft 1.7.2 not starting on Java 8u20 and later. It
/// leaves a game alone when the game has none of the code a fix targets, so every game and every server plays with it.
/// </summary>
internal static class ModLoaderFixes
{
    /// <summary>Where a game or server finds the agent in its container.</summary>
    public const string ContainerPath = "/opt/modloaderfixes/modloaderfixes.jar";

    // The Java agent of the latest release, a jar that loads with -javaagent on every architecture, which GitHub serves through its releases and
    // its API, whose front ends are down apart
    private const string AssetName = "modloaderfixes.jar";
    private static readonly Uri Url = new($"https://github.com/void-community/ModLoaderFixes/releases/latest/download/{AssetName}");
    private static readonly Uri LatestRelease = new("https://api.github.com/repos/void-community/ModLoaderFixes/releases/latest");

    // GitHub's release downloads are sometimes down for longer than a test waits for a server, so a run downloads the agent once, and waits for
    // it for longer, rather than every container downloading it as it starts
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);
    private static readonly Lazy<Task<byte[]>> Agent = new(DownloadAsync);

    /// <summary>Copies the agent into a container, whose game or server then loads it.</summary>
    public static async Task<ContainerBuilder> AddToAsync(ContainerBuilder builder, CancellationToken cancellationToken)
    {
        var agent = await Agent.Value.WaitAsync(cancellationToken);
        return builder.WithResourceMapping(agent, ContainerPath).WithEnvironment("JAVA_AGENTS", ContainerPath);
    }

    private static async Task<byte[]> DownloadAsync()
    {
        using var httpClient = new HttpClient { Timeout = AttemptTimeout };
        using var timeout = new CancellationTokenSource(DownloadTimeout);

        // GitHub's API asks every request to tell what makes it
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Void.IntegrationTests");

        for (var attempt = 1; !timeout.IsCancellationRequested; attempt++)
        {
            try
            {
                return attempt % 2 is 1 ? await httpClient.GetByteArrayAsync(Url, timeout.Token) : await DownloadThroughApiAsync(httpClient, timeout.Token);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException && !timeout.IsCancellationRequested)
            {
                Console.WriteLine($"[Void.IntegrationTests] Downloading {AssetName} failed on attempt {attempt}; retrying in {RetryDelay.TotalSeconds:0} seconds: {exception.Message}");
                await Task.Delay(RetryDelay, timeout.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        throw new IntegrationTestException($"{Url} couldn't be downloaded in {DownloadTimeout.TotalMinutes:0} minutes");
    }

    private static async Task<byte[]> DownloadThroughApiAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        using var release = JsonDocument.Parse(await httpClient.GetStringAsync(LatestRelease, cancellationToken));

        var asset = release.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(asset => asset.GetProperty("name").GetString() == AssetName);
        var assetUrl = asset.ValueKind is JsonValueKind.Object ? asset.GetProperty("url").GetString() : null;

        using var request = new HttpRequestMessage(HttpMethod.Get, assetUrl ?? throw new InvalidOperationException($"The latest release has no {AssetName}"));
        request.Headers.Accept.ParseAdd("application/octet-stream");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await response.EnsureSuccessStatusCode().Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
