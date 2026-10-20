using System;
using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>
/// A mod loader build, by the version the client starts it as and the version servers install it as, or the installer servers run when
/// the version alone doesn't find it.
/// </summary>
public sealed record LoaderBuild(ModLoader ModLoader, string ClientLoaderVersion, string ServerLoaderVersion, Uri? ServerInstallerUri = null)
{
    public override string ToString()
    {
        return $"{ModLoader} {ClientLoaderVersion}";
    }
}
