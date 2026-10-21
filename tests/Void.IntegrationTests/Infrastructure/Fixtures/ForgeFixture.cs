using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

public sealed class ForgeFixture<TProtocolVersion>() : ModdedServersFixture<TProtocolVersion>(ModLoader.Forge, serverCount: 1) where TProtocolVersion : IProtocolVersionType;
