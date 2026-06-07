using Mediator;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Projects.Application;

// List projects in the actor's tenant. Static permission check + ABAC scoping:
// the tenant_id is read from Actor.Attributes and used as a filter, so there is
// no per-row resource authorization — the query itself is tenant-scoped.
//
// Compare with GetProjectQuery, which checks tenant_id at the resource boundary
// (because a single Id could belong to any tenant).
public sealed record ListProjectsQuery
    : IQuery<Result<IReadOnlyList<Project>>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["projects:read"];
}
