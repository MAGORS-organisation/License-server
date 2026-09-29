using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Protocol.Scim;
using Symbolon.Protocol.Tracing;

namespace Symbolon.ControlPlane.Endpoints;

public static partial class ScimEndpoints
{
    private static readonly string[] ListResponseSchemas = ["urn:ietf:params:scim:api:messages:2.0:ListResponse"];
    private static readonly string[] ResourceTypeSchemas = ["urn:ietf:params:scim:schemas:core:2.0:ResourceType"];

    public static RouteGroupBuilder MapScimEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/scim/v2").WithTags("SCIM 2.0");

        // Public discovery endpoints per RFC 7644 §4
        group.MapGet("/ServiceProviderConfig", GetServiceProviderConfig).AllowAnonymous().WithName("ScimServiceProviderConfig");
        group.MapGet("/Schemas", GetSchemas).AllowAnonymous().WithName("ScimSchemas");
        group.MapGet("/ResourceTypes", GetResourceTypes).AllowAnonymous().WithName("ScimResourceTypes");

        // Authenticated resource endpoints
        var secured = group.MapGroup("").RequireAuthorization();

        // Users
        secured.MapGet("/Users", GetUsersAsync).WithName("ScimGetUsers");
        secured.MapGet("/Users/{id}", GetUserByIdAsync).WithName("ScimGetUserById");
        secured.MapPost("/Users", CreateUserAsync).WithName("ScimCreateUser");
        secured.MapPut("/Users/{id}", UpdateUserAsync).WithName("ScimUpdateUser");
        secured.MapPatch("/Users/{id}", PatchUserAsync).WithName("ScimPatchUser");
        secured.MapDelete("/Users/{id}", DeleteUserAsync).WithName("ScimDeleteUser");

        // Groups
        secured.MapGet("/Groups", GetGroupsAsync).WithName("ScimGetGroups");
        secured.MapGet("/Groups/{id}", GetGroupByIdAsync).WithName("ScimGetGroupById");
        secured.MapPost("/Groups", CreateGroupAsync).WithName("ScimCreateGroup");
        secured.MapPut("/Groups/{id}", UpdateGroupAsync).WithName("ScimUpdateGroup");
        secured.MapPatch("/Groups/{id}", PatchGroupAsync).WithName("ScimPatchGroup");
        secured.MapDelete("/Groups/{id}", DeleteGroupAsync).WithName("ScimDeleteGroup");

