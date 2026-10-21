using System;
using System.Collections.Generic;
using Void.Minecraft.Commands.Brigadier.Registry;

namespace Void.Minecraft.Commands.Brigadier.ArgumentTypes;

/// <summary>
/// An argument of a type the proxy doesn't know, such as one a mod registers. It reaches the player exactly as the server sent it, so a
/// client with the same mods reads it as it would on a direct connection.
/// </summary>
/// <param name="Mapping">The argument type as the server named it: its identifier before 1.19, or its numeric ID from 1.19.</param>
/// <param name="Properties">The argument type's properties as the server sent them.</param>
public record UnknownArgumentType(ArgumentSerializerMapping Mapping, ReadOnlyMemory<byte> Properties) : IArgumentType
{
    /// <summary>Always throws because the proxy can't parse input for an argument type it doesn't know.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public IEnumerable<string> Examples => throw new NotSupportedException($"Argument type {Mapping.Identifier} is unknown to the proxy.");

    /// <summary>Always throws because the proxy can't parse input for an argument type it doesn't know.</summary>
    /// <param name="reader">The unused reader.</param>
    /// <returns>This method does not return.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public IArgumentValue Parse(StringReader reader) => throw new NotSupportedException($"Argument type {Mapping.Identifier} is unknown to the proxy.");
}
