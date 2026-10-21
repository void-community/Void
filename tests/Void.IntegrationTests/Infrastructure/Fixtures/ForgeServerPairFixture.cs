using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

public sealed class ForgeServerPairFixture<TProtocolVersion>() : ModdedServersFixture<TProtocolVersion>(ModLoader.Forge, serverCount: 2) where TProtocolVersion : IProtocolVersionType;
