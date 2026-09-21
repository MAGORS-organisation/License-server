using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Symbolon.Data;

namespace Symbolon.ControlPlane.Security;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";
}

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        TimeProvider timeProvider)
        : base(options, logger, encoder)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? rawKey = null;

        if (Request.Headers.TryGetValue("X-Api-Key", out var headerVal) && !string.IsNullOrWhiteSpace(headerVal))
        {
            rawKey = headerVal.ToString().Trim();
        }
        else if (Request.Headers.TryGetValue("Authorization", out var authVal) && !string.IsNullOrWhiteSpace(authVal))
        {
            string authStr = authVal.ToString().Trim();
            if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                rawKey = authStr["Bearer ".Length..].Trim();
            }
        }

        bool requireAuth = _configuration.GetValue<bool>("Security:RequireAuth");

        if (string.IsNullOrWhiteSpace(rawKey))
        {
            if (!requireAuth)
            {
                // In dev/test mode when auth is not explicitly required, default to super-admin
                var devClaims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "usr_dev_admin"),
                    new Claim(ClaimTypes.Name, "Dev SuperAdmin"),
                    new Claim(ClaimTypes.Role, "admin:super"),
                    new Claim("tenant_id", "ten_default")
                };
                var devIdentity = new ClaimsIdentity(devClaims, Scheme.Name);
                var devPrincipal = new ClaimsPrincipal(devIdentity);
                var devTicket = new AuthenticationTicket(devPrincipal, Scheme.Name);
                return AuthenticateResult.Success(devTicket);
            }

            return AuthenticateResult.NoResult();
        }

        string prefix = rawKey.Length >= 16 ? rawKey[..16] : rawKey;
        byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        string providedHashHex = Convert.ToHexStringLower(providedHash);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();

        var candidates = await db.ApiKeys
            .Where(k => (k.Prefix == prefix || k.KeyHash == providedHashHex) && k.RevokedAt == null)
            .ToListAsync(Context.RequestAborted)
            .ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();

        foreach (var key in candidates)
        {
            if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < now)
            {
                continue;
            }

            byte[] storedHash;
            try
            {
                storedHash = Convert.FromHexString(key.KeyHash);
            }
            catch (FormatException)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(providedHash, storedHash))
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, key.Id),
                    new Claim(ClaimTypes.Name, key.Name),
                    new Claim(ClaimTypes.Role, key.Role),
                    new Claim("tenant_id", key.TenantId)
                };

                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);
                return AuthenticateResult.Success(ticket);
            }
        }

        return AuthenticateResult.Fail("Invalid or expired API key.");
    }
}
