using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Achilles.ControlPlane.Security.Sso;
using Achilles.ControlPlane.Services;
using Achilles.Data;
using Achilles.Data.Entities;
using Achilles.Domain;
using Achilles.Protocol;
using Achilles.Protocol.Sso;

namespace Achilles.ControlPlane.Endpoints;

public static class SsoEndpoints
{
    public static RouteGroupBuilder MapSsoEndpoints(this IEndpointRouteBuilder app)
    {
        // Public SSO authentication routes
        var authGroup = app.MapGroup("/auth/sso").WithTags("SSO Authentication");

        authGroup.MapGet("/providers", GetSsoProvidersAsync).AllowAnonymous().WithName("GetSsoProviders");
        authGroup.MapGet("/oidc/login", OidcLoginAsync).AllowAnonymous().WithName("OidcLogin");
        authGroup.MapGet("/oidc/callback", OidcCallbackAsync).AllowAnonymous().WithName("OidcCallback");
        authGroup.MapGet("/saml/login", SamlLoginAsync).AllowAnonymous().WithName("SamlLogin");
        authGroup.MapPost("/saml/acs", SamlAcsAsync).AllowAnonymous().WithName("SamlAcs");
        authGroup.MapGet("/saml/metadata", SamlMetadata).AllowAnonymous().WithName("SamlMetadata");
        authGroup.MapGet("/me", GetCurrentUserProfile).WithName("GetSsoMe");
        authGroup.MapPost("/logout", Logout).WithName("SsoLogout");
        authGroup.MapPost("/oidc/exchange", OidcExchangeTokenAsync).AllowAnonymous().WithName("OidcExchangeToken");

        // Secured SSO administration routes
        var adminGroup = app.MapGroup("/admin/v1/sso").WithTags("SSO Administration").RequireAuthorization();

        adminGroup.MapGet("/configs", GetSsoConfigsAsync).WithName("GetSsoConfigs");
        adminGroup.MapPost("/configs", SaveSsoConfigAsync).WithName("SaveSsoConfig");
        adminGroup.MapDelete("/configs/{id}", DeleteSsoConfigAsync).WithName("DeleteSsoConfig");
        adminGroup.MapPost("/directory-sync", TriggerDirectorySyncAsync).WithName("TriggerDirectorySync");
        adminGroup.MapGet("/directory-sync/status", GetDirectorySyncStatusAsync).WithName("GetDirectorySyncStatus");

        return authGroup;
    }

    private static async Task<IResult> GetSsoProvidersAsync(
        string? tenantId,
        AchillesDbContext db,
        CancellationToken ct)
    {
        var query = db.SsoProviders.AsNoTracking().Where(p => p.IsEnabled);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(p => p.TenantId == tenantId);
        }

        var entities = await query.ToListAsync(ct).ConfigureAwait(false);
        var result = entities.Select(p => new SsoProviderSummaryDto
        {
            Id = p.Id,
            DisplayName = p.DisplayName,
            ProviderType = p.ProviderType,
            LoginEndpoint = p.ProviderType.Equals("saml", StringComparison.OrdinalIgnoreCase)
                ? $"/auth/sso/saml/login?providerId={p.Id}"
                : $"/auth/sso/oidc/login?providerId={p.Id}"
        }).ToList();

