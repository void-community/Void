namespace Void.Client;

internal sealed record Position(
    [property: System.Text.Json.Serialization.JsonPropertyName("x")] double XCoordinate,
    [property: System.Text.Json.Serialization.JsonPropertyName("y")] double YCoordinate,
    [property: System.Text.Json.Serialization.JsonPropertyName("z")] double ZCoordinate
);
