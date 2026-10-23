using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Tomlyn;
using Tomlyn.Serialization;

using File = System.IO.File;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    private const string DefaultPatchEnvironmentPrefix = "CFG_";
    private const string PatchDefinitionsVariable = "VOID_PATCH_DEFINITIONS";
    private const string PatchEnvironmentPrefixVariable = "VOID_PATCH_ENV_PREFIX";

    private static readonly JsonDocumentOptions PatchJsonReadOptions = new() { CommentHandling = JsonCommentHandling.Skip };
    private static readonly JsonSerializerOptions PatchJsonWriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Applies patch definitions in mc-image-helper's format to the files of a game directory. A file that doesn't exist or can't be read, and an
    /// operation whose value names a missing variable, are skipped with a message. A source that can't be read and an operation that fails fail
    /// the start.
    /// </summary>
    /// <returns>A message for each operation applied and each patch or operation skipped.</returns>
    internal static async Task<List<string>> ApplyPatchDefinitionsAsync(HttpClient httpClient, Func<string, string?> getVariable, string gameDirectory, CancellationToken cancellationToken)
    {
        string prefix = getVariable(PatchEnvironmentPrefixVariable) ?? DefaultPatchEnvironmentPrefix;
        List<string> messages = [];

        foreach (string source in SplitList(getVariable(PatchDefinitionsVariable)))
        {
            foreach (JsonObject patch in await ReadPatchDefinitionsAsync(httpClient, source, cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
                await ApplyPatchDefinitionAsync(patch, source, prefix, getVariable, gameDirectory, messages, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        return messages;
    }

    private static void AdvanceToml(TomlReader reader)
    {
        if (!reader.Read())
            throw reader.CreateException(message: "The file ends before its last value");
    }

    private static async Task ApplyPatchDefinitionAsync(
        JsonObject patch,
        string source,
        string prefix,
        Func<string, string?> getVariable,
        string gameDirectory,
        List<string> messages,
        CancellationToken cancellationToken
    )
    {
        string file = GetPatchText(patch, propertyName: "file", source) ?? throw MinecraftPreparationFailure($"A patch definition of {source} has no file");
        string path = Path.Combine(gameDirectory, InterpolatePatchText(file, prefix, getVariable, []));

        if (!File.Exists(path))
        {
            messages.Add($"Unable to patch {path}: it isn't an existing file");

            return;
        }

        string? formatName = GetPatchText(patch, propertyName: "file-format", source) ?? GetPatchFileFormat(path);

        if (formatName is not ("json" or "toml" or "properties"))
        {
            messages.Add(
                formatName is null ? $"Unable to patch {path}: its file format couldn't be identified" : $"Unable to patch {path}: the file format {formatName} isn't supported"
            );

            return;
        }

        if (patch[propertyName: "ops"] is not JsonArray { Count: > 0 } operations)
            return;

        PatchFileText fileText;
        JsonObject data;

        try
        {
            fileText = DecodePatchFile(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false));
            data = DecodePatchData(fileText.Text, formatName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or TomlException or FormatException)
        {
            messages.Add($"Unable to apply patch from {source} to {path}: {exception.Message}");

            return;
        }

        foreach (JsonNode? operation in operations)
        {
            string description = DescribePatchOperation(operation);

            try
            {
                List<string> missingVariables = ApplyPatchOperation(data, operation, prefix, getVariable);

                messages.Add(
                    missingVariables.Count is 0
                    ? $"Applied {description} from {source} to {path}"
                    : $"Unable to apply {description} from {source} to {path}: missing environment variables {string.Join(separator: ", ", missingVariables)}"
                );
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException or OverflowException)
            {
                throw MinecraftPreparationFailure($"Patch from {source} couldn't apply {description} to {path}: {exception.Message}", exception);
            }
        }

        string encoded;

        try
        {
            encoded = EncodePatchData(data, formatName);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            throw MinecraftPreparationFailure($"Patch from {source} couldn't be written to {path}: {exception.Message}", exception);
        }

        // The file keeps its encoding, and a byte order mark when it had one.
        await File.WriteAllBytesAsync(path, [.. fileText.Encoding.GetPreamble(), .. fileText.Encoding.GetBytes(encoded)], cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    /// <returns>The missing variables that skip the operation, or none when it was applied.</returns>
    private static List<string> ApplyPatchOperation(JsonObject data, JsonNode? operation, string prefix, Func<string, string?> getVariable)
    {
        if (operation is not JsonObject { Count: 1 } wrapper || wrapper.First() is not (string kind, JsonObject arguments))
            throw new InvalidOperationException(message: "An operation must be an object with one of $set, $put or $add");

        List<PatchPathSegment> segments = ParsePatchPath(GetPatchArgument(arguments, propertyName: "path") ?? throw new InvalidOperationException(message: "The operation has no path"));
        List<string> missingVariables = [];
        JsonNode? value = CreatePatchValue(arguments[propertyName: "value"], GetPatchArgument(arguments, propertyName: "value-type"), prefix, getVariable, missingVariables);

        if (missingVariables.Count > 0)
            return missingVariables;

        switch (kind)
        {
            case "$set" when segments.Count is 0:
                throw new InvalidOperationException(message: "$set needs a path below the root");
            case "$set":
                {
                    JsonNode parent = ResolvePatchPath(data, segments, segments.Count - 1);

                    switch (segments[^1], parent)
                    {
                        case ({ Name: { } name }, JsonObject parentObject):
                            {
                                parentObject[name] = value;

                                break;
                            }
                        case ({ Name: null, Index: var index }, JsonArray parentArray) when index < parentArray.Count:
                            {
                                parentArray[index] = value;

                                break;
                            }
                        default:
                            throw new InvalidOperationException($"{FormatPatchPath(segments, segments.Count)} doesn't exist");
                    }

                    break;
                }
            case "$put":
                {
                    string key = GetPatchArgument(arguments, propertyName: "key") ?? throw new InvalidOperationException(message: "$put has no key");

                    if (ResolvePatchPath(data, segments, segments.Count) is not JsonObject target)
                        throw new InvalidOperationException($"{FormatPatchPath(segments, segments.Count)} isn't an object");

                    target[key] = value;

                    break;
                }
            case "$add":
                {
                    if (ResolvePatchPath(data, segments, segments.Count) is not JsonArray target)
                        throw new InvalidOperationException($"{FormatPatchPath(segments, segments.Count)} isn't a list");

                    target.Add(value);

                    break;
                }
            default:
                throw new InvalidOperationException($"{kind} isn't an operation, which is one of $set, $put or $add");
        }

        return missingVariables;
    }

    private static JsonValue ConvertPatchValue(string value, string? valueType)
    {
        return valueType switch
        {
            "int" => JsonValue.Create(long.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
            "float" => JsonValue.Create(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)),
            "bool" => JsonValue.Create(ParsePatchBoolean(value) ?? throw new FormatException($"{value} isn't a valid bool, which is true, yes, false or no")),
            "auto" when long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number) => JsonValue.Create(number),
            "auto" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) => JsonValue.Create(number),
            "auto" when ParsePatchBoolean(value) is { } flag => JsonValue.Create(flag),
            _ => JsonValue.Create(value)
        };
    }

    private static int CountTrailingBackslashes(string line)
    {
        int count = 0;

        while (count < line.Length && line[line.Length - 1 - count] is '\\')
            count++;

        return count;
    }

    /// <summary>
    /// Creates the value of an operation. A text value has its placeholders replaced and is converted to its value type, as mc-image-helper does,
    /// while any other value is used as it is.
    /// </summary>
    private static JsonNode? CreatePatchValue(JsonNode? value, string? valueType, string prefix, Func<string, string?> getVariable, List<string> missingVariables)
    {
        if (value is not JsonValue text || text.GetValueKind() is not JsonValueKind.String)
            return value?.DeepClone();

        string interpolated = InterpolatePatchText(text.GetValue<string>(), prefix, getVariable, missingVariables);

        if (valueType?.StartsWith(value: "list of ", StringComparison.Ordinal) is not true)
            return ConvertPatchValue(interpolated, valueType);

        // Trailing empty items are dropped, as Java's String.split drops them.
        string itemType = valueType["list of ".Length..];
        List<string> items = [.. PatchListSeparator().Split(interpolated)];

        while (items.Count > 0 && items[^1].Length is 0)
            items.RemoveAt(items.Count - 1);

        return new JsonArray([.. items.Select(item => ConvertPatchValue(item, itemType))]);
    }

    private static JsonObject DecodePatchData(string text, string formatName)
    {
        return formatName switch
        {
            "json" => JsonNode.Parse(text, documentOptions: PatchJsonReadOptions) as JsonObject ?? throw new FormatException(message: "The file doesn't hold a JSON object"),
            "toml" => DecodeToml(text),
            _ => DecodeProperties(text)
        };
    }

    /// <summary>Decodes a file as UTF-8, or as ISO-8859-1 when it isn't valid UTF-8, as mc-image-helper does.</summary>
    private static PatchFileText DecodePatchFile(byte[] content)
    {
        int preambleLength = content.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        UTF8Encoding utf8 = new(preambleLength > 0, throwOnInvalidBytes: true);

        try
        {
            return new PatchFileText(utf8, utf8.GetString(content, preambleLength, content.Length - preambleLength));
        }
        catch (DecoderFallbackException)
        {
            return new PatchFileText(Encoding.Latin1, Encoding.Latin1.GetString(content));
        }
    }

    /// <summary>Decodes a properties file as Java's <c>Properties.load</c> does, keeping the order of its keys.</summary>
    private static JsonObject DecodeProperties(string text)
    {
        JsonObject properties = [];
        string[] lines = text.ReplaceLineEndings(replacementText: "\n").Split(separator: '\n');

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].TrimStart(trimChars: [' ', '\t', '\f']);

            if (line.Length is 0 || line[index: 0] is '#' or '!')
                continue;

            // A line that ends with an odd number of backslashes continues on the next one, without its leading whitespace.
            while (CountTrailingBackslashes(line) % 2 is 1 && index + 1 < lines.Length)
                line = line[..^1] + lines[++index].TrimStart(trimChars: [' ', '\t', '\f']);

            int keyEnd = 0;

            while (keyEnd < line.Length && line[keyEnd] is not ('=' or ':' or ' ' or '\t' or '\f'))
                keyEnd += line[keyEnd] is '\\' ? 2 : 1;

            keyEnd = Math.Min(keyEnd, line.Length);
            int valueStart = keyEnd;

            while (valueStart < line.Length && line[valueStart] is ' ' or '\t' or '\f')
                valueStart++;

            if (valueStart < line.Length && line[valueStart] is '=' or ':')
                valueStart++;

            while (valueStart < line.Length && line[valueStart] is ' ' or '\t' or '\f')
                valueStart++;

            properties[UnescapeProperty(line[..keyEnd])] = UnescapeProperty(line[valueStart..]);
        }

        return properties;
    }

    private static JsonObject DecodeToml(string text)
    {
        TomlReader reader = TomlReader.Create(text, TomlSerializerOptions.Default);

        // The document starts with its root table.
        while (reader.TokenType is not TomlTokenType.StartTable)
            AdvanceToml(reader);

        return ReadTomlValue(reader) as JsonObject ?? throw new FormatException(message: "The file doesn't hold a TOML table");
    }

    private static string DescribePatchOperation(JsonNode? operation)
    {
        return operation is not JsonObject { Count: 1 } wrapper || wrapper.First() is not (string kind, JsonObject arguments)
            ? "an operation"
            : arguments[propertyName: "path"] is JsonValue path ? $"{kind} {path}" : kind;
    }

    private static string EncodePatchData(JsonObject data, string formatName)
    {
        return formatName switch
        {
            "json" => data.ToJsonString(PatchJsonWriteOptions) + "\n",
            "toml" => EncodeToml(data),
            _ => EncodeProperties(data)
        };
    }

    /// <summary>
    /// Encodes properties as Java's <c>Properties.store</c> writes them to a text writer, without its date comment, and fails on values that
    /// aren't scalar, which a properties file can't hold.
    /// </summary>
    private static string EncodeProperties(JsonObject properties)
    {
        return string.Concat(
            properties.Select(
                static property =>
            $"{EscapeProperty(property.Key, isKey: true)}={EscapeProperty(FormatPropertyValue(property.Key, property.Value), isKey: false)}\n"
            )
        );
    }

    private static string EncodeToml(JsonObject data)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);

        TomlWriter writer = new(output, TomlSerializerOptions.Default);

        writer.WriteStartDocument();
        WriteTomlTable(writer, data, inline: false);
        writer.WriteEndDocument();

        return output.ToString();
    }

    private static string EscapeProperty(string text, bool isKey)
    {
        return string.Concat(
            text.Select(
                (character, index) => character switch
        {
            '\\' => @"\\",
            ' ' when isKey || index is 0 => @"\ ",
            '\t' => @"\t",
            '\n' => @"\n",
            '\r' => @"\r",
            '\f' => @"\f",
            '=' or ':' or '#' or '!' => $"\\{character}",
            _ => new string(character, count: 1)
        }
            )
        );
    }

    private static string FormatPatchPath(List<PatchPathSegment> segments, int count)
    {
        return "$" + string.Concat(segments.Take(count).Select(static segment => segment.Name is null ? $"[{segment.Index}]" : $"['{segment.Name}']"));
    }

    private static string FormatPropertyValue(string key, JsonNode? value)
    {
        return value switch
        {
            JsonValue text when text.GetValueKind() is JsonValueKind.String => text.GetValue<string>(),
            JsonValue flag when flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False => flag.GetValue<bool>() ? "true" : "false",
            JsonValue number when number.GetValueKind() is JsonValueKind.Number => number.ToJsonString(),
            _ => throw new InvalidOperationException($"Properties files can only hold text, numbers and booleans, which {key} isn't")
        };
    }

    private static string? GetPatchArgument(JsonObject arguments, string propertyName)
    {
        return arguments[propertyName] switch
        {
            null => null,
            JsonValue value when value.GetValueKind() is JsonValueKind.String => value.GetValue<string>(),
            _ => throw new InvalidOperationException($"The operation's {propertyName} isn't text")
        };
    }

    private static string? GetPatchFileFormat(string path)
    {
        return Path.GetExtension(path).ToUpperInvariant() switch
        {
            ".JSON" => "json",
            ".JSON5" => "json5",
            ".YAML" or ".YML" => "yaml",
            ".TOML" => "toml",
            ".PROPERTIES" => "properties",
            _ => null
        };
    }

    private static string? GetPatchText(JsonObject patch, string propertyName, string source)
    {
        return patch[propertyName] switch
        {
            null => null,
            JsonValue value when value.GetValueKind() is JsonValueKind.String => value.GetValue<string>(),
            _ => throw MinecraftPreparationFailure($"A patch definition of {source} has a {propertyName} that isn't text")
        };
    }

    /// <summary>
    /// Replaces each <c>${NAME}</c> whose name has the prefix with the variable's value, or with the trimmed content of the file that
    /// <c>NAME_FILE</c> names. Placeholders without the prefix stay as they are, and so do those of missing variables, which are collected.
    /// </summary>
    private static string InterpolatePatchText(string text, string prefix, Func<string, string?> getVariable, List<string> missingVariables)
    {
        return PatchPlaceholder().Replace(
            text,
            match =>
            {
                string name = match.Groups[groupnum: 1].Value;

                if (!name.StartsWith(prefix, StringComparison.Ordinal))
                    return match.Value;

                if (getVariable(name + "_FILE") is { } file)
                {
                    try
                    {
                        return File.ReadAllText(file, Encoding.UTF8).Trim();
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        throw MinecraftPreparationFailure($"The value of placeholder {name} couldn't be read from {file}: {exception.Message}", exception);
                    }
                }

                if (getVariable(name) is { } value)
                    return value;

                missingVariables.Add(name);

                return match.Value;
            }
        );
    }

    private static bool IsTomlTable(JsonNode? value)
    {
        return value is JsonObject || (value is JsonArray { Count: > 0 } array && array.All(static item => item is JsonObject));
    }

    private static bool? ParsePatchBoolean(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "TRUE" or "YES" => true,
            "FALSE" or "NO" => false,
            _ => null
        };
    }

    private static JsonObject ParsePatchJson(string json, string source)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: PatchJsonReadOptions) as JsonObject
                   ?? throw MinecraftPreparationFailure($"Patch definitions {source} don't hold a JSON object");
        }
        catch (JsonException exception)
        {
            throw MinecraftPreparationFailure($"Patch definitions {source} couldn't be read: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Parses the JSON paths that patch definitions use: properties as <c>$.a.b</c> or <c>$['a.b']</c>, and list items as <c>$.list[0]</c>.
    /// </summary>
    private static List<PatchPathSegment> ParsePatchPath(string path)
    {
        if (!path.StartsWith(value: '$'))
            throw new InvalidOperationException($"Path {path} doesn't start with $");

        List<PatchPathSegment> segments = [];
        int index = 1;

        while (index < path.Length)
        {
            if (path[index] is '.')
            {
                int nameEnd = path.IndexOfAny(['.', '['], index + 1) is var next and >= 0 ? next : path.Length;
                string name = path[(index + 1)..nameEnd];

                if (name.Length is 0 || name is "*")
                    throw UnsupportedPatchPath(path);

                segments.Add(new PatchPathSegment(name, Index: 0));
                index = nameEnd;

                continue;
            }

            int itemEnd = path[index] is '[' ? path.IndexOf(value: ']', index) : -1;

            if (itemEnd < 0)
                throw UnsupportedPatchPath(path);

            string item = path[(index + 1)..itemEnd].Trim();

            if (item.Length >= 2 && item[index: 0] is '\'' or '"' && item[^1] == item[index: 0])
                segments.Add(new PatchPathSegment(item[1..^1], Index: 0));
            else if (int.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out int itemIndex))
                segments.Add(new PatchPathSegment(Name: null, itemIndex));
            else
                throw UnsupportedPatchPath(path);

            index = itemEnd + 1;
        }

        return segments;
    }

    [GeneratedRegex(pattern: @"\s*,\s*")]
    private static partial Regex PatchListSeparator();

    [GeneratedRegex(pattern: @"\$\{([^}]+)}")]
    private static partial Regex PatchPlaceholder();

    /// <summary>
    /// Reads the patch definitions of a source: an HTTP or HTTPS URL or a file that holds a patch definition or a patch set, or a directory whose
    /// <c>*.json</c> files each hold a patch definition.
    /// </summary>
    private static async Task<List<JsonObject>> ReadPatchDefinitionsAsync(HttpClient httpClient, string source, CancellationToken cancellationToken)
    {
        if (Directory.Exists(source))
        {
            List<JsonObject> patches = [];

            foreach (string path in Directory.EnumerateFiles(source, searchPattern: "*.json").Order(StringComparer.Ordinal))
                patches.Add(ParsePatchJson(await ReadPatchSourceAsync(path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), path));

            return patches;
        }

        string json;

        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                json = await GetStringWithRetriesAsync(httpClient, uri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw MinecraftPreparationFailure(
                    $"Patch definitions {uri} couldn't be downloaded: {exception.Message}",
                    exception,
                    IsUpstreamUnavailable(exception, cancellationToken)
                );
            }
        }
        else
        {
            json = File.Exists(source)
                ? await ReadPatchSourceAsync(source, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
                : throw MinecraftPreparationFailure($"Patch definitions {source} are neither an existing file or directory nor an HTTP or HTTPS URL");
        }

        JsonObject root = ParsePatchJson(json, source);

        return !root.TryGetPropertyValue(propertyName: "patches", out JsonNode? patchSet)
            ? [root]
            : patchSet is not JsonArray patchList
            ? throw MinecraftPreparationFailure($"Patch definitions {source} have patches that aren't a list")
            : [.. patchList.Select(
            patch => patch as JsonObject ?? throw MinecraftPreparationFailure($"Patch definitions {source} have a patch that isn't an object")
        )];
    }

    private static async Task<string> ReadPatchSourceAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw MinecraftPreparationFailure($"Patch definitions {path} couldn't be read: {exception.Message}", exception);
        }
    }

    /// <summary>Reads the value the reader is on, and leaves the reader on the token after it. Dates keep their TOML type.</summary>
    private static JsonNode ReadTomlValue(TomlReader reader)
    {
        switch (reader.TokenType)
        {
            case TomlTokenType.StartTable:
                {
                    JsonObject table = [];
                    AdvanceToml(reader);

                    // Each value leaves the reader on the token after it, which is the next property or the end of the table.
                    while (reader.TokenType is TomlTokenType.PropertyName)
                    {
                        string name = reader.PropertyName ?? throw reader.CreateException(message: "A property has no name");
                        AdvanceToml(reader);
                        table[name] = ReadTomlValue(reader);
                    }

                    AdvanceToml(reader);

                    return table;
                }
            case TomlTokenType.StartArray:
                {
                    JsonArray array = [];
                    AdvanceToml(reader);

                    while (reader.TokenType is not TomlTokenType.EndArray)
                        array.Add(ReadTomlValue(reader));

                    AdvanceToml(reader);

                    return array;
                }
            default:
                {
                    JsonValue value = reader.TokenType switch
                    {
                        TomlTokenType.String => JsonValue.Create(reader.GetString()) ?? throw reader.CreateException(message: "A string has no value"),
                        TomlTokenType.Integer => JsonValue.Create(reader.GetInt64()),
                        TomlTokenType.Float => JsonValue.Create(reader.GetDouble()),
                        TomlTokenType.Boolean => JsonValue.Create(reader.GetBoolean()),
                        TomlTokenType.DateTime => JsonValue.Create(reader.GetTomlDateTime()) ?? throw reader.CreateException(message: "A date has no value"),
                        _ => throw reader.CreateException($"Unexpected {reader.TokenType}")
                    };

                    AdvanceToml(reader);

                    return value;
                }
        }
    }

    /// <returns>The value that the first <paramref name="count" /> segments of the path lead to.</returns>
    private static JsonNode ResolvePatchPath(JsonObject data, List<PatchPathSegment> segments, int count)
    {
        JsonNode current = data;

        for (int index = 0; index < count; index++)
        {
            current = (segments[index], current) switch
            {
                ({ Name: { } name }, JsonObject currentObject) when currentObject[name] is { } property => property,
                ({ Name: null, Index: var itemIndex }, JsonArray currentArray) when itemIndex < currentArray.Count && currentArray[itemIndex] is { } item => item,
                _ => throw new InvalidOperationException($"{FormatPatchPath(segments, index + 1)} doesn't exist")
            };
        }

        return current;
    }

    private static string UnescapeProperty(string text)
    {
        List<char> characters = new(text.Length);
        int index = 0;

        while (index < text.Length)
        {
            char character = text[index++];

            if (character is not '\\' || index == text.Length)
            {
                characters.Add(character);

                continue;
            }

            char escaped = text[index++];
            bool hasCode = escaped is 'u' && index + 4 <= text.Length;

            if (hasCode && ushort.TryParse(text.AsSpan(index, length: 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
            {
                characters.Add((char)code);
                index += 4;

                continue;
            }

            characters.Add(escaped switch { 't' => '\t', 'n' => '\n', 'r' => '\r', 'f' => '\f', _ => escaped });
        }

        return string.Concat(characters);
    }

    private static InvalidOperationException UnsupportedPatchPath(string path)
    {
        return new InvalidOperationException($"Path {path} isn't supported, which only names properties and list items");
    }

    private static void WriteTomlTable(TomlWriter writer, JsonObject table, bool inline)
    {
        if (inline)
            writer.WriteStartInlineTable();
        else
            writer.WriteStartTable();

        // A table's own values come before its tables, which TOML writes under headers of their own.
        foreach ((string key, JsonNode? value) in table.OrderBy(property => !inline && IsTomlTable(property.Value)))
        {
            writer.WritePropertyName(key);
            WriteTomlValue(writer, value, inline);
        }

        if (inline)
            writer.WriteEndInlineTable();
        else
            writer.WriteEndTable();
    }

    private static void WriteTomlValue(TomlWriter writer, JsonNode? value, bool inline)
    {
        switch (value)
        {
            case JsonObject table:
                {
                    WriteTomlTable(writer, table, inline);

                    break;
                }
            case JsonArray tables when !inline && IsTomlTable(tables):
                {
                    writer.WriteStartTableArray();

                    foreach (JsonObject table in tables.OfType<JsonObject>())
                        WriteTomlTable(writer, table, inline: false);

                    writer.WriteEndTableArray();

                    break;
                }
            case JsonArray array:
                {
                    writer.WriteStartArray();

                    foreach (JsonNode? item in array)
                        WriteTomlValue(writer, item, inline: true);

                    writer.WriteEndArray();

                    break;
                }
            case JsonValue dateTime when dateTime.TryGetValue(out TomlDateTime tomlDateTime):
                {
                    writer.WriteDateTimeValue(tomlDateTime);

                    break;
                }
            case JsonValue text when text.GetValueKind() is JsonValueKind.String:
                {
                    writer.WriteStringValue(text.GetValue<string>());

                    break;
                }
            case JsonValue flag when flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
                {
                    writer.WriteBooleanValue(flag.GetValue<bool>());

                    break;
                }
            case JsonValue integer when integer.TryGetValue(out long number):
                {
                    writer.WriteIntegerValue(number);

                    break;
                }
            case JsonValue number when number.GetValueKind() is JsonValueKind.Number:
                {
                    writer.WriteFloatValue(number.GetValue<double>());

                    break;
                }
            default:
                throw new FormatException(message: "TOML can't hold a null value");
        }
    }

    /// <summary>
    /// Applies the patch definitions that <c>VOID_PATCH_DEFINITIONS</c> lists to the files of the directory the game runs in, as itzg's
    /// <c>PATCH_DEFINITIONS</c> does on servers, and reports what it changed and what it skipped in the container log and the start's
    /// diagnostic session.
    /// </summary>
    private async Task ApplyPatchDefinitionsAsync(string gameDirectory, CancellationToken cancellationToken)
    {
        if (SplitList(Environment.GetEnvironmentVariable(PatchDefinitionsVariable)).Count is 0)
            return;

        using HttpClient httpClient = CreateUpstreamHttpClient();

        List<string> messages = await ApplyPatchDefinitionsAsync(httpClient, Environment.GetEnvironmentVariable, gameDirectory, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        foreach (string message in messages)
        {
            await Console.Error.WriteLineAsync(message).ConfigureAwait(continueOnCapturedContext: false);

            if (diagnostics?.CurrentSessionId is { } sessionId)
                await diagnostics.WriteOutputAsync(sessionId, stream: "preparation", message + Environment.NewLine, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    /// <summary>The text of a file to patch, and the encoding to write it back in.</summary>
    private sealed record PatchFileText(Encoding Encoding, string Text);

    /// <summary>A property of an object, by its name, or an item of a list, by its index when the name is <see langword="null" />.</summary>
    private sealed record PatchPathSegment(string? Name, int Index);
}
