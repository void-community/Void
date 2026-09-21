using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public class DiagnosticsApiTests
{
    [Fact]
    public async Task ListsAndDownloadsRetainedSessionsAndReturnsNotFoundForUnknownIdentifier()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"void-api-diagnostics-{Guid.NewGuid()}");
        var diagnostics = new SessionDiagnostics(new DiagnosticsOptions { Directory = directory });
        var identifier = await diagnostics.BeginAsync("vanilla:1.21", "", TestContext.Current.CancellationToken);
        await diagnostics.WriteOutputAsync(identifier, "stdout", "Minecraft output", TestContext.Current.CancellationToken);
        await diagnostics.CompleteAsync(identifier, TestContext.Current.CancellationToken);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(diagnostics);
        builder.Services.AddSingleton<GameCoordinator>(_ => throw new InvalidOperationException("Diagnostic reads must not invoke the coordinator"));
        await using var application = builder.Build();
        application.MapClientApi();
        try
        {
            await application.StartAsync(TestContext.Current.CancellationToken);
            var addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            Assert.NotNull(addresses);
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses.Addresses)) };
            var sessions = await client.GetFromJsonAsync<DiagnosticSession[]>("/api/game/diagnostics", TestContext.Current.CancellationToken);
            Assert.NotNull(sessions);
            Assert.Equal(identifier, Assert.Single(sessions).SessionIdentifier);
            using var response = await client.GetAsync(sessions[0].DownloadUniformResourceLocator, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains(identifier.ToString(), response.Content.Headers.ContentDisposition?.FileNameStar ?? "");
            using var archive = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)));
            Assert.Contains(archive.Entries, entry => entry.FullName == "session.json");
            Assert.Contains(archive.Entries, entry => entry.FullName == "console-stdout.log");
            using var missing = await client.GetAsync($"/api/game/diagnostics/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            await application.StopAsync(CancellationToken.None);
            Directory.Delete(directory, recursive: true);
        }
    }
}
