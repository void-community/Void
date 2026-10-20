using Microsoft.CodeAnalysis;

namespace Void.IntegrationTests.Generators.Sources;

[Generator(LanguageNames.CSharp)]
public sealed class ProxiedServerRedirectionReleaseGroupGenerator : ReleaseGroupGeneratorBase
{
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatedSourceProvider = context.CompilationProvider.Select(static (_, cancellationToken) => GenerateFamilySource(
            familyName: "ProxiedServerRedirectionTests",
            baseClassName: "ProxiedServerRedirectionTestBase",
            otherFixtures: [("VoidFixture", "voidFixture"), ("PortableMinecraftClientFixture", "portableMinecraftClientFixture")],
            methodNameFormat: "PortableMinecraftClientMovesBetween{0}ServersThroughProxy_WithProtocolVersion",
            cancellationToken
        ));

        context.RegisterSourceOutput(generatedSourceProvider, static (sourceProductionContext, generatedSource) =>
        {
            if (generatedSource.Length is 0)
                return;

            sourceProductionContext.AddSource("ProxiedServerRedirectionTests.g.cs", generatedSource);
        });
    }
}
