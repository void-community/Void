using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class ConsoleTextNormalizerTests
{
    // Captured from PortableMC 5 launching vanilla 1.21.1 with its default (human) output.
    private const string PortableMinecraftOutput = "\r[  ..  ] Loading version 1.21.1\r[  OK  ] Loaded version 1.21.1\n"
                                                   + "\r[02:59:15] [Render thread] [INFO] Created: 64x64x0 minecraft:textures/atlas/map_decorations.png-atlas\n"
                                                   + "\r[02:59:15] [Render thread] [WARN] Shader rendertype_entity_translucent_emissive could not find sampler named Sampler2\n";

    private const string PlainOutput = "[  ..  ] Loading version 1.21.1\n[  OK  ] Loaded version 1.21.1\n"
                                       + "[02:59:15] [Render thread] [INFO] Created: 64x64x0 minecraft:textures/atlas/map_decorations.png-atlas\n"
                                       + "[02:59:15] [Render thread] [WARN] Shader rendertype_entity_translucent_emissive could not find sampler named Sampler2\n";

    [Fact]
    public void TurnsInteractiveOutputIntoPlainLines()
    {
        Assert.Equal(PlainOutput, new ConsoleTextNormalizer().Normalize(PortableMinecraftOutput));
    }

    [Fact]
    public void GivesTheSameResultWhenTextArrivesOneCharacterAtATime()
    {
        var normalizer = new ConsoleTextNormalizer();

        string normalized = string.Concat(PortableMinecraftOutput.Select(character => normalizer.Normalize([character]))) + normalizer.Flush();

        Assert.Equal(PlainOutput, normalized);
    }

    [Theory]
    [InlineData("windows line\r\nnext\r\n", "windows line\nnext\n")]
    [InlineData("\u001b[32mgreen\u001b[0m text\u001b[K\n", "green text\n")]
    [InlineData("plain\n\nline\n", "plain\n\nline\n")]
    public void KeepsOnlyPrintableText(string text, string expected)
    {
        Assert.Equal(expected, new ConsoleTextNormalizer().Normalize(text));
    }

    [Fact]
    public void EndsALineThatAFinalCarriageReturnWouldRedraw()
    {
        var normalizer = new ConsoleTextNormalizer();

        Assert.Equal("[  ..  ] Launching", normalizer.Normalize("\r[  ..  ] Launching\r"));
        Assert.Equal("\n", normalizer.Flush());
        Assert.Equal("", normalizer.Flush());
    }

    [Fact]
    public async Task OutputPumpWritesPlainLinesToTheConsole()
    {
        using var reader = new StringReader(PortableMinecraftOutput);
        using var console = new StringWriter();

        await new GameRuntime().PumpOutputAsync(reader, console, sessionId: null, stream: "stdout", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PlainOutput, console.ToString());
    }
}
