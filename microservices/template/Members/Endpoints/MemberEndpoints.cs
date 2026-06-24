namespace ProjectTrackerTemplate.Members.Endpoints;

using Asp.Versioning;
using Asp.Versioning.Builder;
using Mediator;
using ProjectTrackerTemplate.Members.Application;
using ProjectTrackerTemplate.Members.Domain;
using Trellis.Asp;
using Trellis.Asp.Idempotency;
using Trellis.ServiceLevelIndicators;

// Endpoints live in their own *Endpoints.cs extension method (one per resource) instead of inline
// in Program.cs. The same shape scales — and it is also how minimal APIs are versioned: a route
// group is bound to an ApiVersionSet, and each endpoint is mapped to a specific version. Program.cs
// stays focused on the trust boundary + DI and just calls app.MapMemberEndpoints().
public static class MemberEndpoints
{
    // Date-based API version, matching the rest of the platform. Clients select it with the
    // query string ?api-version=2026-03-26 (the default Asp.Versioning reader).
    private static readonly ApiVersion V20260326 = new(new DateOnly(2026, 3, 26));

    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        ApiVersionSet versionSet = app.NewApiVersionSet("Members")
            .HasApiVersion(V20260326)
            .ReportApiVersions()
            .Build();

        // Conventions shared by EVERY endpoint in the group are declared once here — authorization,
        // the supported version, and SLI emission (the operation name is derived per-route by the
        // middleware, e.g. "GET /api/members/{id}"). The only thing an endpoint adds for itself below
        // is idempotency on the create.
        var members = app.MapGroup("/api/members")
            .WithApiVersionSet(versionSet)
            .WithTags("Members")
            .MapToApiVersion(V20260326)
            .RequireAuthorization()
            .AddServiceLevelIndicator();

        // GET /api/members/{id}: the MemberId route value is bound as a Trellis value object and
        // validated by UseScalarValueValidation (an invalid id is a 422 before the handler runs).
        members.MapGet("/{id}", async (MemberId id, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetMemberQuery(id), ct);
                return result.ToHttpResponse(MemberResponse.From);
            });

        // POST /api/members: invite a new member into the actor's tenant. [Idempotent] lets a caller
        // safely retry with an Idempotency-Key; the SLI measures the operation latency.
        members.MapPost("/", async (InviteMemberRequest body, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new InviteMemberCommand(body.Email, body.Role), ct);
                return result.ToHttpResponse(id => new { id = id.Value });
            })
            .WithMetadata(new IdempotentAttribute());

        return app;
    }
}

// Wire-format DTOs for the Members API.
internal sealed record InviteMemberRequest(string Email, string Role);

internal sealed record MemberResponse(string Id, string TenantId, string Email, string Role)
{
    public static MemberResponse From(Member m) => new(m.Id.Value, m.TenantId.Value, m.Email, m.Role);
}
