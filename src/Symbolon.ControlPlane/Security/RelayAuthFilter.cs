using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data;
using Symbolon.Data.Entities;

namespace Symbolon.ControlPlane.Security;

public sealed class RelayAuthFilter : IEndpointFilter
{
    private readonly SymbolonDbContext _db;
    private readonly TimeProvider _time;

    public RelayAuthFilter(SymbolonDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;

        // 1. Try X-Relay-Api-Key header or Authorization Bearer header
        string? apiKey = httpContext.Request.Headers["X-Relay-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            string? authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                apiKey = authHeader["Bearer ".Length..].Trim();
            }
        }

        // 2. Try mTLS client certificate thumbprint
        string? certThumbprint = httpContext.Connection.ClientCertificate?.Thumbprint;

        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(certThumbprint))
        {
            return Results.Json(
                new { error = "Unauthorized", message = "Missing relay credentials. Provide 'X-Relay-Api-Key' header or mTLS client certificate." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // 3. Find relay matching either ApiKey or MtlsThumbprint
        RelayEntity? relay = null;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            relay = await _db.Relays
                .FirstOrDefaultAsync(r => r.ApiKey == apiKey, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }

        if (relay is null && !string.IsNullOrWhiteSpace(certThumbprint))
        {
            relay = await _db.Relays
                .FirstOrDefaultAsync(r => r.MtlsThumbprint == certThumbprint, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }

        if (relay is null)
        {
            return Results.Json(
                new { error = "Unauthorized", message = "Invalid relay credentials." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Store authenticated relay in HttpContext.Items for downstream endpoints
        httpContext.Items["AuthenticatedRelay"] = relay;
        httpContext.Items["AuthenticatedRelayId"] = relay.Id;

        relay.LastSync = _time.GetUtcNow();
        await _db.SaveChangesAsync(httpContext.RequestAborted).ConfigureAwait(false);

        return await next(context).ConfigureAwait(false);
    }
}
