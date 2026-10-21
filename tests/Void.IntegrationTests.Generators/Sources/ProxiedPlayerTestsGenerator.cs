using Microsoft.CodeAnalysis;

namespace Void.IntegrationTests.Generators.Sources;

[Generator(LanguageNames.CSharp)]
public sealed class ProxiedPlayerTestsGenerator : ConnectionTestsGeneratorBase
{
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatedSourceProvider = context.CompilationProvider.Select(static (_, cancellationToken) => GenerateFamilySource(
            familyName: "ProxiedPlayerTests",
            baseClassName: "global::Void.IntegrationTests.Connections.ProxiedPlayerTestBase",
            clientFixture: ("PortableMinecraftClientPairFixture", "clientFixture"),
            serverPair: false,
            proxied: true,
            methodNameFormat: "PortableMinecraftClientsExposeReciprocalCoordinates",
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
