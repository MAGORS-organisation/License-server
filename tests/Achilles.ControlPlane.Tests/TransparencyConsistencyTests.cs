using System.Net;
using System.Net.Http.Json;
using Achilles.ControlPlane.Models;
using Achilles.Crypto;
using Achilles.Domain.Transparency;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class TransparencyConsistencyTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public TransparencyConsistencyTests(ControlPlaneFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _client = factory.CreateClient();
    }

    [Fact]
    public void MerkleTree_ConsistencyProof_RFC6962_Mathematical_Verification()
    {
        // Generate a sequence of 10 distinct leaf hashes
        var leaves = new List<byte[]>();
        for (int i = 0; i < 10; i++)
        {
            leaves.Add(MerkleTree.HashLeaf(System.Text.Encoding.UTF8.GetBytes($"entry-{i}")));
        }

        // Test consistency across various older and newer tree sizes
        for (int m = 1; m <= leaves.Count; m++)
        {
            byte[] oldRoot = MerkleTree.ComputeRootHash(leaves.Take(m).ToList());

            for (int n = m; n <= leaves.Count; n++)
            {
                byte[] newRoot = MerkleTree.ComputeRootHash(leaves.Take(n).ToList());
                var proof = MerkleTree.GenerateConsistencyProof(leaves, m, n);

                bool valid = MerkleTree.VerifyConsistency(oldRoot, newRoot, m, n, proof);
                Assert.True(valid, $"Consistency proof failed for m={m}, n={n}");

                if (m < n && proof.Count > 0)
                {
                    // Tampered proof must fail
                    var tamperedProof = new List<string>(proof);
                    tamperedProof[0] = Convert.ToHexStringLower(new byte[32]);
                    bool tamperedValid = MerkleTree.VerifyConsistency(oldRoot, newRoot, m, n, tamperedProof);
                    Assert.False(tamperedValid, $"Tampered proof should fail for m={m}, n={n}");
                }
            }
        }
    }

    [Fact]
    public async Task STH_And_ConsistencyEndpoints_E2E_Work()
    {
        // 1. Ensure audit events exist by creating tenant, product, policy, and license
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto($"org-{Guid.NewGuid():N}", "Org"));
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"sth-prod-{Guid.NewGuid():N}", "STH Product", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var prodRes = await _client.SendAsync(prodReq);
        Assert.Equal(HttpStatusCode.Created, prodRes.StatusCode);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product.Id,
            Code = $"sth-pol-{Guid.NewGuid():N}",
            Name = "STH Policy",
            MaxSeats = 5,
            LicenseModel = "floating"
        });
        Assert.Equal(HttpStatusCode.Created, polRes.StatusCode);
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(policy);

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            MaxSeats = 5
        });
        Assert.Equal(HttpStatusCode.Created, licRes.StatusCode);

        var licRes2 = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            MaxSeats = 3
        });
        Assert.Equal(HttpStatusCode.Created, licRes2.StatusCode);

        // 2. Fetch STH
        var sthRes = await _client.GetAsync("/v1/transparency/sth");
        Assert.Equal(HttpStatusCode.OK, sthRes.StatusCode);
        var sthDto = await sthRes.Content.ReadFromJsonAsync<SignedTreeHeadDto>();
        Assert.NotNull(sthDto);
        Assert.True(sthDto.TreeSize >= 2);
        Assert.NotEmpty(sthDto.RootHash);
        Assert.NotEmpty(sthDto.Signature);
        Assert.NotEmpty(sthDto.KeyId);
        Assert.Equal("ES256", sthDto.Algorithm);

        // Verify STH signature against JWKS
        var jwksRes = await _client.GetAsync("/v1/.well-known/symbolon-keys");
        Assert.Equal(HttpStatusCode.OK, jwksRes.StatusCode);
        var jwks = await jwksRes.Content.ReadFromJsonAsync<JsonWebKeySetDto>();
        Assert.NotNull(jwks);
        var signingKey = jwks.Keys.FirstOrDefault(k => k.Kid == sthDto.KeyId);
        Assert.NotNull(signingKey);
        Assert.NotNull(signingKey.X);
        Assert.NotNull(signingKey.Y);

        byte[] payload = MerkleTree.ComputeTreeHeadSigningPayload(sthDto.TreeSize, sthDto.Timestamp, sthDto.RootHash);
        byte[] sigBytes = Convert.FromBase64String(sthDto.Signature);
        byte[] x = System.Buffers.Text.Base64Url.DecodeFromChars(signingKey.X);
        byte[] y = System.Buffers.Text.Base64Url.DecodeFromChars(signingKey.Y);
        using var verifier = Es256SignatureProvider.ImportPublic(x, y, signingKey.Kid);
        bool sigValid = verifier.Verify(payload, sigBytes);
        Assert.True(sigValid, "STH cryptographic signature verification succeeded.");

        // 3. Request consistency proof between oldSize = 1 and newSize = current TreeSize
        int oldSize = 1;
        int newSize = sthDto.TreeSize;

        var consistencyRes = await _client.GetAsync($"/v1/transparency/consistency?oldSize={oldSize}&newSize={newSize}");
        Assert.Equal(HttpStatusCode.OK, consistencyRes.StatusCode);
        var consistencyDto = await consistencyRes.Content.ReadFromJsonAsync<TransparencyConsistencyResponseDto>();
        Assert.NotNull(consistencyDto);
        Assert.Equal(oldSize, consistencyDto.OldSize);
        Assert.Equal(newSize, consistencyDto.NewSize);
        Assert.Equal(sthDto.RootHash, consistencyDto.NewRoot);

        // 4. Verify consistency proof via POST /v1/transparency/verify-consistency
        var verifyRes = await _client.PostAsJsonAsync("/v1/transparency/verify-consistency", new VerifyTransparencyConsistencyRequestDto
        {
            OldSize = consistencyDto.OldSize,
            NewSize = consistencyDto.NewSize,
            OldRoot = consistencyDto.OldRoot,
            NewRoot = consistencyDto.NewRoot,
            Proof = consistencyDto.Proof
        });
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);
        var verifyDto = await verifyRes.Content.ReadFromJsonAsync<VerifyTransparencyConsistencyResponseDto>();
        Assert.NotNull(verifyDto);
        Assert.True(verifyDto.IsConsistent);

        // 5. Tampered consistency proof fails
        var tamperedProof = new List<string>(consistencyDto.Proof);
        if (tamperedProof.Count > 0)
        {
            tamperedProof[0] = Convert.ToHexStringLower(new byte[32]);
        }
        else
        {
            tamperedProof.Add(Convert.ToHexStringLower(new byte[32]));
        }

        var tamperedVerifyRes = await _client.PostAsJsonAsync("/v1/transparency/verify-consistency", new VerifyTransparencyConsistencyRequestDto
        {
            OldSize = consistencyDto.OldSize,
            NewSize = consistencyDto.NewSize,
            OldRoot = consistencyDto.OldRoot,
            NewRoot = consistencyDto.NewRoot,
            Proof = tamperedProof
        });
        Assert.Equal(HttpStatusCode.OK, tamperedVerifyRes.StatusCode);
        var tamperedVerifyDto = await tamperedVerifyRes.Content.ReadFromJsonAsync<VerifyTransparencyConsistencyResponseDto>();
        Assert.NotNull(tamperedVerifyDto);
        Assert.False(tamperedVerifyDto.IsConsistent);

        // 6. Bad requests (oldSize <= 0 or oldSize > newSize)
        var badRes1 = await _client.GetAsync("/v1/transparency/consistency?oldSize=0&newSize=5");
        Assert.Equal(HttpStatusCode.BadRequest, badRes1.StatusCode);

        var badRes2 = await _client.GetAsync("/v1/transparency/consistency?oldSize=10&newSize=5");
        Assert.Equal(HttpStatusCode.BadRequest, badRes2.StatusCode);
    }
}
