using Microsoft.AspNetCore.Mvc;
using Achilles.Domain.Enforcement;

namespace Achilles.ControlPlane.Endpoints;

public sealed record AttachCgroupRequest(string CgroupPath, int[]? Ports);
public sealed record DetachCgroupRequest(string CgroupPath);

public static class EbpfEndpoints
{
    public static RouteGroupBuilder MapEbpfEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/system/ebpf").WithTags("Enforcement");

        group.MapGet("/status", GetEbpfStatus)
            .WithName("GetEbpfStatus")
            .WithSummary("Získa aktuálny stav eBPF kernel enforcementu, pripojených cgroups a počítadiel blokovaní.");

        group.MapGet("/violations", GetEbpfViolations)
            .WithName("GetEbpfViolations")
            .WithSummary("Získa zoznam bezpečnostných udalostí z BPF ring buffera (pokusy o spojenie bez licencie).");

        group.MapPost("/attach", AttachCgroup)
            .WithName("AttachEbpfCgroup")
            .WithSummary("Pripojí eBPF socket filter k zadanej cgroup pre kernel boundary enforcement.");

        group.MapPost("/detach", DetachCgroup)
            .WithName("DetachEbpfCgroup")
            .WithSummary("Odpojí eBPF filter zo zadanej cgroup.");

        return group;
    }

    private static IResult GetEbpfStatus(IEbpfEnforcementEngine engine)
    {
        var status = engine.GetStatus();
        return TypedResults.Ok(status);
    }

    private static IResult GetEbpfViolations(
        [FromQuery] int? limit,
        IEbpfEnforcementEngine engine)
    {
        var violations = engine.GetRecentViolations(limit ?? 50);
        return TypedResults.Ok(violations);
    }

    private static IResult AttachCgroup(
        [FromBody] AttachCgroupRequest request,
        IEbpfEnforcementEngine engine)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CgroupPath))
        {
            return TypedResults.BadRequest("CgroupPath must not be empty.");
        }

        engine.AttachCgroup(request.CgroupPath, request.Ports);
        return TypedResults.Ok(new { message = $"eBPF filter successfully attached to cgroup '{request.CgroupPath}'." });
    }

    private static IResult DetachCgroup(
        [FromBody] DetachCgroupRequest request,
        IEbpfEnforcementEngine engine)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CgroupPath))
        {
            return TypedResults.BadRequest("CgroupPath must not be empty.");
        }

        bool detached = engine.DetachCgroup(request.CgroupPath);
        return detached
            ? TypedResults.Ok(new { message = $"eBPF filter detached from '{request.CgroupPath}'." })
            : TypedResults.NotFound($"Cgroup '{request.CgroupPath}' was not attached.");
    }
}
