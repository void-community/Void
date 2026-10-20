using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

public class VoidFixture : IAsyncLifetime
{
    private readonly List<string> _stoppedProxyWarnings = [];
    private string[] _targetServers = [];
    private VoidProxy? _voidProxy;
    private DateTime _voidLogWindowStartedAt;

    /// <summary>Starts the proxy in front of <paramref name="servers"/>, reusing the running one when it already targets them.</summary>
    public async Task<VoidProxy> StartProxyAsync(MinecraftServers servers, CancellationToken cancellationToken)
    {
        string[] targetServers = [.. servers.Servers.Select(server => $"localhost:{server.Port}")];

        if (_voidProxy is not null && _targetServers.SequenceEqual(targetServers))
            return _voidProxy;

        await StopProxyAsync("its servers changed");

        _voidProxy = await VoidProxy.CreateAsync(Path.Combine(Path.GetTempPath(), nameof(VoidFixture), Path.GetRandomFileName()), targetServers, cancellationToken: cancellationToken);
        _targetServers = targetServers;
        _voidLogWindowStartedAt = DateTime.UtcNow;

        return _voidProxy;
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopProxyAsync($"{nameof(VoidFixture)} disposal");
        GC.SuppressFinalize(this);

        if (_stoppedProxyWarnings.Count > 0)
            Assert.Fail($"Void emitted {LogLevel.Warning} or higher logs:\n{string.Join('\n', _stoppedProxyWarnings)}");
    }

    private async Task StopProxyAsync(string reason)
    {
        if (_voidProxy is not { } voidProxy)
            return;

        await voidProxy.LogWriter.WriteLineAsync($"Stopping {nameof(VoidProxy)} because of {reason}");
        await voidProxy.DisposeAsync();
        _stoppedProxyWarnings.AddRange(voidProxy.GetLogsAtOrAboveSince(_voidLogWindowStartedAt, LogLevel.Warning));
        _voidProxy = null;
    }
}
