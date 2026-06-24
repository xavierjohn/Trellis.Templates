using Mediator;
using ProjectTrackerTemplate.Members.Domain;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Members.Application;

// Read one member.
//
// Authorize enforces tenant isolation — cross-tenant access fails. The combination
// with HideExistence<Member>() (configured in Program.cs) collapses that 403 into
// a 404 at the response-mapping stage. Result: a cross-tenant caller cannot
// distinguish "this member exists but I cannot see it" from "no such member" —
// the canonical defence against employee-enumeration attacks.
public sealed record GetMemberQuery(MemberId Id)
    : IQuery<Result<Member>>, IAuthorize, IAuthorizeResource<Member>, IIdentifyResource<Member, MemberId>
{
    public IReadOnlyList<string> RequiredPermissions => ["members:read"];

    public MemberId GetResourceId() => Id;

    public Trellis.IResult Authorize(Actor actor, Member resource) =>
        actor.TryGetAttribute<TenantId>("tenant_id", out var tenantId)
        && tenantId == resource.TenantId
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden("members.cross_tenant")
            {
                Detail = "Cross-tenant member access is not permitted.",
            });
}

// Reads the SAME Member instance ResourceAuthorizationBehavior loaded for
// Authorize — no second repository round-trip.
public sealed class GetMemberHandler : IQueryHandler<GetMemberQuery, Result<Member>>
{
    private readonly IAuthorizedResource<GetMemberQuery, Member> _authorized;

    public GetMemberHandler(IAuthorizedResource<GetMemberQuery, Member> authorized) => _authorized = authorized;

    public ValueTask<Result<Member>> Handle(GetMemberQuery query, CancellationToken cancellationToken) =>
        new(Result.Ok(_authorized.GetRequiredResource()));
}
