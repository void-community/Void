using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Void.IntegrationTests.Infrastructure.Harness;

public interface IIntegrationSide : IAsyncDisposable
{
    public string LogFileName { get; }
    public IEnumerable<string> Logs { get; }

    public Task<IEnumerable<string>> ReadLogsAsync(DateTime since, CancellationToken cancellationToken = default);
}
