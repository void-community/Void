namespace Void.Client;

internal sealed class DiagnosticsOptions
{
    public string Directory { get; set; } = "/var/lib/void-client/diagnostics";
    public int MaximumSessions { get; set; } = 10;
    public int MaximumTotalMb { get; set; } = 256;
    public int MaximumSessionMb { get; set; } = 32;

    public static DiagnosticsOptions FromConfiguration(IConfiguration configuration)
    {
        var defaults = new DiagnosticsOptions();

        return new DiagnosticsOptions
        {
            Directory = configuration.GetValue<string>(key: "VOID_DIAGNOSTICS_DIRECTORY") ?? defaults.Directory,
            MaximumSessions = configuration.GetValue(key: "VOID_DIAGNOSTICS_MAXIMUM_SESSIONS", defaults.MaximumSessions),
            MaximumTotalMb = configuration.GetValue(key: "VOID_DIAGNOSTICS_MAXIMUM_TOTAL_MB", defaults.MaximumTotalMb),
            MaximumSessionMb = configuration.GetValue(key: "VOID_DIAGNOSTICS_MAXIMUM_SESSION_MB", defaults.MaximumSessionMb)
        };
    }
}
