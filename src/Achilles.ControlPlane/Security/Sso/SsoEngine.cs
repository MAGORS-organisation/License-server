using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Achilles.Data.Entities;
using Achilles.Protocol.Sso;

namespace Achilles.ControlPlane.Security.Sso;

public sealed record SsoStateData(
    string ProviderId,
    string TenantId,
    string? CodeVerifier,
    string? Nonce,
    long Timestamp);

public sealed record SamlAssertionResult(
    bool IsSuccess,
    string? NameId,
    string? DisplayName,
    string? Email,
    IReadOnlyList<string> Groups,
    string? Role,
    string? ErrorMessage);

public sealed class SsoSessionManager
{
    private readonly byte[] _secretKey;

    public SsoSessionManager(IConfiguration? configuration = null)
    {
        string? configSecret = configuration?["Security:SsoSessionSecret"];
        if (!string.IsNullOrWhiteSpace(configSecret))
        {
            _secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(configSecret));
        }
        else
        {
            // Ephemeral 32-byte secret for this server instance
            _secretKey = RandomNumberGenerator.GetBytes(32);
        }
    }

    public string CreateSessionToken(
        string userId,
        string userName,
        string? email,
        string role,
        string tenantId,
        TimeSpan ttl)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long exp = now + (long)ttl.TotalSeconds;

        var claims = new SsoSessionClaims
        {
            Sub = userId,
            Name = userName,
            Email = email,
            Role = role,
            TenantId = tenantId,
            Iat = now,
            Exp = exp
        };

        string payloadJson = JsonSerializer.Serialize(claims);
        string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        using var hmac = new HMACSHA256(_secretKey);
        byte[] sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        string sigB64 = Base64UrlEncode(sig);

        return $"sym_sso_{payloadB64}.{sigB64}";
    }

    public SsoSessionClaims? ValidateSessionToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("sym_sso_", StringComparison.Ordinal))
        {
            return null;
        }

        string raw = token["sym_sso_".Length..];
        int dot = raw.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            return null;
        }

        string payloadB64 = raw[..dot];
        string sigB64 = raw[(dot + 1)..];

        byte[] expectedSig;
        using (var hmac = new HMACSHA256(_secretKey))
        {
            expectedSig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        }

        byte[] providedSig;
        try
        {
            providedSig = Base64UrlDecode(sigB64);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(expectedSig, providedSig))
        {
            return null;
        }

        try
        {
            byte[] payloadBytes = Base64UrlDecode(payloadB64);
            var claims = JsonSerializer.Deserialize<SsoSessionClaims>(payloadBytes);
            if (claims is null)
            {
                return null;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (claims.Exp < now)
            {
                return null; // Expired
            }

            return claims;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public string ProtectState(SsoStateData data)
    {
        string json = JsonSerializer.Serialize(data);
        string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(json));

        using var hmac = new HMACSHA256(_secretKey);
        byte[] sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        string sigB64 = Base64UrlEncode(sig);

        return $"{payloadB64}.{sigB64}";
    }

    public SsoStateData? UnprotectState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        int dot = state.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            return null;
        }

        string payloadB64 = state[..dot];
        string sigB64 = state[(dot + 1)..];

        byte[] expectedSig;
        using (var hmac = new HMACSHA256(_secretKey))
        {
            expectedSig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        }

        byte[] providedSig;
        try
        {
            providedSig = Base64UrlDecode(sigB64);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(expectedSig, providedSig))
        {
            return null;
        }

        try
        {
            byte[] jsonBytes = Base64UrlDecode(payloadB64);
            var data = JsonSerializer.Deserialize<SsoStateData>(jsonBytes);
            if (data is null)
            {
                return null;
            }

            // State validity: 15 minutes
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - data.Timestamp > 900)
            {
                return null;
            }

            return data;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        string padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}

public sealed class SsoEngine
{
    private readonly SsoSessionManager _sessionManager;

    public SsoEngine(SsoSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public SsoSessionManager SessionManager => _sessionManager;

    // --- PKCE & OIDC ---

    public static (string codeVerifier, string codeChallenge) GeneratePkce()
    {
        byte[] verifierBytes = RandomNumberGenerator.GetBytes(32);
        string codeVerifier = Base64UrlEncode(verifierBytes);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier));
        string codeChallenge = Base64UrlEncode(hash);

