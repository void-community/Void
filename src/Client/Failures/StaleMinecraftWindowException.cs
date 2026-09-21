namespace Void.Client;

internal sealed class StaleMinecraftWindowException : Exception
{
    public StaleMinecraftWindowException()
    {
    }

    public StaleMinecraftWindowException(MinecraftWindowLease lease, Exception innerException)
        : base($"Minecraft window lease {lease.Generation} ({lease.Identifier}) is stale", innerException)
    {
        Lease = lease;
    }

    public StaleMinecraftWindowException(string message) : base(message)
    {
    }

    public StaleMinecraftWindowException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public MinecraftWindowLease Lease { get; }
}
