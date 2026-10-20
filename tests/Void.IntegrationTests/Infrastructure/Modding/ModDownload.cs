using System;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>A mod file published on Modrinth, which is downloaded only when it has <see cref="Sha512"/>.</summary>
public sealed record ModDownload(string Project, string VersionNumber, string FileName, string Sha512, Uri DownloadUri);
