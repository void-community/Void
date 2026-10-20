using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>Skips tests whose game isn't released yet with a warning, and tests whose game will never exist without one.</summary>
public static class SkippedTestWarnings
{
    private static readonly ConcurrentDictionary<string, bool> AnnotatedReasons = new();

    [DoesNotReturn]
    public static void Skip(string reason, bool warn)
    {
        if (warn)
        {
            TestContext.Current.AddWarning(reason);

            // GitHub Actions shows each distinct reason once on the run, rather than only in the test output.
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is "true" && AnnotatedReasons.TryAdd(reason, true))
                Console.WriteLine($"::warning title=Integration tests skipped::{reason.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A")}");
        }

        Assert.Skip(reason);
    }
}
