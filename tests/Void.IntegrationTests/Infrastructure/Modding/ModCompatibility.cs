using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>
/// Reads what mods declare they need, so that mods Modrinth lists for a release but built for another one, or missing a mod they require,
/// are passed over before a server is started with them.
/// </summary>
internal static partial class ModCompatibility
{
    // Mods every game provides, by the ids mods require them as.
    private static readonly HashSet<string> GameModIds = new(StringComparer.OrdinalIgnoreCase) { "minecraft", "forge", "neoforge", "fml", "mcp", "javafml" };

    /// <returns>Why the mods can't run together on the build, or <see langword="null"/> when nothing they declare stands in the way.</returns>
    public static string? FindProblem(IReadOnlyList<ModFile> mods, ReleaseVersion release, LoaderBuild loader)
    {
        var declarations = mods.Select(mod => Read(mod.LocalPath)).ToArray();
        var providedModIds = new HashSet<string>(declarations.SelectMany(declaration => declaration.ModIds).Concat(GameModIds), StringComparer.OrdinalIgnoreCase);

        // Forge's language provider has the version of Forge's major version, such as javafml 41 for Forge 41.1.0.
        var forgeMajorVersion = loader.ServerLoaderVersion.Split('.')[0];

        foreach (var (mod, declaration) in mods.Zip(declarations))
        {
            var loaderVersionUnsupported = loader.ModLoader is ModLoader.Forge && declaration.LoaderVersion is { } loaderVersion && !MavenVersionRange.Contains(loaderVersion, forgeMajorVersion);

            if (loaderVersionUnsupported)
                return $"{mod.FileName} needs javafml {declaration.LoaderVersion}, not {forgeMajorVersion}";

            foreach (var dependency in declaration.Dependencies)
            {
                var requiredVersion = dependency.ModId.ToLowerInvariant() switch
                {
                    "minecraft" => release.ToString(),
                    "forge" when loader.ModLoader is ModLoader.Forge => loader.ServerLoaderVersion,
                    "neoforge" when loader.ModLoader is ModLoader.NeoForge => loader.ServerLoaderVersion,
                    _ => null
                };

                if (requiredVersion is not null && !MavenVersionRange.Contains(dependency.VersionRange, requiredVersion))
                    return $"{mod.FileName} needs {dependency.ModId} {dependency.VersionRange}, not {requiredVersion}";

                if (requiredVersion is null && !providedModIds.Contains(dependency.ModId))
                    return $"{mod.FileName} needs the mod {dependency.ModId}, which none of the mods provide";
            }
        }

        return null;
    }

    private static ModDeclaration Read(string jarPath)
    {
        using var jar = ZipFile.OpenRead(jarPath);
        var modsToml = jar.GetEntry("META-INF/neoforge.mods.toml") ?? jar.GetEntry("META-INF/mods.toml");

        return modsToml is null ? ReadLegacy(jar) : ReadModsToml(ReadText(modsToml));
    }

    /// <summary>Reads the mods.toml of Forge from 1.13 and of NeoForge, keeping only the dependencies a game can't start without.</summary>
    private static ModDeclaration ReadModsToml(string toml)
    {
        string? loaderVersion = null;
        string? table = null;
        var modIds = new List<string>();
        var dependencies = new List<ModDependency>();
        var dependency = new Dictionary<string, string>();

        foreach (var line in toml.Split('\n').Select(line => line.Trim()))
        {
            // A table header can be followed by a comment, such as [[mods]] #mandatory.
            if (line.StartsWith('[') && line.IndexOf(']') is var headerEnd and > 0)
            {
                AddDependency();
                table = line[..headerEnd].TrimStart('[').Split('.')[0];
                continue;
            }

            if (TomlStringAssignment().Match(line) is not { Success: true } assignment)
                continue;

            var (key, value) = (assignment.Groups["key"].Value, assignment.Groups["value"].Value);

            if (table is null && key is "loaderVersion")
                loaderVersion = value;
            else if (table is "mods" && key is "modId")
                modIds.Add(value);
            else if (table is "dependencies")
                dependency[key] = value;
        }

        AddDependency();
        return new ModDeclaration(loaderVersion, modIds, dependencies);

        void AddDependency()
        {
            // Forge up to 1.20.1 marks required dependencies as mandatory, later Forge and NeoForge as required.
            var required = dependency.GetValueOrDefault("mandatory") is "true" || dependency.GetValueOrDefault("type") is "required";

            if (required && dependency.TryGetValue("modId", out var modId))
                dependencies.Add(new ModDependency(modId, dependency.GetValueOrDefault("versionRange") ?? ""));

            dependency.Clear();
        }
    }

    /// <summary>Reads the mods of Forge up to 1.12.2, which declare required mods in their @Mod annotation, such as required-after:mcmultipart.</summary>
    private static ModDeclaration ReadLegacy(ZipArchive jar)
    {
        var modIds = new List<string>();
        var dependencies = new List<ModDependency>();

        if (jar.GetEntry("mcmod.info") is { } modInfo)
            modIds.AddRange(McModInfoModId().Matches(ReadText(modInfo)).Select(match => match.Groups["modId"].Value));

        foreach (var entry in jar.Entries.Where(entry => entry.FullName.EndsWith(".class", StringComparison.Ordinal)))
        {
            var classText = ReadText(entry, Encoding.Latin1);

            if (!classText.Contains("required", StringComparison.Ordinal))
                continue;

            dependencies.AddRange(LegacyRequiredMod().Matches(classText).Select(match => new ModDependency(match.Groups["modId"].Value, VersionRange: "")));
        }

        return new ModDeclaration(LoaderVersion: null, modIds, dependencies);
    }

    private static string ReadText(ZipArchiveEntry entry, Encoding? encoding = null)
    {
        using var reader = new StreamReader(entry.Open(), encoding ?? Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [GeneratedRegex("""^(?<key>\w+)\s*=\s*"(?<value>[^"]*)"|^(?<key>\w+)\s*=\s*(?<value>true|false)""")]
    private static partial Regex TomlStringAssignment();

    [GeneratedRegex(""""modid"\s*:\s*"(?<modId>[^"]+)"""", RegexOptions.IgnoreCase)]
    private static partial Regex McModInfoModId();

    [GeneratedRegex("required(?:-after|-before|-client|-server)?:(?<modId>[A-Za-z0-9_\\-]+)")]
    private static partial Regex LegacyRequiredMod();

    private sealed record ModDeclaration(string? LoaderVersion, IReadOnlyList<string> ModIds, IReadOnlyList<ModDependency> Dependencies);

    private sealed record ModDependency(string ModId, string VersionRange);
}
