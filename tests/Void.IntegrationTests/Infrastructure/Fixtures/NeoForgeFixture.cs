using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

public sealed class NeoForgeFixture<TProtocolVersion>() : ModdedServersFixture<TProtocolVersion>(ModLoader.NeoForge, serverCount: 1) where TProtocolVersion : IProtocolVersionType;
