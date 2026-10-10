using FluentAssertions;
using Xunit;

namespace Achilles.Crypto.Tests;

public sealed class AchillesKeyRingTests
{
    [Fact]
    public void KeyRing_AddAndRetrieveKey_Works()
    {
        using var ring = new AchillesKeyRing();
        using var ecKey = Es256SignatureProvider.GenerateKey("kid-1");

        ring.Add(ecKey);

        bool found = ring.TryGet("kid-1", Alg.Es256, out var provider);
        found.Should().BeTrue();
        provider.Should().BeSameAs(ecKey);

        bool notFoundWrongAlg = ring.TryGet("kid-1", Alg.MlDsa65, out _);
        notFoundWrongAlg.Should().BeFalse();

        bool notFoundWrongKid = ring.TryGet("kid-2", Alg.Es256, out _);
        notFoundWrongKid.Should().BeFalse();
    }

    [Fact]
    public void KeyRing_RevokeKid_ReportsRevoked()
    {
        using var ring = new AchillesKeyRing();
        ring.IsRevoked("kid-revoked").Should().BeFalse();

        ring.Revoke("kid-revoked");
        ring.IsRevoked("kid-revoked").Should().BeTrue();
    }
}
