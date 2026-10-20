namespace Void.Client.Abstractions;

/// <summary>The menu the game showed once it finished loading, or why the agent can't tell when it does.</summary>
internal sealed record GameMenu(string? Screen, string? Problem);
