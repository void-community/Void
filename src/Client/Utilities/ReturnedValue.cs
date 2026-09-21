namespace Void.Client;

internal static class ReturnedValue
{
    public static void Consume(bool value)
    {
        if (value)
            return;
    }

    public static void Consume(int value)
    {
        if (value >= 0)
            return;
    }

    public static void Consume(long value)
    {
        if (value >= 0)
            return;
    }

    public static void Consume(StopMode value)
    {
        if (value is StopMode.AlreadyStopped or StopMode.Graceful or StopMode.Forced)
            return;
    }
}
