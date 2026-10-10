using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Achilles.Crypto.Tests;

public class PqcEnvelopeEncryptionTests
{
    [Fact]
    public void PqcEnvelopeEncryption_EncryptDecrypt_RoundTrip_PreservesData()
    {
        if (!MlKemKeyEncapsulationProvider.IsSupported) return;

        using var recipientKey = MlKemKeyEncapsulationProvider.GenerateKey(MLKemAlgorithm.MLKem768, "server-kem-key");
        string originalText = "Top-Secret-Enterprise-Perpetual-License-Payload-2026-NIS2-CNSA2";
        byte[] plaintext = Encoding.UTF8.GetBytes(originalText);

        // Sender encrypts using recipient's public capability
        var envelope = PqcEnvelopeEncryption.Encrypt(plaintext, recipientKey);

        envelope.Alg.Should().Be("ML-KEM-768+A256GCM");
        envelope.Kid.Should().Be("server-kem-key");
        envelope.Ciphertext.Should().NotBeNullOrWhiteSpace();
        envelope.Nonce.Should().NotBeNullOrWhiteSpace();
        envelope.Tag.Should().NotBeNullOrWhiteSpace();
        envelope.Payload.Should().NotBeNullOrWhiteSpace();

        // Recipient decrypts using private key
        byte[] decrypted = PqcEnvelopeEncryption.Decrypt(envelope, recipientKey);
        string decryptedText = Encoding.UTF8.GetString(decrypted);

        decryptedText.Should().Be(originalText);
    }

    [Fact]
    public void PqcEnvelopeEncryption_TamperedPayload_ThrowsCryptographicException()
    {
        if (!MlKemKeyEncapsulationProvider.IsSupported) return;

        using var recipientKey = MlKemKeyEncapsulationProvider.GenerateKey(MLKemAlgorithm.MLKem768, "server-kem-key");
        byte[] plaintext = Encoding.UTF8.GetBytes("Data to be tampered");

        var envelope = PqcEnvelopeEncryption.Encrypt(plaintext, recipientKey);

        // Tamper with payload
        byte[] rawPayload = System.Buffers.Text.Base64Url.DecodeFromChars(envelope.Payload);
        rawPayload[0] ^= 0xFF; // Flip bit
        var tamperedEnvelope = envelope with { Payload = System.Buffers.Text.Base64Url.EncodeToString(rawPayload) };

        var act = () => PqcEnvelopeEncryption.Decrypt(tamperedEnvelope, recipientKey);
        act.Should().Throw<CryptographicException>();
    }
}
