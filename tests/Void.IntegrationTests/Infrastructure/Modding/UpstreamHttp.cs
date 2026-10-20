using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>Reads the services that publish Minecraft, its mod loaders and mods, retrying failures that are likely to pass.</summary>
internal static class UpstreamHttp
{
    private const int MaximumAttempts = 3;

    // Modrinth asks every client to identify itself.
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "void-community/Void integration-tests" } },
        Timeout = TimeSpan.FromSeconds(60)
    };

    public static async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        return await SendAsync(uri, static (response, cancellationToken) => response.Content.ReadAsStringAsync(cancellationToken), cancellationToken);
    }

    public static async Task<T> SendAsync<T>(Uri uri, Func<HttpResponseMessage, CancellationToken, Task<T>> readAsync, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                return await readAsync(response, cancellationToken);
            }
            catch (Exception exception) when (attempt < MaximumAttempts && IsTransient(exception, cancellationToken))
            {
                await Task.Delay(TimeSpan.FromSeconds(2 << (attempt - 1)), cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        return exception switch
        {
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => true,
            HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
            TaskCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false
        };
    }
}
