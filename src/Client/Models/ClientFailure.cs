namespace Void.Client;

internal sealed record ClientFailure(string Code, string Operation, string Stage, string Message, string ExceptionType, string StackTrace)
{
    public static ClientFailure FromException(string code, string operation, string stage, Exception exception)
    {
        return exception is GameClientException clientException
            ? clientException.Failure
            : new(code, operation, stage, exception.Message, exception.GetType().FullName ?? exception.GetType().Name, exception.ToString());
    }
}
