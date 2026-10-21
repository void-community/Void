using Microsoft.CodeAnalysis;

namespace Void.IntegrationTests.Generators.Sources;

[Generator(LanguageNames.CSharp)]
public sealed class ProxiedConnectionTestsGenerator : ConnectionTestsGeneratorBase
{
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatedSourceProvider = context.CompilationProvider.Select(static (_, cancellationToken) => GenerateFamilySource(
            familyName: "ProxiedConnectionTests",
            baseClassName: "ProxiedConnectionTestBase",
            clientFixture: ("PortableMinecraftClientFixture", "portableMinecraftClientFixture"),
            serverPair: false,
            proxied: true,
            methodNameFormat: "PortableMinecraftClientConnectsTo{0}ServerThroughProxy",
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
