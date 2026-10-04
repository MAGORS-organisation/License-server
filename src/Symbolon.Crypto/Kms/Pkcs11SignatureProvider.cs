using System.Security.Cryptography;

namespace Symbolon.Crypto.Kms;

/// <summary>
/// Hardware-isolated signature provider backed by an OASIS PKCS #11 v3.2 Cryptoki session.
/// </summary>
public sealed class Pkcs11SignatureProvider : ISignatureProvider
{
    private readonly ulong _sessionHandle;
    private readonly ulong _keyHandle;
    private readonly JsonWebKeyDto _publicJwk;
    private readonly Pkcs11Native.C_SignInitDelegate? _cSignInit;
    private readonly Pkcs11Native.C_SignDelegate? _cSign;
    private readonly ISignatureProvider? _softwareFallback;
    private bool _disposed;

    public string Alg { get; }
    public string Kid { get; }
    public bool CanSign => true;
    public int SignatureSize { get; }

    internal Pkcs11SignatureProvider(
        string kid,
        string alg,
        ulong sessionHandle,
        ulong keyHandle,
        JsonWebKeyDto publicJwk,
        Pkcs11Native.C_SignInitDelegate cSignInit,
        Pkcs11Native.C_SignDelegate cSign)
    {
        Kid = kid;
        Alg = alg;
        _sessionHandle = sessionHandle;
        _keyHandle = keyHandle;
        _publicJwk = publicJwk;
        _cSignInit = cSignInit;
        _cSign = cSign;
        SignatureSize = alg == Crypto.Alg.MlDsa65 ? 3309 : 64;
    }

    internal Pkcs11SignatureProvider(
        string kid,
        string alg,
        ISignatureProvider softwareFallback)
    {
        Kid = kid;
        Alg = alg;
        _softwareFallback = softwareFallback;
        _publicJwk = softwareFallback.ExportPublicJwk();
        SignatureSize = softwareFallback.SignatureSize;
    }

    public void Sign(ReadOnlySpan<byte> signingInput, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_softwareFallback != null)
        {
            _softwareFallback.Sign(signingInput, destination);
            return;
        }

        if (_cSignInit == null || _cSign == null)
        {
            throw new InvalidOperationException("PKCS#11 signing delegates are not initialized.");
        }

        ulong mechanismType = Alg == Crypto.Alg.MlDsa65
            ? Pkcs11Native.CKM_ML_DSA_65
            : Pkcs11Native.CKM_ECDSA;

        byte[] dataToSign;
        if (Alg == Crypto.Alg.Es256)
        {
            // CKM_ECDSA expects 32-byte SHA-256 digest
            dataToSign = SHA256.HashData(signingInput);
        }
        else
        {
            dataToSign = signingInput.ToArray();
        }

        var mechanism = new Pkcs11Native.CK_MECHANISM
        {
            mechanism = mechanismType,
            pParameter = IntPtr.Zero,
            ulParameterLen = 0
        };

        ulong rv = _cSignInit(_sessionHandle, ref mechanism, _keyHandle);
        if (rv != Pkcs11Native.CKR_OK)
        {
            throw new CryptographicException($"PKCS#11 C_SignInit failed with error code 0x{rv:X8}");
        }

        ulong sigLen = 0;
        rv = _cSign(_sessionHandle, dataToSign, (ulong)dataToSign.Length, null, ref sigLen);
        if (rv != Pkcs11Native.CKR_OK)
        {
            throw new CryptographicException($"PKCS#11 C_Sign (length query) failed with error code 0x{rv:X8}");
        }

        byte[] rawSig = new byte[sigLen];
        rv = _cSign(_sessionHandle, dataToSign, (ulong)dataToSign.Length, rawSig, ref sigLen);
        if (rv != Pkcs11Native.CKR_OK)
        {
            throw new CryptographicException($"PKCS#11 C_Sign failed with error code 0x{rv:X8}");
        }

        rawSig.AsSpan().CopyTo(destination);
    }

    public bool Verify(ReadOnlySpan<byte> signingInput, ReadOnlySpan<byte> signature)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_softwareFallback != null)
        {
            return _softwareFallback.Verify(signingInput, signature);
        }

        // Verify using the public JWK parameters
        if (Alg == Crypto.Alg.Es256)
        {
            using var verifier = Es256SignatureProvider.ImportJwk(_publicJwk);
            return verifier.Verify(signingInput, signature);
        }

        if (Alg == Crypto.Alg.MlDsa65)
        {
            using var verifier = MlDsaSignatureProvider.ImportJwk(_publicJwk);
            return verifier.Verify(signingInput, signature);
        }

        return false;
    }

    public JsonWebKeyDto ExportPublicJwk()
    {
        return _publicJwk;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _softwareFallback?.Dispose();
        }
    }
}
