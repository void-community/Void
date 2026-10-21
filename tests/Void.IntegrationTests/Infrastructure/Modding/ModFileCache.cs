using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Void.IntegrationTests.Infrastructure.Exceptions;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>Downloads mod files once per machine, and only uses files whose SHA-512 matches what their publisher listed.</summary>
internal static class ModFileCache
{
    private static readonly string CacheDirectory = Path.Combine(Path.GetTempPath(), "Void.IntegrationTests", "mods");
    private static readonly ConcurrentDictionary<string, AsyncLazy<ModFile>> Downloads = new();

    public static Task<ModFile> DownloadAsync(ModDownload download, CancellationToken cancellationToken)
    {
        return Downloads.GetOrAdd(download.Sha512, _ => new AsyncLazy<ModFile>(() => DownloadCoreAsync(download), AsyncLazyFlags.RetryOnFailure)).Task.WaitAsync(cancellationToken);
    }

    private static async Task<ModFile> DownloadCoreAsync(ModDownload download)
    {
        var directory = Path.Combine(CacheDirectory, download.Sha512);
        var path = Path.Combine(directory, download.FileName);

        if (File.Exists(path) && await HashAsync(path) == download.Sha512)
            return new ModFile(download, path);

        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Path.GetRandomFileName()}";

        await UpstreamHttp.SendAsync(download.DownloadUri, async (response, cancellationToken) =>
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(temporaryPath);
            await source.CopyToAsync(target, cancellationToken);
            return target.Length;
        }, CancellationToken.None);

        var sha512 = await HashAsync(temporaryPath);

        if (sha512 != download.Sha512)
        {
            File.Delete(temporaryPath);
            throw new IntegrationTestException($"{download.FileName} from {download.DownloadUri} has SHA-512 {sha512} instead of {download.Sha512}");
        }

        File.Move(temporaryPath, path, overwrite: true);
        return new ModFile(download, path);
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA512.HashDataAsync(stream));
    }
}
