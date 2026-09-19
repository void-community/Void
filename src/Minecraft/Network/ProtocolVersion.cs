using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Void.Minecraft.Serializers;

namespace Void.Minecraft.Network;

/// <summary>
/// Represents a numeric Minecraft Java Edition protocol version and the releases that share it.
/// </summary>
/// <remarks>
/// Constructing an instance registers its numeric value process-wide. Protocol versions compare and test equality by <see cref="Value" />, not by release names or reference identity.
/// </remarks>
[JsonConverter(typeof(ProtocolVersionJsonConverter))]
public class ProtocolVersion : IComparable
{
    private static readonly Dictionary<int, ProtocolVersion> Mapping = [];

    /// <summary>Represents releases 1.7.2 through 1.7.5 using protocol <c>4</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_7_2 = new(4, "1.7.2", "1.7.3", "1.7.4", "1.7.5");
    /// <summary>Represents releases 1.7.6 through 1.7.10 using protocol <c>5</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_7_6 = new(5, "1.7.6", "1.7.7", "1.7.8", "1.7.9", "1.7.10");
    /// <summary>Represents releases 1.8 through 1.8.9 using protocol <c>47</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_8 = new(47, "1.8", "1.8.1", "1.8.2", "1.8.3", "1.8.4", "1.8.5", "1.8.6", "1.8.7", "1.8.8", "1.8.9");
    /// <summary>Represents release 1.9 using protocol <c>107</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_9 = new(107, "1.9");
    /// <summary>Represents release 1.9.1 using protocol <c>108</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_9_1 = new(108, "1.9.1");
    /// <summary>Represents release 1.9.2 using protocol <c>109</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_9_2 = new(109, "1.9.2");
    /// <summary>Represents releases 1.9.3 and 1.9.4 using protocol <c>110</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_9_3 = new(110, "1.9.3", "1.9.4");
    /// <summary>Represents releases 1.10 through 1.10.2 using protocol <c>210</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_10 = new(210, "1.10", "1.10.1", "1.10.2");
    /// <summary>Represents release 1.11 using protocol <c>315</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_11 = new(315, "1.11");
    /// <summary>Represents releases 1.11.1 and 1.11.2 using protocol <c>316</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_11_1 = new(316, "1.11.1", "1.11.2");
    /// <summary>Represents release 1.12 using protocol <c>335</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_12 = new(335, "1.12");
    /// <summary>Represents release 1.12.1 using protocol <c>338</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_12_1 = new(338, "1.12.1");
    /// <summary>Represents release 1.12.2 using protocol <c>340</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_12_2 = new(340, "1.12.2");
    /// <summary>Represents release 1.13 using protocol <c>393</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_13 = new(393, "1.13");
    /// <summary>Represents release 1.13.1 using protocol <c>401</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_13_1 = new(401, "1.13.1");
    /// <summary>Represents release 1.13.2 using protocol <c>404</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_13_2 = new(404, "1.13.2");
    /// <summary>Represents release 1.14 using protocol <c>477</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_14 = new(477, "1.14");
    /// <summary>Represents release 1.14.1 using protocol <c>480</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_14_1 = new(480, "1.14.1");
    /// <summary>Represents release 1.14.2 using protocol <c>485</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_14_2 = new(485, "1.14.2");
    /// <summary>Represents release 1.14.3 using protocol <c>490</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_14_3 = new(490, "1.14.3");
    /// <summary>Represents release 1.14.4 using protocol <c>498</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_14_4 = new(498, "1.14.4");
    /// <summary>Represents release 1.15 using protocol <c>573</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_15 = new(573, "1.15");
    /// <summary>Represents release 1.15.1 using protocol <c>575</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_15_1 = new(575, "1.15.1");
    /// <summary>Represents release 1.15.2 using protocol <c>578</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_15_2 = new(578, "1.15.2");
    /// <summary>Represents release 1.16 using protocol <c>735</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_16 = new(735, "1.16");
    /// <summary>Represents release 1.16.1 using protocol <c>736</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_16_1 = new(736, "1.16.1");
    /// <summary>Represents release 1.16.2 using protocol <c>751</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_16_2 = new(751, "1.16.2");
    /// <summary>Represents release 1.16.3 using protocol <c>753</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_16_3 = new(753, "1.16.3");
    /// <summary>Represents releases 1.16.4 and 1.16.5 using protocol <c>754</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_16_4 = new(754, "1.16.4", "1.16.5");
    /// <summary>Represents release 1.17 using protocol <c>755</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_17 = new(755, "1.17");
    /// <summary>Represents release 1.17.1 using protocol <c>756</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_17_1 = new(756, "1.17.1");
    /// <summary>Represents releases 1.18 and 1.18.1 using protocol <c>757</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_18 = new(757, "1.18", "1.18.1");
    /// <summary>Represents release 1.18.2 using protocol <c>758</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_18_2 = new(758, "1.18.2");
    /// <summary>Represents release 1.19 using protocol <c>759</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_19 = new(759, "1.19");
    /// <summary>Represents releases 1.19.1 and 1.19.2 using protocol <c>760</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_19_1 = new(760, "1.19.1", "1.19.2");
    /// <summary>Represents release 1.19.3 using protocol <c>761</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_19_3 = new(761, "1.19.3");
    /// <summary>Represents release 1.19.4 using protocol <c>762</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_19_4 = new(762, "1.19.4");
    /// <summary>Represents releases 1.20 and 1.20.1 using protocol <c>763</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_20 = new(763, "1.20", "1.20.1");
    /// <summary>Represents release 1.20.2 using protocol <c>764</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_20_2 = new(764, "1.20.2");
    /// <summary>
    /// Represents Minecraft Java Edition 1.20.3 and 1.20.4, which use protocol version <c>765</c>.
    /// </summary>
    public static readonly ProtocolVersion MINECRAFT_1_20_3 = new(765, "1.20.3", "1.20.4");
    /// <summary>Represents releases 1.20.5 and 1.20.6 using protocol <c>766</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_20_5 = new(766, "1.20.5", "1.20.6");
    /// <summary>Represents releases 1.21 and 1.21.1 using protocol <c>767</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_21 = new(767, "1.21", "1.21.1");
    /// <summary>Represents releases 1.21.2 and 1.21.3 using protocol <c>768</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_21_2 = new(768, "1.21.2", "1.21.3");
    /// <summary>Represents release 1.21.4 using protocol <c>769</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_21_4 = new(769, "1.21.4");
    /// <summary>
    /// Represents Minecraft Java Edition 1.21.5, which uses protocol version <c>770</c>.
    /// </summary>
    public static readonly ProtocolVersion MINECRAFT_1_21_5 = new(770, "1.21.5");
    /// <summary>Represents release 1.21.6 using protocol <c>771</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_21_6 = new(771, "1.21.6");
    /// <summary>Represents releases 1.21.7 and 1.21.8 using protocol <c>772</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_21_7 = new(772, "1.21.7", "1.21.8");
    /// <summary>Represents releases 1.21.9 and 1.21.10 using protocol <c>773</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_21_9 = new(773, "1.21.9", "1.21.10");
    /// <summary>Represents release 1.21.11 using protocol <c>774</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_1_21_11 = new(774, "1.21.11");
    /// <summary>Represents releases 26.1 through 26.1.2 using protocol <c>775</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_26_1 = new(775, "26.1", "26.1.1", "26.1.2");
    public static readonly ProtocolVersion MINECRAFT_26_2 = new(776, "26.2");
    /// <summary>Represents release 26.3 using protocol <c>777</c>.</summary>
    public static readonly ProtocolVersion MINECRAFT_26_3 = new(777, "26.3");