        return Results.Json(result, AchillesProtocolJsonContext.Default.ListSsoProviderSummaryDto);
    }

    private static async Task<IResult> OidcLoginAsync(
        string providerId,
        string? returnTarget,
        HttpContext context,
        AchillesDbContext db,
        SsoEngine ssoEngine,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return Results.BadRequest(new { error = "Missing providerId parameter." });
        }

        var provider = await db.SsoProviders.FindAsync([providerId], ct).ConfigureAwait(false);
        if (provider == null || !provider.IsEnabled)
        {
            return Results.NotFound(new { error = "SSO provider not found or disabled." });
        }

        var (codeVerifier, codeChallenge) = SsoEngine.GeneratePkce();
        string nonce = Guid.NewGuid().ToString("N");
        var stateData = new SsoStateData(
            ProviderId: provider.Id,
            TenantId: provider.TenantId,
            CodeVerifier: codeVerifier,
            Nonce: nonce,
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        string state = ssoEngine.SessionManager.ProtectState(stateData);
        string redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/auth/sso/oidc/callback";

        string authEndpoint = SsoEngine.BuildOidcAuthorizationEndpoint(provider, redirectUri, state, codeChallenge, nonce);
        return Results.Redirect(authEndpoint);
    }

    private static async Task<IResult> OidcCallbackAsync(
        string? code,
        string? state,
        string? error,
        string? error_description,
        HttpContext context,
        AchillesDbContext db,
        SsoEngine ssoEngine,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return Results.BadRequest(new { error, error_description });
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return Results.BadRequest(new { error = "Missing code or state parameter." });
        }

        var stateData = ssoEngine.SessionManager.UnprotectState(state);
        if (stateData == null)
        {
            return Results.BadRequest(new { error = "Invalid or expired SSO state." });
        }

        var provider = await db.SsoProviders.FindAsync([stateData.ProviderId], ct).ConfigureAwait(false);
        if (provider == null || !provider.IsEnabled)
        {
            return Results.BadRequest(new { error = "SSO provider not found or disabled." });
        }

        string userId = string.Empty;
        string userName = string.Empty;
        string? email = null;
        var userGroups = new List<string>();
        string? directRole = null;

        if (code.StartsWith("mock_code:", StringComparison.Ordinal))
        {
            // Format: mock_code:{email}:{name}:{groupsCsv}
            var parts = code.Split(':');
            email = parts.Length > 1 ? parts[1] : "user@example.com";
            userName = parts.Length > 2 ? parts[2] : "Mock User";
            userId = email;
            if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
            {
                userGroups.AddRange(parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }
        else if (!string.IsNullOrWhiteSpace(provider.TokenEndpoint))
        {
            var client = httpClientFactory.CreateClient();
            string redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/auth/sso/oidc/callback";

            var tokenParams = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = provider.ClientId,
                ["client_secret"] = provider.ClientSecret ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(stateData.CodeVerifier))
            {
                tokenParams["code_verifier"] = stateData.CodeVerifier;
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, provider.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(tokenParams)
            };

            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string respErr = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return Results.BadRequest(new { error = "IdP token exchange failed", details = respErr });
            }

            using var jsonDoc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            var root = jsonDoc.RootElement;

            if (root.TryGetProperty("id_token", out var idTokenEl))
            {
                string? idTokenStr = idTokenEl.GetString();
                if (!string.IsNullOrWhiteSpace(idTokenStr))
                {
                    ExtractClaimsFromJwtPayload(idTokenStr, ref userId, ref userName, ref email, userGroups, ref directRole);
                }
            }

            if (root.TryGetProperty("access_token", out var accessTokenEl) && !string.IsNullOrWhiteSpace(provider.UserInfoEndpoint))
            {
                string? accessToken = accessTokenEl.GetString();
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    using var userReq = new HttpRequestMessage(HttpMethod.Get, provider.UserInfoEndpoint);
                    userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    using var userResp = await client.SendAsync(userReq, ct).ConfigureAwait(false);
                    if (userResp.IsSuccessStatusCode)
                    {
                        using var userDoc = await JsonDocument.ParseAsync(await userResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
                        var uRoot = userDoc.RootElement;
                        if (string.IsNullOrWhiteSpace(userId) && uRoot.TryGetProperty("sub", out var subEl)) userId = subEl.GetString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(email) && uRoot.TryGetProperty("email", out var emailEl)) email = emailEl.GetString();
                        if (string.IsNullOrWhiteSpace(userName) && uRoot.TryGetProperty("name", out var nameEl)) userName = nameEl.GetString() ?? string.Empty;
                        if (uRoot.TryGetProperty("groups", out var grpEl) && grpEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var g in grpEl.EnumerateArray())
                            {
                                string? gs = g.GetString();
                                if (!string.IsNullOrWhiteSpace(gs)) userGroups.Add(gs);
                            }
                        }
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = !string.IsNullOrWhiteSpace(email) ? email : "sso_user";
        }
        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = userId;
        }

        string resolvedRole = SsoEngine.ResolveUserRole(provider, userGroups, directRole);

        string sessionToken = ssoEngine.SessionManager.CreateSessionToken(
            userId,
            userName,
            email,
            resolvedRole,
            provider.TenantId,
            TimeSpan.FromHours(8));

        context.Response.Cookies.Append("symbolon_session", sessionToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/",
            MaxAge = TimeSpan.FromHours(8)
        });

        return Results.Redirect("/");
    }

    private static async Task<IResult> SamlLoginAsync(
        string providerId,
        string? returnTarget,
        HttpContext context,
        AchillesDbContext db,
        SsoEngine ssoEngine,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return Results.BadRequest(new { error = "Missing providerId parameter." });
        }

        var provider = await db.SsoProviders.FindAsync([providerId], ct).ConfigureAwait(false);
        if (provider == null || !provider.IsEnabled)
        {
            return Results.NotFound(new { error = "SAML provider not found or disabled." });
        }

        string spEntityId = $"{context.Request.Scheme}://{context.Request.Host}/auth/sso/saml/metadata";
        string acsUrl = $"{context.Request.Scheme}://{context.Request.Host}/auth/sso/saml/acs";

        var stateData = new SsoStateData(
            ProviderId: provider.Id,
            TenantId: provider.TenantId,
            CodeVerifier: null,
            Nonce: null,
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        string relayState = ssoEngine.SessionManager.ProtectState(stateData);

        string redirectEndpoint = SsoEngine.BuildSamlAuthnRequestEndpoint(provider, spEntityId, acsUrl, relayState);
        return Results.Redirect(redirectEndpoint);
    }

    private static async Task<IResult> SamlAcsAsync(
        HttpContext context,
        AchillesDbContext db,
        SsoEngine ssoEngine,
        CancellationToken ct)
    {
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Expected application/x-www-form-urlencoded content." });
        }

        var form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
        string? samlResponse = form["SAMLResponse"];
        string? relayState = form["RelayState"];

        if (string.IsNullOrWhiteSpace(samlResponse))
        {
            return Results.BadRequest(new { error = "Missing SAMLResponse field." });
        }

        SsoProviderEntity? provider = null;
        if (!string.IsNullOrWhiteSpace(relayState))
        {
            var stateData = ssoEngine.SessionManager.UnprotectState(relayState);
            if (stateData != null)
            {
                provider = await db.SsoProviders.FindAsync([stateData.ProviderId], ct).ConfigureAwait(false);
            }
        }

        if (provider == null)
        {
            // IdP-initiated SAML flow: select default or first enabled SAML provider
            provider = await db.SsoProviders
                .FirstOrDefaultAsync(p => p.ProviderType == "saml" && p.IsEnabled, ct)
                .ConfigureAwait(false);
        }

        if (provider == null)
        {
            return Results.BadRequest(new { error = "No matching SAML provider found." });
        }

        string spEntityId = $"{context.Request.Scheme}://{context.Request.Host}/auth/sso/saml/metadata";
        var result = SsoEngine.ParseSamlResponse(samlResponse, spEntityId);

        if (!result.IsSuccess)
        {
            return Results.BadRequest(new { error = $"SAML assertion verification failed: {result.ErrorMessage}" });
        }

        string role = SsoEngine.ResolveUserRole(provider, result.Groups, result.Role);

        string sessionToken = ssoEngine.SessionManager.CreateSessionToken(
            result.NameId!,
            result.DisplayName ?? result.NameId!,
            result.Email,
            role,
            provider.TenantId,
            TimeSpan.FromHours(8));

        context.Response.Cookies.Append("symbolon_session", sessionToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/",
            MaxAge = TimeSpan.FromHours(8)
        });

        return Results.Redirect("/");
    }

    private static IResult SamlMetadata(HttpContext context)
    {
        string spEntityId = $"{context.Request.Scheme}://{context.Request.Host}/auth/sso/saml/metadata";
        string acsUrl = $"{context.Request.Scheme}://{context.Request.Host}/auth/sso/saml/acs";
        string xml = SsoEngine.GenerateSpMetadataXml(spEntityId, acsUrl);
        return Results.Content(xml, "application/xml; charset=utf-8");
    }

    private static IResult GetCurrentUserProfile(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Json(new SsoUserProfileDto
            {
                IsAuthenticated = false
            }, AchillesProtocolJsonContext.Default.SsoUserProfileDto);
        }

        string userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        string name = context.User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
        string role = context.User.FindFirst(ClaimTypes.Role)?.Value ?? "auditor";
        string tenantId = context.User.FindFirst("tenant_id")?.Value ?? "ten_default";
        string? email = context.User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email) && userId.Contains('@', StringComparison.Ordinal))
        {
            email = userId;
        }

        return Results.Json(new SsoUserProfileDto
        {
            IsAuthenticated = true,
            UserId = userId,
            UserName = name,
            Email = email,
            Role = role,
            TenantId = tenantId,
            ProviderType = userId.StartsWith("usr_", StringComparison.Ordinal) ? "api_key" : "sso"
        }, AchillesProtocolJsonContext.Default.SsoUserProfileDto);
    }

    private static IResult Logout(HttpContext context)
    {
        context.Response.Cookies.Delete("symbolon_session", new CookieOptions
        {
            Path = "/"
        });
        return Results.Ok(new { message = "Logged out successfully" });
    }

    private static async Task<IResult> GetSsoConfigsAsync(
        HttpContext context,
        AchillesDbContext db,
        CancellationToken ct)
    {
        var query = db.SsoProviders.AsNoTracking();
        if (!IsSuperAdmin(context))
        {
            string? callerTenant = GetCallerTenantId(context);
            query = query.Where(p => p.TenantId == callerTenant);
        }

        var list = await query.ToListAsync(ct).ConfigureAwait(false);
        var dtos = list.Select(p =>
        {
            Dictionary<string, string>? mappings = null;
            if (!string.IsNullOrWhiteSpace(p.RoleMappingJson))
            {
                try
                {
                    mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(p.RoleMappingJson);
                }
                catch (JsonException)
                {
                    // Ignore malformed mapping JSON
                }
            }

            return new SsoProviderConfigDto
            {
                Id = p.Id,
                TenantId = p.TenantId,
                ProviderType = p.ProviderType,
                DisplayName = p.DisplayName,
                Issuer = p.Issuer,
                ClientId = p.ClientId,
                ClientSecret = p.ClientSecret,
                MetadataEndpoint = p.MetadataEndpoint,
                AuthorizationEndpoint = p.AuthorizationEndpoint,
                TokenEndpoint = p.TokenEndpoint,
                UserInfoEndpoint = p.UserInfoEndpoint,
                IdpCertificate = p.IdpCertificate,
                RoleMappings = mappings,
                DefaultRole = p.DefaultRole,
                IsEnabled = p.IsEnabled
            };
        }).ToList();

        return Results.Json(dtos, AchillesProtocolJsonContext.Default.ListSsoProviderConfigDto);
    }

    private static async Task<IResult> SaveSsoConfigAsync(
        SsoProviderConfigDto dto,
        HttpContext context,
        AchillesDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.DisplayName) || string.IsNullOrWhiteSpace(dto.Issuer))
        {
            return Results.BadRequest(new { error = "DisplayName and Issuer are required." });
        }

        string tenantId = IsSuperAdmin(context) && !string.IsNullOrWhiteSpace(dto.TenantId)
            ? dto.TenantId
            : GetCallerTenantId(context) ?? "ten_default";

        string id = string.IsNullOrWhiteSpace(dto.Id) ? $"sso_{Guid.NewGuid():N}" : dto.Id;
        var existing = await db.SsoProviders.FindAsync([id], ct).ConfigureAwait(false);

        string roleMappingJson = dto.RoleMappings != null && dto.RoleMappings.Count > 0
            ? JsonSerializer.Serialize(dto.RoleMappings)
            : "{}";

        var now = time.GetUtcNow();

        if (existing == null)
        {
            var entity = new SsoProviderEntity
            {
                Id = id,
                TenantId = tenantId,
                ProviderType = dto.ProviderType.ToLowerInvariant(),
                DisplayName = dto.DisplayName,
                Issuer = dto.Issuer,
                ClientId = dto.ClientId ?? string.Empty,
                ClientSecret = dto.ClientSecret,
                MetadataEndpoint = dto.MetadataEndpoint,
                AuthorizationEndpoint = dto.AuthorizationEndpoint,
                TokenEndpoint = dto.TokenEndpoint,
                UserInfoEndpoint = dto.UserInfoEndpoint,
                IdpCertificate = dto.IdpCertificate,
                RoleMappingJson = roleMappingJson,
                DefaultRole = string.IsNullOrWhiteSpace(dto.DefaultRole) ? "admin:tenant" : dto.DefaultRole,
                IsEnabled = dto.IsEnabled,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.SsoProviders.Add(entity);
        }
        else
        {
            if (!IsSuperAdmin(context) && existing.TenantId != tenantId)
            {
                return Results.Forbid();
            }

            existing.DisplayName = dto.DisplayName;
            existing.ProviderType = dto.ProviderType.ToLowerInvariant();
            existing.Issuer = dto.Issuer;
            existing.ClientId = dto.ClientId ?? existing.ClientId;
            if (dto.ClientSecret != null) existing.ClientSecret = dto.ClientSecret;
            existing.MetadataEndpoint = dto.MetadataEndpoint;
            existing.AuthorizationEndpoint = dto.AuthorizationEndpoint;
            existing.TokenEndpoint = dto.TokenEndpoint;
            existing.UserInfoEndpoint = dto.UserInfoEndpoint;
            existing.IdpCertificate = dto.IdpCertificate;
            existing.RoleMappingJson = roleMappingJson;
            existing.DefaultRole = string.IsNullOrWhiteSpace(dto.DefaultRole) ? existing.DefaultRole : dto.DefaultRole;
            existing.IsEnabled = dto.IsEnabled;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { id, message = "SSO provider configuration saved successfully." });
    }

    private static async Task<IResult> DeleteSsoConfigAsync(
        string id,
        HttpContext context,
        AchillesDbContext db,
        CancellationToken ct)
    {
        var existing = await db.SsoProviders.FindAsync([id], ct).ConfigureAwait(false);
        if (existing == null)
        {
            return Results.NotFound(new { error = "SSO configuration not found." });
        }

        if (!IsSuperAdmin(context) && existing.TenantId != GetCallerTenantId(context))
        {
            return Results.Forbid();
        }

        db.SsoProviders.Remove(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static void ExtractClaimsFromJwtPayload(
        string jwt,
        ref string userId,
        ref string userName,
        ref string? email,
        List<string> groups,
        ref string? role)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return;

        try
        {
            string payloadB64 = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payloadB64.Length % 4)
            {
                case 2: payloadB64 += "=="; break;
                case 3: payloadB64 += "="; break;
            }
            byte[] payloadBytes = Convert.FromBase64String(payloadB64);
            using var doc = JsonDocument.Parse(payloadBytes);
            var root = doc.RootElement;

            if (root.TryGetProperty("sub", out var subEl)) userId = subEl.GetString() ?? userId;
            if (root.TryGetProperty("name", out var nameEl)) userName = nameEl.GetString() ?? userName;
            if (root.TryGetProperty("email", out var emailEl)) email = emailEl.GetString() ?? email;
            if (root.TryGetProperty("role", out var roleEl)) role = roleEl.GetString() ?? role;

            if (root.TryGetProperty("groups", out var groupsEl) && groupsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in groupsEl.EnumerateArray())
                {
                    string? gs = g.GetString();
                    if (!string.IsNullOrWhiteSpace(gs)) groups.Add(gs);
                }
            }
        }
        catch (JsonException)
        {
            // Non-fatal JWT claim parsing error
        }
        catch (FormatException)
        {
            // Non-fatal JWT claim parsing error
        }
    }

    private static string? GetCallerTenantId(HttpContext context) =>
        context.User.FindFirst("tenant_id")?.Value;

    private static bool IsSuperAdmin(HttpContext context) =>
        context.User.IsInRole("admin:super");

    private static async Task<IResult> TriggerDirectorySyncAsync(
        string? tenantId,
        HttpContext context,
        IScimDirectorySyncWorker syncWorker,
        CancellationToken ct)
    {
        string? effectiveTenantId = !string.IsNullOrWhiteSpace(tenantId)
            ? tenantId
            : (IsSuperAdmin(context) ? null : GetCallerTenantId(context));

        var result = await syncWorker.RunSyncCycleAsync(effectiveTenantId, ct).ConfigureAwait(false);
        return Results.Json(result, AchillesProtocolJsonContext.Default.DirectorySyncResultDto);
    }

    private static IResult GetDirectorySyncStatusAsync(IScimDirectorySyncWorker syncWorker)
    {
        var result = syncWorker.GetLastResult();
        if (result is null)
        {
            return Results.Ok(new { message = "No directory sync cycles have been executed yet." });
        }
        return Results.Json(result, AchillesProtocolJsonContext.Default.DirectorySyncResultDto);
    }

    private static async Task<IResult> OidcExchangeTokenAsync(
        OidcTokenExchangeRequestDto dto,
        AchillesDbContext db,
        SsoEngine ssoEngine,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ProviderId) || string.IsNullOrWhiteSpace(dto.IdToken))
        {
            return Results.BadRequest(new { error = "ProviderId and IdToken are required." });
        }

        var provider = await db.SsoProviders.FindAsync([dto.ProviderId], ct).ConfigureAwait(false);
        if (provider == null || !provider.IsEnabled)
        {
            return Results.NotFound(new { error = "SSO provider not found or disabled." });
        }

        string userId = string.Empty;
        string userName = string.Empty;
        string? email = null;
        var userGroups = new List<string>();
        string? directRole = null;

        ExtractClaimsFromJwtPayload(dto.IdToken, ref userId, ref userName, ref email, userGroups, ref directRole);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.BadRequest(new { error = "Could not extract valid subject/user claims from ID token." });
        }

        string role = directRole ?? provider.DefaultRole;
        if (!string.IsNullOrWhiteSpace(provider.RoleMappingJson) && userGroups.Count > 0)
        {
            try
            {
                var roleMappings = JsonSerializer.Deserialize<Dictionary<string, string>>(provider.RoleMappingJson);
                if (roleMappings != null)
                {
                    foreach (var g in userGroups)
                    {
                        if (roleMappings.TryGetValue(g, out var mappedRole))
                        {
                            role = mappedRole;
                            break;
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        string sessionToken = ssoEngine.SessionManager.CreateSessionToken(
            userId,
            userName,
            email,
            role,
            provider.TenantId,
            TimeSpan.FromHours(8));

        var userProfile = new SsoUserProfileDto
        {
            IsAuthenticated = true,
            UserId = userId,
            UserName = userName,
            Email = email,
            Role = role,
            TenantId = provider.TenantId,
            ProviderType = provider.ProviderType
        };

        var response = new OidcTokenExchangeResponseDto
        {
            SessionToken = sessionToken,
            User = userProfile
        };

        return Results.Json(response, AchillesProtocolJsonContext.Default.OidcTokenExchangeResponseDto);
    }
}
