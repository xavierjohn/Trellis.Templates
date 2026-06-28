namespace ProjectTrackerTemplate.Members.Api;

using Asp.Versioning;
using Asp.Versioning.Builder;
using Mediator;
using ProjectTrackerTemplate.Members.Application;
using ProjectTrackerTemplate.Members.Domain;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.ApiVersioning;
using Trellis.Asp.Idempotency;
using Trellis.Primitives;
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
        // validated by UseScalarValueValidation (an invalid id is a 422 before the handler runs). The
        // response carries the aggregate's strong ETag + Last-Modified so a client can later make a
        // conditional write. The route is NAMED so the create endpoint's Location header can resolve to it.
        members.MapGet("/{id}", (MemberId id, IMediator mediator, CancellationToken ct) =>
            mediator.Send(new GetMemberQuery(id), ct)
                .ToHttpResponseAsync(
                    MemberResponse.From,
                    opts => opts
                        .WithETag(m => EntityTagValue.Strong(m.ETag))
                        .WithLastModified(m => m.LastModified)))
            .WithName("Members_GetById");

        // POST /api/members: invite a new member into the actor's tenant. The request body carries value
        // objects (EmailAddress, Role), so .WithScalarValueValidation() turns a malformed email/role into
        // a 422 before the handler runs. On success it returns 201 Created with a Location header pointing
        // at the new member (CreatedAtRoute + WithVersionedRoute injects the api-version so the URL round-
        // trips), plus the member representation and its ETag. [Idempotent] lets a caller safely retry.
        members.MapPost("/", (InviteMemberRequest body, IMediator mediator, CancellationToken ct) =>
                mediator.Send(new InviteMemberCommand(body.Email, body.Role), ct)
                    .ToHttpResponseAsync(
                        MemberResponse.From,
                        opts => opts
                            .CreatedAtRoute("Members_GetById", m => m.Id.Value)
                            .WithVersionedRoute()
                            .WithETag(m => EntityTagValue.Strong(m.ETag))
                            .WithLastModified(m => m.LastModified)))
            .WithScalarValueValidation()
            .WithMetadata(new IdempotentAttribute());

        return app;
    }
}

// Wire-format DTOs for the Members API. The request carries value objects so a malformed email or role
// is rejected with a 422 (see .WithScalarValueValidation() on the create endpoint); the response is
// primitive so clients never couple to the domain types.
internal sealed record InviteMemberRequest(EmailAddress Email, Role Role);

internal sealed record MemberResponse(string Id, string TenantId, string Email, string Role)
{
    public static MemberResponse From(Member m) => new(m.Id, m.TenantId, m.Email, m.Role);
}
