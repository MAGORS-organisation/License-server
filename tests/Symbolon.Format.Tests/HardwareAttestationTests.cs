using FluentAssertions;
using Symbolon.Crypto;
using Symbolon.Format.Attestation;
using Xunit;

namespace Symbolon.Format.Tests;

public sealed class HardwareAttestationTests
{
    [Fact]
    public void TpmQuoteVerifier_Accepts_Valid_Quote_And_Matching_Nonce()
    {
        using var aik = Es256SignatureProvider.GenerateKey("aik-hsm-test-01");
        string nonce = "anti_replay_nonce_123456";
        int[] pcr = [0, 1, 7];

        var quote = TpmQuoteGenerator.CreateQuote(aik, EnclaveType.Tpm20, nonce, pcr);

        var result = TpmQuoteVerifier.VerifyQuote(quote, aik, expectedNonce: nonce);

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
        result.Quote.Should().NotBeNull();
        result.Quote!.EnclaveType.Should().Be(EnclaveType.Tpm20);
        result.Quote.AikId.Should().Be("aik-hsm-test-01");
    }

    [Fact]
    public void TpmQuoteVerifier_Rejects_Replayed_Quote_With_Mismatched_Nonce()
    {
        using var aik = Es256SignatureProvider.GenerateKey("aik-hsm-test-02");
        string originalNonce = "nonce-original-token";
        string attackerNonce = "nonce-replayed-challenge";

        var quote = TpmQuoteGenerator.CreateQuote(aik, EnclaveType.Tpm20, originalNonce, [0, 2]);

        var result = TpmQuoteVerifier.VerifyQuote(quote, aik, expectedNonce: attackerNonce);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("Nonce mismatch");
    }

    [Fact]
    public void TpmQuoteVerifier_Rejects_Tampered_PcrDigest()
    {
        using var aik = Es256SignatureProvider.GenerateKey("aik-hsm-test-03");
        string nonce = "nonce-test-pcr";
        var quote = TpmQuoteGenerator.CreateQuote(aik, EnclaveType.Tpm20, nonce, [0, 1, 7]);

        string expectedDifferentPcr = "sha256:0000000000000000000000000000000000000000000000000000000000000000";

        var result = TpmQuoteVerifier.VerifyQuote(quote, aik, expectedNonce: nonce, expectedPcrDigest: expectedDifferentPcr);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("PCR digest mismatch");
    }

    [Fact]
    public void TpmQuoteVerifier_Rejects_Tampered_Signature()
    {
        using var aik = Es256SignatureProvider.GenerateKey("aik-hsm-test-04");
        string nonce = "nonce-tamper-sig";
        var quote = TpmQuoteGenerator.CreateQuote(aik, EnclaveType.Tpm20, nonce, [0, 1, 7]);

        // Tamper quote data
        var tamperedQuote = quote with { QuoteData = "MODIFIED_PAYLOAD_TAMPERED" };

        var result = TpmQuoteVerifier.VerifyQuote(tamperedQuote, aik, expectedNonce: nonce);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("signature verification failed");
    }

    [Fact]
    public void TpmQuoteVerifier_Validates_License_Binding_Success()
    {
        using var aik = Es256SignatureProvider.GenerateKey("aik-node-lock-01");
        string nonce = "nonce-binding-valid";
        var quote = TpmQuoteGenerator.CreateQuote(aik, EnclaveType.Tpm20, nonce, [0, 1, 7]);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var license = new LicenseClaims
        {
            Iss = "symbolon:controlplane",
            Sub = "lic_tpm_bound_001",
            Aud = "cad-defense-app",
            Jti = "jti_tpm_001",
            Iat = now,
            Exp = now + 86400,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "hybrid-v1",
                RequiredAlgs = [Alg.Es256],
                License = new LicenseMetadata
                {
                    Key = "SYM-TPM-BIND-1234",
                    Model = "nodelock",
                    State = "active"
                },
                Limits = new LicenseLimits { MaxSeats = 1, SeatUnit = "machine" },
                Binding = new BindingClaim
                {
                    Matching = "tpm20",
                    Components = new Dictionary<string, string>
                    {
                        ["aik_id"] = "aik-node-lock-01",
                        ["pcr_digest"] = quote.PcrDigest,
                        ["enclave_type"] = "Tpm20"
                    }
                }
            }
        };

        var result = TpmQuoteVerifier.ValidateLicenseBinding(license, quote, aik, expectedNonce: nonce);

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public void TpmQuoteVerifier_Rejects_License_When_Aik_Mismatches()
    {
        using var aik = Es256SignatureProvider.GenerateKey("aik-actual-machine");
        string nonce = "nonce-binding-fail";
        var quote = TpmQuoteGenerator.CreateQuote(aik, EnclaveType.Tpm20, nonce, [0, 1, 7]);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var license = new LicenseClaims
        {
            Iss = "symbolon:controlplane",
            Sub = "lic_tpm_bound_002",
            Aud = "cad-defense-app",
            Jti = "jti_tpm_002",
            Iat = now,
            Exp = now + 86400,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "hybrid-v1",
                RequiredAlgs = [Alg.Es256],
                License = new LicenseMetadata
                {
                    Key = "SYM-TPM-BIND-9999",
                    Model = "nodelock",
                    State = "active"
                },
                Limits = new LicenseLimits { MaxSeats = 1, SeatUnit = "machine" },
                Binding = new BindingClaim
                {
                    Matching = "tpm20",
                    Components = new Dictionary<string, string>
                    {
                        ["aik_id"] = "aik-authorized-secure-station",
                        ["pcr_digest"] = quote.PcrDigest
                    }
                }
            }
        };

        var result = TpmQuoteVerifier.ValidateLicenseBinding(license, quote, aik, expectedNonce: nonce);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("AIK ID mismatch");
    }
}
