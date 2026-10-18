using System.Text.Json.Serialization;

namespace Void.Client.Models;

internal sealed record Position(
    [property: JsonPropertyName("x")] double XCoordinate,
    [property: JsonPropertyName("y")] double YCoordinate,
    [property: JsonPropertyName("z")] double ZCoordinate
);