        return (codeVerifier, codeChallenge);
    }

    public static string BuildOidcAuthorizationEndpoint(
        SsoProviderEntity provider,
        string redirectEndpoint,
        string state,
        string codeChallenge,
        string nonce)
    {
        string authEndpoint = provider.AuthorizationEndpoint ?? $"{provider.Issuer.TrimEnd('/')}/oauth2/v2.0/authorize";
        var query = new StringBuilder();
        query.Append(authEndpoint);
        query.Append(authEndpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?');
        query.Append("client_id=").Append(Uri.EscapeDataString(provider.ClientId));
        query.Append("&response_type=code");
        query.Append("&scope=").Append(Uri.EscapeDataString("openid profile email groups"));
        query.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectEndpoint));
        query.Append("&state=").Append(Uri.EscapeDataString(state));
        query.Append("&code_challenge=").Append(Uri.EscapeDataString(codeChallenge));
        query.Append("&code_challenge_method=S256");
        query.Append("&nonce=").Append(Uri.EscapeDataString(nonce));

        return query.ToString();
    }

    // --- SAML 2.0 Web Browser SSO ---

    public static string BuildSamlAuthnRequestEndpoint(
        SsoProviderEntity provider,
        string spEntityId,
        string acsEndpoint,
        string relayState)
    {
        string id = $"id_{Guid.NewGuid():N}";
        string issueInstant = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        string authEndpoint = provider.AuthorizationEndpoint ?? $"{provider.Issuer.TrimEnd('/')}/saml2/login";

        string xml = $"""
        <samlp:AuthnRequest xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol"
                            xmlns:saml="urn:oasis:names:tc:SAML:2.0:assertion"
                            ID="{id}"
                            Version="2.0"
                            IssueInstant="{issueInstant}"
                            Destination="{authEndpoint}"
                            AssertionConsumerServiceURL="{acsEndpoint}"
                            ProtocolBinding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST">
            <saml:Issuer>{spEntityId}</saml:Issuer>
            <samlp:NameIDPolicy Format="urn:oasis:names:tc:SAML:1.1:nameid-format:unspecified" AllowCreate="true"/>
        </samlp:AuthnRequest>
        """;

        byte[] deflated;
        using (var output = new MemoryStream())
        {
            using (var compressor = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                byte[] raw = Encoding.UTF8.GetBytes(xml);
                compressor.Write(raw, 0, raw.Length);
            }
            deflated = output.ToArray();
        }

        string encodedRequest = Uri.EscapeDataString(Convert.ToBase64String(deflated));
        var url = new StringBuilder();
        url.Append(authEndpoint);
        url.Append(authEndpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?');
        url.Append("SAMLRequest=").Append(encodedRequest);
        if (!string.IsNullOrWhiteSpace(relayState))
        {
            url.Append("&RelayState=").Append(Uri.EscapeDataString(relayState));
        }

        return url.ToString();
    }

    public static string GenerateSpMetadataXml(string spEntityId, string acsEndpoint)
    {
        return $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{spEntityId}">
            <md:SPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                <md:NameIDFormat>urn:oasis:names:tc:SAML:1.1:nameid-format:unspecified</md:NameIDFormat>
                <md:AssertionConsumerService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"
                                             Location="{acsEndpoint}"
                                             index="0"
                                             isDefault="true"/>
            </md:SPSSODescriptor>
        </md:EntityDescriptor>
        """;
    }

    public static SamlAssertionResult ParseSamlResponse(string base64SamlResponse, string? expectedSpEntityId)
    {
        if (string.IsNullOrWhiteSpace(base64SamlResponse))
        {
            return new SamlAssertionResult(false, null, null, null, [], null, "Empty SAMLResponse");
        }

        byte[] xmlBytes;
        try
        {
            xmlBytes = Convert.FromBase64String(base64SamlResponse);
        }
        catch (FormatException)
        {
            return new SamlAssertionResult(false, null, null, null, [], null, "Invalid base64 encoding in SAMLResponse");
        }

        // XXE-Safe XML settings (SEC-01)
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var ms = new MemoryStream(xmlBytes);
        using var reader = XmlReader.Create(ms, settings);
        XDocument doc;
        try
        {
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            return new SamlAssertionResult(false, null, null, null, [], null, $"XML parsing error: {ex.Message}");
        }

        XNamespace samlp = "urn:oasis:names:tc:SAML:2.0:protocol";
        XNamespace saml = "urn:oasis:names:tc:SAML:2.0:assertion";

        var responseEl = doc.Root;
        if (responseEl is null)
        {
            return new SamlAssertionResult(false, null, null, null, [], null, "Missing root XML element");
        }

        // 1. Verify StatusCode
        var statusCodeEl = responseEl.Descendants(samlp + "StatusCode").FirstOrDefault();
        string? statusCodeVal = statusCodeEl?.Attribute("Value")?.Value;
        if (statusCodeVal != null && !statusCodeVal.EndsWith(":Success", StringComparison.OrdinalIgnoreCase))
        {
            return new SamlAssertionResult(false, null, null, null, [], null, $"SAML response status not Success: {statusCodeVal}");
        }

        // 2. Locate Assertion
        var assertion = responseEl.Descendants(saml + "Assertion").FirstOrDefault();
        if (assertion is null)
        {
            return new SamlAssertionResult(false, null, null, null, [], null, "No SAML Assertion found in response");
        }

        // 3. Check Audience if expected
        if (!string.IsNullOrWhiteSpace(expectedSpEntityId))
        {
            var audience = assertion.Descendants(saml + "Audience").Select(a => a.Value.Trim()).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(audience) && !audience.Equals(expectedSpEntityId, StringComparison.OrdinalIgnoreCase))
            {
                return new SamlAssertionResult(false, null, null, null, [], null, $"Audience mismatch: expected '{expectedSpEntityId}', got '{audience}'");
            }
        }

        // 4. Extract NameID
        string? nameId = assertion.Descendants(saml + "NameID").FirstOrDefault()?.Value.Trim();

        // 5. Extract Attributes
        string? email = null;
        string? displayName = null;
        string? role = null;
        var groups = new List<string>();

        foreach (var attr in assertion.Descendants(saml + "Attribute"))
        {
            string? attrName = attr.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(attrName)) continue;

            var values = attr.Descendants(saml + "AttributeValue").Select(v => v.Value.Trim()).ToList();
            if (values.Count == 0) continue;

            if (attrName.Equals("email", StringComparison.OrdinalIgnoreCase) ||
                attrName.Equals("mail", StringComparison.OrdinalIgnoreCase) ||
                attrName.EndsWith("/emailaddress", StringComparison.OrdinalIgnoreCase))
            {
                email = values[0];
            }
            else if (attrName.Equals("displayName", StringComparison.OrdinalIgnoreCase) ||
                     attrName.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                     attrName.EndsWith("/claims/name", StringComparison.OrdinalIgnoreCase))
            {
                displayName = values[0];
            }
            else if (attrName.Equals("role", StringComparison.OrdinalIgnoreCase) ||
                     attrName.EndsWith("/claims/role", StringComparison.OrdinalIgnoreCase))
            {
                role = values[0];
            }
            else if (attrName.Equals("groups", StringComparison.OrdinalIgnoreCase) ||
                     attrName.Equals("memberOf", StringComparison.OrdinalIgnoreCase) ||
                     attrName.EndsWith("/claims/groups", StringComparison.OrdinalIgnoreCase))
            {
                groups.AddRange(values);
            }
        }

        if (string.IsNullOrWhiteSpace(nameId) && !string.IsNullOrWhiteSpace(email))
        {
            nameId = email;
        }

        if (string.IsNullOrWhiteSpace(nameId))
        {
            return new SamlAssertionResult(false, null, null, null, [], null, "Missing NameID and Email in SAML assertion");
        }

        return new SamlAssertionResult(
            IsSuccess: true,
            NameId: nameId,
            DisplayName: displayName ?? nameId,
            Email: email ?? nameId,
            Groups: groups,
            Role: role,
            ErrorMessage: null);
    }

    // --- Enterprise RBAC Claim Mapper ---

    public static string ResolveUserRole(
        SsoProviderEntity provider,
        IReadOnlyList<string> userGroups,
        string? directIdpRole)
    {
        Dictionary<string, string>? mappings = null;
        if (!string.IsNullOrWhiteSpace(provider.RoleMappingJson))
        {
            try
            {
                mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(provider.RoleMappingJson);
            }
            catch (JsonException)
            {
                // Fallback on parse error
            }
        }

        // 1. Direct role check if given
        if (!string.IsNullOrWhiteSpace(directIdpRole))
        {
            string clean = directIdpRole.Trim();
            if (clean.Equals("admin:super", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("admin:tenant", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("auditor", StringComparison.OrdinalIgnoreCase))
            {
                return clean.ToLowerInvariant();
            }

            if (mappings != null && mappings.TryGetValue(clean, out var mappedRole))
            {
                return mappedRole;
            }
        }

        // 2. Check group memberships with priority hierarchy (super > tenant > auditor)
        if (mappings != null && userGroups.Count > 0)
        {
            // First check if any group yields admin:super
            foreach (var grp in userGroups)
            {
                if (mappings.TryGetValue(grp, out var role) && role.Equals("admin:super", StringComparison.OrdinalIgnoreCase))
                {
                    return "admin:super";
                }
            }

            // Next check admin:tenant
            foreach (var grp in userGroups)
            {
                if (mappings.TryGetValue(grp, out var role) && role.Equals("admin:tenant", StringComparison.OrdinalIgnoreCase))
                {
                    return "admin:tenant";
                }
            }

            // Next check auditor
            foreach (var grp in userGroups)
            {
                if (mappings.TryGetValue(grp, out var role) && role.Equals("auditor", StringComparison.OrdinalIgnoreCase))
                {
                    return "auditor";
                }
            }
        }

        // 3. Fallback on default role
        return string.IsNullOrWhiteSpace(provider.DefaultRole) ? "admin:tenant" : provider.DefaultRole;
    }

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
