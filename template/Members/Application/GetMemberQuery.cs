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
        actor.Attributes.TryGetValue("tenant_id", out var tenantId)
        && string.Equals(tenantId, resource.TenantId, StringComparison.Ordinal)
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden("members.cross_tenant")
            {
                Detail = "Cross-tenant member access is not permitted.",
            });
}
