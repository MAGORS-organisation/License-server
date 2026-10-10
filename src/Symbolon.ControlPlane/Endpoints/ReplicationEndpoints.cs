using Microsoft.AspNetCore.Mvc;
using Symbolon.Domain.Replication;
using Symbolon.Protocol.Replication;

namespace Symbolon.ControlPlane.Endpoints;

public static class ReplicationEndpoints
{
    public const string DefaultClusterSecret = "symbolon-cluster-shared-secret-2026";

    public static RouteGroupBuilder MapReplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/replication").WithTags("Replication");

        group.MapGet("/status", GetClusterStatus)
            .WithName("GetClusterStatus")
            .WithSummary("Získa aktuálny stav vektorových hodín, PN-counterov a topológie geo-klastra.");

        group.MapPost("/sync", SyncClusterDeltas)
            .WithName("SyncClusterDeltas")
            .WithSummary("Spracuje prichádzajúcu replikačnú deltu z iného regiónu a vráti recipročnú deltu.");

        group.MapPost("/peers", RegisterPeerRegion)
            .WithName("RegisterPeerRegion")
            .WithSummary("Zaregistruje nový región do geo-replikačnej topológie.");

        group.MapPost("/failover", ExecuteFailover)
            .WithName("ExecuteFailover")
            .WithSummary("Spustí automatizovaný failover a rekultiváciu sedadiel zlyhaného regiónu.");

        return group;
    }

    private static IResult GetClusterStatus(
        IGeoReplicationEngine engine,
        TimeProvider time)
    {
        var status = engine.GetStatus(time.GetUtcNow());
        return TypedResults.Ok(status);
    }

    private static IResult SyncClusterDeltas(
        [FromBody] ReplicationSyncRequest request,
        IGeoReplicationEngine engine,
        IConfiguration config,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = time.GetUtcNow();

        // 1. Anti-Replay: timestamp freshness verification
        if (!ReplicationSecurity.IsTimestampFresh(request.Timestamp, now))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Timestamp Drift Exceeded",
                detail: "The replication message timestamp is expired or outside acceptable clock drift window.");
        }

        // 2. Cryptographic Authentication: HMAC-SHA256
        string secret = config["Symbolon:ClusterSecret"] ?? DefaultClusterSecret;
        byte[] payload = ReplicationSecurity.CreateCanonicalPayload(
            request.SenderRegionId,
            request.TargetRegionId,
            request.Delta.DeltaId,
            request.Timestamp);

        if (!ReplicationSecurity.VerifyHmac(payload, request.HmacSignature, secret))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid Cluster Signature",
                detail: "HMAC signature verification failed. Authentication denied.");
        }

        // 3. Assimilate incoming delta into local CRDT state
        var response = engine.AssimilateRemoteDelta(request.Delta, now);

        // 4. Generate reciprocal delta for sender to establish bilateral convergence
        var reciprocal = engine.GenerateDeltaForPeer(request.SenderRegionId, request.Delta.VectorClock, now);

        return TypedResults.Ok(response with { ReciprocalDelta = reciprocal });
    }

    private static IResult RegisterPeerRegion(
        [FromBody] RegionPeer peer,
        IGeoReplicationEngine engine)
    {
        ArgumentNullException.ThrowIfNull(peer);

        engine.RegisterPeer(peer.RegionId, peer.Endpoint, peer.SeatRangeStart, peer.SeatRangeEnd);
        return TypedResults.Ok(new { message = $"Peer region '{peer.RegionId}' registered successfully." });
    }

    private static IResult ExecuteFailover(
        [FromBody] DisasterRecoveryFailoverRequest request,
        IGeoReplicationEngine engine,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(request);

        var report = engine.FailoverAndReclaimPeerSeats(request.FailedRegion, time.GetUtcNow());
        return TypedResults.Ok(report);
    }
}
