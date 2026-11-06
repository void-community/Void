using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Void.Minecraft.Commands.Brigadier;
using Void.Minecraft.Commands.Brigadier.Extensions;
using Void.Minecraft.Commands.Brigadier.Tree.Nodes;
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

    [Fact]
    public async Task CopiesCommandsWhilePluginsRegisterAndRemoveThem()
    {
        // A player that joins a server gets the proxy's commands copied into the server's, while plugins that load or unload change them.
        var commands = new CommandService(NullLogger<ICommandService>.Instance, new EventService(NullLogger<EventService>.Instance, new Container()));
        var source = new CommandSource();
        using var changing = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var change = Task.Run(() =>
        {
            for (var index = 0; !changing.IsCancellationRequested; index++)
            {
                var name = $"command{index % 64}";

                commands.Register(builder => builder.Literal(name).Executes(_ => 0));
                ((CommandDispatcher)commands.Dispatcher).Root.RemoveChild(name);
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                for (var copy = 0; copy < 20_000; copy++)
                    await commands.CopyToAsync(new RootCommandNode(), source, TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken)));
        }
        finally
        {
            await changing.CancelAsync();
            await change;
        }
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