    /// <summary>
    /// Initializes and globally registers a protocol version.
    /// </summary>
    /// <param name="value">The numeric protocol identifier.</param>
    /// <param name="names">The releases that use the identifier. The supplied array is retained without copying and can be empty.</param>
    /// <exception cref="InvalidOperationException"><paramref name="value" /> is already registered.</exception>
    public ProtocolVersion(int value, params ReleaseVersion[] names)
    {
        Value = value;
        Releases = names;

        if (!Mapping.TryAdd(value, this))
            throw new InvalidOperationException($"ProtocolVersion {value} already registered, use Get(<version>) instead");
    }
    
    /// <summary>
    /// Gets the registered protocol version with the greatest numeric identifier.
    /// </summary>
    public static ProtocolVersion Latest => Mapping.MaxBy(kv => kv.Key).Value;

    /// <summary>
    /// Gets the registered protocol version with the smallest numeric identifier.
    /// </summary>
    public static ProtocolVersion Oldest => Mapping.MinBy(kv => kv.Key).Value;

    /// <summary>
    /// Gets the numeric Minecraft protocol identifier.
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// Gets the releases associated with this protocol identifier.
    /// </summary>
    /// <value>The array supplied to the constructor; it is not copied.</value>
    public ReleaseVersion[] Releases { get; }

