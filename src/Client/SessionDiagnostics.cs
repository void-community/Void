using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Nito.AsyncEx;

using Void.Client.Configuration;
using Void.Client.Models;
using Void.Client.Utilities;

namespace Void.Client;

/// <summary>Owns bounded session evidence independently of the current game lifecycle.</summary>
internal sealed class SessionDiagnostics
{
    private const int ManifestReserveBytes = 256 * 1024;
    private const int MaximumFileBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private readonly AsyncLock _retentionLock = new();
    private readonly DiagnosticsOptions _options;
    private readonly AsyncLocal<Guid?> _context = new();
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();
    private bool _initialized;
    private long _storedBytes;

    public SessionDiagnostics(DiagnosticsOptions options)
    {
        if (options.MaximumSessions < 1 || options.MaximumSessionMb < 1 || options.MaximumTotalMb < options.MaximumSessionMb)
            throw new ArgumentException(message: "Diagnostics limits must be positive and the total must accommodate one session", nameof(options));

        _options = options;
    }

    public Guid? CurrentSessionId => _context.Value;

    public async Task<Guid> BeginAsync(string launch, string minecraftDirectory, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        // Retention always acquires its lock before any session lock. Session operations never acquire it.
        await PruneAsync((long)_options.MaximumSessionMb * 1024 * 1024, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        Guid id = Guid.NewGuid();

        Session session = new(
            new(id, Limit(launch, maximumCharacters: 1024) ?? "", DateTimeOffset.UtcNow, EndedAt: null, Status: null, LastFailure: null, []),
            Path.Combine(_options.Directory, id.ToString()),
            minecraftDirectory
        );

        using (await session.Lock.LockAsync(cancellationToken))
        {
            bool sessionAdded = _sessions.TryAdd(id, session);
            await CollectReportsAsync(session, baseline: true, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            await SaveManifestAsync(session, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        await PruneAsync(reserveBytes: 0, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return id;
    }

    public async Task CollectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return;

        using (await session.Lock.LockAsync(cancellationToken))
        {
            if (!_sessions.ContainsKey(id))
                return;

            if (session.Metadata.EndedAt is null)
                await CollectReportsAsync(session, baseline: false, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            await SaveManifestAsync(session, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    public async Task CompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return;

        using (await session.Lock.LockAsync(cancellationToken))
        {
            if (!_sessions.ContainsKey(id))
                return;

            if (session.Metadata.EndedAt is null)
                await CollectReportsAsync(session, baseline: false, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            session.Metadata = session.Metadata with { EndedAt = session.Metadata.EndedAt ?? DateTimeOffset.UtcNow };
            await SaveManifestAsync(session, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        await PruneAsync(reserveBytes: 0, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task<byte[]?> DownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (!_sessions.TryGetValue(id, out var session))
            return null;

        Dictionary<string, byte[]> files = [];

        using (await session.Lock.LockAsync(cancellationToken))
        {
            if (!_sessions.ContainsKey(id))
                return null;

            if (session.Metadata.EndedAt is null)
                await CollectReportsAsync(session, baseline: false, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            try
            {
                if (Directory.Exists(session.Directory))
                {
                    foreach (string file in Directory.EnumerateFiles(session.Directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (IsLink(file) || Path.GetFileName(file) == "session.json")
                            continue;

                        files[Path.GetFileName(file)] = await ReadTailAsync(file, MaximumFileBytes, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AddWarning(session, $"Some evidence could not be read: {exception.Message}");
            }

            files[key: "session.json"] = Encoding.UTF8.GetBytes(Redact(session, JsonSerializer.Serialize(session.Metadata, JsonOptions)));
        }

        using MemoryStream output = new();

        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var destination = await archive.CreateEntry(name, CompressionLevel.Fastest).OpenAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                await destination.WriteAsync(content, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
        }

        return output.ToArray();
    }

    public IDisposable Enter(Guid? id)
    {
        var previous = _context.Value;
        _context.Value = id;

        return new ContextScope(() => _context.Value = previous);
    }

    public async Task<IReadOnlyList<DiagnosticSession>> ListAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        cancellationToken.ThrowIfCancellationRequested();

        // Immutable metadata snapshots do not need to wait for a session's file I/O.
        return [.. _sessions.Values.Select(static session => session.Metadata).OrderByDescending(static session => session.StartedAt)];
    }

    public async Task RecordAsync(GameStatus status, CancellationToken cancellationToken = default)
    {
        if (status.SessionId is not { } id || !_sessions.TryGetValue(id, out var session))
            return;

        using (await session.Lock.LockAsync(cancellationToken))
        {
            if (!_sessions.ContainsKey(id))
                return;

            var original = status;
            status = status with
            {
                Message = Limit(status.Message),
                Error = Limit(status.Error),
                Failure = status.Failure is { } failure ? failure with { Message = Limit(failure.Message) ?? "", StackTrace = Limit(failure.StackTrace, maximumCharacters: 8192) ?? "" } : null,
                Warnings = [.. status.Warnings.Take(count: 8).Select(static warning => Limit(warning, maximumCharacters: 256) ?? "")]
            };

            if (original.Error != status.Error || original.Failure?.StackTrace != status.Failure?.StackTrace)
                AddWarning(session, warning: "Long failure details were truncated in retained metadata");

            status = JsonSerializer.Deserialize<GameStatus>(Redact(session, JsonSerializer.Serialize(status, JsonOptions)), JsonOptions) ?? status;
            session.Metadata = session.Metadata with { Status = status, LastFailure = status.Failure ?? session.Metadata.LastFailure };
            await WriteFileAsync(
                session,
                name: "operations.jsonl",
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(status, JsonOptions) + "\n"),
                append: true,
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false);
            await SaveManifestAsync(session, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    public async Task<string> RedactAsync(Guid id, string value, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return value;

        using (await session.Lock.LockAsync(cancellationToken))
            return Redact(session, value);
    }

    public async Task RegisterSecretAsync(string secret, CancellationToken cancellationToken = default)
    {
        if (CurrentSessionId is not { } id || !_sessions.TryGetValue(id, out var session))
            return;

        using (await session.Lock.LockAsync(cancellationToken))
            session.Secrets.Add(secret);
    }

    public async Task SaveScreenshotAsync(Guid id, long operationId, byte[] screenshot, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return;

        using (await session.Lock.LockAsync(cancellationToken))
        {
            if (!_sessions.ContainsKey(id))
                return;

            await WriteFileAsync(session, $"failure-{operationId}.png", screenshot, append: false, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            await SaveManifestAsync(session, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    public async Task WarnAsync(Guid id, string warning, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return;

        using (await session.Lock.LockAsync(cancellationToken))
        {
            if (!_sessions.ContainsKey(id))
                return;

            AddWarning(session, warning);
            await SaveManifestAsync(session, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    public async Task WriteOutputAsync(Guid id, string stream, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text) || !_sessions.TryGetValue(id, out var session))
            return;

        using (await session.Lock.LockAsync(cancellationToken))
        {
            if (!_sessions.ContainsKey(id))
                return;

            await WriteFileAsync(session, $"console-{stream}.log", Encoding.UTF8.GetBytes(Redact(session, text)), append: true, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    private static void AddWarning(Session session, string warning)
    {
        warning = Limit(warning, maximumCharacters: 256) ?? "";

        if (session.Metadata.Warnings.Count < 64 && !session.Metadata.Warnings.Contains(warning))
            session.Metadata = session.Metadata with { Warnings = [.. session.Metadata.Warnings, warning] };
    }

    private static bool IsLink(string path)
    {
        return (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static string? Limit(string? value, int maximumCharacters = 2048)
    {
        return value?.Length > maximumCharacters ? value[..maximumCharacters] + " [truncated]" : value;
    }

    private static async Task<byte[]> ReadTailAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 81920, useAsync: true);

        long streamPosition = stream.Seek(Math.Max(val1: 0, stream.Length - maximumBytes), SeekOrigin.Begin);
        byte[] bytes = new byte[(int)Math.Min(maximumBytes, stream.Length)];
        int count = 0;

        while (count < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (read == 0)
                break;

            count += read;
        }

        return bytes[..count];
    }

    private static string Redact(Session session, string value)
    {
        foreach (string secret in session.Secrets)
            value = value.Replace(secret, newValue: "[redacted]", StringComparison.Ordinal);

        return value;
    }

    private void AddStoredBytes(long value)
    {
        ReturnedValue.Consume(Interlocked.Add(ref _storedBytes, value));
    }

    private async Task CollectReportsAsync(Session session, bool baseline, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(session.MinecraftDirectory))
            return;

        try
        {
            foreach (string? directoryName in new[] { "logs", "debug", "crash-reports" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = Path.Combine(session.MinecraftDirectory, directoryName);

                if (!Directory.Exists(directory) || IsLink(session.MinecraftDirectory) || IsLink(directory))
                    continue;

                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(file);

                    bool isLogEvidence = directoryName == "logs" && name is "latest.log" or "debug.log";

                    bool isDisconnectEvidence = directoryName == "debug"
                                               && name.StartsWith(value: "disconnect-", StringComparison.Ordinal)
                                               && name.EndsWith(value: ".txt", StringComparison.Ordinal);

                    bool isCrashEvidence = directoryName == "crash-reports"
                                          && name.StartsWith(value: "crash-", StringComparison.Ordinal)
                                          && name.EndsWith(value: ".txt", StringComparison.Ordinal);

                    if (IsLink(file) || !(isLogEvidence || isDisconnectEvidence || isCrashEvidence))
                        continue;

                    FileInfo information = new(file);
                    var fingerprint = (information.Length, information.LastWriteTimeUtc);

                    if (baseline)
                    {
                        session.Baseline[file] = fingerprint;

                        continue;
                    }

                    if (session.Baseline.TryGetValue(file, out var original) && original == fingerprint)
                        continue;

                    if (information.Length > MaximumFileBytes)
                        AddWarning(session, $"Truncated {directoryName}/{name} to its last {MaximumFileBytes} bytes");

                    byte[] content = await ReadTailAsync(file, MaximumFileBytes, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                    await WriteFileAsync(
                        session,
                        $"{directoryName}-{name}",
                        Encoding.UTF8.GetBytes(Redact(session, Encoding.UTF8.GetString(content))),
                        append: false,
                        cancellationToken
                    ).ConfigureAwait(continueOnCapturedContext: false);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddWarning(session, $"Report collection failed: {exception.Message}");
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _initialized))
            return;

        using (await _retentionLock.LockAsync(cancellationToken))
        {
            if (_initialized)
                return;

            try
            {
                if (Directory.Exists(_options.Directory))
                {
                    foreach (string directory in Directory.EnumerateDirectories(_options.Directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!Guid.TryParse(Path.GetFileName(directory), out var id) || IsLink(directory) || _sessions.ContainsKey(id))
                            continue;

                        try
                        {
                            string manifest = Path.Combine(directory, path2: "session.json");

                            if (IsLink(manifest))
                                continue;

                            var metadata = JsonSerializer.Deserialize<DiagnosticSession>(await File.ReadAllTextAsync(manifest, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), JsonOptions);

                            if (metadata is null || metadata.SessionId != id)
                                continue;

                            Session session = new(metadata with { EndedAt = metadata.EndedAt ?? DateTimeOffset.UtcNow }, directory, minecraftDirectory: "");

                            foreach (string? file in Directory.EnumerateFiles(directory).Where(static file => !IsLink(file)))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                session.FileSizes[Path.GetFileName(file)] = new FileInfo(file).Length;
                            }

                            session.StoredBytes = session.FileSizes.Values.Sum();

                            if (_sessions.TryAdd(id, session))
                                AddStoredBytes(session.StoredBytes);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                        {
                            await Console.Error.WriteLineAsync($"Could not load diagnostic session {id}: {exception.Message}").ConfigureAwait(continueOnCapturedContext: false);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await Console.Error.WriteLineAsync($"Could not load diagnostic history: {exception.Message}").ConfigureAwait(continueOnCapturedContext: false);
            }

            Volatile.Write(ref _initialized, value: true);
        }

        await PruneAsync(reserveBytes: 0, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task PruneAsync(long reserveBytes, CancellationToken cancellationToken)
    {
        using (await _retentionLock.LockAsync(cancellationToken))
        {
            foreach (var session in _sessions.Values.Where(static session => session.Metadata.EndedAt is not null).OrderBy(static session => session.Metadata.StartedAt).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool retentionWithinLimits = _sessions.Count <= _options.MaximumSessions
                                            && Volatile.Read(ref _storedBytes) + reserveBytes <= (long)_options.MaximumTotalMb * 1024 * 1024;

                if (retentionWithinLimits)
                    break;

                using (await session.Lock.LockAsync(cancellationToken))
                {
                    try
                    {
                        if (Directory.Exists(session.Directory) && !IsLink(session.Directory))
                        {
                            foreach (string file in Directory.EnumerateFiles(session.Directory))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                File.Delete(file);
                                long removedBytes = session.FileSizes.GetValueOrDefault(Path.GetFileName(file));
                                bool fileSizeRemoved = session.FileSizes.Remove(Path.GetFileName(file));
                                session.StoredBytes -= removedBytes;
                                AddStoredBytes(-removedBytes);
                            }
                        }

                        if (Directory.Exists(session.Directory) && !IsLink(session.Directory))
                            Directory.Delete(session.Directory);

                        if (_sessions.TryRemove(session.Metadata.SessionId, out var removedSession))
                        {
                            GC.KeepAlive(removedSession);
                            AddStoredBytes(-session.StoredBytes);
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        AddWarning(session, $"Could not expire session: {exception.Message}");
                    }
                }
            }
        }
    }

    private bool Reserve(long bytes, int manifestReserve, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long total = Volatile.Read(ref _storedBytes);

            if (total + bytes > ((long)_options.MaximumTotalMb * 1024 * 1024) - manifestReserve)
                return false;

            if (Interlocked.CompareExchange(ref _storedBytes, total + bytes, total) == total)
                return true;
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private Task SaveManifestAsync(Session session, CancellationToken cancellationToken)
    {
        return WriteFileAsync(
        session,
        name: "session.json",
        Encoding.UTF8.GetBytes(Redact(session, JsonSerializer.Serialize(session.Metadata, JsonOptions))),
        append: false,
        cancellationToken
    );
    }

    private async Task WriteFileAsync(Session session, string name, byte[] bytes, bool append, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long reservedBytes = 0;
        string path = Path.Combine(session.Directory, name);
        long originalLength = session.FileSizes.GetValueOrDefault(name);

        try
        {
            if (IsLink(session.Directory) || IsLink(path))
                throw new IOException(message: "Diagnostic output is a symbolic link");

            var sessionDirectoryInformation = Directory.CreateDirectory(session.Directory);

            if (bytes.Length > MaximumFileBytes)
            {
                AddWarning(session, $"Omitted or truncated {name}: diagnostic file size limit");

                if (!append)
                    return;

                bytes = bytes[^MaximumFileBytes..];
            }

            if (append && originalLength + bytes.Length > MaximumFileBytes && File.Exists(path))
            {
                string previousName = $"previous-{name}";
                File.Move(path, Path.Combine(session.Directory, previousName), overwrite: true);
                long removedBytes = session.FileSizes.GetValueOrDefault(previousName);
                session.FileSizes[previousName] = originalLength;
                session.FileSizes[name] = 0;
                session.StoredBytes -= removedBytes;
                AddStoredBytes(-removedBytes);
                originalLength = 0;
                AddWarning(session, $"Older {name} output was rotated; only recent output is retained");
            }

            long additionalBytes = append ? bytes.Length : Math.Max(val1: 0, bytes.Length - originalLength);
            int reserve = name == "session.json" ? 0 : ManifestReserveBytes;

            bool sessionLimitExceeded = session.StoredBytes + additionalBytes > ((long)_options.MaximumSessionMb * 1024 * 1024) - reserve;
            bool storageReserved = !sessionLimitExceeded && Reserve(additionalBytes, reserve, cancellationToken);

            if (!storageReserved)
            {
                AddWarning(session, warning: "Diagnostic size limit reached; additional evidence was omitted");

                return;
            }

            reservedBytes = additionalBytes;

            using FileStream stream = new(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 81920, useAsync: true);

            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddWarning(session, $"Could not store {name}: {exception.Message}");
        }
        finally
        {
            // Reconcile reservations even after cancellation or a partial write.
            long actualLength = originalLength;

            try
            {
                actualLength = File.Exists(path) && !IsLink(path) ? new FileInfo(path).Length : 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AddWarning(session, $"Could not measure {name}: {exception.Message}");
            }

            long difference = actualLength - originalLength;
            session.FileSizes[name] = actualLength;
            session.StoredBytes += difference;
            AddStoredBytes(difference - reservedBytes);
        }
    }

    private sealed class ContextScope(Action restore) : IDisposable
    {
        public void Dispose()
        {
            restore();
        }
    }

    private sealed class Session(DiagnosticSession metadata, string directory, string minecraftDirectory)
    {
        public AsyncLock Lock { get; } = new();
        public string Directory { get; } = directory;
        public string MinecraftDirectory { get; } = minecraftDirectory;
        public Dictionary<string, long> FileSizes { get; } = [];
        public DiagnosticSession Metadata
        {
            get => Volatile.Read(ref field);
            set => Volatile.Write(ref field, value);
        } = metadata;
        public Dictionary<string, (long, DateTime)> Baseline { get; } = [];
        public List<string> Secrets { get; } = [];
        public long StoredBytes { get; set; }
    }
}