        return group;
    }

    private static IResult ScimJson<T>(T value, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(value, statusCode: statusCode, contentType: "application/scim+json; charset=utf-8");

    private static IResult ScimError(int statusCode, string? scimType, string detail)
    {
        var error = new ScimErrorDto
        {
            Status = statusCode.ToString(CultureInfo.InvariantCulture),
            ScimType = scimType,
            Detail = detail
        };
        return Results.Json(error, statusCode: statusCode, contentType: "application/scim+json; charset=utf-8");
    }

    private static string ResolveTenantId(HttpContext context, SymbolonDbContext db)
    {
        bool isSuperAdmin = context.User.IsInRole("admin:super");
        string? tenantId = isSuperAdmin
            ? context.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? context.User.FindFirst("tenant_id")?.Value
            : context.User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            var firstTenant = db.Tenants.FirstOrDefault();
            tenantId = firstTenant?.Id ?? "ten_default";
        }

        return tenantId;
    }

    private static string GetBaseUrl(HttpContext context) =>
        $"{context.Request.Scheme}://{context.Request.Host}";

    // --- RFC 7644 Discovery Endpoints ---

    private static IResult GetServiceProviderConfig(HttpContext context)
    {
        string baseUrl = GetBaseUrl(context);
        var config = new ScimServiceProviderConfigDto
        {
            DocumentationUri = new Uri($"{baseUrl}/docs"),
            Patch = new ScimFeatureDto { Supported = true },
            Bulk = new ScimBulkFeatureDto { Supported = false, MaxOperations = 0, MaxPayloadSize = 0 },
            Filter = new ScimFilterFeatureDto { Supported = true, MaxResults = 1000 },
            ChangePassword = new ScimFeatureDto { Supported = false },
            Sort = new ScimFeatureDto { Supported = false },
            Etag = new ScimFeatureDto { Supported = false },
            AuthenticationSchemes =
            [
                new()
                {
                    Name = "OAuth Bearer Token",
                    Description = "Authentication scheme using API Key Bearer Token",
                    SpecUri = new Uri("https://tools.ietf.org/html/rfc6750"),
                    Type = "oauthbearertoken",
                    Primary = true
                }
            ],
            Meta = new ScimMetaDto
            {
                ResourceType = "ServiceProviderConfig",
                Created = DateTimeOffset.UtcNow,
                LastModified = DateTimeOffset.UtcNow,
                Location = $"{baseUrl}/scim/v2/ServiceProviderConfig"
            }
        };

        return ScimJson(config);
    }

    private static IResult GetSchemas(HttpContext context)
    {
        string baseUrl = GetBaseUrl(context);
        var schemas = new[]
        {
            new
            {
                id = "urn:ietf:params:scim:schemas:core:2.0:User",
                name = "User",
                description = "Core SCIM 2.0 User schema",
                meta = new { resourceType = "Schema", location = $"{baseUrl}/scim/v2/Schemas/urn:ietf:params:scim:schemas:core:2.0:User" }
            },
            new
            {
                id = "urn:ietf:params:scim:schemas:core:2.0:Group",
                name = "Group",
                description = "Core SCIM 2.0 Group schema",
                meta = new { resourceType = "Schema", location = $"{baseUrl}/scim/v2/Schemas/urn:ietf:params:scim:schemas:core:2.0:Group" }
            }
        };

        return Results.Json(new
        {
            schemas = ListResponseSchemas,
            totalResults = schemas.Length,
            startIndex = 1,
            itemsPerPage = schemas.Length,
            Resources = schemas
        }, contentType: "application/scim+json; charset=utf-8");
    }

    private static IResult GetResourceTypes(HttpContext context)
    {
        string baseUrl = GetBaseUrl(context);
        var resourceTypes = new[]
        {
            new
            {
                schemas = ResourceTypeSchemas,
                id = "User",
                name = "User",
                endpoint = "/Users",
                description = "User Account",
                schema = "urn:ietf:params:scim:schemas:core:2.0:User",
                meta = new { resourceType = "ResourceType", location = $"{baseUrl}/scim/v2/ResourceTypes/User" }
            },
            new
            {
                schemas = ResourceTypeSchemas,
                id = "Group",
                name = "Group",
                endpoint = "/Groups",
                description = "Group",
                schema = "urn:ietf:params:scim:schemas:core:2.0:Group",
                meta = new { resourceType = "ResourceType", location = $"{baseUrl}/scim/v2/ResourceTypes/Group" }
            }
        };

        return Results.Json(new
        {
            schemas = ListResponseSchemas,
            totalResults = resourceTypes.Length,
            startIndex = 1,
            itemsPerPage = resourceTypes.Length,
            Resources = resourceTypes
        }, contentType: "application/scim+json; charset=utf-8");
    }

    // --- Users API (RFC 7644 §3.2 - §3.6) ---

    private static async Task<IResult> GetUsersAsync(
        HttpContext context,
        SymbolonDbContext db,
        string? filter,
        int? startIndex,
        int? count,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context, db);
        int pageStart = Math.Max(1, startIndex ?? 1);
        int pageSize = Math.Clamp(count ?? 100, 1, 1000);

        var query = db.ScimUsers
            .AsNoTracking()
            .Include(u => u.GroupMemberships)
            .ThenInclude(m => m.Group)
            .Where(u => u.TenantId == tenantId);

        var (attr, val) = ParseSimpleFilter(filter);
        if (!string.IsNullOrWhiteSpace(attr) && !string.IsNullOrWhiteSpace(val))
        {
            if (attr.Equals("userName", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(u => u.UserName == val);
            }
            else if (attr.Equals("externalId", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(u => u.ExternalId == val);
            }
            else if (attr.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(u => u.Id == val);
            }
            else if (attr.Equals("email", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(u => u.Email == val);
            }
        }

        int totalResults = await query.CountAsync(ct).ConfigureAwait(false);
        var entities = await query
            .OrderBy(u => u.UserName)
            .Skip(pageStart - 1)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string baseUrl = GetBaseUrl(context);
        var dtos = entities.Select(e => MapToScimUser(e, baseUrl)).ToList();

        return ScimJson(new ScimListResponseDto<ScimUserDto>
        {
            TotalResults = totalResults,
            StartIndex = pageStart,
            ItemsPerPage = dtos.Count,
            Resources = dtos
        });
    }

    private static async Task<IResult> GetUserByIdAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context, db);
        var entity = await db.ScimUsers
            .AsNoTracking()
            .Include(u => u.GroupMemberships)
            .ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ScimError(StatusCodes.Status404NotFound, null, $"User '{id}' not found.");
        }

        return ScimJson(MapToScimUser(entity, GetBaseUrl(context)));
    }

    private static async Task<IResult> CreateUserAsync(
        ScimUserDto dto,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.UserName))
        {
            return ScimError(StatusCodes.Status400BadRequest, "invalidValue", "Field 'userName' is required.");
        }

        string tenantId = ResolveTenantId(context, db);
        bool exists = await db.ScimUsers.AnyAsync(u => u.TenantId == tenantId && u.UserName == dto.UserName, ct).ConfigureAwait(false);
        if (exists)
        {
            return ScimError(StatusCodes.Status409Conflict, "uniqueness", $"User with userName '{dto.UserName}' already exists.");
        }

        var now = time.GetUtcNow();
        var entity = new ScimUserEntity
        {
            Id = $"usr_{Guid.NewGuid():N}",
            TenantId = tenantId,
            UserName = dto.UserName.Trim(),
            ExternalId = dto.ExternalId?.Trim(),
            GivenName = dto.Name?.GivenName?.Trim(),
            FamilyName = dto.Name?.FamilyName?.Trim(),
            FormattedName = dto.Name?.Formatted?.Trim() ?? dto.DisplayName?.Trim() ?? dto.UserName.Trim(),
            Email = dto.Emails is { Count: > 0 } ? dto.Emails[0].Value.Trim() : null,
            Active = dto.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ScimUsers.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        string baseUrl = GetBaseUrl(context);
        string location = $"{baseUrl}/scim/v2/Users/{entity.Id}";
        context.Response.Headers.Location = location;

        return Results.Json(MapToScimUser(entity, baseUrl), statusCode: StatusCodes.Status201Created, contentType: "application/scim+json; charset=utf-8");
    }

    private static async Task<IResult> UpdateUserAsync(
        string id,
        ScimUserDto dto,
        HttpContext context,
        SymbolonDbContext db,
        LeaseEngine leaseEngine,
        IAuditLedger auditLedger,
        TimeProvider time,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context, db);
        var entity = await db.ScimUsers
            .Include(u => u.GroupMemberships)
            .ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ScimError(StatusCodes.Status404NotFound, null, $"User '{id}' not found.");
        }

        bool wasActive = entity.Active;
        entity.UserName = string.IsNullOrWhiteSpace(dto.UserName) ? entity.UserName : dto.UserName.Trim();
        entity.ExternalId = dto.ExternalId?.Trim();
        entity.GivenName = dto.Name?.GivenName?.Trim();
        entity.FamilyName = dto.Name?.FamilyName?.Trim();
        entity.FormattedName = dto.Name?.Formatted?.Trim() ?? dto.DisplayName?.Trim() ?? entity.UserName;
        entity.Email = dto.Emails is { Count: > 0 } ? dto.Emails[0].Value.Trim() : null;
        entity.Active = dto.Active;
        entity.UpdatedAt = time.GetUtcNow();

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Zero-trust deprovisioning: if transitioned to inactive, immediately revoke all leases
        if (wasActive && !entity.Active)
        {
            await RevokeUserSeatsAsync(tenantId, entity, db, leaseEngine, auditLedger, time, ct).ConfigureAwait(false);
        }

        return ScimJson(MapToScimUser(entity, GetBaseUrl(context)));
    }

    private static async Task<IResult> PatchUserAsync(
        string id,
        ScimPatchOpDto patchOp,
        HttpContext context,
        SymbolonDbContext db,
        LeaseEngine leaseEngine,
        IAuditLedger auditLedger,
        TimeProvider time,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context, db);
        var entity = await db.ScimUsers
            .Include(u => u.GroupMemberships)
            .ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ScimError(StatusCodes.Status404NotFound, null, $"User '{id}' not found.");
        }

        bool wasActive = entity.Active;
        bool shouldRevoke = false;

        foreach (var op in patchOp.Operations)
        {
            bool opIsReplace = string.Equals(op.Op, "replace", StringComparison.OrdinalIgnoreCase);
            bool opIsAdd = string.Equals(op.Op, "add", StringComparison.OrdinalIgnoreCase);

            if (opIsReplace || opIsAdd)
            {
                if (string.Equals(op.Path, "active", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryExtractBool(op.Value, out bool newActive))
                    {
                        if (entity.Active && !newActive)
                        {
                            shouldRevoke = true;
                        }
                        entity.Active = newActive;
                    }
                }
                else if (string.IsNullOrWhiteSpace(op.Path) && op.Value.HasValue && op.Value.Value.ValueKind == JsonValueKind.Object)
                {
                    var obj = op.Value.Value;
                    if (obj.TryGetProperty("active", out var actProp))
                    {
                        if (TryExtractBoolFromJsonElement(actProp, out bool newActive))
                        {
                            if (entity.Active && !newActive)
                            {
                                shouldRevoke = true;
                            }
                            entity.Active = newActive;
                        }
                    }
                    if (obj.TryGetProperty("userName", out var unProp) && unProp.ValueKind == JsonValueKind.String)
                    {
                        entity.UserName = unProp.GetString()!;
                    }
                    if (obj.TryGetProperty("displayName", out var dnProp) && dnProp.ValueKind == JsonValueKind.String)
                    {
                        entity.FormattedName = dnProp.GetString();
                    }
                    if (obj.TryGetProperty("externalId", out var extProp) && extProp.ValueKind == JsonValueKind.String)
                    {
                        entity.ExternalId = extProp.GetString();
                    }
                }
                else if (string.Equals(op.Path, "userName", StringComparison.OrdinalIgnoreCase) && op.Value.HasValue && op.Value.Value.ValueKind == JsonValueKind.String)
                {
                    entity.UserName = op.Value.Value.GetString()!;
                }
                else if (string.Equals(op.Path, "displayName", StringComparison.OrdinalIgnoreCase) && op.Value.HasValue && op.Value.Value.ValueKind == JsonValueKind.String)
                {
                    entity.FormattedName = op.Value.Value.GetString();
                }
                else if (string.Equals(op.Path, "externalId", StringComparison.OrdinalIgnoreCase) && op.Value.HasValue && op.Value.Value.ValueKind == JsonValueKind.String)
                {
                    entity.ExternalId = op.Value.Value.GetString();
                }
                else if (string.Equals(op.Path, "name.givenName", StringComparison.OrdinalIgnoreCase) && op.Value.HasValue && op.Value.Value.ValueKind == JsonValueKind.String)
                {
                    entity.GivenName = op.Value.Value.GetString();
                }
                else if (string.Equals(op.Path, "name.familyName", StringComparison.OrdinalIgnoreCase) && op.Value.HasValue && op.Value.Value.ValueKind == JsonValueKind.String)
                {
                    entity.FamilyName = op.Value.Value.GetString();
                }
            }
        }

        entity.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (shouldRevoke || (wasActive && !entity.Active))
        {
            await RevokeUserSeatsAsync(tenantId, entity, db, leaseEngine, auditLedger, time, ct).ConfigureAwait(false);
        }

        return ScimJson(MapToScimUser(entity, GetBaseUrl(context)));
    }

    private static async Task<IResult> DeleteUserAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        LeaseEngine leaseEngine,
        IAuditLedger auditLedger,
        TimeProvider time,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context, db);
        var entity = await db.ScimUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ScimError(StatusCodes.Status404NotFound, null, $"User '{id}' not found.");
        }

        // Instant revocation before deletion
        await RevokeUserSeatsAsync(tenantId, entity, db, leaseEngine, auditLedger, time, ct).ConfigureAwait(false);

        db.ScimUsers.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    // --- Groups API (RFC 7644 §3.2 - §3.6) ---

    private static async Task<IResult> GetGroupsAsync(
        HttpContext context,
        SymbolonDbContext db,
        string? filter,
        int? startIndex,
        int? count,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context, db);
        int pageStart = Math.Max(1, startIndex ?? 1);
        int pageSize = Math.Clamp(count ?? 100, 1, 1000);

        var query = db.ScimGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .ThenInclude(m => m.User)
            .Where(g => g.TenantId == tenantId);

        var (attr, val) = ParseSimpleFilter(filter);
        if (!string.IsNullOrWhiteSpace(attr) && !string.IsNullOrWhiteSpace(val))
        {
            if (attr.Equals("displayName", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(g => g.DisplayName == val);
            }
            else if (attr.Equals("externalId", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(g => g.ExternalId == val);
            }
            else if (attr.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(g => g.Id == val);
            }
        }

        int totalResults = await query.CountAsync(ct).ConfigureAwait(false);
        var entities = await query
            .OrderBy(g => g.DisplayName)
            .Skip(pageStart - 1)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string baseUrl = GetBaseUrl(context);
        var dtos = entities.Select(e => MapToScimGroup(e, baseUrl)).ToList();

        return ScimJson(new ScimListResponseDto<ScimGroupDto>
        {
            TotalResults = totalResults,
            StartIndex = pageStart,
            ItemsPerPage = dtos.Count,
            Resources = dtos
        });
    }

    private static async Task<IResult> GetGroupByIdAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context, db);
        var entity = await db.ScimGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ScimError(StatusCodes.Status404NotFound, null, $"Group '{id}' not found.");
        }

        return ScimJson(MapToScimGroup(entity, GetBaseUrl(context)));
    }

    private static async Task<IResult> CreateGroupAsync(
        ScimGroupDto dto,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.DisplayName))
        {
            return ScimError(StatusCodes.Status400BadRequest, "invalidValue", "Field 'displayName' is required.");
        }

        string tenantId = ResolveTenantId(context, db);
        var now = time.GetUtcNow();
        var entity = new ScimGroupEntity
        {
            Id = $"grp_{Guid.NewGuid():N}",
            TenantId = tenantId,
            DisplayName = dto.DisplayName.Trim(),
            ExternalId = dto.ExternalId?.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.ScimGroups.Add(entity);

        if (dto.Members is { Count: > 0 })
        {
            foreach (var member in dto.Members)
            {
                if (!string.IsNullOrWhiteSpace(member.Value))
                {
                    db.ScimGroupMembers.Add(new ScimGroupMemberEntity
                    {
                        GroupId = entity.Id,
                        UserId = member.Value.Trim(),
                        AddedAt = now
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        string baseUrl = GetBaseUrl(context);
        string location = $"{baseUrl}/scim/v2/Groups/{entity.Id}";
        context.Response.Headers.Location = location;

        return Results.Json(MapToScimGroup(entity, baseUrl), statusCode: StatusCodes.Status201Created, contentType: "application/scim+json; charset=utf-8");
    }

    private static async Task<IResult> UpdateGroupAsync(
        string id,
        ScimGroupDto dto,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context, db);
        var entity = await db.ScimGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ScimError(StatusCodes.Status404NotFound, null, $"Group '{id}' not found.");
        }

        var now = time.GetUtcNow();
        entity.DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? entity.DisplayName : dto.DisplayName.Trim();
        entity.ExternalId = dto.ExternalId?.Trim();
        entity.UpdatedAt = now;

        // Replace members
        db.ScimGroupMembers.RemoveRange(entity.Members);
        if (dto.Members is { Count: > 0 })
        {
            foreach (var member in dto.Members)
            {
                if (!string.IsNullOrWhiteSpace(member.Value))
                {
                    db.ScimGroupMembers.Add(new ScimGroupMemberEntity
                    {
                        GroupId = entity.Id,
                        UserId = member.Value.Trim(),
                        AddedAt = now
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ScimJson(MapToScimGroup(entity, GetBaseUrl(context)));
    }

    private static async Task<IResult> PatchGroupAsync(
        string id,
        ScimPatchOpDto patchOp,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context, db);
        var entity = await db.ScimGroups
            .Include(g => g.Members)
            .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ScimError(StatusCodes.Status404NotFound, null, $"Group '{id}' not found.");
        }

        var now = time.GetUtcNow();

        foreach (var op in patchOp.Operations)
        {
            if (string.Equals(op.Op, "add", StringComparison.OrdinalIgnoreCase))
            {
                if (op.Value.HasValue && op.Value.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in op.Value.Value.EnumerateArray())
                    {
                        if (item.TryGetProperty("value", out var valProp) && valProp.ValueKind == JsonValueKind.String)
                        {
                            string uid = valProp.GetString()!;
                            if (!entity.Members.Any(m => m.UserId == uid))
                            {
                                db.ScimGroupMembers.Add(new ScimGroupMemberEntity
                                {
                                    GroupId = entity.Id,
                                    UserId = uid,
                                    AddedAt = now
                                });
                            }
                        }
                    }
                }
            }
            else if (string.Equals(op.Op, "remove", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(op.Path))
                {
                    var match = ScimRegexes.GroupMemberRemoveRegex().Match(op.Path.Trim());
                    if (match.Success)
                    {
                        string uid = match.Groups[1].Value;
                        var toRemove = entity.Members.Where(m => m.UserId == uid).ToList();
                        db.ScimGroupMembers.RemoveRange(toRemove);
                    }
                }
            }
            else if (string.Equals(op.Op, "replace", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(op.Path, "displayName", StringComparison.OrdinalIgnoreCase) && op.Value.HasValue && op.Value.Value.ValueKind == JsonValueKind.String)
                {
                    entity.DisplayName = op.Value.Value.GetString()!;
                }
            }
        }

        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ScimJson(MapToScimGroup(entity, GetBaseUrl(context)));
    }

    private static async Task<IResult> DeleteGroupAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context, db);
        var entity = await db.ScimGroups
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return ScimError(StatusCodes.Status404NotFound, null, $"Group '{id}' not found.");
        }

        db.ScimGroups.Remove(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.NoContent();
    }

    // --- Deprovisioning Seat Reclamation Engine ---

    private static async Task<int> RevokeUserSeatsAsync(
        string tenantId,
        ScimUserEntity user,
        SymbolonDbContext db,
        LeaseEngine leaseEngine,
        IAuditLedger auditLedger,
        TimeProvider time,
        CancellationToken ct)
    {
        using var activity = SymbolonTracing.ActivitySource.StartActivity("symbolon.scim.deprovision");
        activity?.SetTag("scim.user_id", user.Id);
        activity?.SetTag("scim.user_name", user.UserName);
        activity?.SetTag("tenant_id", tenantId);

        var now = time.GetUtcNow();
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            user.Id,
            user.UserName
        };
        if (!string.IsNullOrWhiteSpace(user.ExternalId))
        {
            identifiers.Add(user.ExternalId);
        }
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            identifiers.Add(user.Email);
        }

        // 1. Release all active seat leases allocated to this user
        var candidateSeats = await db.Seats
            .Include(s => s.License)
            .Where(s => s.License != null && s.License.TenantId == tenantId && s.LeaseId != null && s.ExpiresAt > now)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int revokedSeatsCount = 0;
        foreach (var seat in candidateSeats)
        {
            bool isUserMatch = (seat.UserId != null && identifiers.Contains(seat.UserId)) ||
                               (seat.ReservedFor != null && identifiers.Contains(seat.ReservedFor));

            if (isUserMatch)
            {
                if (!string.IsNullOrWhiteSpace(seat.LeaseId))
                {
                    await leaseEngine.ReleaseAsync(seat.LeaseId, ct).ConfigureAwait(false);
                    revokedSeatsCount++;
                }
                seat.UserId = null;
            }
        }

        // 2. Cancel waiting queue tickets
        var tickets = await db.QueueTickets
            .Include(q => q.License)
            .Where(q => q.License != null && q.License.TenantId == tenantId && (q.Status == "waiting" || q.Status == "ready"))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int cancelledTicketsCount = 0;
        foreach (var ticket in tickets)
        {
            if (ticket.UserId != null && identifiers.Contains(ticket.UserId))
            {
                ticket.Status = "cancelled";
                cancelledTicketsCount++;
            }
        }

        // 3. Remove Named User assignments
        var licenseUsers = await db.LicenseUsers
            .Include(lu => lu.License)
            .Where(lu => lu.License != null && lu.License.TenantId == tenantId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int removedAssignments = 0;
        foreach (var lu in licenseUsers)
        {
            if (identifiers.Contains(lu.UserId))
            {
                db.LicenseUsers.Remove(lu);
                removedAssignments++;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // 4. Audit ledger entry
        await auditLedger.AppendAsync(new AuditEvent(
            Type: "SCIM_USER_DEPROVISIONED",
            LicenseId: tenantId,
            LeaseId: user.Id,
            Fingerprint: user.UserName,
            Timestamp: now,
            Detail: $"RevokedSeats={revokedSeatsCount};CancelledTickets={cancelledTicketsCount};RemovedNamedAssignments={removedAssignments}"),
            ct).ConfigureAwait(false);

        activity?.SetTag("scim.revoked_seats", revokedSeatsCount);
        activity?.SetTag("scim.cancelled_tickets", cancelledTicketsCount);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return revokedSeatsCount;
    }

    private static (string? attribute, string? value) ParseSimpleFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return (null, null);
        }

        var match = ScimRegexes.FilterRegex().Match(filter.Trim());
        if (match.Success)
        {
            return (match.Groups[1].Value, match.Groups[2].Value);
        }

        return (null, null);
    }

    private static bool TryExtractBool(JsonElement? elem, out bool result)
    {
        result = false;
        if (!elem.HasValue)
        {
            return false;
        }
        return TryExtractBoolFromJsonElement(elem.Value, out result);
    }

    private static bool TryExtractBoolFromJsonElement(JsonElement elem, out bool result)
    {
        result = false;
        if (elem.ValueKind == JsonValueKind.True)
        {
            result = true;
            return true;
        }
        if (elem.ValueKind == JsonValueKind.False)
        {
            result = false;
            return true;
        }
        if (elem.ValueKind == JsonValueKind.String)
        {
            return bool.TryParse(elem.GetString(), out result);
        }
        return false;
    }

    private static ScimUserDto MapToScimUser(ScimUserEntity entity, string baseUrl)
    {
        var emails = string.IsNullOrWhiteSpace(entity.Email)
            ? null
            : new List<ScimEmailDto>
            {
                new() { Value = entity.Email, Type = "work", Primary = true }
            };

        var groups = entity.GroupMemberships?
            .Select(m => new ScimMemberDto
            {
                Value = m.GroupId,
                Display = m.Group?.DisplayName,
                Ref = $"{baseUrl}/scim/v2/Groups/{m.GroupId}"
            })
            .ToList();

        return new ScimUserDto
        {
            Id = entity.Id,
            ExternalId = entity.ExternalId,
            UserName = entity.UserName,
            DisplayName = entity.FormattedName ?? entity.UserName,
            Name = new ScimUserNameDto
            {
                Formatted = entity.FormattedName,
                GivenName = entity.GivenName,
                FamilyName = entity.FamilyName
            },
            Active = entity.Active,
            Emails = emails,
            Groups = groups,
            Meta = new ScimMetaDto
            {
                ResourceType = "User",
                Created = entity.CreatedAt,
                LastModified = entity.UpdatedAt,
                Location = $"{baseUrl}/scim/v2/Users/{entity.Id}",
                Version = $"W/\"{entity.UpdatedAt.ToUnixTimeMilliseconds()}\""
            }
        };
    }

    private static ScimGroupDto MapToScimGroup(ScimGroupEntity entity, string baseUrl)
    {
        var members = entity.Members?
            .Select(m => new ScimMemberDto
            {
                Value = m.UserId,
                Display = m.User?.FormattedName ?? m.User?.UserName,
                Ref = $"{baseUrl}/scim/v2/Users/{m.UserId}"
            })
            .ToList();

        return new ScimGroupDto
        {
            Id = entity.Id,
            DisplayName = entity.DisplayName,
            ExternalId = entity.ExternalId,
            Members = members,
            Meta = new ScimMetaDto
            {
                ResourceType = "Group",
                Created = entity.CreatedAt,
                LastModified = entity.UpdatedAt,
                Location = $"{baseUrl}/scim/v2/Groups/{entity.Id}",
                Version = $"W/\"{entity.UpdatedAt.ToUnixTimeMilliseconds()}\""
            }
        };
    }
}

internal static partial class ScimRegexes
{
    [GeneratedRegex(@"^(\w+)\s+eq\s+[""']?([^""']+)[""']?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex FilterRegex();

    [GeneratedRegex(@"^members\[value\s+eq\s+[""']?([^""']+)[""']?\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex GroupMemberRemoveRegex();
}
