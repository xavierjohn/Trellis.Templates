namespace ProjectTrackerTemplate.Projects.Endpoints;

using Asp.Versioning;
using Asp.Versioning.Builder;
using Mediator;
using ProjectTrackerTemplate.Projects.Application;
using ProjectTrackerTemplate.Projects.ReadModel;
using Trellis.Asp;
using Trellis.ServiceLevelIndicators;

// Versioned route group for the team directory — the read model Projects builds from Members'
// MemberInvited integration events. GET /api/team answers entirely from Projects' OWN store, proving the
// eventing plane end to end: invite a member in the Members service, then watch them appear here with no
// synchronous call between the services. The group mirrors the Projects API conventions (version set,
// authorization, SLI; the operation name is derived per-route by the middleware).
public static class TeamEndpoints
{
    private static readonly ApiVersion V20260326 = new(new DateOnly(2026, 3, 26));

    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        ApiVersionSet versionSet = app.NewApiVersionSet("Team")
            .HasApiVersion(V20260326)
            .ReportApiVersions()
            .Build();

        var team = app.MapGroup("/api/team")
            .WithApiVersionSet(versionSet)
            .WithTags("Team")
            .MapToApiVersion(V20260326)
            .RequireAuthorization()
            .AddServiceLevelIndicator();

        team.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new ListTeamQuery(), ct);
                return result.ToHttpResponse(items => items.Select(TeamMemberResponse.From).ToArray());
            });

        return app;
    }
}

// Wire-format DTO for the team directory. The read model's TenantId value object is projected to its
// string on the way out, mirroring ProjectResponse.
internal sealed record TeamMemberResponse(string MemberId, string TenantId, string Role, DateTimeOffset InvitedAt)
{
    public static TeamMemberResponse From(KnownMember m) =>
        new(m.MemberId, m.TenantId.Value, m.Role, m.InvitedAt);
}