    /// <summary>
    /// Gets the first associated release.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException"><see cref="Releases" /> is empty.</exception>
    public ReleaseVersion FirstRelease => Releases[0];

    /// <summary>
    /// Gets the last associated release.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException"><see cref="Releases" /> is empty.</exception>
    public ReleaseVersion LastRelease => Releases[^1];

    /// <summary>
    /// Enumerates registered protocol versions from this version to another version, inclusively.
    /// </summary>
    /// <param name="other">The version at the opposite end of the range.</param>
    /// <returns>The registered versions between the endpoints, ascending or descending to preserve endpoint order.</returns>
    public IEnumerable<ProtocolVersion> RangeTo(ProtocolVersion other)
    {
        return Range(this, other);
    }

    /// <summary>
    /// Compares this version with an object by numeric protocol identifier.
    /// </summary>
    /// <param name="obj">A protocol version to compare, or <see langword="null" />.</param>
    /// <returns>A positive value for <see langword="null" />; otherwise, the comparison of numeric identifiers.</returns>
    /// <exception cref="ArgumentException"><paramref name="obj" /> is neither <see langword="null" /> nor a <see cref="ProtocolVersion" />.</exception>
    public int CompareTo(object? obj)
    {
        return obj switch
        {
            null => 1,
            ProtocolVersion otherVersion => CompareTo(otherVersion),
            _ => throw new ArgumentException($"Object is not a {nameof(ProtocolVersion)}")
        };
    }

    /// <summary>
    /// Compares this version with another version by numeric protocol identifier.
    /// </summary>
    /// <param name="other">The version to compare, or <see langword="null" />.</param>
    /// <returns>A positive value when <paramref name="other" /> is <see langword="null" />; otherwise, a negative, zero, or positive value according to identifier ordering.</returns>
    public int CompareTo(ProtocolVersion? other)
    {
        return other is null ? 1 : Value.CompareTo(other.Value); // null is considered greater than non-null
    }

    /// <summary>
    /// Gets a previously registered protocol version by numeric identifier.
    /// </summary>
    /// <param name="version">The numeric identifier to find.</param>
    /// <returns>The registered protocol-version instance.</returns>
    /// <exception cref="KeyNotFoundException"><paramref name="version" /> is not registered.</exception>
    public static ProtocolVersion Get(int version)
    {
        return Mapping[version];
    }

    /// <summary>
    /// Gets a registered protocol version or creates and registers an unnamed version for an unknown identifier.
    /// </summary>
    /// <param name="value">The numeric protocol identifier.</param>
    /// <returns>The existing version, or a newly registered version whose <see cref="Releases" /> array is empty.</returns>
    public static ProtocolVersion From(int value)
    {
        if (Mapping.TryGetValue(value, out var version))
            return version;
        
        return new ProtocolVersion(value);
    }

    /// <summary>
    /// Enumerates every registered protocol version in ascending numeric order.
    /// </summary>
    /// <returns>The inclusive range from <see cref="Oldest" /> through <see cref="Latest" />.</returns>
    public static IEnumerable<ProtocolVersion> Range()
    {
        return Range(Oldest, Latest);
    }

    /// <summary>
    /// Enumerates registered protocol versions between two inclusive endpoints.
    /// </summary>
    /// <param name="left">The first endpoint and the value that determines enumeration direction.</param>
    /// <param name="right">The second endpoint.</param>
    /// <returns>Matching registered versions in ascending order when <paramref name="left" /> is not greater than <paramref name="right" />; otherwise, descending order.</returns>
    public static IEnumerable<ProtocolVersion> Range(ProtocolVersion left, ProtocolVersion right)
    {
        var start = Min(left, right);
        var end = Max(left, right);

        var descending = left > right;
        var versions = Mapping.Where(pair => pair.Key >= start.Value && pair.Key <= end.Value).Select(pair => pair.Value);

        return descending ? versions.OrderDescending() : versions.Order();
    }

    /// <summary>
    /// Returns the protocol version with the smaller numeric identifier.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <returns>The lesser version; when identifiers are equal, <paramref name="version2" /> is returned.</returns>
    public static ProtocolVersion Min(ProtocolVersion version1, ProtocolVersion version2)
    {
        return version1 < version2 ? version1 : version2;
    }

