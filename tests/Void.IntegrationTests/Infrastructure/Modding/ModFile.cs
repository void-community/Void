namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>A downloaded and verified mod file, installed into clients and servers alike.</summary>
public sealed record ModFile(ModDownload Source, string LocalPath)
{
    public string FileName => Source.FileName;
}
