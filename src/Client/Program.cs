using System.Text.Json;
using System.Text.Json.Serialization;

using Void.Client;
using Void.Client.Abstractions;
using Void.Client.Configuration;

var builder = WebApplication.CreateBuilder(args);

var configuredJsonServices = builder.Services.ConfigureHttpJsonOptions(static options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

var diagnosticServices = builder.Services.AddSingleton(new SessionDiagnostics(DiagnosticsOptions.FromConfiguration(builder.Configuration)));
var runtimeServices = builder.Services.AddSingleton<IGameRuntime, GameRuntime>();
var coordinatorServices = builder.Services.AddSingleton<GameCoordinator>();
var hostedServices = builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<GameCoordinator>());

var application = builder.Build();
var apiEndpoints = application.MapClientApi();
await application.RunAsync().ConfigureAwait(continueOnCapturedContext: false);
