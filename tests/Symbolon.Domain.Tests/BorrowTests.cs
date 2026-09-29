using FluentAssertions;
using Symbolon.Crypto;
using Symbolon.Domain.Borrow;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.Domain.Tests;

public sealed class BorrowTests
{
    [Fact]
    public void BorrowPolicyEvaluator_Disabled_ReturnsDenied()
    {
        var result = BorrowPolicyEvaluator.Evaluate(
            borrowEnabled: false,
            maxDurationDays: 14,
            maxConcurrent: 5,
            requestedDays: 3,
            currentActiveBorrowsCount: 0);

        result.IsAllowed.Should().BeFalse();
        result.ProblemType.Should().Be(ProblemTypes.BorrowDisabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(15)]
    [InlineData(31)]
    public void BorrowPolicyEvaluator_DurationOutOfRange_ReturnsDenied(int requestedDays)
    {
        var result = BorrowPolicyEvaluator.Evaluate(
            borrowEnabled: true,
            maxDurationDays: 14,
            maxConcurrent: 5,
            requestedDays: requestedDays,
            currentActiveBorrowsCount: 0);

        result.IsAllowed.Should().BeFalse();
        result.ProblemType.Should().Be(ProblemTypes.BorrowDurationExceeded);
    }

    [Fact]
    public void BorrowPolicyEvaluator_MaxConcurrentExceeded_ReturnsDenied()
    {
        var result = BorrowPolicyEvaluator.Evaluate(
            borrowEnabled: true,
            maxDurationDays: 14,
            maxConcurrent: 5,
            requestedDays: 5,
            currentActiveBorrowsCount: 5);

        result.IsAllowed.Should().BeFalse();
        result.ProblemType.Should().Be(ProblemTypes.BorrowLimitExceeded);
    }

    [Fact]
    public void BorrowPolicyEvaluator_ValidRequest_ReturnsAllowed()
    {
        var result = BorrowPolicyEvaluator.Evaluate(
            borrowEnabled: true,
            maxDurationDays: 14,
            maxConcurrent: 5,
            requestedDays: 7,
            currentActiveBorrowsCount: 2);

        result.IsAllowed.Should().BeTrue();
        result.FailureReason.Should().BeNull();
        result.ProblemType.Should().BeNull();
    }

    [Fact]
    public void ProofOfPossession_GenerateKeyAndSign_VerifiesSuccessfully()
    {
        var keyPair = ProofOfPossessionEngine.GenerateKeyPair();
        keyPair.PublicKeyJwk.Should().Contain("P-256");
        keyPair.PrivateKeyJwk.Should().Contain("\"d\":");

        string nonce = "sample-challenge-nonce-0123456789abcdef";
        string signature = ProofOfPossessionEngine.SignChallenge(nonce, keyPair.PrivateKeyJwk);
        signature.Should().NotBeNullOrWhiteSpace();

        bool verified = ProofOfPossessionEngine.VerifyProof(nonce, signature, keyPair.PublicKeyJwk);
        verified.Should().BeTrue();
    }

    [Fact]
    public void ProofOfPossession_TamperedNonce_FailsVerification()
    {
        var keyPair = ProofOfPossessionEngine.GenerateKeyPair();
        string nonce = "sample-challenge-nonce-0123456789abcdef";
        string signature = ProofOfPossessionEngine.SignChallenge(nonce, keyPair.PrivateKeyJwk);

        bool verified = ProofOfPossessionEngine.VerifyProof("tampered-nonce", signature, keyPair.PublicKeyJwk);
        verified.Should().BeFalse();
    }

    [Fact]
    public void ProofOfPossession_WrongKey_FailsVerification()
    {
        var keyPair1 = ProofOfPossessionEngine.GenerateKeyPair();
        var keyPair2 = ProofOfPossessionEngine.GenerateKeyPair();

        string nonce = "sample-challenge-nonce-0123456789abcdef";
        string signature = ProofOfPossessionEngine.SignChallenge(nonce, keyPair1.PrivateKeyJwk);

        bool verified = ProofOfPossessionEngine.VerifyProof(nonce, signature, keyPair2.PublicKeyJwk);
        verified.Should().BeFalse();
    }

    [Fact]
    public async Task InMemoryReturnChallengeStore_CreateAndConsume_SucceedsOnce()
    {
        var store = new InMemoryReturnChallengeStore();
        string leaseId = "lse_test_123";

        var (nonce, expiresAt) = await store.CreateChallengeAsync(leaseId, TimeSpan.FromMinutes(5));
        nonce.Should().NotBeNullOrWhiteSpace();
        expiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        // First consume succeeds
        bool consumedFirst = await store.TryConsumeChallengeAsync(leaseId, nonce, DateTimeOffset.UtcNow);
        consumedFirst.Should().BeTrue();

        // Second consume (replay attack) fails
        bool consumedSecond = await store.TryConsumeChallengeAsync(leaseId, nonce, DateTimeOffset.UtcNow);
        consumedSecond.Should().BeFalse();
    }

    [Fact]
    public async Task InMemoryReturnChallengeStore_ExpiredChallenge_FailsToConsume()
    {
        var store = new InMemoryReturnChallengeStore();
        string leaseId = "lse_expired_test";

        var (nonce, _) = await store.CreateChallengeAsync(leaseId, TimeSpan.FromSeconds(1));

        bool consumed = await store.TryConsumeChallengeAsync(
            leaseId,
            nonce,
            DateTimeOffset.UtcNow.AddMinutes(1)); // consumed after expiration

        consumed.Should().BeFalse();
    }

    [Fact]
    public async Task InMemoryReturnChallengeStore_WrongNonce_FailsToConsume()
    {
        var store = new InMemoryReturnChallengeStore();
        string leaseId = "lse_wrong_nonce_test";

        await store.CreateChallengeAsync(leaseId, TimeSpan.FromMinutes(5));

        bool consumed = await store.TryConsumeChallengeAsync(
            leaseId,
            "WRONG_NONCE",
            DateTimeOffset.UtcNow);

        consumed.Should().BeFalse();
    }
}
