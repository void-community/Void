using Microsoft.CodeAnalysis;

namespace Void.IntegrationTests.Generators.Sources;

[Generator(LanguageNames.CSharp)]
public sealed class ProxiedConnectionReleaseGroupGenerator : ReleaseGroupGeneratorBase
{
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatedSourceProvider = context.CompilationProvider.Select(static (_, cancellationToken) => GenerateFamilySource(
            familyName: "ProxiedConnectionTests",
            baseClassName: "ProxiedConnectionTestBase",
            otherFixtures: [("VoidFixture", "voidFixture"), ("PortableMinecraftClientFixture", "portableMinecraftClientFixture")],
            methodNameFormat: "PortableMinecraftClientConnectsTo{0}ServerThroughProxy_WithProtocolVersion",
            cancellationToken
        ));

        context.RegisterSourceOutput(generatedSourceProvider, static (sourceProductionContext, generatedSource) =>
        {
            if (generatedSource.Length is 0)
                return;

            sourceProductionContext.AddSource("ProxiedConnectionTests.g.cs", generatedSource);
        });
    }
}
