using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Void.Minecraft.Commands.Brigadier.Extensions;
using Void.Proxy.Api.Commands;
using Void.Proxy.Commands;
using Void.Proxy.Events;
using Xunit;

namespace Void.UnitTests.Commands;

public class CommandServiceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public async Task RunsEveryExecutionWithoutCriticalLogs(int callers)
    {
        // A command that returns at once often finishes before ExecuteAsync returns, from one player or many at once (issue #1598).
        const int executions = 2_000;

        var logger = new CapturingLogger();
        var commands = new CommandService(logger, new EventService(NullLogger<EventService>.Instance, new Container()));
        var executed = 0;
        var allExecuted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        commands.Register(builder => builder
            .Literal("ping")
            .Executes(_ =>
            {
                if (Interlocked.Increment(ref executed) is executions)
                    allExecuted.SetResult();

                return 0;
            }));

        var options = new ParallelOptions { MaxDegreeOfParallelism = callers, CancellationToken = TestContext.Current.CancellationToken };

        await Parallel.ForAsync(0, executions, options, async (_, cancellationToken) =>
        {
            var result = await commands.ExecuteAsync(new CommandSource(), "ping", cancellationToken);
            Assert.Equal(CommandExecutionResult.Executed, result);
        });

        await allExecuted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Empty(logger.Critical);
    }

    private sealed class CommandSource : ICommandSource
    {
        public ValueTask SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingLogger : ILogger<ICommandService>
    {
        public ConcurrentQueue<string> Critical { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel is LogLevel.Critical)
                Critical.Enqueue(formatter(state, exception));
        }
    }
}
