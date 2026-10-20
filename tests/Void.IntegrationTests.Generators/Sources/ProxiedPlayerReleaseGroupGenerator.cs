using Microsoft.CodeAnalysis;

namespace Void.IntegrationTests.Generators.Sources;

[Generator(LanguageNames.CSharp)]
public sealed class ProxiedPlayerReleaseGroupGenerator : ReleaseGroupGeneratorBase
{
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatedSourceProvider = context.CompilationProvider.Select(static (_, cancellationToken) => GenerateFamilySource(
            familyName: "ProxiedPlayerTests",
            baseClassName: "global::Void.IntegrationTests.Connections.ProxiedPlayerTestBase",
            otherFixtures: [("VoidFixture", "voidFixture"), ("PortableMinecraftClientPairFixture", "clientFixture")],
            methodNameFormat: "PortableMinecraftClientsExposeReciprocalCoordinates_WithProtocolVersion",
            cancellationToken
        ));

        context.RegisterSourceOutput(generatedSourceProvider, static (sourceProductionContext, generatedSource) =>
        {
            if (generatedSource.Length is 0)
                return;

            sourceProductionContext.AddSource("ProxiedPlayerTests.g.cs", generatedSource);
        });
    }
}
