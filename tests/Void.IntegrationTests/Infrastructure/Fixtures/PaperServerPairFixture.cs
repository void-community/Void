namespace Void.IntegrationTests.Infrastructure.Fixtures;

public sealed class PaperServerPairFixture(PaperFixture paperFixture) : PaperServersFixture([paperFixture.Server1, paperFixture.Server2]);
