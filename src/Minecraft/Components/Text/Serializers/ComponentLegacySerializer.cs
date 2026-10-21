using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Void.Minecraft.Components.Text.Formats;
using Void.Minecraft.Components.Text.Properties;
using Void.Minecraft.Components.Text.Properties.Content;

namespace Void.Minecraft.Components.Text.Serializers;

/// <summary>Converts component trees to and from section-style legacy formatting strings.</summary>
public static class ComponentLegacySerializer
{
    /// <summary>Provides an example containing legacy colors, a hexadecimal color, styles, and reset formatting.</summary>
    public const string ExampleLegacyString = "&1Hello, &2this is a &x&F&F&A&A&0&1hex colored " +
                                              "text, &lwith bold, &oitalic, &nunderline, &mstrikethrough, " +
                                              "&kobfuscated, &rand reset.";

    /// <summary>Provides a dense example that exercises legacy codes among varied literal characters.</summary>
    public const string ExampleComplexLegacyString = "&0Aa1!b@2$&k3#Cd$Ef%&1Gh7%h^j&lKl8&Lm*&2No9(Q)r" +
                                                     "&mSt0_Op+&3Uv-1=Wx&nYz2!Za@&4b#3$Cd%&oEf4&Gh*&5" +
                                                     "Ij5)Kl(&rMn6)Op?&6Qr7_Rs-&7Tu8*Vw&&8Xy9@Za!&9Bc0#De$&a" +
                                                     "Fg1%Hi^&bJk2&Lm*&cNo3(Pq)&dRs4_St+&eUv5=Wx-&fYz6!Ab@";

    /// <summary>Serializes a component and its direct children to legacy-formatted text.</summary>
    /// <param name="component">The root component.</param>
    /// <param name="prefix">The character emitted before each formatting code. Use the null character to omit formatting codes.</param>
    /// <returns>The legacy text.</returns>
    /// <remarks>Content that has no native legacy form is reduced to a textual approximation. Descendants below direct children are not traversed.</remarks>
    public static string Serialize(Component component, char prefix = '&')
    {
        var builder = new StringBuilder();
        var formatting = Formatting.Default;

        ApplyComponent(component);

        foreach (var child in component.Children.Extra)
            ApplyComponent(child);

        return builder.ToString();

        void ApplyComponent(Component component)
        {
            var text = component.Content switch
            {
                TextContent content => content.Value,
                KeybindContent content => content.Value,
                SelectorContent content => content.Value,
                TranslatableContent content => content.Translate, // skipped child component
                ScoreContent content => string.Concat(content.Name, ":", content.Objective),
                NbtContent content => string.Join(':', [content.Source, content.Path, content.Interpret, content.Block, content.Entity, content.Storage]),  // skipped child component
                var content => content.ToString()
            };

            if (prefix is not '\0')
            {
                var removedColor = component.Formatting.Color is null && formatting.Color is not null;
                var removedIsBold = component.Formatting.IsBold is null or false && formatting.IsBold is true;
                var removedIsItalic = component.Formatting.IsItalic is null or false && formatting.IsItalic is true;
                var removedIsUnderlined = component.Formatting.IsUnderlined is null or false && formatting.IsUnderlined is true;
                var removedIsStrikethrough = component.Formatting.IsStrikethrough is null or false && formatting.IsStrikethrough is true;
                var removedIsObfuscated = component.Formatting.IsObfuscated is null or false && formatting.IsObfuscated is true;

                if (removedColor || removedIsBold || removedIsItalic || removedIsUnderlined || removedIsStrikethrough || removedIsObfuscated)
                {
                    formatting = Formatting.Default;
                    builder.Append(prefix);
                    builder.Append('r');
                }

                if (component.Formatting.Color is { } color && color != formatting.Color)
                {
                    var name = color.Name.AsSpan();

                    if (LegacyTextFormat.TryFromName(name, out var legacyTextColor))
                    {
                        builder.Append(prefix);
                        builder.Append(legacyTextColor.Code);
                    }
                    else if (name[0] is '#' && name.Length is 7)
                    {
                        builder.Append(prefix);
                        builder.Append('x');

                        foreach (var digit in name[1..])
                        {
                            builder.Append(prefix);
                            builder.Append(digit);
                        }
                    }
                    else
                    {
                        throw new Exception($"Error serializing color: {color}");
                    }
                }

                if (component.Formatting.IsBold is true && component.Formatting.IsBold != formatting.IsBold)
                {
                    builder.Append(prefix);
                    builder.Append('l');
                }

                if (component.Formatting.IsItalic is true && component.Formatting.IsItalic != formatting.IsItalic)
                {
                    builder.Append(prefix);
                    builder.Append('o');
                }

                if (component.Formatting.IsUnderlined is true && component.Formatting.IsUnderlined != formatting.IsUnderlined)
                {
                    builder.Append(prefix);
                    builder.Append('n');
                }

                if (component.Formatting.IsStrikethrough is true && component.Formatting.IsStrikethrough != formatting.IsStrikethrough)
                {
                    builder.Append(prefix);
                    builder.Append('m');
                }

                if (component.Formatting.IsObfuscated is true && component.Formatting.IsObfuscated != formatting.IsObfuscated)
                {
                    builder.Append(prefix);
                    builder.Append('k');
                }
            }

            formatting = component.Formatting;
            builder.Append(text);
        }
    }

