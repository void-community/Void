using System.Collections.Generic;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>A build of a content mod, with the mods it requires.</summary>
public sealed record ContentModBuild(string Project, IReadOnlyList<ModDownload> Downloads);
