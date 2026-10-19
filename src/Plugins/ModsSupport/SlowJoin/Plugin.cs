using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Void.Minecraft.Network;
using Void.Proxy.Api.Configurations;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Plugins.Common.Plugins;
using Void.Proxy.Plugins.ModsSupport.SlowJoin.Services;

namespace Void.Proxy.Plugins.ModsSupport.SlowJoin;

/// <summary>
/// Keeps players connected while their client is still busy loading after joining a server, which large modpacks need for longer
/// than a backend waits for a keep-alive answer.
/// </summary>
public class Plugin(IDependencyService dependencies, IConfigurationService configs, IConsoleService console) : IProtocolPlugin
{
    public readonly Option<int> SlowJoinTimeoutOption = new("--slow-join-timeout")
    {
        Description = "Sets how long a player may take to answer a keep-alive while loading after joining a server, in seconds"
    };

    public readonly Option<int> SlowJoinPromptAnswersOption = new("--slow-join-prompt-answers")
    {
        Description = "Sets how many keep-alives in a row a loading player must answer promptly before keep-alives are forwarded again"
    };

    public readonly Option<int> SlowJoinPromptAnswerTimeOption = new("--slow-join-prompt-answer-time")
    {
        Description = "Sets how soon a loading player must answer a keep-alive for the answer to count as prompt, in seconds"
    };

    public static IEnumerable<ProtocolVersion> SupportedVersions => ProtocolVersion.Range();

    public string Name => nameof(SlowJoin);

    [Subscribe]
    public async ValueTask OnPluginLoading(PluginLoadingEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Plugin != this)
            return;

        var settings = await configs.GetAsync<Settings>(cancellationToken);

        dependencies.Register(services =>
        {
            services.AddSingleton(settings);
            services.AddScoped<SlowJoinService>();
        });

        console.EnsureOptionDiscovered(SlowJoinTimeoutOption);
        console.EnsureOptionDiscovered(SlowJoinPromptAnswersOption);
        console.EnsureOptionDiscovered(SlowJoinPromptAnswerTimeOption);
    }
}
