using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>A mod loader build, by the version servers report it as, such as Forge 10.13.4.1614 for Minecraft 1.7.10.</summary>
public sealed record LoaderBuild(ModLoader ModLoader, string Version)
{
    public override string ToString()
    {
        return $"{ModLoader} {Version}";
    }
}
