using System.Text.Json;
using System.Text.Json.Serialization;

namespace Symbolon.Protocol.Scim;

public sealed record ScimUserDto
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = ["urn:ietf:params:scim:schemas:core:2.0:User"];

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("externalId")]
    public string? ExternalId { get; init; }

    [JsonPropertyName("userName")]
    public string UserName { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public ScimUserNameDto? Name { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("emails")]
    public IReadOnlyList<ScimEmailDto>? Emails { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; } = true;

    [JsonPropertyName("groups")]
    public IReadOnlyList<ScimMemberDto>? Groups { get; init; }

    [JsonPropertyName("meta")]
    public ScimMetaDto? Meta { get; init; }
}

public sealed record ScimUserNameDto
{
    [JsonPropertyName("formatted")]
    public string? Formatted { get; init; }

    [JsonPropertyName("familyName")]
    public string? FamilyName { get; init; }

    [JsonPropertyName("givenName")]
    public string? GivenName { get; init; }
}

public sealed record ScimEmailDto
{
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; init; } = "work";

    [JsonPropertyName("primary")]
    public bool Primary { get; init; } = true;
}

public sealed record ScimGroupDto
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = ["urn:ietf:params:scim:schemas:core:2.0:Group"];

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("externalId")]
    public string? ExternalId { get; init; }

    [JsonPropertyName("members")]
    public IReadOnlyList<ScimMemberDto>? Members { get; init; }

    [JsonPropertyName("meta")]
    public ScimMetaDto? Meta { get; init; }
}

public sealed record ScimMemberDto
{
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("display")]
    public string? Display { get; init; }

    [JsonPropertyName("$ref")]
    public string? Ref { get; init; }
}

public sealed record ScimMetaDto
{
    [JsonPropertyName("resourceType")]
    public string? ResourceType { get; init; }

    [JsonPropertyName("created")]
    public DateTimeOffset? Created { get; init; }

    [JsonPropertyName("lastModified")]
    public DateTimeOffset? LastModified { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

public sealed record ScimListResponseDto<T>
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = ["urn:ietf:params:scim:api:messages:2.0:ListResponse"];

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; init; }

    [JsonPropertyName("startIndex")]
    public int StartIndex { get; init; } = 1;

    [JsonPropertyName("itemsPerPage")]
    public int ItemsPerPage { get; init; }

    [JsonPropertyName("Resources")]
    public IReadOnlyList<T> Resources { get; init; } = [];
}

public sealed record ScimFeatureDto
{
    [JsonPropertyName("supported")]
    public bool Supported { get; init; }
}

public sealed record ScimBulkFeatureDto
{
    [JsonPropertyName("supported")]
    public bool Supported { get; init; }

    [JsonPropertyName("maxOperations")]
    public int MaxOperations { get; init; }

    [JsonPropertyName("maxPayloadSize")]
    public int MaxPayloadSize { get; init; }
}

public sealed record ScimFilterFeatureDto
{
    [JsonPropertyName("supported")]
    public bool Supported { get; init; }

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; init; }
}

public sealed record ScimAuthSchemeDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("specUri")]
    public Uri? SpecUri { get; init; }

    [JsonPropertyName("documentationUri")]
    public Uri? DocumentationUri { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "oauthbearertoken";

    [JsonPropertyName("primary")]
    public bool Primary { get; init; } = true;
}

public sealed record ScimServiceProviderConfigDto
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = ["urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig"];

    [JsonPropertyName("documentationUri")]
    public Uri? DocumentationUri { get; init; }

    [JsonPropertyName("patch")]
    public ScimFeatureDto Patch { get; init; } = new() { Supported = true };

    [JsonPropertyName("bulk")]
    public ScimBulkFeatureDto Bulk { get; init; } = new() { Supported = false, MaxOperations = 0, MaxPayloadSize = 0 };

    [JsonPropertyName("filter")]
    public ScimFilterFeatureDto Filter { get; init; } = new() { Supported = true, MaxResults = 1000 };

    [JsonPropertyName("changePassword")]
    public ScimFeatureDto ChangePassword { get; init; } = new() { Supported = false };

    [JsonPropertyName("sort")]
    public ScimFeatureDto Sort { get; init; } = new() { Supported = false };

    [JsonPropertyName("etag")]
    public ScimFeatureDto Etag { get; init; } = new() { Supported = false };

    [JsonPropertyName("authenticationSchemes")]
    public IReadOnlyList<ScimAuthSchemeDto> AuthenticationSchemes { get; init; } =
    [
        new()
        {
            Name = "Bearer Token",
            Description = "Authentication via API Key Bearer Token",
            Type = "oauthbearertoken",
            Primary = true
        }
    ];

    [JsonPropertyName("meta")]
    public ScimMetaDto? Meta { get; init; }
}

public sealed record ScimPatchOperation
{
    [JsonPropertyName("op")]
    public string Op { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }
}

public sealed record ScimPatchOpDto
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = ["urn:ietf:params:scim:api:messages:2.0:PatchOp"];

    [JsonPropertyName("Operations")]
    public IReadOnlyList<ScimPatchOperation> Operations { get; init; } = [];
}

public sealed record ScimErrorDto
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = ["urn:ietf:params:scim:api:messages:2.0:Error"];

    [JsonPropertyName("scimType")]
    public string? ScimType { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "400";
}
