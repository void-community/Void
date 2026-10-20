using Microsoft.CodeAnalysis;

namespace Void.IntegrationTests.Generators.Sources;

[Generator(LanguageNames.CSharp)]
public sealed class DirectConnectionReleaseGroupGenerator : ReleaseGroupGeneratorBase
{
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatedSourceProvider = context.CompilationProvider.Select(static (_, cancellationToken) => GenerateFamilySource(
            familyName: "DirectConnectionTests",
            baseClassName: "DirectConnectionTestBase",
            otherFixtures: [("PortableMinecraftClientFixture", "portableMinecraftClientFixture")],
            methodNameFormat: "PortableMinecraftClientConnectsTo{0}Server_WithProtocolVersion",
            cancellationToken
        ));

        context.RegisterSourceOutput(generatedSourceProvider, static (sourceProductionContext, generatedSource) =>
        {
            if (generatedSource.Length is 0)
                return;

            sourceProductionContext.AddSource("DirectConnectionTests.g.cs", generatedSource);
        });
    }
}
