using Void.Minecraft.Buffers;
using Void.Minecraft.Commands.Brigadier.ArgumentTypes;
using Void.Minecraft.Commands.Brigadier.Registry;
using StringReader = Void.Minecraft.Commands.Brigadier.StringReader;

namespace Void.Proxy.Plugins.ModsSupport.CrossStitch;

/// <summary>A modded argument that CrossStitch wrapped because the client may not know its parser.</summary>
/// <param name="Mapping">The unwrapped argument mapping.</param>
/// <param name="Data">The unwrapped argument properties.</param>
/// <param name="ParserId">The backend's own parser ID as read from 1.19 and newer protocols, including -1 for argument types the backend never registered.</param>
public record CrossStitchModArgumentType(ArgumentSerializerMapping Mapping, BufferMemory Data, int? ParserId = null) : IArgumentType
{
    public IEnumerable<string> Examples => throw new NotSupportedException();

    public IArgumentValue Parse(StringReader reader) => throw new NotSupportedException();
}
