namespace Void.Client;

internal class GameClientException : Exception
{
    private readonly string _code = string.Empty;
    private readonly string _operation = string.Empty;
    private readonly string _stage = string.Empty;

    public GameClientException()
    {
    }

    public GameClientException(string code, string operation, string stage, string message, Exception? innerException = null) : base(message, innerException)
    {
        _code = code;
        _operation = operation;
        _stage = stage;
    }

    public GameClientException(string message) : base(message)
    {
    }

    public GameClientException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ClientFailure Failure => new(_code, _operation, _stage, Message, GetType().FullName ?? GetType().Name, ToString());
}
