namespace Void.Client;

internal static class X11FailureClassifier
{
    public static bool IsExplicitStaleWindow(ExternalProcessException exception)
    {
        return exception.FileName is "xdotool"
            && exception.StandardError.Contains(value: "BadWindow", StringComparison.Ordinal)
            && exception.StandardError.Contains(value: "invalid Window parameter", StringComparison.Ordinal);
    }
}
