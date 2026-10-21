using Microsoft.CodeAnalysis;

namespace Void.IntegrationTests.Generators.Sources;

[Generator(LanguageNames.CSharp)]
public sealed class DirectConnectionTestsGenerator : ConnectionTestsGeneratorBase
{
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatedSourceProvider = context.CompilationProvider.Select(static (_, cancellationToken) => GenerateFamilySource(
            familyName: "DirectConnectionTests",
            baseClassName: "DirectConnectionTestBase",
            clientFixture: ("PortableMinecraftClientFixture", "portableMinecraftClientFixture"),
            serverPair: false,
            proxied: false,
            methodNameFormat: "PortableMinecraftClientConnectsTo{0}Server",
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
