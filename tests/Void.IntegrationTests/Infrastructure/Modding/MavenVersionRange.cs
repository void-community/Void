using System;
using System.Linq;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>Matches versions against the Maven version ranges mods declare, such as <c>[43,)</c> or <c>[1.19.2,1.20)</c>.</summary>
internal static class MavenVersionRange
{
    /// <summary>Tells whether <paramref name="version"/> is in <paramref name="range"/>; a range that can't be read accepts every version.</summary>
    public static bool Contains(string range, string version)
    {
        range = range.Trim();

        // A bare version is only a preference in Maven, and a placeholder was never filled in by the mod's build.
        if (range.Length is 0 || range is "*" || range.Contains('$') || range[0] is not ('[' or '('))
            return true;

        // Restrictions such as [1.20,1.20.1],[1.20.2,) are alternatives.
        for (var start = range.IndexOfAny(['[', '(']); start >= 0; start = range.IndexOfAny(['[', '('], start + 1))
        {
            var end = range.IndexOfAny([']', ')'], start);

            if (end < 0)
                return true;

            if (RestrictionContains(range[start..(end + 1)], version))
                return true;

            start = end;
        }

        return false;
    }

    private static bool RestrictionContains(string restriction, string version)
    {
        var lowerInclusive = restriction[0] is '[';
        var upperInclusive = restriction[^1] is ']';
        var bounds = restriction.Trim('[', '(', ']', ')').Split(',');

        if (bounds is [var exact])
            return Compare(version, exact.Trim()) is 0;

        var lower = bounds[0].Trim();
        var upper = bounds[1].Trim();
        var aboveLower = lower.Length is 0 || (lowerInclusive ? Compare(version, lower) >= 0 : Compare(version, lower) > 0);
        var belowUpper = upper.Length is 0 || (upperInclusive ? Compare(version, upper) <= 0 : Compare(version, upper) < 0);

        return aboveLower && belowUpper;
    }

    private static int Compare(string left, string right)
    {
        var leftNumbers = Numbers(left);
        var rightNumbers = Numbers(right);

        for (var index = 0; index < Math.Max(leftNumbers.Length, rightNumbers.Length); index++)
        {
            var comparison = leftNumbers.ElementAtOrDefault(index).CompareTo(rightNumbers.ElementAtOrDefault(index));

            if (comparison is not 0)
                return comparison;
        }

        return 0;
    }

    private static int[] Numbers(string version)
    {
        return [.. version.Split('-')[0].Split('.').Select(component => int.TryParse(new string([.. component.TakeWhile(char.IsDigit)]), out var number) ? number : 0)];
    }
}
