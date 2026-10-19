using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Void.Minecraft;

/// <summary>
///     Represents a Minecraft release version number in <c>Major.Minor[.Patch]</c> form.
/// </summary>
/// <remarks>
///     <para>
///         Patch is optional: when it is <c>0</c>, <see cref="ToString" /> omits it and returns the two-component form
///         (for example, <c>"1.21"</c>). A non-zero patch produces the three-component form (for example,
///         <c>"1.21.4"</c>).
///     </para>
///     <para>
///         Ordering follows the natural numeric ordering of the components: Major first, then Minor, then Patch.
///     </para>
///     <para>
///         Implicit conversions are provided in both directions so that a
///         <see cref="ReleaseVersion" /> can be assigned from or cast to a <see langword="string" /> without an explicit
///         call to <see cref="Parse" />.
///     </para>
/// </remarks>
/// <param name="Major">The major component of the version number (for example, <c>1</c> in <c>1.21.4</c>).</param>
/// <param name="Minor">The minor component of the version number (for example, <c>21</c> in <c>1.21.4</c>).</param>
/// <param name="Patch">
///     The patch component of the version number (for example, <c>4</c> in <c>1.21.4</c>). Pass <c>0</c>
///     for releases that have no patch segment.
/// </param>
public record ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    /// <summary>
    /// Compares the current instance with another version and returns an integer that indicates whether the current
    /// instance precedes, follows, or occurs in the same position in the sort order as the other version.
    /// </summary>
    /// <param name="other">The version to compare with this instance.</param>
    /// <returns>A value that indicates the relative order of the objects being compared. Less than zero if this instance
    /// precedes <paramref name="other"/> in the sort order. Zero if this instance occurs in the same position as
    /// <paramref name="other"/> in the sort order. Greater than zero if this instance follows <paramref name="other"/>
    /// in the sort order, or if <paramref name="other"/> is <see langword="null"/>.</returns>
    public int CompareTo(ReleaseVersion? other)
    {
        return other is null ? 1 : (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
    }

    /// <summary>
    /// Converts the version to its string representation.
    /// </summary>
    /// <returns>A string in the format "Major.Minor" when Patch is zero, otherwise "Major.Minor.Patch".</returns>
    public override string ToString()
    {
        return Patch is 0
            ? $"{Major}.{Minor}"
            : $"{Major}.{Minor}.{Patch}";
    }

    /// <summary>
    /// Attempts to parse a version string into a <see cref="ReleaseVersion"/> instance.
    /// </summary>
    /// <param name="versionString">The version string to parse in "major.minor" or "major.minor.patch" format.</param>
    /// <param name="version">When successful, contains the parsed <see cref="ReleaseVersion"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the version string was successfully parsed; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string versionString, [MaybeNullWhen(false)] out ReleaseVersion version)
    {
        return (version = versionString.Split('.') switch
        {
            [var major, var minor] when int.TryParse(major, out var majorValue) && int.TryParse(minor, out var minorValue)
                => new ReleaseVersion(majorValue, minorValue, Patch: 0),
            [var major, var minor, var patch] when int.TryParse(major, out var majorValue) && int.TryParse(minor, out var minorValue) && int.TryParse(patch, out var patchValue)
                => new ReleaseVersion(majorValue, minorValue, patchValue),
            _ => null
        }) is not null;
    }

    /// <summary>
    /// Converts the string representation of a version to its <see cref="ReleaseVersion"/> equivalent.
    /// </summary>
    /// <param name="versionString">A string containing the version to convert.</param>
    /// <returns>A <see cref="ReleaseVersion"/> equivalent to the version contained in <paramref name="versionString"/>.</returns>
    /// <exception cref="FormatException"><paramref name="versionString"/> is not in a valid format.</exception>
    public static ReleaseVersion Parse(string versionString)
    {
        return !TryParse(versionString, out var version)
            ? throw new FormatException($"Invalid version string: '{versionString}'.")
            : version;
    }

    /// <summary>
    /// Converts a string to a ReleaseVersion.
    /// </summary>
    /// <param name="versionString">The string representation of the version.</param>
    public static implicit operator ReleaseVersion(string versionString)
    {
        return Parse(versionString);
    }

    /// <summary>
    /// Implicitly converts a ReleaseVersion to its string representation.
    /// </summary>
    /// <param name="version">The ReleaseVersion to convert.</param>
    public static implicit operator string(ReleaseVersion version)
    {
        return version.ToString();
    }

    /// <summary>
    /// Determines whether one release version is greater than another. <see langword="null"/> sorts before any version.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> is a later release; otherwise, <see langword="false" />.</returns>
    public static bool operator >(ReleaseVersion? left, ReleaseVersion? right)
    {
        return Comparer<ReleaseVersion>.Default.Compare(left, right) > 0;
    }

    /// <summary>
    /// Determines whether one release version is less than another. <see langword="null"/> sorts before any version.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> is an earlier release; otherwise, <see langword="false" />.</returns>
    public static bool operator <(ReleaseVersion? left, ReleaseVersion? right)
    {
        return Comparer<ReleaseVersion>.Default.Compare(left, right) < 0;
    }

    /// <summary>
    /// Determines whether one release version is greater than or equal to another. <see langword="null"/> sorts before any version.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> is not an earlier release; otherwise, <see langword="false" />.</returns>
    public static bool operator >=(ReleaseVersion? left, ReleaseVersion? right)
    {
        return Comparer<ReleaseVersion>.Default.Compare(left, right) >= 0;
    }

    /// <summary>
    /// Determines whether one release version is less than or equal to another. <see langword="null"/> sorts before any version.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> is not a later release; otherwise, <see langword="false" />.</returns>
    public static bool operator <=(ReleaseVersion? left, ReleaseVersion? right)
    {
        return Comparer<ReleaseVersion>.Default.Compare(left, right) <= 0;
    }
}
