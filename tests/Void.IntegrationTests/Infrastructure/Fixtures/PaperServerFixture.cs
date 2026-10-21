namespace Void.IntegrationTests.Infrastructure.Fixtures;

public sealed class PaperServerFixture(PaperFixture paperFixture) : PaperServersFixture([paperFixture.Server1]);
