namespace Void.Client;

internal sealed class GameCommandException : Exception
{
    public GameCommandException()
    {
    }

    public GameCommandException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public GameCommandException(string message) : base(message)
    {
    }

    public GameCommandException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public int StatusCode { get; }
}
