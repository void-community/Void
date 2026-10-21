using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

public sealed class NeoForgePairFixture<TProtocolVersion>() : ModdedServersFixture<TProtocolVersion>(ModLoader.NeoForge, serverCount: 2) where TProtocolVersion : IProtocolVersionType;
