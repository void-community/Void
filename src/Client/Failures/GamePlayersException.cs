namespace Void.Client;

internal sealed class GamePlayersException : Exception
{
    private readonly string? _code;
    private readonly string? _stage;

    public GamePlayersException()
    {
    }

    public GamePlayersException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public GamePlayersException(int statusCode, string code, string stage, string message, Exception? innerException = null) : base(message, innerException)
    {
        StatusCode = statusCode;
        _code = code;
        _stage = stage;
    }

    public GamePlayersException(string message) : base(message)
    {
    }

    public GamePlayersException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ClientFailure? Failure => _code is null || _stage is null
        ? null
        : new(_code, Operation: "players", _stage, Message, GetType().FullName ?? GetType().Name, ToString());

    public int StatusCode { get; }
}
