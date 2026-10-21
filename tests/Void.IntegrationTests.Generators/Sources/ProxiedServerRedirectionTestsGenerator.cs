using Microsoft.CodeAnalysis;

namespace Void.IntegrationTests.Generators.Sources;

[Generator(LanguageNames.CSharp)]
public sealed class ProxiedServerRedirectionTestsGenerator : ConnectionTestsGeneratorBase
{
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatedSourceProvider = context.CompilationProvider.Select(static (_, cancellationToken) => GenerateFamilySource(
            familyName: "ProxiedServerRedirectionTests",
            baseClassName: "ProxiedServerRedirectionTestBase",
            clientFixture: ("PortableMinecraftClientFixture", "portableMinecraftClientFixture"),
            serverPair: true,
            methodNameFormat: "PortableMinecraftClientMovesBetween{0}ServersThroughProxy",
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
