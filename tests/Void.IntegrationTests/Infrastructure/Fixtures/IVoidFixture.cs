using Void.IntegrationTests.Infrastructure.Harness.Sides;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>Provides the proxy in front of the servers a test class plays on.</summary>
public interface IVoidFixture
{
    public VoidProxy VoidProxy { get; }
}