    /// <summary>
    /// Returns the protocol version with the greater numeric identifier.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <returns>The greater version; when identifiers are equal, <paramref name="version2" /> is returned.</returns>
    public static ProtocolVersion Max(ProtocolVersion version1, ProtocolVersion version2)
    {
        return version1 > version2 ? version1 : version2;
    }

    /// <summary>
    /// Determines whether one protocol identifier is greater than another.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> has a greater identifier; otherwise, <see langword="false" />.</returns>
    public static bool operator >(ProtocolVersion left, ProtocolVersion right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <summary>
    /// Determines whether one protocol identifier is less than another.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> has a smaller identifier; otherwise, <see langword="false" />.</returns>
    public static bool operator <(ProtocolVersion left, ProtocolVersion right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <summary>
    /// Determines whether one protocol identifier is greater than or equal to another.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> is not less than <paramref name="right" />; otherwise, <see langword="false" />.</returns>
    public static bool operator >=(ProtocolVersion left, ProtocolVersion right)
    {
        return left.CompareTo(right) >= 0;
    }

    /// <summary>
    /// Determines whether <paramref name="left"/> is less than or equal to <paramref name="right"/> by comparing their protocol values.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> is not greater than <paramref name="right" />; otherwise, <see langword="false" />.</returns>
    public static bool operator <=(ProtocolVersion left, ProtocolVersion right)
    {
        return left.CompareTo(right) <= 0;
    }

    /// <summary>
    /// Determines whether two protocol versions are both null or have equal numeric identifiers.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true" /> when the operands are equal; otherwise, <see langword="false" />.</returns>
    public static bool operator ==(ProtocolVersion? left, ProtocolVersion? right)
    {
        if (left is null)
            return right is null;

        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two protocol versions have different null state or numeric identifiers.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true" /> when the operands differ; otherwise, <see langword="false" />.</returns>
    public static bool operator !=(ProtocolVersion? left, ProtocolVersion? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Moves by an index offset within the numerically sorted registered protocol versions.
    /// </summary>
    /// <param name="protocolVersion">The starting registered version.</param>
    /// <param name="offset">The number of registered entries to move; negative values move toward older versions.</param>
    /// <returns>The registered version at the resulting index.</returns>
    /// <exception cref="InvalidOperationException">The resulting index is outside the registered range, including when the starting version is not registered.</exception>
    public static ProtocolVersion operator +(ProtocolVersion protocolVersion, int offset)
    {
        var sortedVersions = Mapping.Values.OrderBy(protocolVersion => protocolVersion.Value).ToList();
        var currentIndex = sortedVersions.IndexOf(protocolVersion);
        var newIndex = currentIndex + offset;

        if (newIndex < 0 || newIndex >= sortedVersions.Count)
            throw new InvalidOperationException($"No ProtocolVersion at offset {offset} from {protocolVersion}");

        return sortedVersions[newIndex];
    }

    /// <summary>
    /// Moves backward by an index offset within the registered protocol versions.
    /// </summary>
    /// <param name="version">The starting registered version.</param>
    /// <param name="offset">The number of registered entries to move toward older versions; negative values move toward newer versions.</param>
    /// <returns>The registered version at the resulting index.</returns>
    /// <exception cref="InvalidOperationException">The resulting index is outside the registered range, including when the starting version is not registered.</exception>
    public static ProtocolVersion operator -(ProtocolVersion version, int offset)
    {
        return version + -offset;
    }

    /// <summary>
    /// Determines whether an object is a protocol version with the same numeric identifier.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns><see langword="true" /> for a protocol version with an equal <see cref="Value" />; otherwise, <see langword="false" />.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is not ProtocolVersion version)
            return false;

        return Value == version.Value;
    }

    /// <summary>
    /// Returns the hash code of the numeric protocol identifier.
    /// </summary>
    /// <returns>The hash code of <see cref="Value" />.</returns>
    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    /// <summary>
    /// Formats the associated release span.
    /// </summary>
    /// <returns>The single release name, or <c>FirstRelease-LastRelease</c> when multiple releases share the protocol.</returns>
    /// <exception cref="IndexOutOfRangeException"><see cref="Releases" /> is empty.</exception>
    public override string ToString()
    {
        return Releases.Length is 1 ? FirstRelease : $"{FirstRelease}-{LastRelease}";
    }
}
