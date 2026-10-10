using System.Text.Json.Serialization;

namespace Symbolon.Protocol.Sso;

public sealed record SsoProviderSummaryDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("providerType")]
    public required string ProviderType { get; init; }

    [JsonPropertyName("loginEndpoint")]
    public required string LoginEndpoint { get; init; }
}

public sealed record SsoProviderConfigDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    [JsonPropertyName("providerType")]
    public string ProviderType { get; init; } = "oidc";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("issuer")]
    public string Issuer { get; init; } = string.Empty;

    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; init; }

    [JsonPropertyName("metadataEndpoint")]
    public string? MetadataEndpoint { get; init; }

    [JsonPropertyName("authorizationEndpoint")]
    public string? AuthorizationEndpoint { get; init; }

    [JsonPropertyName("tokenEndpoint")]
    public string? TokenEndpoint { get; init; }

    [JsonPropertyName("userInfoEndpoint")]
    public string? UserInfoEndpoint { get; init; }

    [JsonPropertyName("idpCertificate")]
    public string? IdpCertificate { get; init; }

    [JsonPropertyName("roleMappings")]
    public IReadOnlyDictionary<string, string>? RoleMappings { get; init; }

    [JsonPropertyName("defaultRole")]
    public string DefaultRole { get; init; } = "admin:tenant";

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; init; } = true;
}

public sealed record SsoUserProfileDto
{
    [JsonPropertyName("isAuthenticated")]
    public bool IsAuthenticated { get; init; }

    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    [JsonPropertyName("userName")]
    public string? UserName { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    [JsonPropertyName("providerType")]
    public string? ProviderType { get; init; }
}

public sealed record SsoLoginInitiateResponseDto
{
    [JsonPropertyName("authorizationEndpoint")]
    public required string AuthorizationEndpoint { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }
}

public sealed record SsoSessionClaims
{
    [JsonPropertyName("sub")]
    public required string Sub { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("iat")]
    public long Iat { get; init; }

    [JsonPropertyName("exp")]
    public long Exp { get; init; }
}

public sealed record DirectorySyncResultDto
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("inactiveUsersScanned")]
    public int InactiveUsersScanned { get; init; }

    [JsonPropertyName("reclaimedSeatsCount")]
    public int ReclaimedSeatsCount { get; init; }

    [JsonPropertyName("reclaimedAssignmentsCount")]
    public int ReclaimedAssignmentsCount { get; init; }

    [JsonPropertyName("synchronizedGroupsCount")]
    public int SynchronizedGroupsCount { get; init; }

    [JsonPropertyName("isSuccess")]
    public bool IsSuccess { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }
}

public sealed record OidcTokenExchangeRequestDto
{
    [JsonPropertyName("providerId")]
    public required string ProviderId { get; init; }

    [JsonPropertyName("idToken")]
    public required string IdToken { get; init; }
}

public sealed record OidcTokenExchangeResponseDto
{
    [JsonPropertyName("sessionToken")]
    public required string SessionToken { get; init; }

    [JsonPropertyName("user")]
    public required SsoUserProfileDto User { get; init; }
}

