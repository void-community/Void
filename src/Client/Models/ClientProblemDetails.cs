namespace Void.Client.Models;

internal sealed record ClientProblemDetails(string Type, string Title, int Status, string Detail, ClientFailure? Failure);