    /// <summary>Deserializes legacy-formatted text into a component tree.</summary>
    /// <param name="source">The text to parse.</param>
    /// <param name="prefix">The character introducing formatting codes.</param>
    /// <returns>A default component for empty input, a single component for one segment, or a root whose children contain subsequent segments.</returns>
    /// <remarks>Unknown or incomplete formatting sequences are retained as literal text.</remarks>
    public static Component Deserialize(string source, char prefix = '&')
    {
        var span = source.AsSpan();
        var segments = new List<Component>();
        var text = new StringBuilder();

        var formatting = Formatting.Default;

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] != prefix || i + 1 >= span.Length)
            {
                text.Append(span[i]);
                continue;
            }

            if (text.Length > 0)
                MoveNext();

            i++; // move to code
            var code = span[i];

            if (code is 'x')
            {
                if (TryParseHexSequence(prefix, span, ref i, out var hex))
                {
                    formatting = formatting with { Color = "#" + hex };
                }
                else
                {
                    text.Append(prefix);
                    text.Append('x');
                }
            }
            else if (code is 'r')
            {
                formatting = new Formatting
                {
                    IsBold = false,
                    IsItalic = false,
                    IsUnderlined = false,
                    IsStrikethrough = false,
                    IsObfuscated = false
                };
            }
            else if (LegacyTextFormat.TryFromCode(code, out var format))
            {
                formatting = format.Transform(formatting);
            }
            else
            {
                text.Append(prefix);
                i--; // move back, this is not code
            }
        }

        if (text.Length > 0)
            MoveNext();

        if (segments.Count is 0)
            return Component.Default;

        if (segments.Count is 1)
            return segments[0];

        return segments[0] with { Children = new(segments[1..]) };

        void MoveNext()
        {
            var content = new TextContent(text.ToString());
            text.Clear();

            segments.Add(Component.Default with
            {
                Content = content,
                Formatting = formatting
            });
        }
    }

    private static bool TryParseHexSequence(char prefix, ReadOnlySpan<char> source, ref int sourceIndex, [MaybeNullWhen(false)] out string result)
    {
        var length = 6 * 2; // prefix before each digit
        result = null;

        if (sourceIndex + length - 1 >= source.Length)
            return false;

        var builder = new StringBuilder();

        for (var index = sourceIndex + 1; index < sourceIndex + length; index++)
        {
            var digit = source[index + 1];

            if (source[index] == prefix && IsHexDigit(digit))
            {
                index++;
                builder.Append(digit);
            }
            else
            {
                break;
            }
        }

        result = builder.ToString();
        sourceIndex += length;

        return true;
    }

    private static bool IsHexDigit(char c) => c is
            >= '0' and <= '9' or
            >= 'a' and <= 'f' or
            >= 'A' and <= 'F';
}
